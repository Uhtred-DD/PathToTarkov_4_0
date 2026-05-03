using System.Collections.Generic;
using System.Linq;
using PathToTarkov.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Servers;

namespace PathToTarkov.Services;

/// <summary>Port of keep-fir-tweak.ts</summary>
public class FirTweakService
{
    private readonly SaveServer _saveServer;
    public FirTweakService(SaveServer saveServer) => _saveServer = saveServer;

    public int SetFoundInRaidOnEquipment(string sessionId, bool isPlayerScav)
    {
        var profile  = _saveServer.GetProfile(new MongoId(sessionId));
        var charData = isPlayerScav
            ? profile?.CharacterData?.ScavData
            : profile?.CharacterData?.PmcData;

        var items   = charData?.Inventory?.Items?.ToList() ?? new List<Item>();
        // Equipment is Nullable<MongoId> — a nullable value-type struct
        var equip   = charData?.Inventory?.Equipment;
        var equipId = equip.HasValue ? equip.Value.ToString() : "";
        if (string.IsNullOrEmpty(equipId)) return 0;

        var equipped = GetContainedItems(items, equipId);
        int count    = MarkSpawnedInSession(equipped);
        _ = _saveServer.SaveProfileAsync(new MongoId(sessionId));
        return count;
    }

    private static List<Item> GetContainedItems(List<Item> all, string parentId)
    {
        var result = new List<Item>();
        foreach (var item in all.Where(i => i.ParentId == parentId))
        {
            result.Add(item);
            // Item.Id is MongoId (non-nullable struct) — call ToString() directly
            result.AddRange(GetContainedItems(all, item.Id.ToString()));
        }
        return result;
    }

    private static int MarkSpawnedInSession(List<Item> items)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item.Upd == null)
            { item.Upd = new Upd { SpawnedInSession = true }; count++; }
            else if (item.Upd.SpawnedInSession != true)
            { item.Upd.SpawnedInSession = true; count++; }
        }
        return count;
    }
}
