using ErenshorBuddy.Contracts;

namespace ErenshorBuddy.Core;

public interface IGameWorldAdapter
{
    GameSnapshot CaptureSnapshot();
}

public interface IBotActuator
{
    bool AcquireTarget(BotProfile profile, GameSnapshot snapshot);
    bool StartAutoAttack(GameSnapshot snapshot);
    bool UseAbility(string abilityId, BotProfile profile, GameSnapshot snapshot);
    bool MoveTowardTarget(GameSnapshot snapshot);
    bool Loot(GameSnapshot snapshot);
    void StopAll();
}

public interface IRuntimeEventSink
{
    void PublishSnapshot(GameSnapshot snapshot);
    void PublishStatus(BotStatusPayload status);
    void PublishLog(string message);
}
