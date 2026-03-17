using System;
using System.Linq;
using ErenshorBuddy.Contracts;

namespace ErenshorBuddy.Core;

public static class TargetSelection
{
    public static EntitySnapshot? SelectPullTarget(BotProfile profile, GameSnapshot snapshot)
    {
        var candidates = snapshot.NearbyEntities
            .Where(entity => entity.IsHostile && !entity.IsDead && entity.Distance <= profile.PullRadius)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        if (profile.MobPriorityNames.Count == 0)
        {
            return candidates.OrderBy(entity => entity.Distance).First();
        }

        var prioritized = candidates
            .Select(entity => new
            {
                Entity = entity,
                PriorityIndex = GetPriorityIndex(profile, entity.Name)
            })
            .Where(item => item.PriorityIndex >= 0)
            .OrderBy(item => item.PriorityIndex)
            .ThenBy(item => item.Entity.Distance)
            .Select(item => item.Entity)
            .FirstOrDefault();

        return prioritized ?? candidates.OrderBy(entity => entity.Distance).First();
    }

    private static int GetPriorityIndex(BotProfile profile, string entityName)
    {
        for (var index = 0; index < profile.MobPriorityNames.Count; index++)
        {
            var priority = profile.MobPriorityNames[index];
            if (string.IsNullOrWhiteSpace(priority))
            {
                continue;
            }

            if (entityName.IndexOf(priority, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return index;
            }
        }

        return -1;
    }
}
