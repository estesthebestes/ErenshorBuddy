using System.IO.Pipes;
using System.Text;
using ErenshorBuddy.Contracts;
using Newtonsoft.Json;

namespace ErenshorBuddy.Companion;

internal sealed class NamedPipeCompanionClient : IDisposable
{
    private NamedPipeClientStream? _client;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public event Action<PluginEventEnvelope>? EventReceived;

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _client.ConnectAsync(3000, cancellationToken).ConfigureAwait(false);
        _writer = new StreamWriter(_client, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        _ = Task.Run(() => ReadLoopAsync(_client, _cts.Token), _cts.Token);
    }

    public async Task SendAsync(BotCommandEnvelope command, CancellationToken cancellationToken)
    {
        if (_writer == null)
        {
            throw new InvalidOperationException("Companion client is not connected.");
        }

        var json = JsonConvert.SerializeObject(command);
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _writer?.Dispose();
        _client?.Dispose();
        _cts?.Dispose();
    }

    private async Task ReadLoopAsync(NamedPipeClientStream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
        while (!cancellationToken.IsCancellationRequested && stream.IsConnected)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var envelope = JsonConvert.DeserializeObject<PluginEventEnvelope>(line);
            if (envelope != null)
            {
                EventReceived?.Invoke(envelope);
            }
        }
    }
}

