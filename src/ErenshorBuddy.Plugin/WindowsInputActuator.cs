using System;
using System.Collections.Generic;
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
        return TapKey(ParseVirtualKey(_settings.PrimaryTargetKey));
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
        KeyUp(ParseVirtualKey(_settings.MoveForwardKey));
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

