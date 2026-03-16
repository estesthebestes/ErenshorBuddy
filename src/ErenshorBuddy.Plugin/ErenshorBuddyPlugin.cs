using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace ErenshorBuddy.Plugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ErenshorBuddyPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.hunter.erenshorbuddy";
    public const string PluginName = "ErenshorBuddy";
    public const string PluginVersion = "0.1.0";

    private Harmony? _harmony;
    private BotPluginController? _controller;

    private void Awake()
    {
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();

        var settings = PluginSettings.Bind(Config);
        _controller = new BotPluginController(Logger, settings);
        _controller.Start();

        Logger.LogInfo($"{PluginName} initialized.");
    }

    private void Update()
    {
        _controller?.Tick();
    }

    private void OnDestroy()
    {
        _controller?.Dispose();
        _harmony?.UnpatchSelf();
    }
}

internal sealed class PluginSettings
{
    public string PipeName { get; private set; } = "ErenshorBuddyPipe";
    public string PrimaryTargetKey { get; private set; } = "TAB";
    public string LootKey { get; private set; } = "F";
    public string MoveForwardKey { get; private set; } = "W";
    public string PlayerTag { get; private set; } = "Player";
    public string EnemyTag { get; private set; } = "Enemy";
    public bool EnableDebugLogs { get; private set; }

    public static PluginSettings Bind(ConfigFile config)
    {
        return new PluginSettings
        {
            PipeName = config.Bind("IPC", "PipeName", "ErenshorBuddyPipe", "Named pipe used for the companion app.").Value,
            PrimaryTargetKey = config.Bind("Input", "TargetKey", "TAB", "Key used to acquire targets.").Value,
            LootKey = config.Bind("Input", "LootKey", "F", "Key used to loot.").Value,
            MoveForwardKey = config.Bind("Input", "MoveForwardKey", "W", "Key used for short repositioning pulses.").Value,
            PlayerTag = config.Bind("World", "PlayerTag", "Player", "Tag used to locate the player GameObject.").Value,
            EnemyTag = config.Bind("World", "EnemyTag", "Enemy", "Tag used to locate hostile GameObjects.").Value,
            EnableDebugLogs = config.Bind("General", "EnableDebugLogs", false, "Emit extra trace logs.").Value
        };
    }
}

