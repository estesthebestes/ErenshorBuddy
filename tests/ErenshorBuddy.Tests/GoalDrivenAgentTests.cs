using ErenshorBuddy.Contracts;
using ErenshorBuddy.Core;
using Xunit;

namespace ErenshorBuddy.Tests;

public sealed class GoalDrivenAgentTests
{
    private readonly GoalDrivenAgent _agent = new();

    [Fact]
    public void Decide_NoTargetInFarmArea_AcquiresTarget()
    {
        var decision = _agent.Decide(CreateProfile(), CreateSnapshot(), new BotRuntimeMemory());
        Assert.Equal(AgentDecisionType.AcquireTarget, decision.DecisionType);
    }

    [Fact]
    public void Decide_TargetInRangeAndAbilityReady_UsesAbility()
    {
        var snapshot = CreateSnapshot();
        snapshot.CurrentTarget = new TargetSnapshot
        {
            Id = "mob-1",
            Name = "Forest Boar",
            IsHostile = true,
            HealthPercent = 100,
            Distance = 5
        };
        snapshot.Abilities.Add(new AbilitySnapshot
        {
            AbilityId = "slot1",
            DisplayName = "Strike",
            IsReady = true,
            ResourceCostPercent = 0
        });
        snapshot.IsTargetInRange = true;

        var decision = _agent.Decide(CreateProfile(), snapshot, new BotRuntimeMemory());
        Assert.Equal(AgentDecisionType.UseAbility, decision.DecisionType);
        Assert.Equal("slot1", decision.AbilityId);
    }

    [Fact]
    public void Decide_WrongZone_RaisesAlert()
    {
        var snapshot = CreateSnapshot();
        snapshot.ZoneId = "DifferentZone";

        var decision = _agent.Decide(CreateProfile(), snapshot, new BotRuntimeMemory());
        Assert.Equal(AgentDecisionType.RaiseAlert, decision.DecisionType);
    }

    [Fact]
    public void Decide_StopConditionReached_Stops()
    {
        var memory = new BotRuntimeMemory();
        memory.Counters.Kills = 5;

        var profile = CreateProfile();
        profile.StopConditions.MaxKills = 5;

        var decision = _agent.Decide(profile, CreateSnapshot(), memory);
        Assert.Equal(AgentDecisionType.Stop, decision.DecisionType);
    }

    [Fact]
    public void Decide_UiBlocked_RaisesAlert()
    {
        var snapshot = CreateSnapshot();
        snapshot.IsUiBlocked = true;

        var decision = _agent.Decide(CreateProfile(), snapshot, new BotRuntimeMemory());
        Assert.Equal(AgentDecisionType.RaiseAlert, decision.DecisionType);
    }

    private static BotProfile CreateProfile()
    {
        return new BotProfile
        {
            Name = "Boar Farm",
            FarmAreaId = "StarterZone",
            AbilityRotation =
            {
                new AbilityRule
                {
                    AbilityId = "slot1",
                    DisplayName = "Strike"
                }
            }
        };
    }

    private static GameSnapshot CreateSnapshot()
    {
        return new GameSnapshot
        {
            ZoneId = "StarterZone",
            Player = new PlayerSnapshot
            {
                HealthPercent = 100,
                ResourcePercent = 100,
                DurabilityPercent = 100
            },
            IsTargetInRange = false
        };
    }
}
