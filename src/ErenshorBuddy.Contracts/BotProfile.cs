using System;
using System.Collections.Generic;

namespace ErenshorBuddy.Contracts;

public sealed class BotProfile
{
    public string Name { get; set; } = "Default Farm Profile";
    public string FarmAreaId { get; set; } = string.Empty;
    public List<string> MobPriorityNames { get; set; } = new();
    public List<AbilityRule> AbilityRotation { get; set; } = new();
    public ResourceThresholds ResourceThresholds { get; set; } = new();
    public StopConditions StopConditions { get; set; } = new();
    public float PullRadius { get; set; } = 20f;
    public float LeashRadius { get; set; } = 35f;
    public bool LootCorpses { get; set; } = true;
}

public sealed class AbilityRule
{
    public string AbilityId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public float MinPlayerHealthPercent { get; set; }
    public float MinResourcePercent { get; set; }
}

public sealed class ResourceThresholds
{
    public float MinimumHealthPercent { get; set; } = 25f;
    public float MinimumResourcePercent { get; set; } = 5f;
}

public sealed class StopConditions
{
    public bool StopWhenInventoryFull { get; set; } = true;
    public bool StopWhenDurabilityLow { get; set; } = true;
    public float MinimumDurabilityPercent { get; set; } = 10f;
    public int? MaxRuntimeMinutes { get; set; }
    public int? MaxKills { get; set; }
}

