using System;
using System.Collections.Concurrent;
using BepInEx.Logging;
using ErenshorBuddy.Contracts;
using ErenshorBuddy.Core;

namespace ErenshorBuddy.Plugin;

internal sealed class BotPluginController : IDisposable, IRuntimeEventSink
{
    private readonly ManualLogSource _logger;
    private readonly PluginSettings _settings;
    private readonly GoalDrivenAgent _agent = new();
    private readonly BotRuntimeMemory _memory = new();
    private readonly FileBotRuntime _runtime;
    private readonly IGameWorldAdapter _worldAdapter;
    private readonly IBotActuator _actuator;
    private readonly ConcurrentQueue<BotCommandEnvelope> _pendingCommands = new();

    private BotRunState _state = BotRunState.Idle;
    private BotProfile? _activeProfile;
    private AlertCode _alertCode = AlertCode.None;
    private string? _alertDetail;
    private string _currentAction = "Idle";
    private GameSnapshot? _lastSnapshot;
    private DateTime _lastStatusPushUtc = DateTime.MinValue;
    private DateTime _lastSnapshotPushUtc = DateTime.MinValue;
    private DateTime _lastHeartbeatPushUtc = DateTime.MinValue;
    private string _lastTickSource = string.Empty;
    private DateTime _lastTickAtUtc = DateTime.MinValue;
    private int _consecutiveTickFailures;
    private long _tickCount;

    public BotPluginController(ManualLogSource logger, PluginSettings settings)
    {
        _logger = logger;
        _settings = settings;
        _worldAdapter = new ReflectionGameWorldAdapter(_logger, settings);
        _actuator = new WindowsInputActuator(_logger, settings);
        _runtime = new FileBotRuntime(settings.RuntimeDirectory, _pendingCommands, logger);
    }

    public void Start()
    {
        _runtime.Start();
        PublishLog($"Runtime directory: {_settings.RuntimeDirectory}");
    }

    public void Tick(string source, DateTime tickUtc)
    {
        _lastTickSource = source;
        _lastTickAtUtc = tickUtc;
        _consecutiveTickFailures = 0;
        _tickCount++;
        if (tickUtc - _lastHeartbeatPushUtc >= TimeSpan.FromSeconds(1))
        {
            _runtime.PublishHeartbeat(new RuntimeHeartbeat
            {
                TimestampUtc = tickUtc,
                TickCount = _tickCount,
                Phase = "TickStart",
                LastTickSource = _lastTickSource,
                LastTickUtc = _lastTickAtUtc,
                ConsecutiveTickFailures = _consecutiveTickFailures
            });
            _lastHeartbeatPushUtc = tickUtc;
        }

        _runtime.DrainCommands();
        DrainCommands();

        _lastSnapshot = _worldAdapter.CaptureSnapshot();
        if (tickUtc - _lastSnapshotPushUtc >= TimeSpan.FromMilliseconds(250))
        {
            PublishSnapshot(_lastSnapshot);
            _lastSnapshotPushUtc = tickUtc;
        }

        if (_state == BotRunState.Running && _activeProfile != null)
        {
            ExecuteAgentTick(_activeProfile, _lastSnapshot);
        }

        if (tickUtc - _lastStatusPushUtc >= TimeSpan.FromSeconds(1))
        {
            PublishStatus(BuildStatus());
            _lastStatusPushUtc = tickUtc;
        }
    }

    public void Dispose()
    {
        _runtime.Dispose();
        _actuator.StopAll();
    }

    public void PublishSnapshot(GameSnapshot snapshot)
    {
        _runtime.Publish(new PluginEventEnvelope
        {
            EventType = PluginEventType.Snapshot,
            Snapshot = snapshot
        });
    }

    public void PublishStatus(BotStatusPayload status)
    {
        _runtime.Publish(new PluginEventEnvelope
        {
            EventType = PluginEventType.Status,
            Status = status
        });
    }

