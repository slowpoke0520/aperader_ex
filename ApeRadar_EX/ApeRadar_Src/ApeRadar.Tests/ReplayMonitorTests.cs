using ApeRadar.History;
using Xunit;

namespace ApeRadar.Tests;

public sealed class ReplayMonitorTests
{
    [Fact]
    public void EarlyExitAfterDeath_WaitsUntilBattleCanActuallyFinishBeforeApiCheck()
    {
        DateTimeOffset started = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset exited = started.AddMinutes(7);
        BattleRecord battle = new() { StartedAt = started };
        ReplayParseResult replay = new() { ErrorCode = "BattleExitedAfterDeath", ExitedAfterDeath = true };

        DateTimeOffset next = ReplayMonitor.CalculateNextResultCheckAt(battle, replay, exited);

        Assert.Equal(started.AddMinutes(21), next);
    }

    [Fact]
    public void OldIncompleteReplay_CanBeCheckedImmediatelyBecauseBattleMustBeOver()
    {
        DateTimeOffset started = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset imported = started.AddHours(1);
        BattleRecord battle = new() { StartedAt = started };
        ReplayParseResult replay = new() { ErrorCode = "BattleNotFinished" };

        DateTimeOffset next = ReplayMonitor.CalculateNextResultCheckAt(battle, replay, imported);

        Assert.Equal(imported, next);
    }
}
