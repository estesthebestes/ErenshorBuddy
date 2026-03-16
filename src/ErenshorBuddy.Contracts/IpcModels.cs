using System;

namespace ErenshorBuddy.Contracts;

public enum BotRunState
{
    Idle,
    Running,
    Paused,
    Alerting
}

public enum BotCommandType
{
    StartProfile,
    Stop,
    Pause,
    Resume,
    RequestSnapshot,
    AcknowledgeAlert
}

public enum PluginEventType
{
    Status,
    Snapshot,
    Log
}

public enum AlertCode
{
    None,
    LostTarget,
    Stuck,
    UiBlocked,
    ActionFailure,
    WrongZone,
    StopConditionReached
}

public sealed class BotCommandEnvelope
{
    public BotCommandType CommandType { get; set; }
    public BotProfile? Profile { get; set; }
    public string? Message { get; set; }
}

public sealed class PluginEventEnvelope
{
    public PluginEventType EventType { get; set; }
    public BotStatusPayload? Status { get; set; }
    public GameSnapshot? Snapshot { get; set; }
    public string? Message { get; set; }
}

public sealed class BotStatusPayload
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public BotRunState State { get; set; }
    public string? ProfileName { get; set; }
    public string CurrentAction { get; set; } = string.Empty;
    public AlertCode AlertCode { get; set; }
    public string? AlertDetail { get; set; }
    public SessionCounters Counters { get; set; } = new();
}

public sealed class SessionCounters
{
    public int Kills { get; set; }
    public int ConsumablesUsed { get; set; }
    public TimeSpan Elapsed { get; set; }
}

