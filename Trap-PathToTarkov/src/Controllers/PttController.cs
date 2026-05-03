using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PathToTarkov.Helpers;
using PathToTarkov.Models;
using PathToTarkov.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;

namespace PathToTarkov.Controllers;

public class PttController
{
    private readonly PttConfig _config;
    private readonly SpawnConfig _spawnConfig;
    private readonly UserConfig _userConfig;
    private readonly ExfilsConfig _exfilsConfig;
    private readonly SpawnsConfig _spawnsConfig;
    private readonly SaveServer _saveServer;
    private readonly Action<string> _log;
    private readonly Action<string> _logWarn;
    private readonly Action<string> _logDebug;

    private readonly TradersAvailabilityService _tradersAvailability;
    private readonly FirTweakService _firTweak;
    private readonly TradersController _tradersController;
    private readonly StashController _stashController;
    private readonly DatabaseService _db;
    private readonly LocaleService _localeService;
    private readonly CustomItemService _customItemService;
    private readonly ItemFilterService _itemFilterService;
    private RaidCacheService? _raidCache;

    private readonly Dictionary<string, PttConfig> _configCache = new();

    public const string PTT_PROFILE_KEY = "PathToTarkov";

    public PttController(
        PttConfig config,
        SpawnConfig spawnConfig,
        UserConfig userConfig,
        ExfilsConfig exfilsConfig,
        SpawnsConfig spawnsConfig,
        SaveServer saveServer,
        DatabaseService db,
        LocaleService localeService,
        CustomItemService customItemService,
        ItemFilterService itemFilterService,
        Action<string>? log = null,
        Action<string>? logWarn = null,
        Action<string>? logDebug = null)
    {
        _config            = config;
        _spawnConfig       = spawnConfig;
        _userConfig        = userConfig;
        _exfilsConfig      = exfilsConfig;
        _spawnsConfig      = spawnsConfig;
        _saveServer        = saveServer;
        _db                = db;
        _localeService     = localeService;
        _customItemService = customItemService;
        _itemFilterService = itemFilterService;
        _log         = log      ?? (_ => {});
        _logWarn     = logWarn  ?? (_ => {});
        _logDebug    = logDebug ?? (_ => {});

        _tradersAvailability = new TradersAvailabilityService();
        _firTweak            = new FirTweakService(saveServer);
        _tradersController   = new TradersController(
            _tradersAvailability, userConfig, db, saveServer,
            msg => _log(msg), msg => _logWarn(msg),
            (pos, sid) => IsOffraidPositionUnlocked(pos, sid));
        _stashController     = new StashController(
            GetConfig, userConfig, db, saveServer,
            msg => _log(msg),
            (pos, sid) => IsOffraidPositionUnlocked(pos, sid));
    }

    /// <summary>Called from Mod.cs after RaidCacheService is created.</summary>
    public void SetRaidCache(RaidCacheService raidCache)
        => _raidCache = raidCache;

    /// <summary>Called after database is imported. Initialises quest-based trader availability.</summary>
    public void OnLoaded()
    {
        var quests = _db.GetTables()?.Templates?.Quests;
        if (quests != null)
            _tradersAvailability.Init(quests);

        _tradersController.InitTraders(_config);
        _stashController.InitSecondaryStashTemplates(_config.HideoutSecondaryStashes.Select(SecondaryStashConfig.FromRaw).ToList());
        GameplayMutations.ApplyRestrictionsInRaid(_config, _db);
        GameplayMutations.DisableRunThrough(_db);

        // Create and register PTT intel items (offraid position conditions)
        var intelService = new IntelItemService(
            _customItemService, _db, _localeService, _itemFilterService,
            msg => _log(msg),
            msg => _logWarn(msg));
        intelService.InitIntelItems(_config);

        _log($"[PTT] OnLoaded: traders initialised, stash templates added, restrictions applied, intel items created");
    }

    // ---- Config access ----

