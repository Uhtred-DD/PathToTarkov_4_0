using System;
using System.Collections.Generic;
using PathToTarkov.Helpers;
using PathToTarkov.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace PathToTarkov.Services;

public class TradersController
{
    private readonly TradersAvailabilityService _availability;
    private readonly UserConfig                 _userConfig;
    private readonly DatabaseService            _db;
    private readonly SaveServer                 _saveServer;
    private readonly Action<string>             _log;
    private readonly Action<string>             _logWarn;
    private readonly Func<string, string, bool>? _isOffraidPositionUnlocked;

    public TradersController(
        TradersAvailabilityService availability,
        UserConfig userConfig,
        DatabaseService db,
        SaveServer saveServer,
        Action<string>? log = null,
        Action<string>? logWarn = null,
        Func<string, string, bool>? isOffraidPositionUnlocked = null)
    {
        _availability               = availability;
        _userConfig                 = userConfig;
        _db                         = db;
        _saveServer                 = saveServer;
        _log                        = log     ?? (_ => {});
        _logWarn                    = logWarn ?? (_ => {});
        _isOffraidPositionUnlocked  = isOffraidPositionUnlocked;
    }

    private static bool IsValidMongoId(string id) =>
        id.Length == 24 && System.Text.RegularExpressions.Regex.IsMatch(id, @"^[0-9a-fA-F]{24}$");

    public void InitTraders(PttConfig config)
    {
        if (!_userConfig.Gameplay.TradersAccessRestriction) return;

        var traders = _db.GetTables()?.Traders;
        if (traders == null) return;

        foreach (var (traderId, traderCfg) in config.TradersConfig)
        {
            if (!IsValidMongoId(traderId))
            {
                _logWarn($"[PTT] InitTraders: skipping non-MongoId trader '{traderId}'");
                continue;
            }
            var key = new MongoId(traderId);
            if (traders.TryGetValue(key, out var trader) && trader?.Base != null)
                trader.Base.UnlockedByDefault = false;
            else if (traderCfg.DisableWarning != true)
                _logWarn($"[PTT] InitTraders: unknown trader '{traderId}'");
        }

        _log($"[PTT] InitTraders: {config.TradersConfig.Count} traders configured");
    }

    public void UpdateTraders(PttConfig config, string offraidPosition, string sessionId)
    {
        if (!_userConfig.Gameplay.TradersAccessRestriction) return;

        var profile     = _saveServer.GetProfile(new MongoId(sessionId));
        var pmc         = profile?.CharacterData?.PmcData;
        var tradersInfo = pmc?.TradersInfo;
        if (tradersInfo == null) return;

        foreach (var (traderId, traderCfg) in config.TradersConfig)
        {
            if (!IsValidMongoId(traderId)) continue;

            bool questOk  = _availability.IsAvailable(traderId, pmc!.Quests);
            bool posOk    = PttHelpers.CheckAccessVia(traderCfg.AccessVia, offraidPosition);
            bool condOk   = _isOffraidPositionUnlocked == null ||
                            _isOffraidPositionUnlocked(offraidPosition, sessionId);
            bool unlocked = questOk && posOk && condOk;

            var key = new MongoId(traderId);
            if (tradersInfo.TryGetValue(key, out var info) && info != null)
                info.Unlocked = unlocked;
        }
    }
}
