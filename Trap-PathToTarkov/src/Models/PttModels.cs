using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PathToTarkov.Models;

// ============================================================
// Shared PTT model types
// ============================================================

// ---- Spawn config (shared_player_spawnpoints.json5) ----

public class SpawnPointPosition
{
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
    [JsonPropertyName("z")] public float Z { get; set; }
}

public class SpawnPointData
{
    [JsonPropertyName("Position")]
    [System.Text.Json.Serialization.JsonConverter(typeof(SpawnPositionConverter))]
    public SpawnPointPosition Position { get; set; } = new();
    [JsonPropertyName("Rotation")]  public float Rotation { get; set; }
}

/// <summary>mapName -> spawnId -> SpawnPointData</summary>
public class SpawnConfig : Dictionary<string, Dictionary<string, SpawnPointData>> { }

// ---- Exfils config (exfils_config.json5 per profile) ----

public class ExfilEntry
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>mapName -> exitName -> ExfilEntry</summary>
public class ExfilsConfig : Dictionary<string, Dictionary<string, ExfilEntry>> { }

// ---- Spawns config (spawns_config.json5 per profile) ----

public class SpawnEntry
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>mapName -> spawnName -> SpawnEntry</summary>
public class SpawnsConfig : Dictionary<string, Dictionary<string, SpawnEntry>> { }

// ---- User config ----

public class GameplayConfig
{
    [JsonPropertyName("multistash")]
    public bool Multistash { get; set; } = true;
    [JsonPropertyName("tradersAccessRestriction")]
    public bool TradersAccessRestriction { get; set; } = true;
    [JsonPropertyName("resetOffraidPositionOnPlayerDeath")]
    public bool ResetOffraidPositionOnPlayerDeath { get; set; } = true;
    [JsonPropertyName("playerScavMoveOffraidPosition")]
    public bool PlayerScavMoveOffraidPosition { get; set; } = false;
    [JsonPropertyName("keepFoundInRaidTweak")]
    public bool KeepFoundInRaidTweak { get; set; } = true;
}

public class UserConfig
{
    [JsonPropertyName("selectedConfig")]
    public string SelectedConfig { get; set; } = "Default";
    [JsonPropertyName("gameplay")]
    public GameplayConfig Gameplay { get; set; } = new();
    [JsonPropertyName("runUninstallProcedure")]
    public bool RunUninstallProcedure { get; set; } = false;
}