    public PttConfig GetConfig(string sessionId)
    {
        if (_configCache.TryGetValue(sessionId, out var cached))
            return cached;

        // Apply override_by_profiles if present
        var profile = _saveServer.GetProfile(new MongoId(sessionId));
        var edition = profile?.ProfileInfo?.Edition ?? "";

        if (_config.OverrideByProfiles != null &&
            _config.OverrideByProfiles.TryGetValue(edition, out var overrideProfile))
        {
            var cloned = CloneConfig(_config);
            if (overrideProfile.InitialOffraidPosition != null)
                cloned.InitialOffraidPosition = overrideProfile.InitialOffraidPosition;
            if (overrideProfile.RespawnAt != null)
                cloned.RespawnAt = overrideProfile.RespawnAt;
            _configCache[sessionId] = cloned;
            return cloned;
        }

        _configCache[sessionId] = _config;
        return _config;
    }

    private static PttConfig CloneConfig(PttConfig src)
    {
        var json = JsonSerializer.Serialize(src);
        return JsonSerializer.Deserialize<PttConfig>(json) ?? new PttConfig();
    }

    public UserConfig GetUserConfig() => _userConfig;

    public int? GetStashSize(string sessionId)
        => _stashController.GetStashSize(GetOffraidPosition(sessionId), sessionId);

    public bool GetHideoutEnabled(string sessionId)
        => _stashController.GetHideoutEnabled(GetOffraidPosition(sessionId), sessionId);

    // ---- Offraid position ----

    public string GetOffraidPosition(string sessionId)
    {
        var config  = GetConfig(sessionId);
        var profile = _saveServer.GetProfile(new MongoId(sessionId));
        var pttData = GetPttData(profile);

        if (string.IsNullOrEmpty(pttData.OffraidPosition))
            pttData.OffraidPosition = GetInitialOffraidPosition(sessionId);

        if (!config.Infiltrations.ContainsKey(pttData.OffraidPosition!))
        {
            var initial = GetInitialOffraidPosition(sessionId);
            _logDebug($"[PTT] Unknown offraid position '{pttData.OffraidPosition}', resetting to '{initial}'");
            pttData.OffraidPosition = initial;
        }

        SavePttData(profile, pttData);
        return pttData.OffraidPosition!;
    }

    private string GetInitialOffraidPosition(string sessionId)
    {
        var config  = GetConfig(sessionId);
        var profile = _saveServer.GetProfile(new MongoId(sessionId));
        var edition = profile?.ProfileInfo?.Edition ?? "";

        if (config.OverrideByProfiles != null &&
            config.OverrideByProfiles.TryGetValue(edition, out var ov) &&
            ov.InitialOffraidPosition != null)
            return ov.InitialOffraidPosition;

        return config.InitialOffraidPosition;
    }

    public void UpdateOffraidPosition(string sessionId, string offraidPosition)
    {
        var profile = _saveServer.GetProfile(new MongoId(sessionId));
        var pttData = GetPttData(profile);
        var prev    = pttData.OffraidPosition;

        pttData.OffraidPosition = offraidPosition;
        SavePttData(profile, pttData);

        if (prev != offraidPosition)
            _log($"[PTT] Player '{sessionId}' offraid position -> '{offraidPosition}'");

        _tradersController.UpdateTraders(GetConfig(sessionId), offraidPosition, sessionId);
        _stashController.UpdateStash(offraidPosition, sessionId);
        _ = _saveServer.SaveProfileAsync(new MongoId(sessionId));
    }

    public void OnPlayerDies(string sessionId)
    {
        if (!_userConfig.Gameplay.ResetOffraidPositionOnPlayerDeath) return;
        var config   = GetConfig(sessionId);
        var respawnAt = config.RespawnAt;
        var pos = respawnAt.Count > 0
            ? respawnAt[new Random().Next(respawnAt.Count)]
            : config.InitialOffraidPosition;
        UpdateOffraidPosition(sessionId, pos);
    }

    public void OnPlayerExtracts(string sessionId, string newOffraidPosition, bool isPlayerScav)
    {
        if (_userConfig.Gameplay.KeepFoundInRaidTweak)
        {
            int n = _firTweak.SetFoundInRaidOnEquipment(sessionId, isPlayerScav);
            _logDebug($"[PTT] FIR tweak: {n} items marked SpawnedInSession");
        }
        UpdateOffraidPosition(sessionId, newOffraidPosition);
    }

