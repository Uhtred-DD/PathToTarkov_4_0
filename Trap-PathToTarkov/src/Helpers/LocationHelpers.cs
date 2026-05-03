using System;
using System.Collections.Generic;
using PathToTarkov.Models;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace PathToTarkov.Helpers;

public static class LocationHelpers
{
    private const double DEFAULT_EXFIL_TIME = 30.0;

    /// <summary>
    /// Maps PTT config exit names to their Unity scene object names for SecretExfiltrationPoint
    /// exits whose scene name differs from the config/database name.
    /// EFT initialises these exits from locationBase.exits by scene name — adding both names
    /// with Chance=100, PassageRequirement=None lets EFT activate them without the Voron note.
    /// These are intentionally NOT touched by InitAllExfiltrationPointsPatch (doing so breaks AI).
    /// Key = config name (used in exfiltrations config), Value = Unity scene object name.
    /// </summary>
    public static readonly Dictionary<string, string> SecretExitSceneAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["ZB-1012"]         = "customs_secret_voron_bunker",
        ["Smuggler's Boat"] = "customs_secret_voron_boat",
    };

    public static Exit BuildExitPoint(string name, Exit? original)
    {
        var passageReq = RequirementState.None;
        ExfiltrationType? exfilType = ExfiltrationType.Individual;
        string reqTip  = "";
        string id      = "";
        int    count   = 0;
        double minTime = 0.0;
        double maxTime = 0.0;
        double exfilTime = DEFAULT_EXFIL_TIME;

        if (original != null &&
            (original.PassageRequirement == RequirementState.WorldEvent ||
             original.PassageRequirement == RequirementState.Train))
        {
            passageReq = original.PassageRequirement;
            exfilType  = original.ExfiltrationType ?? exfilType;
            reqTip     = original.RequirementTip   ?? reqTip;
            id         = original.Id               ?? id;
            count      = original.Count            ?? count;
            minTime    = original.MinTime          ?? minTime;
            maxTime    = original.MaxTime          ?? maxTime;
        }

        return new Exit
        {
            Name               = name,
            EntryPoints        = PttHelpers.PTT_INFILTRATION,
            Id                 = id,
            Chance             = 100,
            ChancePVE          = 100,
            Count              = count,
            CountPVE           = count,
            MinTime            = minTime,
            MinTimePVE         = minTime,
            MaxTime            = maxTime,
            MaxTimePVE         = maxTime,
            ExfiltrationTime   = exfilTime,
            ExfiltrationTimePVE= exfilTime,
            PlayersCount       = 0,
            PlayersCountPVE    = 0,
            ExfiltrationType   = exfilType,
            PassageRequirement = passageReq,
            RequirementTip     = reqTip,
            EventAvailable     = false,
        };
    }

    public static SpawnPointParam BuildSpawnPoint(SpawnPointData data, string spawnId)
    {
        return new SpawnPointParam
        {
            Id                 = spawnId,
            Position           = new XYZ { X = data.Position.X, Y = data.Position.Y, Z = data.Position.Z },
            Rotation           = data.Rotation,
            Sides              = new List<string> { "All" },
            Categories         = new List<string> { "Player" },
            Infiltration       = PttHelpers.PTT_INFILTRATION,
            DelayToCanSpawnSec = 3,
            CorePointId        = 0,
            BotZoneName        = "",
            ColliderParams     = new ColliderParams
            {
                Parent     = "SpawnSphereParams",
                Properties = new ColliderProperties
                {
                    Center = new XYZ { X = 0, Y = 0, Z = 0 },
                    Radius = 0f,
                },
            },
        };
    }

    public static IEnumerable<LocationBase> GetAllLocationBases(
        SPTarkov.Server.Core.Models.Spt.Server.Locations locs)
    {
        if (locs.Bigmap?.Base        != null) yield return locs.Bigmap.Base;
        if (locs.Factory4Day?.Base   != null) yield return locs.Factory4Day.Base;
        if (locs.Factory4Night?.Base != null) yield return locs.Factory4Night.Base;
        if (locs.Interchange?.Base   != null) yield return locs.Interchange.Base;
        if (locs.Shoreline?.Base     != null) yield return locs.Shoreline.Base;
        if (locs.Woods?.Base         != null) yield return locs.Woods.Base;
        if (locs.RezervBase?.Base    != null) yield return locs.RezervBase.Base;
        if (locs.Lighthouse?.Base    != null) yield return locs.Lighthouse.Base;
        if (locs.TarkovStreets?.Base != null) yield return locs.TarkovStreets.Base;
        if (locs.Laboratory?.Base    != null) yield return locs.Laboratory.Base;
        if (locs.Sandbox?.Base       != null) yield return locs.Sandbox.Base;
        if (locs.SandboxHigh?.Base   != null) yield return locs.SandboxHigh.Base;
        if (locs.Labyrinth?.Base     != null) yield return locs.Labyrinth.Base;
    }
}
