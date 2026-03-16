using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ErenshorBuddy.Contracts;
using ErenshorBuddy.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorBuddy.Plugin;

internal sealed class ReflectionGameWorldAdapter : IGameWorldAdapter
{
    private readonly ManualLogSource _logger;
    private readonly PluginSettings _settings;
    private readonly Lazy<List<Assembly>> _assemblies;

    public ReflectionGameWorldAdapter(ManualLogSource logger, PluginSettings settings)
    {
        _logger = logger;
        _settings = settings;
        _assemblies = new Lazy<List<Assembly>>(() => AppDomain.CurrentDomain.GetAssemblies().ToList());
    }

    public GameSnapshot CaptureSnapshot()
    {
        var snapshot = new GameSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            ZoneId = SceneManager.GetActiveScene().name,
            Player = CapturePlayer(),
            CurrentTarget = CaptureCurrentTarget(),
            NearbyEntities = CaptureNearbyEntities(),
            Abilities = CaptureAbilities()
        };

        snapshot.IsInCombat = snapshot.CurrentTarget != null && !snapshot.CurrentTarget.IsDead;
        snapshot.IsTargetInRange = snapshot.CurrentTarget == null || snapshot.CurrentTarget.Distance <= 25f;
        snapshot.IsTargetInLineOfSight = true;
        snapshot.IsLootAvailable = snapshot.CurrentTarget != null && snapshot.CurrentTarget.IsDead && snapshot.CurrentTarget.Distance <= 4f;
        snapshot.IsUiBlocked = false;
        snapshot.IsModalOpen = false;
        snapshot.ErrorFlags = new ErrorFlags
        {
            WrongZone = false,
            LostTarget = snapshot.IsInCombat && snapshot.CurrentTarget == null
        };

