using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PathToTarkov.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

namespace PathToTarkov.Patches;

/// <summary>
/// Overrides DataCallbacks to inject secondary stash size and hideout availability.
/// - GetTemplateItems: overrides the stash grid height for secondary stash positions
/// - GetHideoutAreas: disables hideout when not at main stash position
/// </summary>
[Injectable(InjectionType.Singleton, typeof(DataCallbacks))]
public class PttDataCallbacks : DataCallbacks
{
    public static PttController? Controller;

    public PttDataCallbacks(
        HttpResponseUtil httpResponseUtil,
        DatabaseService databaseService,
        TraderController traderController,
        HideoutController hideoutController,
        LocaleService localeService)
        : base(httpResponseUtil, databaseService, traderController, hideoutController, localeService)
    { }

    private static readonly string[] VANILLA_STASH_IDS =
    {
        "566abbc34bdc2d92178b4576", // Standard
        "5811ce572459770cba1a34ea", // Left Behind
        "5811ce662459770f6f490f32", // Prepare for escape
        "5811ce772459770e9e5f9532", // Edge of darkness
        "6602bcf19cc643f44a04274b", // Unheard
    };

    public override async ValueTask<string> GetTemplateItems(
        string url, EmptyRequestData _, MongoId sessionID)
    {
        var raw = await base.GetTemplateItems(url, _, sessionID);
        var sid = sessionID.ToString();

        if (Controller == null) return raw;

        var size = Controller.GetStashSize(sid);
        if (size == null) return raw; // main stash — no override

        try
        {
            var json  = JsonNode.Parse(raw)!;
            var items = json["data"]!.AsObject();

            foreach (var stashId in VANILLA_STASH_IDS)
            {
                if (items[stashId] is JsonObject item)
                {
                    var grids = item["_props"]?["Grids"]?.AsArray();
                    var grid  = grids?.Count > 0 ? grids[0]?.AsObject() : null;
                    var props = grid?["_props"]?.AsObject();
                    if (props != null)
                        props["cellsV"] = size.Value;
                }
            }

            return json.ToJsonString();
        }
        catch { return raw; }
    }

    public override async ValueTask<string> GetHideoutAreas(
        string url, EmptyRequestData _, MongoId sessionID)
    {
        var raw = await base.GetHideoutAreas(url, _, sessionID);
        var sid = sessionID.ToString();

        if (Controller == null) return raw;
        if (Controller.GetHideoutEnabled(sid)) return raw;

        try
        {
            var json  = JsonNode.Parse(raw)!;
            var areas = json["data"]!.AsArray();

            foreach (var area in areas)
            {
                var type = (int?)area?["type"];
                // Skip essential areas that would crash if disabled
                if (type is 4 or 6 or 10 or 16 or 17 or 21 or 27) continue;
                if (area?.AsObject() is { } obj)
                    obj["enabled"] = false;
            }

            return json.ToJsonString();
        }
        catch { return raw; }
    }
}
