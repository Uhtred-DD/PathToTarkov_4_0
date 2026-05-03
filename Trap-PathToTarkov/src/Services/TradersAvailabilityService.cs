using System;
using System.Collections.Generic;
using System.Linq;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace PathToTarkov.Services;

public class TradersAvailabilityService
{
    private Dictionary<string, HashSet<string>>? _tradersLockedByQuests;

    public void Init(Dictionary<MongoId, Quest> quests)
    {
        _tradersLockedByQuests = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (questId, quest) in quests)
        {
            if (quest.Rewards == null) continue;
            foreach (var (_, rewardList) in quest.Rewards)
                foreach (var reward in rewardList ?? Enumerable.Empty<Reward>())
                    if (reward.Type == RewardType.TraderUnlock &&
                        !string.IsNullOrEmpty(reward.Target))
                    {
                        if (!_tradersLockedByQuests.TryGetValue(reward.Target!, out var set))
                            _tradersLockedByQuests[reward.Target!] = set = new(StringComparer.OrdinalIgnoreCase);
                        set.Add(questId.ToString());
                    }
        }
    }

    public bool IsAvailable(string traderId, IEnumerable<QuestStatus>? pmcQuests)
    {
        if (_tradersLockedByQuests == null)
            throw new InvalidOperationException("TradersAvailabilityService not initialized");
        // Non-MongoId traders (e.g. "Coyote", "Sally") have no quest locks — always available
        if (!_tradersLockedByQuests.TryGetValue(traderId, out var unlockQuests) || unlockQuests.Count == 0)
            return true;
        return pmcQuests?.Any(q =>
            q.Status == QuestStatusEnum.Success &&
            unlockQuests.Contains(q.QId.ToString())) ?? false;
    }
}
