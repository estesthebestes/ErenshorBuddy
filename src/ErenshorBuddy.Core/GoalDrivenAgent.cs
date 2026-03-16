using System;
using System.Linq;
using ErenshorBuddy.Contracts;

namespace ErenshorBuddy.Core;

public sealed class GoalDrivenAgent
{
    public AgentDecision Decide(BotProfile profile, GameSnapshot snapshot, BotRuntimeMemory memory)
    {
        if (snapshot.IsUiBlocked || snapshot.IsModalOpen || snapshot.ErrorFlags.UiBlocked)
        {
            return Alert("The game UI is blocked by a modal or overlay.");
        }

        if (!string.Equals(snapshot.ZoneId, profile.FarmAreaId, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentDecision
            {
                DecisionType = AgentDecisionType.RaiseAlert,
                Reason = $"Wrong zone: expected '{profile.FarmAreaId}', actual '{snapshot.ZoneId}'."
            };
        }

        if (snapshot.ErrorFlags.Stuck)
        {
            return Alert("The character appears to be stuck.");
        }

        if (ShouldStop(profile, snapshot, memory, out var stopReason))
        {
            return new AgentDecision
            {
                DecisionType = AgentDecisionType.Stop,
                Reason = stopReason
            };
        }

        if (snapshot.CurrentTarget == null)
        {
            if (snapshot.IsInCombat || snapshot.ErrorFlags.LostTarget)
            {
                return Alert("Combat was active but the target is missing.");
            }

            return new AgentDecision
            {
                DecisionType = AgentDecisionType.AcquireTarget,
                Reason = "No target selected."
            };
        }

        if (snapshot.CurrentTarget.IsDead || snapshot.CurrentTarget.HealthPercent <= 0f)
        {
            if (profile.LootCorpses && snapshot.IsLootAvailable)
            {
                return new AgentDecision
                {
                    DecisionType = AgentDecisionType.Loot,
                    Reason = "Target died and loot is available."
                };
            }

            return new AgentDecision
            {
                DecisionType = AgentDecisionType.AcquireTarget,
                Reason = "Target died; acquiring a new one."
            };
        }

        if (!snapshot.CurrentTarget.IsHostile)
        {
            return new AgentDecision
            {
                DecisionType = AgentDecisionType.AcquireTarget,
                Reason = "Current target is not hostile."
            };
        }

        if (!snapshot.IsTargetInRange || !snapshot.IsTargetInLineOfSight)
        {
            return new AgentDecision
            {
                DecisionType = AgentDecisionType.Reposition,
                Reason = "Target is out of range or line of sight."
            };
        }

        var usableAbility = profile.AbilityRotation.FirstOrDefault(rule =>
        {
            var ability = snapshot.Abilities.FirstOrDefault(a => string.Equals(a.AbilityId, rule.AbilityId, StringComparison.OrdinalIgnoreCase));
            return ability != null
                   && ability.IsReady
                   && snapshot.Player.HealthPercent >= rule.MinPlayerHealthPercent
                   && snapshot.Player.ResourcePercent >= Math.Max(rule.MinResourcePercent, ability.ResourceCostPercent);
        });

        if (usableAbility != null)
        {
            return new AgentDecision
            {
                DecisionType = AgentDecisionType.UseAbility,
                AbilityId = usableAbility.AbilityId,
                Reason = $"Using ability '{usableAbility.DisplayName ?? usableAbility.AbilityId}'."
            };
        }

        return new AgentDecision
        {
            DecisionType = AgentDecisionType.Idle,
            Reason = "No ability is ready."
        };
    }

    private static AgentDecision Alert(string reason)
    {
        return new AgentDecision
        {
            DecisionType = AgentDecisionType.RaiseAlert,
            Reason = reason
        };
    }

    private static bool ShouldStop(BotProfile profile, GameSnapshot snapshot, BotRuntimeMemory memory, out string reason)
    {
        if (profile.StopConditions.StopWhenInventoryFull && snapshot.Player.InventoryFull)
        {
            reason = "Inventory is full.";
            return true;
        }

        if (profile.StopConditions.StopWhenDurabilityLow && snapshot.Player.DurabilityPercent <= profile.StopConditions.MinimumDurabilityPercent)
        {
            reason = "Durability is below the configured threshold.";
            return true;
        }

        if (profile.StopConditions.MaxRuntimeMinutes.HasValue
            && memory.StartedAtUtc != DateTime.MinValue
            && DateTime.UtcNow - memory.StartedAtUtc >= TimeSpan.FromMinutes(profile.StopConditions.MaxRuntimeMinutes.Value))
        {
            reason = "Maximum runtime reached.";
            return true;
        }

        if (profile.StopConditions.MaxKills.HasValue && memory.Counters.Kills >= profile.StopConditions.MaxKills.Value)
        {
            reason = "Maximum kills reached.";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}

