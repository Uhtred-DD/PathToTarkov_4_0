using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PathToTarkov.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace PathToTarkov.Routers;

[Injectable]
public class PttStaticRouter : StaticRouter
{
    public static PttController? Controller;

    private static ISptLogger<PttStaticRouter>? _log;

    public PttStaticRouter(JsonUtil jsonUtil, HttpResponseUtil http, ISptLogger<PttStaticRouter> logger)
        : base(jsonUtil, BuildRoutes(http))
    {
        _log = logger;
    }

    private static IEnumerable<RouteAction> BuildRoutes(HttpResponseUtil http) =>
    [
        new RouteAction(
            "/PathToTarkov/Version",
            async (url, _, sessionId, output) => HandleVersion()),

        new RouteAction(
            "/PathToTarkov/CurrentLocationData",
            async (url, info, sessionId, output) =>
                HandleCurrentLocationData(http, info as CurrentLocationDataRequest, sessionId.ToString()),
            typeof(CurrentLocationDataRequest)),

        new RouteAction(
            "/PathToTarkov/GiveIntelItem",
            async (url, info, sessionId, output) =>
                HandleGiveIntelItem(http, info as GiveIntelItemRequest, sessionId.ToString()),
            typeof(GiveIntelItemRequest)),
    ];

    private static string HandleVersion()
    {
        return "{\"uninstalled\":false,\"fullVersion\":\"6.0.0\"}";
    }

    private static string HandleCurrentLocationData(HttpResponseUtil http, CurrentLocationDataRequest? req, string sessionId)
    {
        var exfilsTargets = new Dictionary<string, object>();
        var locationId = req?.LocationId;
        _log?.Info($"[PTT] CurrentLocationData locationId='{locationId}' session='{sessionId}'");

        if (Controller != null && locationId != null)
        {
            var config  = Controller.GetConfig(sessionId);
            var mapName = Helpers.MapNameResolver.ResolveMapName(locationId);

            if (mapName != null)
            {
                // Add all exits from PTT config with their targets
                if (config.Exfiltrations.TryGetValue(mapName, out var exfilMap))
                {
                    foreach (var (exitName, rawTargets) in exfilMap)
                    {
                        var targets = Helpers.PttHelpers.NormalizeAccessVia(rawTargets);

                        // Filter out exits whose target offraid position is locked
                        var allowedTargets = targets.Where(t =>
                        {
                            if (t.Contains('.')) return true; // transit — no offraid position check
                            return Controller.IsOffraidPositionUnlocked(t, sessionId);
                        }).ToList();

                        if (allowedTargets.Count == 0) continue; // all targets locked — hide exit

                        var exfilTargets = allowedTargets
                            .Select(t => ParseExfilTarget(exitName, t))
                            .Where(t => t != null)
                            .ToList();

                        if (exfilTargets.Count > 0)
                            exfilsTargets[exitName] = exfilTargets;
                    }
                }

                // Build set of exits that are locked by offraid_position_conditions
                // so the vanilla passthrough loop below doesn't re-add them.
                var lockedExitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (config.Exfiltrations.TryGetValue(mapName, out var exfilMapForLock))
                {
                    foreach (var (exitName, rawTargets) in exfilMapForLock)
                    {
                        var targets = Helpers.PttHelpers.NormalizeAccessVia(rawTargets);
                        bool allLocked = targets.Any() && targets.All(t =>
                            !t.Contains('.') && !Controller.IsOffraidPositionUnlocked(t, sessionId));
                        if (allLocked)
                            lockedExitNames.Add(exitName);
                    }
                }

                // Also include ALL exits from the map database so vanilla exits work
                // (exits not in PTT config get a passthrough entry so IsExfiltrationPointEnabled returns true)
                // Skip any exit that is locked by offraid_position_conditions.
                var allExtracts = GetAllExtractsForMap(locationId);
                foreach (var exitName in allExtracts)
                {
                    if (lockedExitNames.Contains(exitName)) continue; // locked — don't re-add
                    if (!exfilsTargets.ContainsKey(exitName))
                    {
                        // Empty target list = vanilla behavior, but exit is enabled
                        exfilsTargets[exitName] = new List<object>
                        {
                            new { exitName, isTransit = false, offraidPosition = (string?)null,
                                  transitMapId = "", transitSpawnPointId = "",
                                  nextMaps = Array.Empty<string>(), nextTraders = Array.Empty<string>() }
                        };
                    }
                }

                // Patch 0.16 secret exits use different scene names vs config/database names.
                // Add scene name aliases so IsExfiltrationPointEnabled returns true for them.
                AddSceneAlias(exfilsTargets, "customs_secret_voron_boat", "Smuggler's Boat");
                AddSceneAlias(exfilsTargets, "customs_secret_voron_bunker", "ZB-1012");
            }
        }

        var result = http.NoBody(new { exfilsTargets });
        _log?.Info($"[PTT] /CurrentLocationData response ({result.Length} chars): {result[..Math.Min(300, result.Length)]}...");
        return result;
    }

