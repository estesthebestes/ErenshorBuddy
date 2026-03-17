using System;
using System.Collections;
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
    private static readonly HashSet<string> HostileFactions = new(StringComparer.OrdinalIgnoreCase)
    {
        "PreyAnimal",
        "PredatorAnimal",
        "Undead",
        "EvilHuman",
        "EvilGuard",
        "OtherEvil"
    };

    private readonly ManualLogSource _logger;
    private readonly PluginSettings _settings;
    private readonly Lazy<List<Assembly>> _assemblies;
    private Type? _playerControlType;
    private Type? _targetTrackerType;
    private Type? _playerCombatType;
    private Type? _characterType;
    private Type? _statsType;
    private FieldInfo? _playerControlCurrentTargetField;
    private FieldInfo? _playerControlTargetingField;
    private FieldInfo? _playerControlMyCombatField;
    private FieldInfo? _playerControlMyselfField;
    private FieldInfo? _targetTrackerNearbyTargetsField;
    private FieldInfo? _playerCombatAutoattackField;
    private MethodInfo? _playerCombatCheckTargetInMeleeRangeMethod;
    private FieldInfo? _characterAliveField;
    private FieldInfo? _characterIsNpcField;
    private FieldInfo? _characterIsVendorField;
    private FieldInfo? _characterMyFactionField;
    private FieldInfo? _characterAggressiveTowardsField;
    private FieldInfo? _characterMyStatsField;
    private FieldInfo? _characterNearbyEnemiesField;
    private FieldInfo? _statsMyNameField;
    private FieldInfo? _statsCurrentHPField;
    private FieldInfo? _statsCurrentMaxHPField;
    private FieldInfo? _statsCurrentManaField;
    private FieldInfo? _statsCurrentMaxManaField;

    public ReflectionGameWorldAdapter(ManualLogSource logger, PluginSettings settings)
    {
        _logger = logger;
        _settings = settings;
        _assemblies = new Lazy<List<Assembly>>(() => AppDomain.CurrentDomain.GetAssemblies().ToList());
    }

    public GameSnapshot CaptureSnapshot()
    {
        var context = ResolvePlayerContext();
        var player = context == null ? CapturePlayerFallback() : CapturePlayer(context);
        var currentTarget = context == null ? null : CaptureTargetSnapshot(context.CurrentTarget, context.PlayerCharacter);
        var nearbyEntities = context == null ? new List<EntitySnapshot>() : CaptureNearbyEntities(context);

        var snapshot = new GameSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            ZoneId = SceneManager.GetActiveScene().name,
            Player = player,
            CurrentTarget = currentTarget,
            NearbyEntities = nearbyEntities,
            Abilities = CaptureAbilities()
        };

        snapshot.IsInCombat = ResolveIsInCombat(context, currentTarget);
        snapshot.IsTargetInRange = ResolveIsTargetInRange(context, currentTarget);
        snapshot.IsTargetInLineOfSight = true;
        snapshot.IsLootAvailable = currentTarget != null && currentTarget.IsDead && currentTarget.Distance <= 4f;
        snapshot.IsUiBlocked = false;
        snapshot.IsModalOpen = false;
        snapshot.ErrorFlags = new ErrorFlags
        {
            WrongZone = false,
            LostTarget = snapshot.IsInCombat && snapshot.CurrentTarget == null
        };

        return snapshot;
    }

    private PlayerSnapshot CapturePlayer(PlayerContext context)
    {
        var playerTransform = GetTransform(context.PlayerCharacter);
        return new PlayerSnapshot
        {
            Name = ResolveCharacterName(context.PlayerCharacter) ?? "UnknownPlayer",
            Position = playerTransform == null
                ? new Vector3Data()
                : new Vector3Data
                {
                    X = playerTransform.position.x,
                    Y = playerTransform.position.y,
                    Z = playerTransform.position.z
                },
            HealthPercent = ResolveStatPercent(context.PlayerCharacter, _statsCurrentHPField, _statsCurrentMaxHPField, 100f),
            ResourcePercent = ResolveStatPercent(context.PlayerCharacter, _statsCurrentManaField, _statsCurrentMaxManaField, 100f),
            DurabilityPercent = ResolvePercent("Durability", "CurrentDurability", 100f),
            InventoryFull = ResolveBool("Inventory", "IsFull")
        };
    }

    private PlayerSnapshot CapturePlayerFallback()
    {
        var playerGameObject = TryFindByTag(_settings.PlayerTag) ?? GameObject.Find("Player");
        var playerTransform = playerGameObject?.transform;

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

    private TargetSnapshot? CaptureTargetSnapshot(object? character, object? playerCharacter)
    {
        if (!IsLiveUnityObject(character))
        {
            return null;
        }

        var unityObject = (UnityEngine.Object)character!;
        return new TargetSnapshot
        {
            Id = unityObject.GetInstanceID().ToString(),
            Name = ResolveCharacterName(character) ?? unityObject.name,
            Distance = CalculateDistance(playerCharacter, character),
            HealthPercent = ResolveStatPercent(character, _statsCurrentHPField, _statsCurrentMaxHPField, 100f),
            IsHostile = ResolveHostility(character, playerCharacter),
            IsDead = !ResolveCharacterAlive(character)
        };
    }

    private List<EntitySnapshot> CaptureNearbyEntities(PlayerContext context)
    {
        var characters = GetNearbyCharacters(context)
            .Where(character => IsLiveUnityObject(character))
            .Where(character => !SameUnityObject(character, context.PlayerCharacter))
            .GroupBy(character => ((UnityEngine.Object)character!).GetInstanceID().ToString(), StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(character => new EntitySnapshot
            {
                Id = ((UnityEngine.Object)character!).GetInstanceID().ToString(),
                Name = ResolveCharacterName(character) ?? ((UnityEngine.Object)character!).name,
                Distance = CalculateDistance(context.PlayerCharacter, character),
                IsHostile = ResolveHostility(character, context.PlayerCharacter),
                IsDead = !ResolveCharacterAlive(character)
            })
            .OrderBy(entity => entity.Distance)
            .Take(25)
            .ToList();

        return characters;
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

    private bool ResolveIsInCombat(PlayerContext? context, TargetSnapshot? currentTarget)
    {
        if (context?.PlayerCombat != null
            && _playerCombatAutoattackField?.GetValue(context.PlayerCombat) is bool autoAttack)
        {
            return autoAttack || (currentTarget != null && !currentTarget.IsDead);
        }

        return currentTarget != null && !currentTarget.IsDead;
    }

    private bool ResolveIsTargetInRange(PlayerContext? context, TargetSnapshot? currentTarget)
    {
        if (currentTarget == null)
        {
            return true;
        }

        if (context?.PlayerCombat != null
            && context.CurrentTarget != null
            && _playerCombatCheckTargetInMeleeRangeMethod != null)
        {
            try
            {
                if (_playerCombatCheckTargetInMeleeRangeMethod.Invoke(context.PlayerCombat, new[] { context.CurrentTarget }) is bool inRange)
                {
                    return inRange;
                }
            }
            catch (Exception ex)
            {
                if (_settings.EnableDebugLogs)
                {
                    _logger.LogDebug($"Failed to evaluate melee range: {ex.Message}");
                }
            }
        }

        return currentTarget.Distance <= 25f;
    }

    private PlayerContext? ResolvePlayerContext()
    {
        EnsureReflectionBindings();
        if (_playerControlType == null)
        {
            return null;
        }

        var playerControl = UnityEngine.Object.FindObjectOfType(_playerControlType);
        if (!IsLiveUnityObject(playerControl))
        {
            return null;
        }

        return new PlayerContext
        {
            PlayerControl = playerControl,
            CurrentTarget = _playerControlCurrentTargetField?.GetValue(playerControl!),
            PlayerCharacter = _playerControlMyselfField?.GetValue(playerControl!),
            PlayerCombat = _playerControlMyCombatField?.GetValue(playerControl!),
            Targeting = _playerControlTargetingField?.GetValue(playerControl!)
        };
    }

    private IEnumerable<object> GetNearbyCharacters(PlayerContext context)
    {
        if (context.Targeting != null
            && _targetTrackerNearbyTargetsField?.GetValue(context.Targeting) is IEnumerable nearbyTargets)
        {
            return nearbyTargets.Cast<object>();
        }

        if (context.PlayerCharacter != null
            && _characterNearbyEnemiesField?.GetValue(context.PlayerCharacter) is IEnumerable nearbyEnemies)
        {
            return nearbyEnemies.Cast<object>();
        }

        return Enumerable.Empty<object>();
    }

    private bool ResolveHostility(object? character, object? playerCharacter)
    {
        if (!IsLiveUnityObject(character) || !IsLiveUnityObject(playerCharacter))
        {
            return false;
        }

        if (!ResolveCharacterAlive(character) || !ResolveCharacterBool(character, _characterIsNpcField))
        {
            return false;
        }

        if (ResolveCharacterBool(character, _characterIsVendorField) || SameUnityObject(character, playerCharacter))
        {
            return false;
        }

        var playerFaction = _characterMyFactionField?.GetValue(playerCharacter);
        var targetFaction = _characterMyFactionField?.GetValue(character);
        if (playerFaction == null || targetFaction == null)
        {
            return false;
        }

        if (Equals(playerFaction, targetFaction))
        {
            return false;
        }

        if (HasAggressiveToward(playerCharacter, targetFaction) || HasAggressiveToward(character, playerFaction))
        {
            return true;
        }

        return HostileFactions.Contains(targetFaction.ToString() ?? string.Empty);
    }

    private bool HasAggressiveToward(object? character, object faction)
    {
        if (character == null || _characterAggressiveTowardsField?.GetValue(character) is not IEnumerable aggressiveTowards)
        {
            return false;
        }

        foreach (var item in aggressiveTowards)
        {
            if (Equals(item, faction))
            {
                return true;
            }
        }

        return false;
    }

    private string? ResolveCharacterName(object? character)
    {
        var stats = _characterMyStatsField?.GetValue(character!);
        return _statsMyNameField?.GetValue(stats)?.ToString();
    }

    private float ResolveStatPercent(object? character, FieldInfo? currentField, FieldInfo? maxField, float fallback)
    {
        var stats = _characterMyStatsField?.GetValue(character!);
        if (stats == null || currentField == null || maxField == null)
        {
            return fallback;
        }

        var current = ConvertToSingle(currentField.GetValue(stats));
        var maximum = ConvertToSingle(maxField.GetValue(stats));
        if (maximum <= 0f)
        {
            return fallback;
        }

        return ClampPercent((current / maximum) * 100f);
    }

    private float CalculateDistance(object? originCharacter, object? targetCharacter)
    {
        var origin = GetTransform(originCharacter)?.position ?? Vector3.zero;
        var target = GetTransform(targetCharacter)?.position ?? Vector3.zero;
        return Vector3.Distance(origin, target);
    }

    private static Transform? GetTransform(object? value)
    {
        return value as Component != null
            ? ((Component)value).transform
            : null;
    }

    private bool ResolveCharacterAlive(object? character)
    {
        return ResolveCharacterBool(character, _characterAliveField, fallback: true);
    }

    private bool ResolveCharacterBool(object? character, FieldInfo? field, bool fallback = false)
    {
        if (character == null || field?.GetValue(character) is not bool value)
        {
            return fallback;
        }

        return value;
    }

    private void EnsureReflectionBindings()
    {
        if (_playerControlType != null)
        {
            return;
        }

        var assembly = _assemblies.Value.FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal));
        if (assembly == null)
        {
            return;
        }

        _playerControlType = assembly.GetType("PlayerControl");
        _targetTrackerType = assembly.GetType("TargetTracker");
        _playerCombatType = assembly.GetType("PlayerCombat");
        _characterType = assembly.GetType("Character");
        _statsType = assembly.GetType("Stats");

        if (_playerControlType != null)
        {
            _playerControlCurrentTargetField = _playerControlType.GetField("CurrentTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _playerControlTargetingField = _playerControlType.GetField("Targeting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _playerControlMyCombatField = _playerControlType.GetField("MyCombat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _playerControlMyselfField = _playerControlType.GetField("Myself", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (_targetTrackerType != null)
        {
            _targetTrackerNearbyTargetsField = _targetTrackerType.GetField("NearbyTargets", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (_playerCombatType != null)
        {
            _playerCombatAutoattackField = _playerCombatType.GetField("Autoattack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _playerCombatCheckTargetInMeleeRangeMethod = _playerCombatType.GetMethod("CheckTargetInMeleeRange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (_characterType != null)
        {
            _characterAliveField = _characterType.GetField("Alive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _characterIsNpcField = _characterType.GetField("isNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _characterIsVendorField = _characterType.GetField("isVendor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _characterMyFactionField = _characterType.GetField("MyFaction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _characterAggressiveTowardsField = _characterType.GetField("AggressiveTowards", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _characterMyStatsField = _characterType.GetField("MyStats", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _characterNearbyEnemiesField = _characterType.GetField("NearbyEnemies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (_statsType != null)
        {
            _statsMyNameField = _statsType.GetField("MyName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _statsCurrentHPField = _statsType.GetField("CurrentHP", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _statsCurrentMaxHPField = _statsType.GetField("CurrentMaxHP", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _statsCurrentManaField = _statsType.GetField("CurrentMana", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _statsCurrentMaxManaField = _statsType.GetField("CurrentMaxMana", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
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

    private static float ConvertToSingle(object? value)
    {
        return value switch
        {
            float single => single,
            int integer => integer,
            _ => 0f
        };
    }

    private static float ClampPercent(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }

        return value > 100f ? 100f : value;
    }

    private static bool SameUnityObject(object? left, object? right)
    {
        return left is UnityEngine.Object leftObject
               && right is UnityEngine.Object rightObject
               && leftObject != null
               && rightObject != null
               && leftObject.GetInstanceID() == rightObject.GetInstanceID();
    }

    private static bool IsLiveUnityObject(object? value)
    {
        return value is UnityEngine.Object unityObject && unityObject != null;
    }

    private sealed class PlayerContext
    {
        public object? PlayerControl { get; set; }
        public object? PlayerCharacter { get; set; }
        public object? PlayerCombat { get; set; }
        public object? Targeting { get; set; }
        public object? CurrentTarget { get; set; }
    }
}
