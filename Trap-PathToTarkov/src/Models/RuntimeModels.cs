using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PathToTarkov.Models;

/// <summary>
/// Persisted per-profile PTT state stored inside the SPT profile's
/// extra data dictionary under the key "PathToTarkov".
/// </summary>
public class PttProfileData
{
    [JsonPropertyName("offraidPosition")]
    public string? OffraidPosition { get; set; }

    [JsonPropertyName("mainStashId")]
    public string? MainStashId { get; set; }
}

/// <summary>
/// Cache per session for one raid lifecycle.
/// </summary>
public class RaidCache
{
    public string? SessionId { get; set; }
    public string? CurrentLocationName { get; set; }
    public string? ExitName { get; set; }
    public string? TargetOffraidPosition { get; set; }
    public string? TransitTargetMapName { get; set; }
    public string? TransitTargetSpawnPointId { get; set; }
    public bool IsPlayerScav { get; set; }
    public string? ExitStatus { get; set; } // "Survived","Killed","Transit", etc.
}
