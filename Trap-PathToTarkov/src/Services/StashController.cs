using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Reflection;
using PathToTarkov.Helpers;
using PathToTarkov.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace PathToTarkov.Services;

/// <summary>
/// Manages the secondary stash system. Port of stash-controller.ts
///
/// Key IDs are deterministic from the stash name so they survive server restarts:
///   mongoId          = PTT_<name>
///   mongoTemplateId  = PTT_template_<name>
///   mongoGridId      = PTT_grid_<name>
/// </summary>
public class StashController
{
    // Standard stash template to clone for custom sizes
    private const string STANDARD_STASH_ID = "566abbc34bdc2d92178b4576";

    // All vanilla stash template IDs (override grid size for secondary stash display)
    private static readonly string[] VANILLA_STASH_IDS =
    {
        "566abbc34bdc2d92178b4576", // Standard
        "5811ce572459770cba1a34ea", // Left Behind
        "5811ce662459770f6f490f32", // Prepare for escape
        "5811ce772459770e9e5f9532", // Edge of darkness
        "6602bcf19cc643f44a04274b", // Unheard
    };

    private const string SLOT_HIDEOUT      = "hideout";
    private const string SLOT_LOCKED_STASH = "lockedStash";

    // Empty stash used when at an unreachable position
    private static readonly StashConfig EMPTY_STASH = MakeStashConfig("PathToTarkov_Empty_Stash", 0);

    private readonly Func<string, PttConfig>            _getConfig;
    private readonly UserConfig                          _userConfig;
    private readonly DatabaseService                     _db;
    private readonly SaveServer                          _saveServer;
    private readonly Action<string>                      _log;
    private readonly Func<string, string, bool>?         _isOffraidPositionUnlocked;

    public StashController(
        Func<string, PttConfig> getConfig,
        UserConfig userConfig,
        DatabaseService db,
        SaveServer saveServer,
        Action<string>? log = null,
        Func<string, string, bool>? isOffraidPositionUnlocked = null)
    {
        _getConfig                  = getConfig;
        _userConfig                 = userConfig;
        _db                         = db;
        _saveServer                 = saveServer;
        _log                        = log ?? (_ => {});
        _isOffraidPositionUnlocked  = isOffraidPositionUnlocked;
    }

    // ---- Deterministic ID generation ----

    private static StashConfig MakeStashConfig(string name, int size, List<string>? accessVia = null)
    {
        // Deterministic 24-hex IDs from stash name using simple hash
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        var hash  = System.Security.Cryptography.MD5.HashData(bytes);
        var mongoId          = BitConverter.ToString(hash).Replace("-", "").ToLower().PadRight(24, '0')[..24];
        var templateBytes    = System.Text.Encoding.UTF8.GetBytes("template_" + name);
        var templateHash     = System.Security.Cryptography.MD5.HashData(templateBytes);
        var mongoTemplateId  = BitConverter.ToString(templateHash).Replace("-", "").ToLower().PadRight(24, '0')[..24];
        var gridBytes        = System.Text.Encoding.UTF8.GetBytes("grid_" + name);
        var gridHash         = System.Security.Cryptography.MD5.HashData(gridBytes);
        var mongoGridId      = BitConverter.ToString(gridHash).Replace("-", "").ToLower().PadRight(24, '0')[..24];
        return new StashConfig(name, size, accessVia ?? new(), mongoId, mongoTemplateId, mongoGridId);
    }

    private static StashConfig ToStashConfig(SecondaryStashConfig raw)
        => MakeStashConfig(raw.Id, raw.Size, raw.AccessVia ?? new());

    // ---- DB Initialisation (called from OnLoaded) ----