    /// <summary>
    /// Adds the intel item for the given config ID directly to the player's stash at runtime.
    /// Safe to call any time after OnLoaded() — template is already registered.
    /// Returns true if item was given, false if player already has it.
    /// </summary>
    public bool GiveIntelItem(string sessionId, string configItemId)
    {
        // Already owns it?
        if (HasItemInStash(sessionId, configItemId))
            return false;

        var tplId   = IntelItemService.GetMongoId(configItemId);
        var profile = _saveServer.GetProfile(new MongoId(sessionId));
        var pmc     = profile?.CharacterData?.PmcData;
        if (pmc?.Inventory == null) return false;

        var stashId = pmc.Inventory.Stash?.ToString();
        if (stashId == null) return false;

        // Generate a unique instance ID (random 24-char hex)
        var instanceId = Guid.NewGuid().ToString("N")[..24];

        pmc.Inventory.Items ??= new List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item>();
        pmc.Inventory.Items.Add(new SPTarkov.Server.Core.Models.Eft.Common.Tables.Item
        {
            Id       = new MongoId(instanceId),
            Template = new MongoId(tplId),
            ParentId = new MongoId(stashId),
            SlotId   = "hideout",
        });

        _ = _saveServer.SaveProfileAsync(new MongoId(sessionId));
        _log($"[PTT] Gave intel item '{configItemId}' (tpl={tplId}) to '{sessionId}'");
        return true;
    }

    // ---- Offraid position conditions (intel item gating) ----

    /// <summary>
    /// Returns true if the given offraid position is accessible to the player.
    /// Checks own stash first, then co-raiders' stashes (Fika group sharing).
    /// If no condition is configured for the position, always returns true.
    /// </summary>
    public bool IsOffraidPositionUnlocked(string offraidPosition, string sessionId)
    {
        var config = GetConfig(sessionId);
        if (config.OffRaidPositionConditions == null ||
            !config.OffRaidPositionConditions.TryGetValue(offraidPosition, out var requiredItemId) ||
            string.IsNullOrEmpty(requiredItemId))
            return true; // no condition = always unlocked

        _log($"[PTT] Checking unlock: '{offraidPosition}' requires '{requiredItemId}'");

        // Check own stash
        if (HasItemInStash(sessionId, requiredItemId))
        {
            _log($"[PTT] '{offraidPosition}' UNLOCKED — item found in stash");
            return true;
        }

        // Check co-raiders (Fika group members on same map, same time window)
        if (_raidCache != null)
        {
            foreach (var coRaiderSessionId in _raidCache.GetCoRaiderSessionIds(sessionId))
            {
                if (HasItemInStash(coRaiderSessionId, requiredItemId))
                {
                    _logDebug($"[PTT] '{offraidPosition}' unlocked for '{sessionId}' via co-raider '{coRaiderSessionId}'");
                    return true;
                }
            }
        }

        _logDebug($"[PTT] '{offraidPosition}' locked for '{sessionId}' — missing item '{requiredItemId}'");
        return false;
    }

    /// <summary>
    /// Returns true if the player's PMC inventory OR any secondary stash contains
    /// an item with the given template ID.
    /// </summary>
    public bool HasItemInStash(string sessionId, string templateId)
    {
        try
        {
            var tplMongoId = new MongoId(IntelItemService.GetMongoId(templateId));
            var profile = _saveServer.GetProfile(new MongoId(sessionId));

            if (profile == null)
            {
                _log($"[PTT] HasItemInStash: profile null for session '{sessionId}'");
                return false;
            }

            var inv = profile?.CharacterData?.PmcData?.Inventory;
            if (inv == null) return false;

            var items = inv.Items;
            if (items == null) return false;

            // Only check main stash and its direct/indirect children.
            // This excludes equipped gear, backpack, and pockets — the item
            // must be physically stored in the hideout stash, not carried.
            var stashRootId = inv.Stash?.ToString();
            if (string.IsNullOrEmpty(stashRootId))
            {
                _log($"[PTT] HasItemInStash: no stash root found for session '{sessionId}'");
                return false;
            }

            // Build set of all item IDs that are children of the stash root
            var stashChildren = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(stashRootId);

            while (queue.Count > 0)
            {
                var parentId = queue.Dequeue();
                foreach (var item in items.Where(i => i.ParentId == parentId))
                {
                    var childId = item.Id.ToString();
                    stashChildren.Add(childId);
                    queue.Enqueue(childId);
                }
            }

            // Check if any stash child has our TPL
            var found = items.Any(i => stashChildren.Contains(i.Id.ToString()) && i.Template == tplMongoId);
            _log($"[PTT] HasItemInStash: session='{sessionId}' stashChildren={stashChildren.Count} found={found}");
            return found;
        }
        catch (Exception ex)
        {
            _logWarn($"[PTT] HasItemInStash exception: {ex.Message}");
            return false;
        }
    }

