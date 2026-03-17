using System.Diagnostics;
using System.Text.RegularExpressions;
using ErenshorBuddy.Contracts;
using Newtonsoft.Json;

namespace ErenshorBuddy.Companion;

internal sealed class MainForm : Form
{
    private static readonly Regex TimestampedLogLine = new(@"^\[\d{2}:\d{2}:\d{2}\]\s", RegexOptions.Compiled);
    private readonly FileBotRuntimeClient _client = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _runtimeDirectory = new()
    {
        Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ErenshorBuddy", "Runtime")
    };
    private readonly Label _connection = new() { AutoSize = true, Text = "Disconnected" };
    private readonly Label _state = new() { AutoSize = true, Text = "State: Idle" };
    private readonly Label _zone = new() { AutoSize = true, Text = "Zone: -" };
    private readonly Label _target = new() { AutoSize = true, Text = "Target: -" };
    private readonly Label _action = new() { AutoSize = true, Text = "Action: -" };
    private readonly Label _alert = new() { AutoSize = true, Text = "Alert: -" };
    private readonly Label _counters = new() { AutoSize = true, Text = "Kills: 0 | Elapsed: 00:00:00" };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Height = 180,
        Dock = DockStyle.Fill
    };

    private readonly string _profilesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ErenshorBuddy",
        "Profiles");

    public MainForm()
    {
        Text = "ErenshorBuddy Companion";
        Width = 900;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;

        _client.EventReceived += OnEventReceived;
        _client.Disconnected += () =>
        {
            SafeUi(() => _connection.Text = "Disconnected");
            AppendLog("Disconnected from plugin.");
        };
        EnsureProfilesDirectory();

        Controls.Add(BuildLayout());
        LoadProfiles();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _client.Dispose();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 210,
            ColumnCount = 4,
            RowCount = 6,
            Padding = new Padding(12)
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var connectButton = NewButton("Connect", async (_, _) => await ConnectAsync().ConfigureAwait(false));
        var refreshButton = NewButton("Refresh Profiles", (_, _) => LoadProfiles());
        var openFolderButton = NewButton("Open Profile Folder", (_, _) => Process.Start("explorer.exe", _profilesDirectory));
        var startButton = NewButton("Start", async (_, _) => await SendStartAsync().ConfigureAwait(false));
        var pauseButton = NewButton("Pause", async (_, _) => await SendCommandAsync(BotCommandType.Pause).ConfigureAwait(false));
        var resumeButton = NewButton("Resume", async (_, _) => await SendCommandAsync(BotCommandType.Resume).ConfigureAwait(false));
        var stopButton = NewButton("Stop", async (_, _) => await SendCommandAsync(BotCommandType.Stop).ConfigureAwait(false));
        var snapshotButton = NewButton("Request Snapshot", async (_, _) => await SendCommandAsync(BotCommandType.RequestSnapshot).ConfigureAwait(false));
        var ackButton = NewButton("Ack Alert", async (_, _) => await SendCommandAsync(BotCommandType.AcknowledgeAlert).ConfigureAwait(false));

        topPanel.Controls.Add(new Label { Text = "Runtime Dir", AutoSize = true }, 0, 0);
        topPanel.Controls.Add(_runtimeDirectory, 1, 0);
        topPanel.Controls.Add(connectButton, 2, 0);
        topPanel.Controls.Add(_connection, 3, 0);

        topPanel.Controls.Add(new Label { Text = "Profile", AutoSize = true }, 0, 1);
        topPanel.Controls.Add(_profiles, 1, 1);
        topPanel.Controls.Add(refreshButton, 2, 1);
        topPanel.Controls.Add(openFolderButton, 3, 1);

        topPanel.Controls.Add(startButton, 0, 2);
        topPanel.Controls.Add(pauseButton, 1, 2);
        topPanel.Controls.Add(resumeButton, 2, 2);
        topPanel.Controls.Add(stopButton, 3, 2);

        topPanel.Controls.Add(snapshotButton, 0, 3);
        topPanel.Controls.Add(ackButton, 1, 3);
        topPanel.Controls.Add(_state, 2, 3);
        topPanel.Controls.Add(_alert, 3, 3);

        topPanel.Controls.Add(_zone, 0, 4);
        topPanel.Controls.Add(_target, 1, 4);
        topPanel.Controls.Add(_action, 2, 4);
        topPanel.Controls.Add(_counters, 3, 4);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(topPanel, 0, 0);
        root.Controls.Add(_log, 0, 1);
        return root;
    }

    private async Task ConnectAsync()
    {
        if (_client.IsConnected)
        {
            AppendLog("Already connected.");
            return;
        }

        try
        {
            await _client.ConnectAsync(_runtimeDirectory.Text.Trim(), _cts.Token).ConfigureAwait(false);
            SafeUi(() => _connection.Text = "Connected");
            AppendLog("Connected to plugin.");
        }
        catch (Exception ex)
        {
            AppendLog($"Connection failed: {ex.Message}");
        }
    }

    private async Task SendStartAsync()
    {
        if (_profiles.SelectedItem is not string selected)
        {
            AppendLog("No profile selected.");
            return;
        }

        var profile = await LoadProfileAsync(selected).ConfigureAwait(false);
        if (profile == null)
        {
            return;
        }

        await SendCommandAsync(BotCommandType.StartProfile, profile).ConfigureAwait(false);
    }

    private async Task SendCommandAsync(BotCommandType commandType, BotProfile? profile = null)
    {
        if (!_client.IsConnected)
        {
            AppendLog("Connect to the plugin before sending commands.");
            return;
        }

        try
        {
            await _client.SendAsync(new BotCommandEnvelope
            {
                CommandType = commandType,
                Profile = profile
            }, _cts.Token).ConfigureAwait(false);

            AppendLog($"Sent command: {commandType}");
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to send command: {ex.Message}");
        }
    }

    private async Task<BotProfile?> LoadProfileAsync(string selected)
    {
        var fullPath = Path.Combine(_profilesDirectory, selected);
        try
        {
            var json = await File.ReadAllTextAsync(fullPath, _cts.Token).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<BotProfile>(json);
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to load profile '{selected}': {ex.Message}");
            return null;
        }
    }

    private void LoadProfiles()
    {
        EnsureProfilesDirectory();
        var files = Directory.GetFiles(_profilesDirectory, "*.json")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name)
            .ToArray();

        _profiles.Items.Clear();
        _profiles.Items.AddRange(files!);

        if (_profiles.Items.Count > 0)
        {
            _profiles.SelectedIndex = 0;
        }

        AppendLog($"Loaded {_profiles.Items.Count} profile(s) from {_profilesDirectory}");
    }

    private void EnsureProfilesDirectory()
    {
        Directory.CreateDirectory(_profilesDirectory);
    }

    private void OnEventReceived(PluginEventEnvelope envelope)
    {
        SafeUi(() =>
        {
            switch (envelope.EventType)
            {
                case PluginEventType.Status:
                    if (envelope.Status != null)
                    {
                        _state.Text = $"State: {envelope.Status.State}";
                        _action.Text = $"Action: {envelope.Status.CurrentAction}";
                        _alert.Text = $"Alert: {envelope.Status.AlertCode} {envelope.Status.AlertDetail}";
                        _counters.Text = $"Kills: {envelope.Status.Counters.Kills} | Elapsed: {envelope.Status.Counters.Elapsed:hh\\:mm\\:ss}";
                    }
                    break;

                case PluginEventType.Snapshot:
                    if (envelope.Snapshot != null)
                    {
                        _zone.Text = $"Zone: {envelope.Snapshot.ZoneId}";
                        _target.Text = envelope.Snapshot.CurrentTarget == null
                            ? "Target: -"
                            : $"Target: {envelope.Snapshot.CurrentTarget.Name} ({envelope.Snapshot.CurrentTarget.HealthPercent:0}%)";
                    }
                    break;

                case PluginEventType.Log:
                    AppendLog(envelope.Message ?? string.Empty, addTimestamp: !LooksTimestamped(envelope.Message));
                    break;
            }
        });
    }

    private void AppendLog(string message, bool addTimestamp = true)
    {
        SafeUi(() =>
        {
            var rendered = addTimestamp
                ? $"[{DateTime.Now:HH:mm:ss}] {message}"
                : message;
            _log.AppendText($"{rendered}{Environment.NewLine}");
        });
    }

    private void SafeUi(Action action)
    {
        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private static Button NewButton(string label, EventHandler onClick)
    {
        var button = new Button
        {
            Text = label,
            Width = 140,
            Height = 32,
            Dock = DockStyle.Fill
        };
        button.Click += onClick;
        return button;
    }

    private static bool LooksTimestamped(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) && TimestampedLogLine.IsMatch(message);
    }
}
