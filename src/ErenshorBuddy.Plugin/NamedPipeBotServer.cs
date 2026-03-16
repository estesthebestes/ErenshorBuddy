using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using ErenshorBuddy.Contracts;
using Newtonsoft.Json;

namespace ErenshorBuddy.Plugin;

internal sealed class NamedPipeBotServer : IDisposable
{
    private readonly string _pipeName;
    private readonly ConcurrentQueue<BotCommandEnvelope> _commands;
    private readonly ManualLogSource _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writerLock = new();

    private Task? _acceptLoop;
    private StreamWriter? _currentWriter;

    public NamedPipeBotServer(string pipeName, ConcurrentQueue<BotCommandEnvelope> commands, ManualLogSource logger)
    {
        _pipeName = pipeName;
        _commands = commands;
        _logger = logger;
    }

    public void Start()
    {
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public void Publish(PluginEventEnvelope envelope)
    {
        var json = JsonConvert.SerializeObject(envelope);
        lock (_writerLock)
        {
            if (_currentWriter == null)
            {
                return;
            }

            try
            {
                _currentWriter.WriteLine(json);
                _currentWriter.Flush();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to write IPC message: {ex.Message}");
                _currentWriter = null;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_writerLock)
        {
            _currentWriter?.Dispose();
            _currentWriter = null;
        }
        try
        {
            _acceptLoop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Best-effort shutdown for the background IPC loop.
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _logger.LogInfo("Companion app connected.");
            using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true };

            lock (_writerLock)
            {
                _currentWriter = writer;
            }

            while (!_cts.IsCancellationRequested && server.IsConnected)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (IOException)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var command = JsonConvert.DeserializeObject<BotCommandEnvelope>(line);
                    if (command != null)
                    {
                        _commands.Enqueue(command);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning($"Invalid IPC payload: {ex.Message}");
                }
            }

            lock (_writerLock)
            {
                if (ReferenceEquals(_currentWriter, writer))
                {
                    _currentWriter = null;
                }
            }

            _logger.LogInfo("Companion app disconnected.");
        }
    }
}
