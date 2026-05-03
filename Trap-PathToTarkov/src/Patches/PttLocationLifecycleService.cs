using System;
using System.Linq;
using PathToTarkov.Controllers;
using PathToTarkov.Models;
using PathToTarkov.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace PathToTarkov.Patches;

/// <summary>
/// Overrides LocationLifecycleService so we intercept StartLocalRaid / EndLocalRaid.
/// The [Injectable] attribute with typeOverride makes the DI container inject this
/// wherever LocationLifecycleService is requested.
/// </summary>
[Injectable(InjectionType.Singleton, typeof(LocationLifecycleService))]
public class PttLocationLifecycleService : LocationLifecycleService
{
    // Set by PttMod after the DI graph is wired
    public static PttController?    Controller;
    public static RaidCacheService? RaidCache;

    private readonly ISptLogger<PttLocationLifecycleService> _pttLogger;

    [System.Obsolete]
    public PttLocationLifecycleService(
        ISptLogger<LocationLifecycleService> logger,
        RewardHelper rewardHelper,
        ConfigServer configServer,
        TimeUtil timeUtil,
        DatabaseService databaseService,
        ProfileHelper profileHelper,
        BackupService backupService,
        ProfileActivityService profileActivityService,
        BotNameService botNameService,
        ICloner cloner,
        RaidTimeAdjustmentService raidTimeAdjustmentService,
        LocationLootGenerator locationLootGenerator,
        ServerLocalisationService serverLocalisationService,
        BotLootCacheService botLootCacheService,
        LootGenerator lootGenerator,
        MailSendService mailSendService,
        TraderHelper traderHelper,
        RandomUtil randomUtil,
        InRaidHelper inRaidHelper,
        PlayerScavGenerator playerScavGenerator,
        SaveServer saveServer,
        HealthHelper healthHelper,
        PmcChatResponseService pmcChatResponseService,
        PmcWaveGenerator pmcWaveGenerator,
        QuestHelper questHelper,
        InsuranceService insuranceService,
        MatchBotDetailsCacheService matchBotDetailsCacheService,
        BtrDeliveryService btrDeliveryService,
        ISptLogger<PttLocationLifecycleService> pttLogger)
        : base(logger, rewardHelper, configServer, timeUtil, databaseService,
               profileHelper, backupService, profileActivityService, botNameService,
               cloner, raidTimeAdjustmentService, locationLootGenerator,
               serverLocalisationService, botLootCacheService, lootGenerator,
               mailSendService, traderHelper, randomUtil, inRaidHelper,
               playerScavGenerator, saveServer, healthHelper, pmcChatResponseService,
               pmcWaveGenerator, questHelper, insuranceService,
               matchBotDetailsCacheService, btrDeliveryService)
    {
        _pttLogger = pttLogger;
    }

    public override StartLocalRaidResponseData StartLocalRaid(
        MongoId sessionId, StartLocalRaidRequestData request)
    {
        var result = base.StartLocalRaid(sessionId, request);
        var sid    = sessionId.ToString();

        if (Controller == null || RaidCache == null)
            return result;

        var existingCache = RaidCache.Get(sid);
        bool isTransit    = existingCache?.ExitStatus == "Transit";
        RaidCache.Init(sid, preserveTransit: isTransit);

        var cache = RaidCache.GetOrCreate(sid);
        cache.CurrentLocationName = request.Location;
        cache.IsPlayerScav        = request.PlayerSide == "Savage";

        // Record raid start for Fika co-raider detection (intel item group sharing)
        RaidCache.RecordRaidStart(sid, request.Location ?? "");

        if (result?.LocationLoot != null)
        {
            _pttLogger.Info($"[PTT] StartLocalRaid: syncing '{request.Location}' for session '{sid}'");
            Controller.SyncLocationBase(result.LocationLoot, sid, cache);
        }

        return result!;
    }

