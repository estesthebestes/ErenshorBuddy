using System;
using System.Collections.Generic;

namespace ErenshorBuddy.Contracts;

public sealed class GameSnapshot
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string ZoneId { get; set; } = string.Empty;
    public PlayerSnapshot Player { get; set; } = new();
    public TargetSnapshot? CurrentTarget { get; set; }
    public List<EntitySnapshot> NearbyEntities { get; set; } = new();
    public List<AbilitySnapshot> Abilities { get; set; } = new();
    public bool IsInCombat { get; set; }
    public bool IsUiBlocked { get; set; }
    public bool IsModalOpen { get; set; }
    public bool IsLootAvailable { get; set; }
    public bool IsTargetInRange { get; set; }
    public bool IsTargetInLineOfSight { get; set; } = true;
    public ErrorFlags ErrorFlags { get; set; } = new();
}

public sealed class PlayerSnapshot
{
    public string Name { get; set; } = string.Empty;
    public Vector3Data Position { get; set; } = new();
    public float HealthPercent { get; set; } = 100f;
    public float ResourcePercent { get; set; } = 100f;
    public float DurabilityPercent { get; set; } = 100f;
    public bool InventoryFull { get; set; }
}

public sealed class TargetSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float HealthPercent { get; set; } = 100f;
    public float Distance { get; set; }
    public bool IsHostile { get; set; }
    public bool IsDead { get; set; }
}

public sealed class EntitySnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float Distance { get; set; }
    public bool IsHostile { get; set; }
    public bool IsDead { get; set; }
}

public sealed class AbilitySnapshot
{
    public string AbilityId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    public float ResourceCostPercent { get; set; }
}

public sealed class ErrorFlags
{
    public bool LostTarget { get; set; }
    public bool Stuck { get; set; }
    public bool UiBlocked { get; set; }
    public bool WrongZone { get; set; }
    public bool ActionFailure { get; set; }
}

public sealed class Vector3Data
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