    // ---- Profile PTT data ----

    private static PttProfileData GetPttData(SptProfile profile)
    {
        if (profile.ExtensionData != null &&
            profile.ExtensionData.TryGetValue(PTT_PROFILE_KEY, out var raw))
        {
            if (raw is JsonElement el)
                return JsonSerializer.Deserialize<PttProfileData>(el.GetRawText()) ?? new PttProfileData();
            if (raw is PttProfileData d) return d;
        }
        return new PttProfileData();
    }

    private static void SavePttData(SptProfile profile, PttProfileData data)
    {
        profile.ExtensionData ??= new Dictionary<string, object?>();
        profile.ExtensionData[PTT_PROFILE_KEY] = data;
    }

    // ---- LocationBase mutation (called from Harmony patch before raid starts) ----

    public void SyncLocationBase(LocationBase locationBase, string sessionId, RaidCache? raidCache)
    {
        if (!IsLocationAvailable(locationBase)) return;

        if (raidCache?.ExitStatus == "Transit" && raidCache?.TransitTargetMapName != null)
        {
            // Player used a vanilla transit — set PTT infiltration on all existing player spawns
            UpdateInfiltrationForPlayerSpawnPoints(locationBase);
        }
        else if (raidCache?.TransitTargetMapName != null && raidCache?.TransitTargetSpawnPointId != null)
        {
            // Player used a PTT transit exit
            UpdateSpawnPointsForTransit(locationBase, sessionId,
                raidCache.TransitTargetMapName, raidCache.TransitTargetSpawnPointId);
        }
        else
        {
            // Normal PTT extract or fresh start
            UpdateSpawnPoints(locationBase, sessionId);
        }

        UpdateLocationExits(locationBase, sessionId);
        UpdateLocationTransits(locationBase, sessionId);
    }

    private static bool IsLocationAvailable(LocationBase lb)
        => !string.IsNullOrEmpty(lb.Scene?.Path) && !string.IsNullOrEmpty(lb.Scene?.Rcid);