    public int InitSecondaryStashTemplates(List<SecondaryStashConfig> rawConfigs)
    {
        var configs   = rawConfigs.Select(ToStashConfig).Prepend(EMPTY_STASH).ToList();
        var items     = _db.GetTables()?.Templates?.Items;
        var standard  = items?.GetValueOrDefault(new MongoId(STANDARD_STASH_ID));

        if (standard == null || items == null)
        {
            _log("[PTT] StashController: standard stash template not found, skipping");
            return 0;
        }

        int added = 0;

        // Use MemberwiseClone via reflection to shallow-clone TemplateItem
        // without going through JsonSerializer (which requires SPT's internal
        // JsonSerializerOptions to handle MongoId, and crashes on null string setters).
        var memberwiseClone = typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var cfg in configs)
        {
            // Shallow clone the standard template
            var newItem = (TemplateItem)memberwiseClone.Invoke(standard, null)!;

            // Overwrite identity fields directly
            newItem.Id   = new MongoId(cfg.MongoTemplateId);
            newItem.Name = $"{cfg.Name} of size {cfg.Size}";

            // Deep-clone and patch the Properties.Grids list so we don't
            // mutate the shared original object
            if (standard.Properties?.Grids != null)
            {
                var clonedProps = (TemplateItemProperties)memberwiseClone.Invoke(standard.Properties, null)!;
                var gridList    = standard.Properties.Grids.ToList();

                if (gridList.Count > 0)
                {
                    var originalGrid  = gridList[0];
                    var clonedGrid    = (Grid)memberwiseClone.Invoke(originalGrid, null)!;
                    clonedGrid.Id     = cfg.MongoGridId;
                    clonedGrid.Parent = cfg.MongoTemplateId;

                    if (originalGrid.Properties != null)
                    {
                        var clonedGridProps        = (GridProperties)memberwiseClone.Invoke(originalGrid.Properties, null)!;
                        clonedGridProps.CellsV     = cfg.Size;
                        clonedGrid.Properties      = clonedGridProps;
                    }

                    gridList[0] = clonedGrid;
                }

                clonedProps.Grids = gridList;
                newItem.Properties = clonedProps;
            }

            items[new MongoId(cfg.MongoTemplateId)] = newItem;
            added++;
        }