    /// <summary>
    /// Copies exfilsTargets entry from configName to sceneName so the client
    /// can identify the scene object by its PTT-tracked offraid position.
    /// </summary>
    private static void AddSceneAlias(Dictionary<string, object> exfilsTargets, string sceneName, string configName)
    {
        if (!exfilsTargets.ContainsKey(sceneName) && exfilsTargets.TryGetValue(configName, out var targets))
            exfilsTargets[sceneName] = targets;
        else if (!exfilsTargets.ContainsKey(sceneName))
            // Not in config either — add passthrough so it's at least enabled
            exfilsTargets[sceneName] = new List<object>
            {
                new { exitName = sceneName, isTransit = false, offraidPosition = (string?)null,
                      transitMapId = "", transitSpawnPointId = "",
                      nextMaps = Array.Empty<string>(), nextTraders = Array.Empty<string>() }
            };
    }

    private static List<string> GetAllExtractsForMap(string locationId)
    {
        try
        {
            var mapName = Helpers.MapNameResolver.ResolveMapName(locationId) ?? locationId;
            var folderName = mapName switch
            {
                "bigmap"         => "bigmap",
                "woods"          => "woods",
                "rezervbase"     => "rezervbase",
                "interchange"    => "interchange",
                "lighthouse"     => "lighthouse",
                "shoreline"      => "shoreline",
                "laboratory"     => "laboratory",
                "tarkovstreets"  => "tarkovstreets",
                "sandbox"        => "sandbox",
                "sandbox_high"   => "sandbox",
                "factory4_day"   => "factory4_day",
                "factory4_night" => "factory4_night",
                _                => mapName
            };

            var modDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var locDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(modDir, "..", "..", "..", "SPT_Data", "database", "locations", folderName));

            var names = new HashSet<string>();

            // Read allExtracts.json (scav + pmc exits)
            var allExtractsPath = System.IO.Path.Combine(locDir, "allExtracts.json");
            if (System.IO.File.Exists(allExtractsPath))
            {
                var extracts = System.Text.Json.JsonSerializer.Deserialize<List<ExtractEntry>>(System.IO.File.ReadAllText(allExtractsPath));
                foreach (var e in extracts ?? [])
                    if (e.Name != null) names.Add(e.Name);
            }

            // Also read base.json exits (catches Smuggler's Boat, ZB-1012, etc.)
            var basePath = System.IO.Path.Combine(locDir, "base.json");
            if (System.IO.File.Exists(basePath))
            {
                var baseJson = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(basePath));
                if (baseJson.RootElement.TryGetProperty("exits", out var exits))
                    foreach (var exit in exits.EnumerateArray())
                        if (exit.TryGetProperty("Name", out var n) && n.GetString() is string name)
                            names.Add(name);
            }

            _log?.Info($"[PTT] GetAllExtractsForMap({mapName}): {names.Count} total exits");
            return names.ToList();
        }
        catch (Exception ex)
        {
            _log?.Warning($"[PTT] Failed to read extracts for map: {ex.Message}");
            return new List<string>();
        }
    }

    private class ExtractEntry
    {
        [JsonPropertyName("Name")]
        public string? Name { get; set; }
    }

    private static object? ParseExfilTarget(string exitName, string raw)
    {
        var dotIdx = raw.IndexOf('.');
        if (dotIdx > 0)
        {
            var transitMapName = raw[..dotIdx];
            var spawnId        = raw[(dotIdx + 1)..];
            var locationId     = Helpers.MapNameResolver.ResolveLocationId(transitMapName) ?? transitMapName;
            return new
            {
                exitName, isTransit = true, offraidPosition = "",
                transitMapId = locationId, transitSpawnPointId = spawnId,
                nextMaps = Array.Empty<string>(), nextTraders = Array.Empty<string>(),
            };
        }
        return new
        {
            exitName, isTransit = false, offraidPosition = raw,
            transitMapId = "", transitSpawnPointId = "",
            nextMaps = Array.Empty<string>(), nextTraders = Array.Empty<string>(),
        };
    }

    public class CurrentLocationDataRequest : IRequestData
    {
        [JsonPropertyName("locationId")]
        public string? LocationId { get; set; }
    }

    public class GiveIntelItemRequest : IRequestData
    {
        [JsonPropertyName("itemConfigId")]
        public string? ItemConfigId { get; set; }
    }

    private static string HandleGiveIntelItem(HttpResponseUtil http, GiveIntelItemRequest? req, string sessionId)
    {
        if (Controller == null)
            return http.NoBody(new { success = false, error = "PTT not loaded" });

        var configId = req?.ItemConfigId;
        if (string.IsNullOrWhiteSpace(configId))
            return http.NoBody(new { success = false, error = "Missing itemConfigId" });

        var result = Controller.GiveIntelItem(sessionId, configId);
        _log?.Info($"[PTT] GiveIntelItem '{configId}' for '{sessionId}': {(result ? "given" : "already owned")}");
        return http.NoBody(new { success = true, given = result });
    }
}
