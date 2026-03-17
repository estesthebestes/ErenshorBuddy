using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Logging;
using ErenshorBuddy.Contracts;
using ErenshorBuddy.Core;

namespace ErenshorBuddy.Plugin;

internal sealed class WindowsInputActuator : IBotActuator
{
    private readonly ManualLogSource _logger;
    private readonly PluginSettings _settings;
    private readonly Dictionary<string, ushort> _abilityKeyMap;
    private Type? _playerControlType;
    private Type? _targetTrackerType;
    private FieldInfo? _playerControlCurrentTargetField;
    private FieldInfo? _playerControlTargetingField;
    private FieldInfo? _playerControlMyCombatField;
    private FieldInfo? _targetTrackerNearbyTargetsField;
    private MethodInfo? _characterTargetMeMethod;
    private MethodInfo? _playerCombatForceAttackOnMethod;
    private MethodInfo? _playerCombatForceAttackOffMethod;

    public WindowsInputActuator(ManualLogSource logger, PluginSettings settings)
    {
        _logger = logger;
        _settings = settings;
        _abilityKeyMap = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["slot1"] = 0x31,
            ["slot2"] = 0x32,
            ["slot3"] = 0x33,
            ["slot4"] = 0x34,
            ["slot5"] = 0x35,
            ["slot6"] = 0x36
        };
    }

    public bool AcquireTarget(BotProfile profile, GameSnapshot snapshot)
    {
        var selectedTarget = TargetSelection.SelectPullTarget(profile, snapshot);
        if (selectedTarget == null)
        {
            return false;
        }

        if (TryTargetCharacter(selectedTarget.Id))
        {
            return true;
        }

        return TapKey(ParseVirtualKey(_settings.PrimaryTargetKey));
    }

    public bool StartAutoAttack(GameSnapshot snapshot)
    {
        EnsureReflectionBindings();
        var playerControl = ResolvePlayerControl();
        var playerCombat = playerControl == null
            ? null
            : _playerControlMyCombatField?.GetValue(playerControl);

        if (playerCombat == null || _playerCombatForceAttackOnMethod == null)
        {
            return false;
        }

        _playerCombatForceAttackOnMethod.Invoke(playerCombat, Array.Empty<object>());
        return true;
    }

    public bool UseAbility(string abilityId, BotProfile profile, GameSnapshot snapshot)
    {
        if (!_abilityKeyMap.TryGetValue(abilityId, out var key))
        {
            _logger.LogWarning($"No key binding exists for ability '{abilityId}'.");
            return false;
        }

        return TapKey(key);
    }

    public bool MoveTowardTarget(GameSnapshot snapshot)
    {
        var success = KeyDown(ParseVirtualKey(_settings.MoveForwardKey));
        Thread.Sleep(125);
        KeyUp(ParseVirtualKey(_settings.MoveForwardKey));
        return success;
    }

    public bool Loot(GameSnapshot snapshot)
    {
        return TapKey(ParseVirtualKey(_settings.LootKey));
    }

    public void StopAll()
    {
        EnsureReflectionBindings();
        var playerControl = ResolvePlayerControl();
        var playerCombat = playerControl == null
            ? null
            : _playerControlMyCombatField?.GetValue(playerControl);

        if (playerCombat != null && _playerCombatForceAttackOffMethod != null)
        {
            _playerCombatForceAttackOffMethod.Invoke(playerCombat, Array.Empty<object>());
        }

        KeyUp(ParseVirtualKey(_settings.MoveForwardKey));
    }

    private bool TryTargetCharacter(string targetId)
    {
        EnsureReflectionBindings();

        var playerControl = ResolvePlayerControl();
        if (playerControl == null)
        {
            return false;
        }

        if (MatchesCharacterId(_playerControlCurrentTargetField?.GetValue(playerControl), targetId))
        {
            return true;
        }

        var targeting = _playerControlTargetingField?.GetValue(playerControl);
        var targetCharacter = GetNearbyTargets(targeting).FirstOrDefault(character => MatchesCharacterId(character, targetId));
        if (targetCharacter == null || _characterTargetMeMethod == null)
        {
            return false;
        }

        _characterTargetMeMethod.Invoke(targetCharacter, Array.Empty<object>());
        return MatchesCharacterId(_playerControlCurrentTargetField?.GetValue(playerControl), targetId);
    }

    private void EnsureReflectionBindings()
    {
        if (_playerControlType != null)
        {
            return;
        }

        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal));
        if (assembly == null)
        {
            return;
        }

        _playerControlType = assembly.GetType("PlayerControl");
        _targetTrackerType = assembly.GetType("TargetTracker");
        var playerCombatType = assembly.GetType("PlayerCombat");
        var characterType = assembly.GetType("Character");

        if (_playerControlType != null)
        {
            _playerControlCurrentTargetField = _playerControlType.GetField("CurrentTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _playerControlTargetingField = _playerControlType.GetField("Targeting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _playerControlMyCombatField = _playerControlType.GetField("MyCombat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (_targetTrackerType != null)
        {
            _targetTrackerNearbyTargetsField = _targetTrackerType.GetField("NearbyTargets", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (characterType != null)
        {
            _characterTargetMeMethod = characterType.GetMethod("TargetMe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (playerCombatType != null)
        {
            _playerCombatForceAttackOnMethod = playerCombatType.GetMethod("ForceAttackOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _playerCombatForceAttackOffMethod = playerCombatType.GetMethod("ForceAttackOff", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }

    private object? ResolvePlayerControl()
    {
        EnsureReflectionBindings();
        return _playerControlType == null
            ? null
            : UnityEngine.Object.FindObjectOfType(_playerControlType);
    }

    private IEnumerable<object> GetNearbyTargets(object? targeting)
    {
        if (targeting == null || _targetTrackerNearbyTargetsField == null)
        {
            return Enumerable.Empty<object>();
        }

        if (_targetTrackerNearbyTargetsField.GetValue(targeting) is not IEnumerable nearbyTargets)
        {
            return Enumerable.Empty<object>();
        }

        return nearbyTargets
            .Cast<object>()
            .Where(IsLiveUnityObject);
    }

    private static bool MatchesCharacterId(object? character, string targetId)
    {
        return character is UnityEngine.Object unityObject
               && unityObject != null
               && string.Equals(unityObject.GetInstanceID().ToString(), targetId, StringComparison.Ordinal);
    }

    private static bool IsLiveUnityObject(object? value)
    {
        return value is UnityEngine.Object unityObject && unityObject != null;
    }

    private bool TapKey(ushort virtualKey)
    {
        return KeyDown(virtualKey) && KeyUp(virtualKey);
    }

    private static ushort ParseVirtualKey(string configured)
    {
        if (string.Equals(configured, "TAB", StringComparison.OrdinalIgnoreCase))
        {
            return 0x09;
        }

        if (configured.Length == 1)
        {
            return char.ToUpperInvariant(configured[0]);
        }

        return 0x09;
    }

    private static bool KeyDown(ushort keyCode)
    {
        return SendKey(keyCode, 0);
    }

    private static bool KeyUp(ushort keyCode)
    {
        return SendKey(keyCode, 0x0002);
    }

    private static bool SendKey(ushort keyCode, uint flags)
    {
        var input = new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = keyCode,
                    dwFlags = flags
                }
            }
        };

        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
