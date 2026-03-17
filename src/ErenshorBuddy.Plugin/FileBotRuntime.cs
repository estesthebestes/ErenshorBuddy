using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using ErenshorBuddy.Contracts;
using Newtonsoft.Json;

namespace ErenshorBuddy.Plugin;

internal sealed class FileBotRuntime : IDisposable
{
    private readonly string _runtimeDirectory;
    private readonly string _commandsDirectory;
    private readonly string _statusPath;
    private readonly string _snapshotPath;
    private readonly string _logPath;
    private readonly string _heartbeatPath;
    private readonly ConcurrentQueue<BotCommandEnvelope> _commands;
    private readonly ManualLogSource _logger;

    public FileBotRuntime(string runtimeDirectory, ConcurrentQueue<BotCommandEnvelope> commands, ManualLogSource logger)
    {
        _runtimeDirectory = runtimeDirectory;
        _commandsDirectory = Path.Combine(runtimeDirectory, "commands");
        _statusPath = Path.Combine(runtimeDirectory, "status.json");
        _snapshotPath = Path.Combine(runtimeDirectory, "snapshot.json");
        _logPath = Path.Combine(runtimeDirectory, "events.log");
        _heartbeatPath = Path.Combine(runtimeDirectory, "heartbeat.json");
        _commands = commands;
        _logger = logger;
    }

    public void Start()
    {
        Directory.CreateDirectory(_runtimeDirectory);
        Directory.CreateDirectory(_commandsDirectory);
        File.WriteAllText(_statusPath, string.Empty);
        File.WriteAllText(_snapshotPath, string.Empty);
        File.WriteAllText(_heartbeatPath, string.Empty);
        if (!File.Exists(_logPath))
        {
            File.WriteAllText(_logPath, string.Empty);
        }
    }

    public void PublishHeartbeat(RuntimeHeartbeat heartbeat)
    {
        try
        {
            WriteJson(_heartbeatPath, heartbeat);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to publish heartbeat: {ex.Message}");
        }
    }

    public void Publish(PluginEventEnvelope envelope)
    {
        try
        {
            switch (envelope.EventType)
            {
                case PluginEventType.Status when envelope.Status != null:
                    WriteJson(_statusPath, envelope.Status);
                    break;

                case PluginEventType.Snapshot when envelope.Snapshot != null:
                    WriteJson(_snapshotPath, envelope.Snapshot);
                    break;

                case PluginEventType.Log:
                    var line = $"[{DateTime.Now:HH:mm:ss}] {envelope.Message ?? string.Empty}{Environment.NewLine}";
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to publish runtime event: {ex.Message}");
        }
    }

    public void DrainCommands()
    {
        if (!Directory.Exists(_commandsDirectory))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.GetFiles(_commandsDirectory, "*.json")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to enumerate command files: {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file, Encoding.UTF8);
                var command = JsonConvert.DeserializeObject<BotCommandEnvelope>(json);
                if (command != null)
                {
                    _commands.Enqueue(command);
                }

                File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to process command file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
    }

    private static void WriteJson<T>(string path, T payload)
    {
        var tempPath = $"{path}.tmp";
        var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }
}

internal sealed class RuntimeHeartbeat
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public long TickCount { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string LastTickSource { get; set; } = string.Empty;
    public DateTime LastTickUtc { get; set; }
    public int? ConsecutiveTickFailures { get; set; }
}