        _log($"[PTT] StashController: {added} secondary stash templates added");
        return added;
    }

    // ---- Profile Initialisation ----

    public void InitProfile(string sessionId)
    {
        var profile  = _saveServer.GetProfile(new MongoId(sessionId));
        var pttData  = GetPttData(profile);
        var pmc      = profile.CharacterData?.PmcData;
        if (pmc == null) return;

        if (string.IsNullOrEmpty(pttData.MainStashId))
        {
            var allConfigs = GetAllStashConfigs(sessionId);
            // Find the real main stash (a vanilla stash item not owned by PTT)
            var mainStashId = RetrieveMainStashId(pmc.Inventory?.Items?.ToList(), allConfigs);
            pttData.MainStashId = mainStashId ?? pmc.Inventory?.Stash?.ToString();
            SavePttData(profile, pttData);
        }
    }

    // ---- Stash Update (called when offraid position changes) ----

    public void UpdateStash(string offraidPosition, string sessionId)
    {
        if (!_userConfig.Gameplay.Multistash) return;

        var profile = _saveServer.GetProfile(new MongoId(sessionId));
        var pmc     = profile.CharacterData?.PmcData;
        if (pmc?.Inventory == null) return;

        var mainAvailable  = GetMainStashAvailable(offraidPosition, sessionId);
        var secondaryStash = GetSecondaryStash(offraidPosition, sessionId);

        if (mainAvailable)
            SetMainStash(profile);
        else
            SetSecondaryStash(secondaryStash, profile);

        // Fix slot IDs so items in the active stash use "hideout", others use "lockedStash"
        var activeStashId = pmc.Inventory.Stash?.ToString() ?? "";
        FixInventorySlotIds(pmc, activeStashId, GetSecondaryStashConfigs(sessionId));
    }

    // ---- Stash Size (for /client/items override) ----

    /// <summary>Returns the grid height override for the current secondary stash, or null if main stash.</summary>
    public int? GetStashSize(string offraidPosition, string sessionId)
    {
        if (!_userConfig.Gameplay.Multistash || GetMainStashAvailable(offraidPosition, sessionId))
            return null;
        return GetSecondaryStash(offraidPosition, sessionId).Size;
    }

    public bool GetHideoutEnabled(string offraidPosition, string sessionId)
        => !_userConfig.Gameplay.Multistash || GetMainStashAvailable(offraidPosition, sessionId);

    // ---- Private helpers ----

    private bool GetMainStashAvailable(string offraidPosition, string sessionId)
    {
        var accessVia = GetMainStashAccessVia(sessionId);
        return PttHelpers.CheckAccessVia(accessVia, offraidPosition);
    }

    private List<string> GetMainStashAccessVia(string sessionId)
    {
        var config = _getConfig(sessionId);
        return config.HideoutMainStashAccessViaList;
    }

    private StashConfig GetSecondaryStash(string offraidPosition, string sessionId)
    {
        return GetSecondaryStashConfigs(sessionId)
            .FirstOrDefault(s =>
                PttHelpers.CheckAccessVia(s.AccessVia, offraidPosition) &&
                (_isOffraidPositionUnlocked == null || _isOffraidPositionUnlocked(offraidPosition, sessionId)))
            ?? EMPTY_STASH;
    }

    private List<StashConfig> GetSecondaryStashConfigs(string sessionId)
        => _getConfig(sessionId).HideoutSecondaryStashesTyped
            .Select(ToStashConfig).ToList();

    private List<StashConfig> GetAllStashConfigs(string sessionId)
        => GetSecondaryStashConfigs(sessionId).Prepend(EMPTY_STASH).ToList();

    private void SetMainStash(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile profile)
    {
        var pttData   = GetPttData(profile);
        var mainStash = pttData.MainStashId ?? profile.CharacterData?.PmcData?.Inventory?.Stash?.ToString();
        if (profile.CharacterData?.PmcData?.Inventory != null && mainStash != null)
            profile.CharacterData.PmcData.Inventory.Stash = new MongoId(mainStash);
    }

    private void SetSecondaryStash(StashConfig stash,
        SPTarkov.Server.Core.Models.Eft.Profile.SptProfile profile)
    {
        var inv = profile.CharacterData?.PmcData?.Inventory;
        if (inv == null) return;

        var stashId    = new MongoId(stash.MongoId);
        var templateId = new MongoId(stash.MongoTemplateId);
        inv.Stash      = stashId;

        var items = inv.Items?.ToList() ?? new();
        if (!items.Any(i => i.Id == stashId))
        {
            items.Add(new Item { Id = stashId, Template = templateId });
            inv.Items = items;
        }
    }

    private static void FixInventorySlotIds(
        SPTarkov.Server.Core.Models.Eft.Common.PmcData pmc,
        string activeStashId,
        List<StashConfig> configs)
    {
        var allStashIds = configs.Select(s => s.MongoId)
            .Append(activeStashId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = pmc.Inventory?.Items?.ToList();
        if (items == null) return;

        foreach (var item in items)
        {
            if (item.SlotId != SLOT_HIDEOUT && item.SlotId != SLOT_LOCKED_STASH) continue;
            item.SlotId = item.ParentId == activeStashId ? SLOT_HIDEOUT : SLOT_LOCKED_STASH;
        }
    }

    private static string? RetrieveMainStashId(List<Item>? items, List<StashConfig> configs)
    {
        if (items == null) return null;
        var pttIds = configs.SelectMany(c => new[] { c.MongoId, c.MongoTemplateId })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items.FirstOrDefault(i =>
            VANILLA_STASH_IDS.Contains(i.Template.ToString()) &&
            !pttIds.Contains(i.Id.ToString()))?.Id.ToString();
    }

    // PTT data helpers (reuse same ExtensionData key as PttController)
    private static PttProfileData GetPttData(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile profile)
    {
        if (profile.ExtensionData?.TryGetValue("PathToTarkov", out var raw) == true)
        {
            if (raw is System.Text.Json.JsonElement el)
                return JsonSerializer.Deserialize<PttProfileData>(el.GetRawText()) ?? new();
            if (raw is PttProfileData d) return d;
        }
        return new PttProfileData();
    }

    private static void SavePttData(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile profile, PttProfileData data)
    {
        profile.ExtensionData ??= new Dictionary<string, object?>();
        profile.ExtensionData["PathToTarkov"] = data;
    }
}

public record StashConfig(
    string Name,
    int Size,
    List<string> AccessVia,
    string MongoId,
    string MongoTemplateId,
    string MongoGridId);

/// <summary>
/// Converts JSON null to empty string for TemplateItem properties with non-nullable setters.
/// </summary>
internal sealed class NullToEmptyStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? "" : reader.GetString() ?? "";

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