    public void PublishLog(string message)
    {
        _runtime.Publish(new PluginEventEnvelope
        {
            EventType = PluginEventType.Log,
            Message = message
        });
    }

    public void ReportTickException(string source, DateTime tickUtc, Exception ex)
    {
        _lastTickSource = source;
        _lastTickAtUtc = tickUtc;
        _consecutiveTickFailures++;
        PublishLog($"Tick exception: {ex}");
        _runtime.PublishHeartbeat(new RuntimeHeartbeat
        {
            TimestampUtc = tickUtc,
            TickCount = _tickCount,
            Phase = "TickException",
            LastTickSource = _lastTickSource,
            LastTickUtc = _lastTickAtUtc,
            ConsecutiveTickFailures = _consecutiveTickFailures
        });
        _lastHeartbeatPushUtc = tickUtc;
    }

    private void DrainCommands()
    {
        while (_pendingCommands.TryDequeue(out var command))
        {
            switch (command.CommandType)
            {
                case BotCommandType.StartProfile:
                    if (command.Profile == null)
                    {
                        SetAlert(AlertCode.ActionFailure, "StartProfile was received without a profile payload.");
                        break;
                    }

                    _activeProfile = command.Profile;
                    _memory.StartedAtUtc = DateTime.UtcNow;
                    _memory.Counters.Kills = 0;
                    _memory.Counters.ConsumablesUsed = 0;
                    _memory.Counters.Elapsed = TimeSpan.Zero;
                    _memory.LastTargetId = null;
                    _memory.LastOpenedTargetId = null;
                    _memory.AutoAttackTargetId = null;
                    _memory.ConsecutiveActionFailures = 0;
                    _state = BotRunState.Running;
                    _alertCode = AlertCode.None;
                    _alertDetail = null;
                    _currentAction = $"Running profile '{_activeProfile.Name}'";
                    PublishLog(_currentAction);
                    break;

                case BotCommandType.Stop:
                    StopBot("Stopped by companion command.");
                    break;

                case BotCommandType.Pause:
                    _state = BotRunState.Paused;
                    _currentAction = "Paused";
                    _actuator.StopAll();
                    break;

                case BotCommandType.Resume:
                    if (_activeProfile != null)
                    {
                        _state = BotRunState.Running;
                        _alertCode = AlertCode.None;
                        _alertDetail = null;
                        _currentAction = $"Resumed profile '{_activeProfile.Name}'";
                    }
                    break;

                case BotCommandType.RequestSnapshot:
                    if (_lastSnapshot != null)
                    {
                        PublishSnapshot(_lastSnapshot);
                    }
                    break;

                case BotCommandType.AcknowledgeAlert:
                    _alertCode = AlertCode.None;
                    _alertDetail = null;
                    _state = _activeProfile == null ? BotRunState.Idle : BotRunState.Paused;
                    _currentAction = "Alert acknowledged";
                    break;
            }
        }
    }