    private void UpdateSpawnPoints(LocationBase locationBase, string sessionId)
    {
        var mapName = MapNameResolver.ResolveMapName(locationBase.Id ?? "");
        if (mapName == null) return;

        var config        = GetConfig(sessionId);
        var offraidPos    = GetOffraidPosition(sessionId);
        var infiltrations = config.Infiltrations;

        if (!infiltrations.TryGetValue(offraidPos, out var mapSpawns)) return;
        if (!mapSpawns.TryGetValue(mapName, out var spawnIds) || spawnIds.Count == 0) return;
        if (spawnIds[0] == "*") return; // wildcard — use all existing spawns

        // Gate: if the player's current offraid position requires an intel item, check it
        if (!IsOffraidPositionUnlocked(offraidPos, sessionId))
        {
            _logDebug($"[PTT] Spawns locked on '{mapName}' — offraid position '{offraidPos}' requires intel item");
            return;
        }

        // Apply spawns_config.json5 — filter out disabled spawn points
        if (_spawnsConfig.TryGetValue(mapName, out var spawnEntries))
            spawnIds = spawnIds.Where(id =>
                !spawnEntries.TryGetValue(id, out var entry) || entry.Enabled).ToList();

        if (spawnIds.Count == 0) return; // all disabled — fall through to vanilla spawns

        // Remove all player categories from existing spawnpoints
        var existing = (locationBase.SpawnPointParams ?? Enumerable.Empty<SpawnPointParam>()).ToList();
        var cleaned  = existing
            .Select(sp => new SpawnPointParam
            {
                Id = sp.Id, Position = sp.Position, Rotation = sp.Rotation,
                Sides = sp.Sides, Categories = sp.Categories?.Where(c => c != "Player").ToList(),
                Infiltration = sp.Infiltration, DelayToCanSpawnSec = sp.DelayToCanSpawnSec,
                CorePointId = sp.CorePointId, BotZoneName = sp.BotZoneName,
                ColliderParams = sp.ColliderParams,
            })
            .Where(sp => sp.Categories != null && sp.Categories.Any())
            .ToList();

        // Add PTT spawnpoints
        foreach (var spawnId in spawnIds)
        {
            if (_spawnConfig.TryGetValue(mapName, out var mapSpawnData) &&
                mapSpawnData.TryGetValue(spawnId, out var spawnData))
            {
                cleaned.Add(LocationHelpers.BuildSpawnPoint(spawnData, spawnId));
                _logDebug($"[PTT] Added spawn '{spawnId}' on {mapName}");
            }
        }

        locationBase.SpawnPointParams = cleaned;
        // Ensure all player spawn points use PTT_INFILTRATION so they match exits
        UpdateInfiltrationForPlayerSpawnPoints(locationBase);
    }

    private void UpdateSpawnPointsForTransit(LocationBase locationBase, string sessionId,
        string transitMapName, string transitSpawnId)
    {
        var mapName = MapNameResolver.ResolveMapName(locationBase.Id ?? "");
        if (mapName != transitMapName) return;

        if (_spawnConfig.TryGetValue(mapName, out var mapSpawnData) &&
            mapSpawnData.TryGetValue(transitSpawnId, out var spawnData))
        {
            var existing = (locationBase.SpawnPointParams ?? Enumerable.Empty<SpawnPointParam>())
                .Select(sp => new SpawnPointParam
                {
                    Id = sp.Id, Position = sp.Position, Rotation = sp.Rotation,
                    Sides = sp.Sides, Categories = sp.Categories?.Where(c => c != "Player").ToList(),
                    Infiltration = sp.Infiltration, DelayToCanSpawnSec = sp.DelayToCanSpawnSec,
                    CorePointId = sp.CorePointId, BotZoneName = sp.BotZoneName,
                    ColliderParams = sp.ColliderParams,
                })
                .Where(sp => sp.Categories != null && sp.Categories.Any())
                .ToList();

            existing.Add(LocationHelpers.BuildSpawnPoint(spawnData, transitSpawnId));
            locationBase.SpawnPointParams = existing;
            UpdateInfiltrationForPlayerSpawnPoints(locationBase);
            _logDebug($"[PTT] Transit spawn '{transitSpawnId}' added on {mapName}");
        }
    }

    private static void UpdateInfiltrationForPlayerSpawnPoints(LocationBase locationBase)
    {
        var spawns = (locationBase.SpawnPointParams ?? Enumerable.Empty<SpawnPointParam>()).ToList();
        foreach (var sp in spawns)
            if (sp.Categories?.Contains("Player") == true)
                sp.Infiltration = PttHelpers.PTT_INFILTRATION;
        locationBase.SpawnPointParams = spawns;
    }

