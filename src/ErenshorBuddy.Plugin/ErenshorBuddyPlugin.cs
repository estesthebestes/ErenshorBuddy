using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;

namespace ErenshorBuddy.Plugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ErenshorBuddyPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.hunter.erenshorbuddy";
    public const string PluginName = "ErenshorBuddy";
    public const string PluginVersion = "0.1.0";

    private static Harmony? _harmony;
    private static BotPluginController? _controller;
    private static ManualLogSource? _logger;
    private static bool _loggedFirstTick;
    private static string? _lastTickSource;
    private static DateTime _lastTickUtc = DateTime.MinValue;

    private void Awake()
    {
        _logger = Logger;

        if (_harmony == null)
        {
            _harmony = new Harmony(PluginGuid);
            PatchLifecycleHooks(_harmony);
        }

        if (_controller == null)
        {
            var settings = PluginSettings.Bind(Config);
            _controller = new BotPluginController(Logger, settings);
            _controller.Start();

            Logger.LogInfo($"{PluginName} initialized.");
        }
    }

    private void OnApplicationQuit()
    {
        _controller?.Dispose();
        _controller = null;

        if (_harmony != null)
        {
            _harmony.UnpatchSelf();
            _harmony = null;
        }

        _logger = null;
        _loggedFirstTick = false;
        _lastTickSource = null;
        _lastTickUtc = DateTime.MinValue;
    }

    private static void TickController(string source)
    {
        if (_controller == null)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        if (nowUtc - _lastTickUtc < TimeSpan.FromMilliseconds(50))
        {
            return;
        }

        _lastTickUtc = nowUtc;
        if (!string.Equals(_lastTickSource, source, StringComparison.Ordinal))
        {
            if (!_loggedFirstTick)
            {
                _logger?.LogInfo($"Plugin tick loop started via {source}.");
            }
            else
            {
                _logger?.LogInfo($"Plugin tick source changed to {source}.");
            }

            _lastTickSource = source;
        }

        if (!_loggedFirstTick)
        {
            _loggedFirstTick = true;
        }

        try
        {
            _controller.Tick(source, nowUtc);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"ErenshorBuddy tick failed via {source}: {ex}");
            _controller.ReportTickException(source, nowUtc, ex);
        }
    }

    private static void TickFromPatch(string source)
    {
        TickController(source);
    }

    private static void PatchLifecycleHooks(Harmony harmony)
    {
        PatchUpdateHook(harmony, "MainMenu", "Update", nameof(MainMenuUpdatePostfix));
        PatchUpdateHook(harmony, "GameManager", "Update", nameof(GameManagerUpdatePostfix));
    }

    private static void PatchUpdateHook(Harmony harmony, string typeName, string methodName, string postfixName)
    {
        var targetType = AccessTools.TypeByName(typeName);
        var targetMethod = targetType == null
            ? null
            : AccessTools.DeclaredMethod(targetType, methodName);

        if (targetMethod == null)
        {
            _logger?.LogWarning($"Failed to bind Harmony patch for {typeName}.{methodName}.");
            return;
        }

        var postfix = new HarmonyMethod(typeof(ErenshorBuddyPlugin).GetMethod(postfixName, BindingFlags.NonPublic | BindingFlags.Static));
        harmony.Patch(targetMethod, postfix: postfix);
    }

    private static void MainMenuUpdatePostfix()
    {
        TickFromPatch("MainMenu.Update");
    }

    private static void GameManagerUpdatePostfix()
    {
        TickFromPatch("GameManager.Update");
    }
}

internal sealed class PluginSettings
{
    public string RuntimeDirectory { get; private set; } = string.Empty;
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
            RuntimeDirectory = config.Bind(
                "IPC",
                "RuntimeDirectory",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ErenshorBuddy", "Runtime"),
                "Directory used for file-based IPC with the companion app.").Value,
            PrimaryTargetKey = config.Bind("Input", "TargetKey", "TAB", "Key used to acquire targets.").Value,
            LootKey = config.Bind("Input", "LootKey", "F", "Key used to loot.").Value,
            MoveForwardKey = config.Bind("Input", "MoveForwardKey", "W", "Key used for short repositioning pulses.").Value,
            PlayerTag = config.Bind("World", "PlayerTag", "Player", "Tag used to locate the player GameObject.").Value,
            EnemyTag = config.Bind("World", "EnemyTag", "Enemy", "Tag used to locate hostile GameObjects.").Value,
            EnableDebugLogs = config.Bind("General", "EnableDebugLogs", false, "Emit extra trace logs.").Value
        };
    }
}
