using System;
using System.Linq;
using PathToTarkov.Models;
using SPTarkov.Server.Core.Services;

namespace PathToTarkov.Services;

public static class GameplayMutations
{
    /// <summary>Port of helpers.ts changeRestrictionsInRaid</summary>
    public static void ApplyRestrictionsInRaid(PttConfig config, DatabaseService db)
    {
        var restrictions = db.GetTables()?.Globals?.Configuration?.RestrictionsInRaid;
        if (restrictions == null) return;

        foreach (var r in restrictions)
        {
            var id = r.TemplateId.ToString();
            if (config.RestrictionsInRaid.TryGetValue(id, out var val))
            {
                r.MaxInRaid  = val.Value;
                r.MaxInLobby = Math.Max(val.Value, r.MaxInLobby);
            }
        }
    }

    /// <summary>Port of helpers.ts disableRunThrough</summary>
    public static void DisableRunThrough(DatabaseService db)
    {
        var matchEnd = db.GetTables()?.Globals?.Configuration?.Exp?.MatchEnd;
        if (matchEnd == null) return;
        matchEnd.SurvivedExperienceRequirement = 0;
        matchEnd.SurvivedSecondsRequirement    = 0;
    }
}
