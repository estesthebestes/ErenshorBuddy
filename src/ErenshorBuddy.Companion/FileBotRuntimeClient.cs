using System.Text;
using ErenshorBuddy.Contracts;
using Newtonsoft.Json;

namespace ErenshorBuddy.Companion;

internal sealed class FileBotRuntimeClient : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private string? _runtimeDirectory;
    private DateTime _lastStatusWriteUtc = DateTime.MinValue;
    private DateTime _lastSnapshotWriteUtc = DateTime.MinValue;
    private long _lastLogLength;

    public event Action<PluginEventEnvelope>? EventReceived;
    public event Action? Disconnected;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _cts != null && _runtimeDirectory != null;
            }
        }
    }

    public Task ConnectAsync(string runtimeDirectory, CancellationToken cancellationToken)
    {
        DisposeConnection();

        Directory.CreateDirectory(runtimeDirectory);
        Directory.CreateDirectory(Path.Combine(runtimeDirectory, "commands"));
        var logPath = Path.Combine(runtimeDirectory, "events.log");
        var initialLogLength = File.Exists(logPath)
            ? new FileInfo(logPath).Length
            : 0;

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _runtimeDirectory = runtimeDirectory;
            _cts = linkedCts;
            _lastStatusWriteUtc = DateTime.MinValue;
            _lastSnapshotWriteUtc = DateTime.MinValue;
            _lastLogLength = initialLogLength;
        }

        _pollTask = Task.Run(() => PollLoop(runtimeDirectory, linkedCts.Token), linkedCts.Token);
        return Task.CompletedTask;
    }

    public Task SendAsync(BotCommandEnvelope command, CancellationToken cancellationToken)
    {
        string? runtimeDirectory;
        lock (_sync)
        {
            runtimeDirectory = _runtimeDirectory;
        }

        if (runtimeDirectory == null)
        {
            throw new InvalidOperationException("Companion client is not connected.");
        }

        var commandsDirectory = Path.Combine(runtimeDirectory, "commands");
        Directory.CreateDirectory(commandsDirectory);
        var path = Path.Combine(commandsDirectory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
        var json = JsonConvert.SerializeObject(command, Formatting.Indented);
        return File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
    }

    public void Dispose()
    {
        DisposeConnection();
    }

    private async Task PollLoop(string runtimeDirectory, CancellationToken cancellationToken)
    {
        var statusPath = Path.Combine(runtimeDirectory, "status.json");
        var snapshotPath = Path.Combine(runtimeDirectory, "snapshot.json");
        var logPath = Path.Combine(runtimeDirectory, "events.log");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PollJson<BotStatusPayload>(statusPath, ref _lastStatusWriteUtc, payload =>
                {
                    EventReceived?.Invoke(new PluginEventEnvelope
                    {
                        EventType = PluginEventType.Status,
                        Status = payload
                    });
                });

                PollJson<GameSnapshot>(snapshotPath, ref _lastSnapshotWriteUtc, payload =>
                {
                    EventReceived?.Invoke(new PluginEventEnvelope
                    {
                        EventType = PluginEventType.Snapshot,
                        Snapshot = payload
                    });
                });

                PollLog(logPath);
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            DisposeConnection();
            Disconnected?.Invoke();
        }
    }

    private void PollJson<T>(string path, ref DateTime lastWriteUtc, Action<T> onPayload)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (lastWrite <= lastWriteUtc)
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var payload = JsonConvert.DeserializeObject<T>(json);
            if (payload == null)
            {
                return;
            }

            lastWriteUtc = lastWrite;
            onPayload(payload);
        }
        catch
        {
            // Ignore transient file read races while the plugin is updating files.
        }
    }

    private void PollLog(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < _lastLogLength)
            {
                _lastLogLength = 0;
            }

            if (stream.Length == _lastLogLength)
            {
                return;
            }

            stream.Seek(_lastLogLength, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    EventReceived?.Invoke(new PluginEventEnvelope
                    {
                        EventType = PluginEventType.Log,
                        Message = line
                    });
                }
            }

            _lastLogLength = stream.Position;
        }
        catch
        {
            // Ignore transient file read races.
        }
    }

    private void DisposeConnection()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _cts;
            _cts = null;
            _runtimeDirectory = null;
        }

        cts?.Cancel();
        cts?.Dispose();
    }
}
