namespace ErenshorBuddy.Core;

public enum AgentDecisionType
{
    Idle,
    AcquireTarget,
    StartAutoAttack,
    UseAbility,
    Reposition,
    Loot,
    RaiseAlert,
    Stop
}

public sealed class AgentDecision
{
    public AgentDecisionType DecisionType { get; set; }
    public string? AbilityId { get; set; }
    public string? Reason { get; set; }
}