    public override void EndLocalRaid(MongoId sessionId, EndLocalRaidRequestData request)
    {
        var sid = sessionId.ToString();

        if (Controller != null && RaidCache != null)
        {
            var cache      = RaidCache.Get(sid);
            var exitName   = request.LocationTransit?.SptExitName ?? request.Results?.ExitName;
            var exitStatus = request.Results?.Result?.ToString() ?? "Unknown";

            if (cache != null)
            {
                cache.ExitStatus = exitStatus;
                cache.ExitName   = exitName;
            }

            ProcessEndOfRaid(sid, exitName, exitStatus, request, cache);
        }

        base.EndLocalRaid(sessionId, request);
    }

    private void ProcessEndOfRaid(string sessionId, string? exitName,
        string exitStatus, EndLocalRaidRequestData request, RaidCache? cache)
    {
        if (Controller == null) return;

        var isPlayerScav = cache?.IsPlayerScav ?? false;
        var userConfig   = Controller.GetUserConfig();

        if (isPlayerScav && !userConfig.Gameplay.PlayerScavMoveOffraidPosition)
        {
            _pttLogger.Debug($"[PTT] Scav raid ended — offraid position unchanged");
            return;
        }

        // Player died / MIA
        if (string.IsNullOrEmpty(exitName) ||
            exitStatus == "Killed" || exitStatus == "MissingInAction")
        {
            _pttLogger.Info($"[PTT] Player died — resetting offraid position");
            Controller.OnPlayerDies(sessionId);
            return;
        }

        // Vanilla or PTT transit
        if (exitStatus == "Transit")
        {
            var transitMap   = request.LocationTransit?.Location;
            var transitSpawn = request.LocationTransit?.SptExitName;
            if (cache != null)
            {
                cache.ExitStatus               = "Transit";
                cache.TransitTargetMapName     = transitMap;
                cache.TransitTargetSpawnPointId = transitSpawn;
            }
            _pttLogger.Info($"[PTT] Transit → '{transitMap}' via spawnpoint '{transitSpawn}'");
            return;
        }

        // Normal PTT extract — resolve offraid position from exfiltrations config
        var pttConfig = Controller.GetConfig(sessionId);
        var locName   = cache?.CurrentLocationName ?? "";
        var mapName   = PathToTarkov.Helpers.MapNameResolver.ResolveMapName(locName) ?? "";

        // Resolve scene alias names back to config names
        // e.g. "customs_secret_voron_bunker" -> "ZB-1012"
        var resolvedExitName = exitName!;
        foreach (var (configName, sceneName) in PathToTarkov.Helpers.LocationHelpers.SecretExitSceneAliases)
        {
            if (string.Equals(sceneName, exitName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedExitName = configName;
                _pttLogger.Info($"[PTT] Resolved secret exit scene name '{exitName}' -> config name '{configName}'");
                break;
            }
        }

        if (!string.IsNullOrEmpty(mapName) &&
            pttConfig.Exfiltrations.TryGetValue(mapName, out var mapExfils) &&
            mapExfils.TryGetValue(resolvedExitName, out var targetRaw))
        {
            var target = NormalizeExfilTarget(targetRaw);
            if (!string.IsNullOrEmpty(target))
            {
                // Check if the target offraid position is locked (intel item gating)
                if (!Controller.IsOffraidPositionUnlocked(target, sessionId))
                {
                    _pttLogger.Info($"[PTT] Extract '{exitName}' -> offraid '{target}' BLOCKED — missing required intel item. Offraid position unchanged.");
                    // Do not update offraid position — player stays where they were.
                    // They will respawn at their current offraid position next raid.
                    return;
                }

                Controller.OnPlayerExtracts(sessionId, target, isPlayerScav);
                _pttLogger.Info($"[PTT] Extract '{exitName}' -> offraid '{target}'");
                return;
            }
        }

        _pttLogger.Warning($"[PTT] Could not resolve offraid position for exit '{resolvedExitName}' (scene: '{exitName}') on map '{mapName}'");
    }

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
}