    private void UpdateLocationExits(LocationBase locationBase, string sessionId)
    {
        var config = GetConfig(sessionId);
        if (config.BypassExfilsOverride) return;

        var mapName = MapNameResolver.ResolveMapName(locationBase.Id ?? "");
        if (mapName == null) return;

        if (!config.Exfiltrations.TryGetValue(mapName, out var exfilMap) || exfilMap.Count == 0)
        {
            _logWarn($"[PTT] No exfils configured for map '{mapName}'");
            return;
        }

        var extractNames = exfilMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Apply exfils_config.json5 — remove exits disabled by the user
        if (_exfilsConfig.TryGetValue(mapName, out var exfilEntries))
        {
            var disabled = exfilEntries
                .Where(kv => !kv.Value.Enabled)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (disabled.Count > 0)
            {
                extractNames.ExceptWith(disabled);
                _logDebug($"[PTT] ExfilsConfig: removed {disabled.Count} disabled exits on {mapName}");
            }
        }
        var indexedExits = (locationBase.Exits ?? Enumerable.Empty<Exit>())
            .Where(e => e.Name != null)
            .ToDictionary(e => e.Name!, e => e, StringComparer.OrdinalIgnoreCase);

        // Filter exits by offraid_position_conditions —
        // remove exits whose target offraid position requires an intel item the player doesn't have
        var filteredNames = extractNames.Where(exitName =>
        {
            if (!exfilMap.TryGetValue(exitName, out var targetRaw)) return true;
            var target = NormalizeExfilTarget(targetRaw);
            if (string.IsNullOrEmpty(target)) return true;
            // Transit notation (mapName.spawnId) has no offraid position — always allow
            if (target.Contains('.')) return true;
            var unlocked = IsOffraidPositionUnlocked(target, sessionId);
            if (!unlocked)
                _log($"[PTT] Exit '{exitName}' BLOCKED on {mapName} — offraid '{target}' requires intel item");
            return unlocked;
        }).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var builtExits = filteredNames
            .Select(name => LocationHelpers.BuildExitPoint(name, indexedExits.GetValueOrDefault(name)))
            .ToList();

        // Add scene-name-only aliases for secret exits whose Unity scene object names differ
        // from their config names. Only add alias if the config name passed the filter above.
        foreach (var (configName, sceneName) in LocationHelpers.SecretExitSceneAliases)
        {
            if (filteredNames.Contains(configName))
            {
                var original = indexedExits.GetValueOrDefault(sceneName)
                               ?? indexedExits.GetValueOrDefault(configName);
                builtExits.Add(LocationHelpers.BuildExitPoint(sceneName, original));
                _logDebug($"[PTT] Added secret exit scene alias '{sceneName}' for '{configName}'");
            }
        }

        locationBase.Exits = builtExits;
        _logDebug($"[PTT] Rebuilt {builtExits.Count} exits on {mapName}");
    }

    private void UpdateLocationTransits(LocationBase locationBase, string sessionId)
    {
        var config = GetConfig(sessionId);
        if (config.EnableAllVanillaTransits) return;

        var transits = (locationBase.Transits ?? Enumerable.Empty<Transit>()).ToList();
        foreach (var transit in transits)
            transit.IsActive = false;
        locationBase.Transits = transits;
    }

    /// <summary>
    /// Extracts the first string target from an exfiltration config value.
    /// Handles both plain string and array-of-strings JSON representations.
    /// </summary>
    private static string? NormalizeExfilTarget(object raw)
    {
        if (raw is System.Text.Json.JsonElement el)
        {
            if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                return el.GetString();
            if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var first = el.EnumerateArray().FirstOrDefault();
                return first.ValueKind == System.Text.Json.JsonValueKind.String
                    ? first.GetString() : null;
            }
        }
        return raw?.ToString();
    }

    // ---- Map locking (applied to all location bases at load time) ----

    public void ApplyMapLocks(
        SPTarkov.Server.Core.Models.Spt.Server.Locations locations,
        string sessionId)
    {
        var config     = GetConfig(sessionId);
        var offraidPos = GetOffraidPosition(sessionId);
        var unlockedMaps = config.Infiltrations.TryGetValue(offraidPos, out var maps)
            ? maps.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();

        foreach (var lb in LocationHelpers.GetAllLocationBases(locations))
        {
            var mapName = MapNameResolver.ResolveMapName(lb.Id ?? "");
            if (mapName == null) continue;

            var locked = !unlockedMaps.Contains(mapName);
            lb.Locked  = locked;
            lb.Enabled = !locked;

            if (!locked)
                SyncLocationBase(lb, sessionId, null);
        }
    }

    // ---- Init player (on game start or profile creation) ----

    public void InitPlayer(string sessionId)
    {
        _stashController.InitProfile(sessionId);
        var offraidPos = GetOffraidPosition(sessionId);
        UpdateOffraidPosition(sessionId, offraidPos);
    }
}
