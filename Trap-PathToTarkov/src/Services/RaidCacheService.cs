using System;
using System.Collections.Generic;
using System.Linq;
using PathToTarkov.Models;

namespace PathToTarkov.Services;

/// <summary>
/// Holds per-session raid state. Lives in PttMod singleton, indexed by sessionId.
/// Also tracks raid start times per map so co-raiders (Fika group members) can be
/// identified server-side without needing client-side group packets.
/// </summary>
public class RaidCacheService
{
    // Window in seconds within which two sessions starting the same map
    // are considered to be in the same Fika group.
    // Fika sessions are always explicit (no matchmaking), so this is safe.
    private const int CO_RAIDER_WINDOW_SECONDS = 30;

    private readonly Dictionary<string, RaidCache>       _caches    = new();
    private readonly Dictionary<string, RaidStartRecord> _startTimes = new();

    // ---- Raid cache (per-session raid lifecycle state) ----

    public void Init(string sessionId, bool preserveTransit = false)
    {
        if (preserveTransit && _caches.TryGetValue(sessionId, out var existing)
            && existing.ExitStatus == "Transit")
            return;

        _caches[sessionId] = new RaidCache { SessionId = sessionId };
    }

    public RaidCache? Get(string sessionId)
        => _caches.TryGetValue(sessionId, out var c) ? c : null;

    public RaidCache GetOrCreate(string sessionId)
    {
        if (!_caches.TryGetValue(sessionId, out var c))
        {
            c = new RaidCache { SessionId = sessionId };
            _caches[sessionId] = c;
        }
        return c;
    }

    // ---- Co-raider tracking (for Fika group intel sharing) ----

    /// <summary>
    /// Records that a session started a raid on a given map at the current time.
    /// Called from PttLocationLifecycleService.StartLocalRaid().
    /// </summary>
    public void RecordRaidStart(string sessionId, string mapName)
    {
        _startTimes[sessionId] = new RaidStartRecord
        {
            SessionId = sessionId,
            MapName   = mapName,
            StartedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Returns session IDs of players who started a raid on the same map
    /// within CO_RAIDER_WINDOW_SECONDS of this session. Excludes the session itself.
    /// These are the player's Fika group members.
    /// </summary>
    public IEnumerable<string> GetCoRaiderSessionIds(string sessionId)
    {
        if (!_startTimes.TryGetValue(sessionId, out var myRecord))
            return Enumerable.Empty<string>();

        var cutoff = myRecord.StartedAt.AddSeconds(-CO_RAIDER_WINDOW_SECONDS);

        return _startTimes
            .Where(kv =>
                kv.Key != sessionId &&
                kv.Value.MapName == myRecord.MapName &&
                kv.Value.StartedAt >= cutoff &&
                kv.Value.StartedAt <= myRecord.StartedAt.AddSeconds(CO_RAIDER_WINDOW_SECONDS))
            .Select(kv => kv.Key);
    }
}

/// <summary>Lightweight record of when a session started a raid.</summary>
public class RaidStartRecord
{
    public string   SessionId { get; set; } = "";
    public string   MapName   { get; set; } = "";
    public DateTime StartedAt { get; set; }
}
