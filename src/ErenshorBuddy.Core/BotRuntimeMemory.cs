using System;
using ErenshorBuddy.Contracts;

namespace ErenshorBuddy.Core;

public sealed class BotRuntimeMemory
{
    public DateTime StartedAtUtc { get; set; } = DateTime.MinValue;
    public string? LastTargetId { get; set; }
    public int ConsecutiveActionFailures { get; set; }
    public SessionCounters Counters { get; } = new();
}

