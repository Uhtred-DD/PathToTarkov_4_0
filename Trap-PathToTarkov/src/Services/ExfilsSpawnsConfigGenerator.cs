using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PathToTarkov.Models;

namespace PathToTarkov.Services;

/// <summary>
/// Generates and maintains exfils_config.json5 and spawns_config.json5
/// per profile folder. On first run these files are created from the SPT
/// database and shared spawn points. On subsequent runs any NEW exits or
/// spawn points added by a SPT update are appended (enabled by default).
/// </summary>
public static class ExfilsSpawnsConfigGenerator
{
    // Maps excluded from config generation (not real raid maps)
    private static readonly HashSet<string> IgnoredMaps = new(StringComparer.OrdinalIgnoreCase)
        { "develop", "town", "terminal" };

    // PTT map name -> SPT database folder name
    private static readonly Dictionary<string, string> MapFolderAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"]         = "bigmap",
        ["factory4_day"]   = "factory4_day",
        ["factory4_night"] = "factory4_night",
        ["interchange"]    = "interchange",
        ["laboratory"]     = "laboratory",
        ["labyrinth"]      = "labyrinth",
        ["lighthouse"]     = "lighthouse",
        ["rezervbase"]     = "rezervbase",
        ["sandbox"]        = "sandbox",
        ["sandbox_high"]   = "sandbox",    // inherits sandbox data
        ["shoreline"]      = "shoreline",
        ["tarkovstreets"]  = "tarkovstreets",
        ["woods"]          = "woods",
    };

    // Exits that should default to disabled — dangerous or non-functional
    private static readonly Dictionary<string, HashSet<string>> DefaultDisabledExits =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Custom_scav_pmc",       // Co-op only — BREAKS AI if enabled
            "customs_sniper_exit",   // Internal AI sniper exit — not a player exit
        },
        ["interchange"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Interchange Cooperation", // Co-op only — BREAKS AI if enabled
        },
        ["lighthouse"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "tunnel_shared", // Co-op only — BREAKS AI if enabled
        },
        ["rezervbase"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "EXFIL_ScavCooperation", // Co-op only — BREAKS AI if enabled
        },
        ["sandbox"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Scav_coop_exit", // Co-op only — BREAKS AI if enabled
        },
        ["sandbox_high"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Scav_coop_exit", // Co-op only — BREAKS AI if enabled
        },
        ["shoreline"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Smugglers_Trail_coop", // Co-op only — BREAKS AI if enabled
            "Rock Passage",         // chance=0% in database — never spawns
        },
        ["tarkovstreets"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Exit_E10_coop", // Co-op only — BREAKS AI if enabled
            "E6",            // chance=0% in database — never spawns
        },
        ["woods"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Factory Gate",      // Co-op only — BREAKS AI if enabled
            "wood_sniper_exit",  // Internal AI sniper exit — not a player exit
        },
    };

    // Per-exit notes for documentation in the generated file
    private static readonly Dictionary<string, Dictionary<string, string>> ExitNotes =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ZB-1011"]                    = "PMC | always open | IEApi: yes",
            ["ZB-1012"]                    = "PMC | light must be on (50% chance) | secret exit — no IEApi",
            ["EXFIL_ZB013"]                = "PMC | switch must be activated | IEApi: yes",
            ["Crossroads"]                 = "PMC | always open | IEApi: yes",
            ["RUAF Roadblock"]             = "PMC + Scav | always open | IEApi: yes",
            ["Trailer Park"]               = "PMC | always open | IEApi: yes",
            ["Smuggler's Boat"]            = "PMC | fire must be lit (50% chance) | secret exit — no IEApi",
            ["Dorms V-Ex"]                 = "PMC | car extract — 5000 roubles, 50% chance | IEApi: yes",
            ["Old Gas Station"]            = "PMC | green flare required, 50% chance | IEApi: yes",
            ["Custom_scav_pmc"]            = "Co-op (PMC + Scav required) | BREAKS AI if enabled",
            ["customs_sniper_exit"]        = "Internal AI sniper exit — not a player exit",
            ["Warehouse 17"]               = "Scav (promoted) | always open | IEApi: yes",
            ["Shack"]                      = "Scav (promoted) | always open | IEApi: yes",
            ["Beyond Fuel Tank"]           = "Scav (promoted) | always open | IEApi: yes",
            ["Railroad To Military Base"]  = "Scav (promoted) | always open | IEApi: yes",
            ["Old Road Gate"]              = "Scav (promoted) | always open | IEApi: yes",
            ["Sniper Roadblock"]           = "Scav (promoted) | always open | IEApi: yes",
            ["Railroad To Port"]           = "Scav (promoted) | always open | IEApi: yes",
            ["Trailer Park Workers Shack"] = "Scav (promoted) | always open | IEApi: yes",
            ["Railroad To Tarkov"]         = "Scav (promoted) | always open | IEApi: yes",
            ["RUAF Roadblock_scav"]        = "Scav (promoted) | always open | IEApi: yes",
            ["Factory Shacks"]             = "Scav (promoted) | always open | IEApi: yes",
            ["Warehouse 4"]                = "Scav (promoted) | always open | IEApi: yes",
            ["Old Azs Gate"]               = "Scav (promoted) | always open | IEApi: yes",
            ["Factory Far Corner"]         = "Scav (promoted) | always open | IEApi: yes",
            ["Administration Gate"]        = "Scav (promoted) | always open | IEApi: yes",
            ["Military Checkpoint"]        = "Scav (promoted) | always open | IEApi: yes",
        },
        ["factory4_day"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Gate 3"]             = "PMC + Scav | always open | IEApi: yes",
            ["Gate 0"]             = "PMC + Scav | always open | IEApi: yes",
            ["Gate m"]             = "PMC + Scav | always open | IEApi: yes",
            ["Gate_o"]             = "PMC + Scav | always open | IEApi: yes",
            ["Cellars"]            = "PMC + Scav | always open | IEApi: yes",
            ["Camera Bunker Door"] = "Scav (promoted) | always open | IEApi: yes",
            ["Office Window"]      = "Scav (promoted) | always open | IEApi: yes",
        },
        ["factory4_night"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Gate 3"]             = "PMC + Scav | always open | IEApi: yes",
            ["Gate 0"]             = "PMC + Scav | always open | IEApi: yes",
            ["Gate m"]             = "PMC + Scav | always open | IEApi: yes",
            ["Gate_o"]             = "PMC + Scav | always open | IEApi: yes",
            ["Cellars"]            = "PMC + Scav | always open | IEApi: yes",
            ["Camera Bunker Door"] = "Scav (promoted) | always open | IEApi: yes",
            ["Office Window"]      = "Scav (promoted) | always open | IEApi: yes",
        },
        ["interchange"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NW Exfil"]               = "PMC | always open | IEApi: yes",
            ["SE Exfil"]               = "PMC | always open | IEApi: yes",
            ["PP Exfil"]               = "PMC + Scav | car extract — 5000 roubles, 50% chance | IEApi: yes",
            ["Saferoom Exfil"]         = "PMC | power + flush urinal + Object 11SR keycard | IEApi: yes",
            ["Hole Exfill"]            = "PMC + Scav | no backpack required | IEApi: yes",
            ["Interchange Cooperation"]= "Co-op (PMC + Scav required) | BREAKS AI if enabled",
        },
        ["lighthouse"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nothern_Checkpoint"] = "PMC | always open | IEApi: yes",
            ["Coastal_South_Road"] = "PMC | always open | IEApi: yes",
            ["Shorl_free"]         = "PMC | always open | IEApi: yes",
            ["Alpinist_light"]     = "PMC | Red Rebel Ice Pick + Paracord + no armor vest | IEApi: yes",
            [" V-Ex_light"]        = "PMC | car extract — 5000 roubles, 50% chance | IEApi: yes",
            ["EXFIL_Train"]        = "PMC + Scav | armored train (arrives 25-35 min in) | IEApi: yes",
            ["tunnel_shared"]      = "Co-op (PMC + Scav required) | BREAKS AI if enabled",
            ["Shorl_free_scav"]            = "Scav (promoted) | always open | IEApi: yes",
            ["Scav_Coastal_South"]         = "Scav (promoted) | always open | IEApi: yes",
            ["Scav_Underboat_Hideout"]     = "Scav (promoted) | always open | IEApi: yes",
            ["Scav_Hideout_at_the_grotto"] = "Scav (promoted) | always open | IEApi: yes",
            ["Scav_Industrial_zone"]       = "Scav (promoted) | always open | IEApi: yes",
        },
        ["rezervbase"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Alpinist"]             = "PMC | Red Rebel Ice Pick + Paracord + no armor vest | IEApi: yes",
            ["EXFIL_Bunker"]         = "PMC + Scav | lever must be activated (4-min window) | IEApi: yes",
            ["EXFIL_Bunker_D2"]      = "PMC + Scav | power must be restored in command bunker | IEApi: yes",
            ["EXFIL_vent"]           = "PMC + Scav | no backpack required | IEApi: yes",
            ["EXFIL_Train"]          = "PMC + Scav | armored train (arrives 25-35 min in) | IEApi: yes",
            ["EXFIL_ScavCooperation"]= "Co-op (PMC + Scav required) | BREAKS AI if enabled",
            ["Exit1"]                = "Scav (promoted) | always open | IEApi: yes",
            ["Exit2"]                = "Scav (promoted) | always open | IEApi: yes",
            ["Exit3"]                = "Scav (promoted) | always open | IEApi: yes",
            ["Exit4"]                = "Scav (promoted) | always open | IEApi: yes",
        },
        ["shoreline"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Tunnel"]             = "PMC | always open | IEApi: yes",
            ["Pier Boat"]          = "PMC | always open, 50% chance | IEApi: yes",
            ["CCP Temporary"]      = "PMC | always open, 50% chance | IEApi: yes",
            ["Road to Customs"]    = "PMC + Scav | always open | IEApi: yes",
            ["Lighthouse_pass"]    = "PMC + Scav | always open | IEApi: yes",
            ["Road_at_railbridge"] = "PMC | always open | IEApi: yes",
            ["Shorl_V-Ex"]         = "PMC | car extract — 5000 roubles | IEApi: yes",
            ["RedRebel_alp"]       = "PMC | Red Rebel Ice Pick + Paracord + no armor vest | IEApi: yes",
            ["Smugglers_Trail_coop"]= "Co-op (PMC + Scav required) | BREAKS AI if enabled",
            ["Rock Passage"]       = "chance=0% in database — never spawns",
            ["Lighthouse"]         = "Scav (promoted) | always open | IEApi: yes",
            ["RWing Gym Entrance"]  = "Scav (promoted) | always open | IEApi: yes",
            ["Adm Basement"]       = "Scav (promoted) | always open | IEApi: yes",
            ["Scav Road to Customs"]= "Scav (promoted) | always open | IEApi: yes",
            ["Wrecked Road"]       = "Scav (promoted) | always open | IEApi: yes",
        },
        ["tarkovstreets"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["E1"]           = "PMC | always open | IEApi: yes",
            ["E2"]           = "PMC | always open | IEApi: yes",
            ["E3"]           = "PMC | always open | IEApi: yes",
            ["E4"]           = "PMC | always open | IEApi: yes",
            ["E5"]           = "PMC | always open | IEApi: yes",
            ["E6"]           = "chance=0% in database — never spawns",
            ["E7"]           = "PMC | always open | IEApi: yes",
            ["E7_car"]       = "PMC | car extract — 5000 roubles, 50% chance | IEApi: yes",
            ["E8"]           = "PMC | always open | IEApi: yes",
            ["E8_yard"]      = "PMC | 40% chance | IEApi: yes",
            ["E9_sniper"]    = "PMC | always open | IEApi: yes",
            ["Exit_E10_coop"]= "Co-op (PMC + Scav required) | BREAKS AI if enabled",
            ["scav_e2"]      = "Scav (promoted) | always open | IEApi: yes",
            ["scav_e3"]      = "Scav (promoted) | always open | IEApi: yes",
            ["scav_e4"]      = "Scav (promoted) | always open | IEApi: yes",
            ["scav_e5"]      = "Scav (promoted) | always open | IEApi: yes",
            ["scav_e7"]      = "Scav (promoted) | always open | IEApi: yes",
            ["scav_e8"]      = "Scav (promoted) | always open | IEApi: yes",
        },
        ["woods"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Outskirts"]      = "PMC + Scav | always open | IEApi: yes",
            ["UN Roadblock"]   = "PMC + Scav | always open | IEApi: yes",
            ["RUAF Gate"]      = "PMC | 66% chance | IEApi: yes",
            ["ZB-016"]         = "PMC | green flare required, 66% chance | IEApi: yes",
            ["ZB-014"]         = "PMC | 66% chance | IEApi: yes",
            ["South V-Ex"]     = "PMC | car extract — 3000 roubles, 50% chance | IEApi: yes",
            ["Factory Gate"]   = "Co-op (PMC + Scav required) | BREAKS AI if enabled",
            ["un-sec"]         = "PMC | always open | IEApi: yes",
            ["wood_sniper_exit"]= "Internal AI sniper exit — not a player exit",
            ["East Gate"]      = "Scav (promoted) | always open | IEApi: yes",
            ["Mountain Stash"] = "Scav (promoted) | always open | IEApi: yes",
            ["Dead Man's Place"]= "Scav (promoted) | always open | IEApi: yes",
            ["The Boat"]       = "Scav (promoted) | always open | IEApi: yes",
            ["Scav House"]     = "Scav (promoted) | always open | IEApi: yes",
            ["Old Station"]    = "Scav (promoted) | always open | IEApi: yes",
            ["West Border"]    = "Scav (promoted) | always open | IEApi: yes",
            ["RUAF Roadblock"] = "Scav (promoted) | always open | IEApi: yes",
        },
        ["laboratory"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["lab_Parking_Gate"]           = "PMC | switch must be activated, 60% chance | IEApi: yes",
            ["lab_Hangar_Gate"]            = "PMC | switch must be activated, 60% chance | IEApi: yes",
            ["lab_Elevator_Med"]           = "PMC | switch must be activated | IEApi: yes",
            ["lab_Under_Storage_Collector"]= "PMC | switch must be activated | IEApi: yes",
            ["lab_Elevator_Main"]          = "PMC | switch must be activated | IEApi: yes",
            ["lab_Vent"]                   = "PMC | no backpack required | IEApi: yes",
            ["lab_Elevator_Cargo"]         = "PMC | switch must be activated | IEApi: yes",
        },
        ["sandbox"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Unity_free_exit"]          = "PMC + Scav | always open | IEApi: yes",
            ["Nakatani_stairs_free_exit"] = "PMC + Scav | always open | IEApi: yes",
            ["Sniper_exit"]              = "PMC + Scav | always open | IEApi: yes",
            ["Sandbox_VExit"]            = "PMC + Scav | car extract — 5000 roubles | IEApi: yes",
            ["Scav_coop_exit"]           = "Co-op (PMC + Scav required) | BREAKS AI if enabled",
        },
        ["sandbox_high"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Unity_free_exit"]          = "PMC + Scav | always open | IEApi: yes",
            ["Nakatani_stairs_free_exit"] = "PMC + Scav | always open | IEApi: yes",
            ["Sniper_exit"]              = "PMC + Scav | always open | IEApi: yes",
            ["Sandbox_VExit"]            = "PMC + Scav | car extract — 5000 roubles | IEApi: yes",
            ["Scav_coop_exit"]           = "Co-op (PMC + Scav required) | BREAKS AI if enabled",
        },
        ["labyrinth"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["labir_exit"] = "Only exit | time-limited (open for first 15 min) | IEApi: unknown",
        },
    };

    // Per-spawn-point notes for documentation
    private static readonly Dictionary<string, Dictionary<string, string>> SpawnNotes =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Military Base CP"]           = "Near Scav checkpoint / military base road",
            ["ZB-1011"]                    = "ZB-1011 bunker entrance",
            ["ZB-1012"]                    = "ZB-1012 bunker area",
            ["ZB-1013"]                    = "ZB-1013 bunker area",
            ["Crossroads"]                 = "West side crossroads",
            ["Trailer Park"]               = "Trailer park area",
            ["Trailer Park Workers Shack"] = "Near workers shack",
            ["RUAF Roadblock"]             = "RUAF checkpoint",
            ["Smugglers Boat"]             = "River dock / smuggler area",
            ["Sniper Roadblock"]           = "Northeast sniper road",
            ["Factory Far Corner"]         = "Far east factory corner",
            ["Old Gas Scav"]               = "Old gas station scav area",
            ["RR to Military Base"]        = "Railroad north to military base",
            ["RR to Port"]                 = "Railroad to port",
            ["RR to Tarkov"]               = "Railroad south to Tarkov",
            ["Warehouse 17"]               = "Warehouse 17 area (Skier hideout)",
            ["Dorms Car"]                  = "Dorms vehicle extract area",
            ["Scav CP"]                    = "Scav checkpoint east",
            ["Administration Gate"]        = "Admin gate east",
        },
        ["factory4_day"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Gate 3"]             = "Main PMC gate",
            ["Courtyard"]          = "Central courtyard",
            ["Gate 0"]             = "West gate",
            ["Med tent gates"]     = "Medical area gates",
            ["Transit to Customs"] = "Transit spawn from Customs",
            ["Transit to Labs"]    = "Transit spawn from Labs",
            ["Camera Bunker Door"] = "Camera / bunker area",
            ["Cellars"]            = "Underground cellars",
        },
        ["factory4_night"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Gate 3"]             = "Main PMC gate",
            ["Courtyard"]          = "Central courtyard",
            ["Gate 0"]             = "West gate",
            ["Med tent gates"]     = "Medical area gates",
            ["Transit to Customs"] = "Transit spawn from Customs",
            ["Transit to Labs"]    = "Transit spawn from Labs",
            ["Camera Bunker Door"] = "Camera / bunker area",
            ["Cellars"]            = "Underground cellars",
        },
        ["woods"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Northern UN Roadblock"] = "North UN checkpoint",
            ["Factory Gate"]          = "Factory gate south",
            ["RUAF Gate"]             = "RUAF military gate",
            ["ZB-014"]                = "ZB-014 bunker area",
            ["ZB-016"]                = "ZB-016 bunker area",
            ["Sawmill River"]         = "Sawmill riverside",
            ["Outskirts"]             = "Outskirts area southeast",
            ["The Boat"]              = "The boat area",
            ["Mountain Stash"]        = "Jaeger mountain stash",
            ["Sniper Rock Bunker"]    = "High ground rock bunker",
            ["UN Roadblock"]          = "UN roadblock south",
            ["Scav Bridge"]           = "Scav bridge area",
            ["Woods Vehicle Extract"] = "Vehicle extract area",
            ["Old Station"]           = "Old sawmill station",
            ["Scav Bunker"]           = "Scav bunker south",
        },
        ["rezervbase"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Scav lands"]      = "Scav lands southwest",
            ["Scav lands rail"] = "Scav lands rail siding",
            ["Cliff"]           = "Cliff descent north (Red Rebel)",
            ["Checkpoint Fence"]= "CP fence exit4",
            ["Bunker Hermetic"] = "Bunker hermetic door",
            ["Depot Hermetic"]  = "Depot hermetic door",
            ["Heating Pipe"]    = "Heating pipe scav area",
            ["Hole In Wall"]    = "Hole in the wall exit",
            ["Reserve Manhole"] = "Sewer manhole (no backpack)",
            ["Train Station"]   = "Armored train station",
            ["D-2"]             = "D-2 bunker (power restore required)",
        },
        ["interchange"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Interchange Vehicle Extract"] = "Car extract (5000 roubles)",
            ["Fence Gap"]                   = "Hole in fence (no backpack)",
            ["Railway"]                     = "NW railway exit",
            ["Emercom"]                     = "Emercom checkpoint",
            ["Scav Camp"]                   = "Co-op scav camp",
            ["FromCrossroads"]              = "Spawn from Customs crossroads transit",
            ["Safe Room"]                   = "Saferoom (keycard required)",
        },
        ["shoreline"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["North Fence Passage"]     = "North dorms car exit area",
            ["Shoreline Vehicle Extract"]= "Car extract",
            ["Old Bunker"]              = "Old bunker area",
            ["Climbers Trail"]          = "Climbers trail path",
            ["Road to Customs"]         = "North road to Customs",
            ["Pier Boat"]               = "Pier boat exit",
            ["Shoreline Tunnel"]        = "Tunnel to Lighthouse",
            ["Ruined Road"]             = "Ruined road scav exit",
            ["Admin Basement"]          = "Admin building basement",
            ["CCP Temporary"]           = "CCP temporary checkpoint",
            ["Shoreline Northern Cliffs"]= "Northern cliffs",
            ["Railway Bridge"]          = "Railway bridge exit",
            ["Path to Lighthouse"]      = "Path to Lighthouse",
            ["Smugglers Path"]          = "Smugglers trail co-op area",
        },
        ["lighthouse"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Path to Shoreline"]          = "Path to Shoreline",
            ["Lighthouse Vehicle Extract"] = "Car extract",
            ["Industrial Gates"]           = "Industrial zone gates",
            ["Lighthouse Tunnel"]          = "Tunnel to Shoreline",
            ["Armored Train LH"]           = "Armored train",
            ["Northeast Mountains"]        = "Northeast mountain area",
            ["Southern Road Water"]        = "South road waterfront",
            ["Grotto"]                     = "Grotto area",
            ["Northern CP"]                = "Northern checkpoint",
            ["Lighthouse Docks Boat"]      = "Docks boat area",
            ["Mountain Pass"]              = "Mountain pass (Red Rebel)",
            ["Southern Road"]              = "Southern road exit",
        },
        ["tarkovstreets"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Streets Vehicle Extract"]       = "Car extract E7_car",
            ["Basement Descent"]              = "Basement entrance",
            ["Catacombs"]                     = "Underground catacombs",
            ["Evacuation Zone"]               = "Evacuation zone",
            ["Klimov Street"]                 = "Klimov street exit",
            ["Sewer River"]                   = "Sewer river area",
            ["Streets Manhole"]               = "Manhole cover",
            ["Streets Ruined House"]          = "Ruined house E3",
            ["Streets Vents"]                 = "Ventilation shaft",
            ["Underpass"]                     = "Underpass tunnel",
            ["Zmeevsky Alley"]                = "Zmeevsky alley",
            ["Crane"]                         = "Crane area",
            ["Expo Checkpoint"]               = "Expo checkpoint",
            ["Cardinal Appartments Parking"]  = "Cardinal apartments parking",
            ["Cardinal Appartments"]          = "Cardinal apartments",
            ["Crash Site"]                    = "Crash site E4",
            ["Kamchatskaya Arch"]             = "Kamchatskaya arch",
            ["Stylobate Elevator"]            = "Stylobate elevator",
        },
        ["laboratory"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cargo Elevator"]    = "Cargo elevator shaft",
            ["Hangar Gate"]       = "Hangar gate",
            ["Lab Sewage Conduit"]= "Sewage conduit",
            ["Lab Vents"]         = "Ventilation shaft (no backpack)",
            ["Main Elevator"]     = "Main elevator",
            ["Med Block Elevator"]= "Med block elevator",
            ["Parking Gate"]      = "Parking gate",
        },
        ["sandbox"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nakatani Basement Stairs"] = "Nakatani stairs free exit",
            ["Mira Ave"]                 = "Mira Avenue / sniper exit area",
            ["Police Car"]               = "Police car vehicle extract",
            ["Scav Hideout"]             = "Scav hideout co-op area",
            ["EmercomGZ"]                = "Emercom checkpoint Ground Zero",
        },
    };

    // ---- Public API ----

    /// <summary>
    /// Ensures exfils_config.json5 and spawns_config.json5 exist in configDir.
    /// Creates them from the database on first run; on subsequent runs adds
    /// any new exits/spawns introduced by SPT updates (enabled by default).
    /// Returns the loaded configs.
    /// </summary>
    public static (ExfilsConfig exfils, SpawnsConfig spawns) EnsureConfigs(
        string configDir,
        string sptDataDir,
        SpawnConfig sharedSpawnConfig,
        Action<string> log)
    {
        try
        {
            log($"[PTT] EnsureConfigs: configDir={configDir}, sptDataDir={sptDataDir}");
            var exfilsPath = Path.Combine(configDir, "exfils_config.json5");
            var spawnsPath = Path.Combine(configDir, "spawns_config.json5");

            var exfils = LoadOrCreate<ExfilsConfig>(exfilsPath);
            var spawns = LoadOrCreate<SpawnsConfig>(spawnsPath);

            bool exfilsDirty = MergeExfilsFromDatabase(exfils, sptDataDir, log);
            bool spawnsDirty = MergeSpawnsFromSharedConfig(spawns, sharedSpawnConfig, log);

            if (exfilsDirty) WriteJson5(exfilsPath, SerializeExfilsConfig(exfils), log);
            if (spawnsDirty) WriteJson5(spawnsPath, SerializeSpawnsConfig(spawns), log);

            if (!exfilsDirty && !spawnsDirty)
                log($"[PTT] ExfilsConfig and SpawnsConfig are up to date — no changes needed");

            log($"[PTT] ExfilsConfig: {exfils.Sum(m => m.Value.Count)} exits across {exfils.Count} maps");
            log($"[PTT] SpawnsConfig: {spawns.Sum(m => m.Value.Count)} spawn points across {spawns.Count} maps");

            return (exfils, spawns);
        }
        catch (Exception ex)
        {
            log($"[PTT] ERROR in EnsureConfigs: {ex.Message}\n{ex.StackTrace}");
            return (new ExfilsConfig(), new SpawnsConfig());
        }
    }

    private static T LoadOrCreate<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            var raw  = File.ReadAllText(path, Encoding.UTF8);
            var json = Json5Converter.ToJson(raw);
            return JsonSerializer.Deserialize<T>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true })
                ?? new T();
        }
        catch (Exception ex)
        {
            // Log but continue — treat as empty config
            Console.WriteLine($"[PTT] Warning: could not load {path}: {ex.Message}");
            return new T();
        }
    }

    private static void WriteJson5(string path, string content, Action<string> log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Encoding.UTF8);
            log($"[PTT] Generated: {path}");
        }
        catch (Exception ex) { log($"[PTT] ERROR writing {path}: {ex.Message}\n{ex.StackTrace}"); }
    }

    // ---- Exfils merge ----

    private static bool MergeExfilsFromDatabase(ExfilsConfig exfils, string sptDataDir, Action<string> log)
    {
        bool dirty = false;
        var locDir = Path.Combine(sptDataDir, "database", "locations");

        foreach (var (mapName, folderName) in MapFolderAliases)
        {
            if (IgnoredMaps.Contains(mapName)) continue;
            var mapDir = Path.Combine(locDir, folderName);
            if (!Directory.Exists(mapDir)) continue;

            var exits = CollectAllExits(mapDir);
            if (exits.Count == 0) continue;

            if (!exfils.TryGetValue(mapName, out var mapExfils))
            {
                exfils[mapName] = mapExfils = new Dictionary<string, ExfilEntry>(StringComparer.OrdinalIgnoreCase);
                dirty = true;
            }

            foreach (var exitName in exits)
            {
                if (mapExfils.ContainsKey(exitName)) continue;
                var disabled = DefaultDisabledExits.TryGetValue(mapName, out var dis) && dis.Contains(exitName);
                var note     = ExitNotes.TryGetValue(mapName, out var notes) && notes.TryGetValue(exitName, out var n) ? n : null;
                mapExfils[exitName] = new ExfilEntry { Enabled = !disabled, Notes = note };
                dirty = true;
                log($"[PTT] ExfilsConfig: added '{exitName}' on {mapName} (enabled={!disabled})");
            }
        }
        return dirty;
    }

    private static HashSet<string> CollectAllExits(string mapDir)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // base.json exits
        var basePath = Path.Combine(mapDir, "base.json");
        if (File.Exists(basePath))
        {
            try
            {
                var doc = JsonDocument.Parse(File.ReadAllText(basePath));
                if (doc.RootElement.TryGetProperty("exits", out var exits))
                    foreach (var exit in exits.EnumerateArray())
                        if (exit.TryGetProperty("Name", out var n) && n.GetString() is string s && s.Length > 0)
                            names.Add(s);
            }
            catch { /* ignore malformed */ }
        }

        // allExtracts.json — scav exits
        var allPath = Path.Combine(mapDir, "allExtracts.json");
        if (File.Exists(allPath))
        {
            try
            {
                var arr = JsonDocument.Parse(File.ReadAllText(allPath));
                foreach (var item in arr.RootElement.EnumerateArray())
                    if (item.TryGetProperty("Name", out var n) && n.GetString() is string s && s.Length > 0)
                        names.Add(s);
            }
            catch { /* ignore */ }
        }

        return names;
    }

    // ---- Spawns merge ----

    private static bool MergeSpawnsFromSharedConfig(SpawnsConfig spawns, SpawnConfig sharedSpawnConfig, Action<string> log)
    {
        bool dirty = false;

        foreach (var (mapName, mapSpawnPoints) in sharedSpawnConfig)
        {
            if (IgnoredMaps.Contains(mapName)) continue;

            if (!spawns.TryGetValue(mapName, out var mapSpawns))
            {
                spawns[mapName] = mapSpawns = new Dictionary<string, SpawnEntry>(StringComparer.OrdinalIgnoreCase);
                dirty = true;
            }

            foreach (var spawnName in mapSpawnPoints.Keys)
            {
                if (mapSpawns.ContainsKey(spawnName)) continue;
                var note = SpawnNotes.TryGetValue(mapName, out var notes) && notes.TryGetValue(spawnName, out var n) ? n : null;
                mapSpawns[spawnName] = new SpawnEntry { Enabled = true, Notes = note };
                dirty = true;
                log($"[PTT] SpawnsConfig: added '{spawnName}' on {mapName}");
            }
        }
        return dirty;
    }

    // ---- JSON5 serialization with inline comments ----

    private static string SerializeExfilsConfig(ExfilsConfig exfils)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// exfils_config.json5 — Per-exit enable/disable control for Path To Tarkov");
        sb.AppendLine("// Auto-generated from SPT database. New exits are added automatically on server start.");
        sb.AppendLine("// enabled: true  = exit is active this raid");
        sb.AppendLine("// enabled: false = exit is completely removed from the game this raid");
        sb.AppendLine("//");
        sb.AppendLine("// IEApi = whether the Interactable Exfils API F-key prompt works for this exit");
        sb.AppendLine("{");

        var maps = exfils.Keys.OrderBy(k => k).ToList();
        for (int mi = 0; mi < maps.Count; mi++)
        {
            var mapName  = maps[mi];
            var mapExfils = exfils[mapName];
            sb.AppendLine();
            sb.AppendLine($"  // {'═'.ToString().PadRight(60, '═')}");
            sb.AppendLine($"  // {mapName}");
            sb.AppendLine($"  // {'═'.ToString().PadRight(60, '═')}");
            sb.AppendLine($"  '{EscapeJson5Key(mapName)}': {{");

            var exits = mapExfils.Keys.OrderBy(k => k).ToList();
            for (int ei = 0; ei < exits.Count; ei++)
            {
                var exitName = exits[ei];
                var entry    = mapExfils[exitName];
                var comma    = ei < exits.Count - 1 ? "," : "";
                var note     = entry.Notes != null ? $" // {entry.Notes}" : "";
                var enabled  = entry.Enabled ? "true " : "false";
                sb.AppendLine($"    '{EscapeJson5Key(exitName)}': {{ enabled: {enabled} }}{comma}{note}");
            }

            var mapComma = mi < maps.Count - 1 ? "," : "";
            sb.AppendLine($"  }}{mapComma}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string SerializeSpawnsConfig(SpawnsConfig spawns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// spawns_config.json5 — Per-spawn-point enable/disable control for Path To Tarkov");
        sb.AppendLine("// Auto-generated from shared_player_spawnpoints.json5. New spawn points are added automatically.");
        sb.AppendLine("// enabled: true  = spawn point is available for use");
        sb.AppendLine("// enabled: false = spawn point is removed from this profile");
        sb.AppendLine("{");

        var maps = spawns.Keys.OrderBy(k => k).ToList();
        for (int mi = 0; mi < maps.Count; mi++)
        {
            var mapName  = maps[mi];
            var mapSpawns = spawns[mapName];
            sb.AppendLine();
            sb.AppendLine($"  // {'═'.ToString().PadRight(60, '═')}");
            sb.AppendLine($"  // {mapName}");
            sb.AppendLine($"  // {'═'.ToString().PadRight(60, '═')}");
            sb.AppendLine($"  '{EscapeJson5Key(mapName)}': {{");

            var points = mapSpawns.Keys.OrderBy(k => k).ToList();
            for (int pi = 0; pi < points.Count; pi++)
            {
                var spawnName = points[pi];
                var entry     = mapSpawns[spawnName];
                var comma     = pi < points.Count - 1 ? "," : "";
                var note      = entry.Notes != null ? $" // {entry.Notes}" : "";
                var enabled   = entry.Enabled ? "true " : "false";
                sb.AppendLine($"    '{EscapeJson5Key(spawnName)}': {{ enabled: {enabled} }}{comma}{note}");
            }

            var mapComma = mi < maps.Count - 1 ? "," : "";
            sb.AppendLine($"  }}{mapComma}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EscapeJson5Key(string key)
        => key.Replace("'", "\\'");
}