    private void ExecuteAgentTick(BotProfile profile, GameSnapshot snapshot)
    {
        _memory.Counters.Elapsed = DateTime.UtcNow - _memory.StartedAtUtc;

        if (_memory.LastTargetId != null
            && snapshot.CurrentTarget != null
            && snapshot.CurrentTarget.IsDead
            && snapshot.CurrentTarget.Id == _memory.LastTargetId)
        {
            _memory.Counters.Kills++;
            _memory.LastTargetId = null;
            _memory.LastOpenedTargetId = null;
            _memory.AutoAttackTargetId = null;
        }

        if (snapshot.CurrentTarget != null
            && !snapshot.CurrentTarget.IsDead
            && !string.Equals(_memory.LastTargetId, snapshot.CurrentTarget.Id, StringComparison.Ordinal))
        {
            _memory.LastTargetId = snapshot.CurrentTarget.Id;
            _memory.LastOpenedTargetId = null;
            _memory.AutoAttackTargetId = null;
        }

        var decision = _agent.Decide(profile, snapshot, _memory);
        _currentAction = decision.Reason ?? decision.DecisionType.ToString();

        var success = true;
        switch (decision.DecisionType)
        {
            case AgentDecisionType.Idle:
                return;

            case AgentDecisionType.AcquireTarget:
                success = _actuator.AcquireTarget(profile, snapshot);
                break;

            case AgentDecisionType.UseAbility:
                if (string.IsNullOrWhiteSpace(decision.AbilityId))
                {
                    success = false;
                    break;
                }

                var abilityId = decision.AbilityId!;
                success = _actuator.UseAbility(abilityId, profile, snapshot);
                if (success && snapshot.CurrentTarget != null)
                {
                    _memory.LastOpenedTargetId = snapshot.CurrentTarget.Id;
                }
                break;

            case AgentDecisionType.StartAutoAttack:
                success = _actuator.StartAutoAttack(snapshot);
                if (success && snapshot.CurrentTarget != null)
                {
                    _memory.AutoAttackTargetId = snapshot.CurrentTarget.Id;
                }
                break;

            case AgentDecisionType.Reposition:
                success = _actuator.MoveTowardTarget(snapshot);
                break;

            case AgentDecisionType.Loot:
                success = _actuator.Loot(snapshot);
                break;

            case AgentDecisionType.Stop:
                StopBot(decision.Reason ?? "Stop condition reached.");
                return;

            case AgentDecisionType.RaiseAlert:
                SetAlert(MapAlert(snapshot, decision), decision.Reason ?? "Unknown alert.");
                return;
        }

        if (!success)
        {
            _memory.ConsecutiveActionFailures++;
            if (_memory.ConsecutiveActionFailures >= 3)
            {
                SetAlert(AlertCode.ActionFailure, $"Action failed repeatedly while {_currentAction.ToLowerInvariant()}");
            }
        }
        else
        {
            _memory.ConsecutiveActionFailures = 0;
        }
    }

    private BotStatusPayload BuildStatus()
    {
        return new BotStatusPayload
        {
            State = _state,
            ProfileName = _activeProfile?.Name,
            CurrentAction = _currentAction,
            AlertCode = _alertCode,
            AlertDetail = _alertDetail,
            Counters = new SessionCounters
            {
                Kills = _memory.Counters.Kills,
                ConsumablesUsed = _memory.Counters.ConsumablesUsed,
                Elapsed = _memory.Counters.Elapsed
            }
        };
    }

    private void StopBot(string reason)
    {
        _state = BotRunState.Idle;
        _currentAction = reason;
        _activeProfile = null;
        _memory.LastTargetId = null;
        _memory.LastOpenedTargetId = null;
        _memory.AutoAttackTargetId = null;
        _actuator.StopAll();
        PublishLog(reason);
    }

    private void SetAlert(AlertCode code, string detail)
    {
        _state = BotRunState.Alerting;
        _alertCode = code;
        _alertDetail = detail;
        _currentAction = detail;
        _actuator.StopAll();
        PublishLog($"Alert: {detail}");
    }

    private static AlertCode MapAlert(GameSnapshot snapshot, AgentDecision decision)
    {
        if (snapshot.ErrorFlags.WrongZone)
        {
            return AlertCode.WrongZone;
        }

        if (snapshot.ErrorFlags.Stuck)
        {
            return AlertCode.Stuck;
        }

        if (snapshot.IsUiBlocked || snapshot.ErrorFlags.UiBlocked)
        {
            return AlertCode.UiBlocked;
        }

        if (snapshot.ErrorFlags.LostTarget || (snapshot.IsInCombat && snapshot.CurrentTarget == null))
        {
            return AlertCode.LostTarget;
        }

        return decision.DecisionType == AgentDecisionType.Stop
            ? AlertCode.StopConditionReached
            : AlertCode.ActionFailure;
    }
}
