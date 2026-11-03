using ApeRadar.History;
using Xunit;

namespace ApeRadar.Tests;

public sealed class ReplayBattleClockTests
{
    [Fact]
    public void PreparationToActiveToEnded_ExcludesPreparationAndPostBattleWait()
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(0, 2);
        clock.ObserveStage(0, 1);
        Assert.Null(clock.StartedAt);
        Assert.Null(clock.DurationSeconds);

        clock.ObserveStage(44.34092, 0);
        Assert.Equal(44.34092, clock.StartedAt);
        Assert.Null(clock.DurationSeconds);
        Assert.Equal(550.61813, clock.SurvivalUntil(594.95905)!.Value, 5);

        clock.ObserveStage(610.0885, 3);
        clock.ObserveStage(618.8561, 3);
        Assert.True(clock.HasEnded);
        Assert.Equal(565.74758, clock.DurationSeconds!.Value, 5);
        Assert.Equal(610.0885, clock.EndedAt);
    }

    [Fact]
    public void ZeroTimestampDuplicateEndFromReplayTail_PreservesConfirmedBoundaries()
    {
        ReplayBattleClock clock = CompleteClock();
        clock.ObserveStage(0, 3);
        Assert.False(clock.Invalid);
        Assert.True(clock.HasEnded);
        Assert.Equal(30, clock.StartedAt);
        Assert.Equal(630, clock.EndedAt);
        Assert.Equal(600, clock.DurationSeconds);
    }

    [Fact]
    public void DuplicateActiveSnapshot_DoesNotMoveTheBattleOrigin()
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(0, 1);
        clock.ObserveStage(30, 0);
        clock.ObserveStage(200, 0);
        clock.ObserveStage(0, 0);
        clock.ObserveStage(630, 3);
        Assert.Equal(30, clock.StartedAt);
        Assert.Equal(600, clock.DurationSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MissingPreparationToActiveTransition_DoesNotInventAnOrigin(int initialStage)
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(0, initialStage);
        clock.ObserveStage(5, 0);
        clock.ObserveStage(649.478, 3);
        Assert.True(clock.HasEnded);
        Assert.Null(clock.StartedAt);
        Assert.Null(clock.DurationSeconds);
        Assert.Null(clock.SurvivalUntil(400));
    }

    [Fact]
    public void Archived158Reconnect_InitialActiveAndBufferedCountdownHaveNoOrigin()
    {
        // Archived 2026-10-07 20:32:50 event excerpt, not a new live-game run:
        // initial stage 0 at packet time 0; countdown snapshots 978..973 also
        // arrive at packet time 0. Final result is 876 s, not the 649 s fragment.
        ReplayBattleClock clock = new();
        foreach (int ignoredCountdown in new[] { 978, 977, 976, 975, 974, 973 })
            clock.ObserveStage(0, 0);
        clock.ObserveStage(649.478, 3);
        Assert.True(clock.HasEnded);
        Assert.Null(clock.DurationSeconds);
        Assert.Null(clock.SurvivalUntil(649.478));
    }

    [Fact]
    public void EarlyDeathWithoutEnd_HasRelativeSurvivalButNoBattleDuration()
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(0, 1);
        clock.ObserveStage(30, 0);
        Assert.Equal(0, clock.SurvivalUntil(30));
        Assert.Equal(150, clock.SurvivalUntil(180));
        Assert.Null(clock.DurationSeconds);
        Assert.False(clock.HasEnded);
    }

    [Fact]
    public void Archived158PostEndDeath_IsCappedAtCombatEnd()
    {
        // Archived 2026-10-07 22:04:12 event excerpt. The own ship dies 8.16 s
        // after stage 3; the validated final result still says survived=false.
        ReplayBattleClock clock = new();
        clock.ObserveStage(0, 1);
        clock.ObserveStage(31.649067, 0);
        clock.ObserveStage(894.65234, 3);
        Assert.Equal(863.003273, clock.SurvivalUntil(902.8083)!.Value, 6);
        Assert.Equal(clock.DurationSeconds, clock.SurvivalUntil(902.8083));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void ReversedStageTransition_InvalidatesTheClock(int before, int after)
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(10, before);
        clock.ObserveStage(20, after);
        Assert.True(clock.Invalid);
        Assert.False(clock.HasEnded);
        Assert.Null(clock.DurationSeconds);
        Assert.Null(clock.SurvivalUntil(100));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void InvalidObservationTime_InvalidatesEvenADuplicateStage(double time)
    {
        ReplayBattleClock clock = CompleteClock();
        clock.ObserveStage(time, 3);
        Assert.True(clock.Invalid);
        Assert.Null(clock.StartedAt);
        Assert.Null(clock.EndedAt);
        Assert.Null(clock.DurationSeconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void UnknownStage_InvalidatesTheClock(int stage)
    {
        ReplayBattleClock clock = CompleteClock();
        clock.ObserveStage(700, stage);
        Assert.True(clock.Invalid);
        Assert.Null(clock.DurationSeconds);
    }

    [Fact]
    public void StageChangeWithRegressedTime_CannotCreateAnEarlierBattleOrigin()
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(100, 1);
        clock.ObserveStage(50, 0);
        clock.ObserveStage(700, 3);
        Assert.True(clock.Invalid);
        Assert.Null(clock.StartedAt);
        Assert.Null(clock.DurationSeconds);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(3630.01)]
    public void NegativeZeroOrUnboundedDuration_IsUnavailable(double end)
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(0, 1);
        clock.ObserveStage(30, 0);
        clock.ObserveStage(end, 3);
        Assert.True(clock.Invalid);
        Assert.Null(clock.DurationSeconds);
        Assert.Null(clock.SurvivalUntil(50));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(29.99)]
    public void InvalidDeathTime_DoesNotCreateSurvivalOrEraseAValidBattle(double death)
    {
        ReplayBattleClock clock = CompleteClock();
        Assert.Null(clock.SurvivalUntil(death));
        Assert.False(clock.Invalid);
        Assert.Equal(600, clock.DurationSeconds);
    }

    [Fact]
    public void UnboundedDeathWithoutAnEnd_RemainsUnknown()
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(0, 1);
        clock.ObserveStage(30, 0);
        Assert.Null(clock.SurvivalUntil(3630.01));
        clock.ObserveStage(3630, 3);
        Assert.Equal(3600, clock.DurationSeconds);
        Assert.Equal(3600, clock.SurvivalUntil(3630.01));
    }

    [Fact]
    public void DifferentReplayInstances_DoNotShareClockState()
    {
        BattleHistoryReplay first = new(), second = new();
        first.BattleClock.ObserveStage(0, 1);
        second.BattleClock.ObserveStage(0, 0);
        first.BattleClock.ObserveStage(45, 0);
        second.BattleClock.ObserveStage(649, 3);
        first.BattleClock.ObserveStage(945, 3);
        Assert.Equal(900, first.BattleClock.DurationSeconds);
        Assert.Null(second.BattleClock.DurationSeconds);
        Assert.NotSame(first.BattleClock, second.BattleClock);
        second.BattleClock.ObserveStage(650, 1);
        Assert.True(second.BattleClock.Invalid);
        Assert.False(first.BattleClock.Invalid);
        Assert.Equal(900, first.BattleClock.DurationSeconds);
    }

    private static ReplayBattleClock CompleteClock()
    {
        ReplayBattleClock clock = new();
        clock.ObserveStage(0, 1);
        clock.ObserveStage(30, 0);
        clock.ObserveStage(630, 3);
        return clock;
    }
}
