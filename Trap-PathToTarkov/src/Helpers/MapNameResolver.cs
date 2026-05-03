using System.Collections.Generic;

namespace PathToTarkov.Helpers;

/// <summary>
/// Maps LocationBase.Id values (folder names like "bigmap", "Woods") to PTT config map keys.
/// Also maps hex location IDs used in transit targets.
/// </summary>
public static class MapNameResolver
{
    // LocationBase.Id (case-insensitive) -> PTT mapName key
    private static readonly Dictionary<string, string> _idToMap =
        new(System.StringComparer.OrdinalIgnoreCase)
    {
        // Folder-name IDs (what LocationBase.Id actually contains)
        ["bigmap"]         = "bigmap",
        ["Woods"]          = "woods",
        ["Shoreline"]      = "shoreline",
        ["Interchange"]    = "interchange",
        ["RezervBase"]     = "rezervbase",
        ["Lighthouse"]     = "lighthouse",
        ["TarkovStreets"]  = "tarkovstreets",
        ["Sandbox"]        = "sandbox",
        ["Sandbox_high"]   = "sandbox_high",
        ["laboratory"]     = "laboratory",
        ["factory4_day"]   = "factory4_day",
        ["factory4_night"] = "factory4_night",
        ["suburbs"]        = "suburbs",
        ["terminal"]       = "terminal",
        ["town"]           = "town",
        ["labyrinth"]      = "labyrinth",

        // Hex IDs used in transit targets and LocationTransit.Location
        ["56f40101d2720b2a4d8b45d6"] = "bigmap",
        ["5704e3c2d2720bac5b8b4567"] = "woods",
        ["5704e4dad2720bb55b8b4567"] = "shoreline",
        ["5714dbc024597771384a510d"] = "interchange",
        ["5704e5fad2720bc05b8b4567"] = "rezervbase",
        ["5f0ed1382e57b9006113a431"] = "lighthouse",
        ["5714dc692459777137212e12"] = "laboratory",
        ["5704e554d2720bac5b8b456e"] = "tarkovstreets",
        ["653e6760052c01c1c805532f"] = "sandbox",
        ["6660268a7f2f1e90b8058560"] = "sandbox_high",
        ["55f2d3fd4bdc2d5f408b4567"] = "factory4_day",
        ["59fc81d786f774390775787e"] = "factory4_night",
    };

    private static readonly Dictionary<string, string> _mapToHexId =
        new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"]         = "56f40101d2720b2a4d8b45d6",
        ["woods"]          = "5704e3c2d2720bac5b8b4567",
        ["shoreline"]      = "5704e4dad2720bb55b8b4567",
        ["interchange"]    = "5714dbc024597771384a510d",
        ["rezervbase"]     = "5704e5fad2720bc05b8b4567",
        ["lighthouse"]     = "5f0ed1382e57b9006113a431",
        ["laboratory"]     = "5714dc692459777137212e12",
        ["tarkovstreets"]  = "5704e554d2720bac5b8b456e",
        ["sandbox"]        = "653e6760052c01c1c805532f",
        ["sandbox_high"]   = "6660268a7f2f1e90b8058560",
        ["factory4_day"]   = "55f2d3fd4bdc2d5f408b4567",
        ["factory4_night"] = "59fc81d786f774390775787e",
    };

    public static string? ResolveMapName(string locationId)
    {
        _idToMap.TryGetValue(locationId, out var name);
        return name;
    }

    public static string? ResolveLocationId(string mapName)
    {
        _mapToHexId.TryGetValue(mapName, out var id);
        return id;
    }

    public static bool IsLocationAvailable(string? scenePath, string? sceneRcid)
        => !string.IsNullOrEmpty(scenePath) && !string.IsNullOrEmpty(sceneRcid);
}

public record LocationAvailabilityCheck(bool HasScene);