        return snapshot;
    }

    private PlayerSnapshot CapturePlayer()
    {
        var playerGameObject = TryFindByTag(_settings.PlayerTag) ?? GameObject.Find("Player");
        var playerTransform = playerGameObject != null ? playerGameObject.transform : null;

        return new PlayerSnapshot
        {
            Name = playerGameObject != null ? playerGameObject.name : "UnknownPlayer",
            Position = playerTransform == null
                ? new Vector3Data()
                : new Vector3Data
                {
                    X = playerTransform.position.x,
                    Y = playerTransform.position.y,
                    Z = playerTransform.position.z
                },
            HealthPercent = ResolvePercent("Player", "Health", 100f),
            ResourcePercent = ResolvePercent("Player", "Mana", 100f),
            DurabilityPercent = ResolvePercent("Durability", "CurrentDurability", 100f),
            InventoryFull = ResolveBool("Inventory", "IsFull")
        };
    }

    private TargetSnapshot? CaptureCurrentTarget()
    {
        var target = ResolveCurrentTargetGameObject();
        if (target == null)
        {
            return null;
        }

        var playerPosition = TryFindByTag(_settings.PlayerTag)?.transform.position ?? Vector3.zero;
        return new TargetSnapshot
        {
            Id = target.GetInstanceID().ToString(),
            Name = target.name,
            Distance = Vector3.Distance(playerPosition, target.transform.position),
            HealthPercent = ResolveComponentPercent(target, "Health", 100f),
            IsHostile = true,
            IsDead = ResolveComponentBool(target, "Dead")
        };
    }

    private List<EntitySnapshot> CaptureNearbyEntities()
    {
        var playerPosition = TryFindByTag(_settings.PlayerTag)?.transform.position ?? Vector3.zero;
        GameObject[] enemies;
        try
        {
            enemies = GameObject.FindGameObjectsWithTag(_settings.EnemyTag);
        }
        catch (UnityException ex)
        {
            if (_settings.EnableDebugLogs)
            {
                _logger.LogDebug($"Enemy tag '{_settings.EnemyTag}' could not be resolved: {ex.Message}");
            }

            enemies = Array.Empty<GameObject>();
        }

        return enemies
            .Select(enemy => new EntitySnapshot
            {
                Id = enemy.GetInstanceID().ToString(),
                Name = enemy.name,
                Distance = Vector3.Distance(playerPosition, enemy.transform.position),
                IsHostile = true,
                IsDead = ResolveComponentBool(enemy, "Dead")
            })
            .OrderBy(entity => entity.Distance)
            .Take(25)
            .ToList();
    }

    private List<AbilitySnapshot> CaptureAbilities()
    {
        var abilities = new List<AbilitySnapshot>();
        for (var slot = 1; slot <= 6; slot++)
        {
            abilities.Add(new AbilitySnapshot
            {
                AbilityId = $"slot{slot}",
                DisplayName = $"Action Slot {slot}",
                IsReady = true,
                ResourceCostPercent = 0f
            });
        }

        return abilities;
    }

    private GameObject? ResolveCurrentTargetGameObject()
    {
        foreach (var assembly in _assemblies.Value)
        {
            Type? targetManagerType = null;
            try
            {
                targetManagerType = assembly.GetTypes().FirstOrDefault(type =>
                    type.Name.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0
                    && type.Name.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            if (targetManagerType == null)
            {
                continue;
            }

            var instance = targetManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                           ?? targetManagerType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

            var currentTarget = targetManagerType.GetProperty("CurrentTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
            if (currentTarget is GameObject gameObject)
            {
                return gameObject;
            }
        }

        return null;
    }

    private float ResolvePercent(string typeHint, string memberHint, float fallback)
    {
        foreach (var assembly in _assemblies.Value)
        {
            Type? targetType = null;
            try
            {
                targetType = assembly.GetTypes().FirstOrDefault(type => type.Name.IndexOf(typeHint, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            if (targetType == null)
            {
                continue;
            }

            var instance = targetType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                           ?? targetType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

            if (instance == null)
            {
                continue;
            }

            var value = targetType.GetProperty(memberHint, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance)
                        ?? targetType.GetField(memberHint, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);

            if (value is float single)
            {
                return single;
            }

            if (value is int integer)
            {
                return integer;
            }
        }

        return fallback;
    }

    private bool ResolveBool(string typeHint, string memberHint)
    {
        foreach (var assembly in _assemblies.Value)
        {
            Type? targetType = null;
            try
            {
                targetType = assembly.GetTypes().FirstOrDefault(type => type.Name.IndexOf(typeHint, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            if (targetType == null)
            {
                continue;
            }

            var instance = targetType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                           ?? targetType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

            if (instance == null)
            {
                continue;
            }

            var value = targetType.GetProperty(memberHint, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance)
                        ?? targetType.GetField(memberHint, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);

            if (value is bool boolean)
            {
                return boolean;
            }
        }

        return false;
    }

    private static bool ResolveComponentBool(GameObject gameObject, string memberHint)
    {
        foreach (var component in gameObject.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }

            var componentType = component.GetType();
            var value = componentType.GetProperty(memberHint, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(component)
                        ?? componentType.GetField(memberHint, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(component);

            if (value is bool boolean)
            {
                return boolean;
            }
        }

        return false;
    }

    private static float ResolveComponentPercent(GameObject gameObject, string memberHint, float fallback)
    {
        foreach (var component in gameObject.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }

            var componentType = component.GetType();
            var value = componentType.GetProperty(memberHint, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(component)
                        ?? componentType.GetField(memberHint, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(component);

            if (value is float single)
            {
                return single;
            }

            if (value is int integer)
            {
                return integer;
            }
        }

        return fallback;
    }

    private GameObject? TryFindByTag(string tag)
    {
        try
        {
            return GameObject.FindGameObjectWithTag(tag);
        }
        catch (UnityException ex)
        {
            if (_settings.EnableDebugLogs)
            {
                _logger.LogDebug($"Tag '{tag}' could not be resolved: {ex.Message}");
            }

            return null;
        }
    }
}
