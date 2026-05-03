using System;
using System.Linq;
using PathToTarkov.Controllers;
using PathToTarkov.Helpers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Location;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace PathToTarkov.Patches;

/// <summary>
/// Overrides LocationController.GenerateAll to apply per-session map locking.
/// Called by /client/locations — the map select screen.
/// </summary>
[Injectable(InjectionType.Singleton, typeof(LocationController))]
public class PttLocationController : LocationController
{
    public static PttController? Controller;

    private readonly ISptLogger<PttLocationController> _pttLogger;

    public PttLocationController(
        ISptLogger<LocationController> logger,
        DatabaseService databaseService,
        AirdropService airdropService,
        ISptLogger<PttLocationController> pttLogger)
        : base(logger, databaseService, airdropService)
    {
        _pttLogger = pttLogger;
    }

    public override LocationsGenerateAllResponse GenerateAll(MongoId sessionId)
    {
        var result = base.GenerateAll(sessionId);
        var sid    = sessionId.ToString();

        if (Controller == null || result?.Locations == null)
            return result!;

        // Ensure traders are locked/unlocked to match current offraid position on every map screen load
        Controller.InitPlayer(sid);

        var config     = Controller.GetConfig(sid);
        var offraidPos = Controller.GetOffraidPosition(sid);

        if (!config.Infiltrations.TryGetValue(offraidPos, out var unlockedMaps))
        {
            _pttLogger.Warning($"[PTT] GenerateAll: no infiltrations for offraid '{offraidPos}'");
            return result;
        }

        var unlocked = unlockedMaps.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int lockedCount = 0, unlockedCount = 0;

        // Locations is Dictionary<MongoId, LocationBase>
        foreach (var lb in result.Locations.Values)
        {
            if (lb == null) continue;

            // Resolve PTT map name from the location's _Id field
            var mapName = MapNameResolver.ResolveMapName(lb.Id ?? lb.Name ?? "");
            if (mapName == null) continue;

            bool isLocked = !unlocked.Contains(mapName);
            lb.Locked  = isLocked;
            lb.Enabled = !isLocked;

            if (isLocked) lockedCount++; else unlockedCount++;
        }

        _pttLogger.Info($"[PTT] GenerateAll: {unlockedCount} unlocked, {lockedCount} locked for '{offraidPos}'");
        return result;
    }
}
