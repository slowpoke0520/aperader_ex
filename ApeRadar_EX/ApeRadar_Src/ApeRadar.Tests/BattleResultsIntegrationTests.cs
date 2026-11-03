using ApeRadar.History;
using Microsoft.Extensions.Logging.Abstractions;
using Nodsoft.WowsReplaysUnpack.Core.Entities;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using System.Text.Json;
using Xunit;

namespace ApeRadar.Tests;

public sealed class BattleResultsIntegrationTests
{
    [Fact]
    public void VerifiedResult_ReplacesLastEventSnapshotWithoutConfusingSpottingAndReceivedDamage()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        AddResult(replay, ReplayResultsFixture.Create((_, own, _) => own[201] = 25_097));

        ReplayBattleResults? result = Validate(replay, out string error);
        var basic = NodsoftReplayParserAdapter.FindResultMetrics(replay, result);
        BattleAdvancedMetrics advanced = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay, result);

        Assert.Empty(error);
        Assert.Equal(BattleMetricSource.ReplayExact, basic.Source);
        Assert.Equal(120_222, basic.Damage);
        Assert.Equal(2, basic.Frags);
        Assert.Equal(BattleResult.Win, basic.Result);
        Assert.Equal(7_515, advanced.SpottingDamage);
        Assert.Equal(25_097, advanced.DamageTaken);
        Assert.Equal(1_000_000, advanced.PotentialDamage);
        Assert.Equal(MetricAvailability.Experimental, advanced.DamageTakenAvailability);
        Assert.DoesNotContain(NodsoftReplayParserAdapter.FindDamageBreakdowns(replay), x => x.Direction == DamageDirection.Received);
        BattleRecord battle = new() { Damage = basic.Damage };
        Assert.Null(SessionAnalysisService.MetricValue(battle, advanced, "TradeRatio", false));
        Assert.Equal(120_222d / 25_097, SessionAnalysisService.MetricValue(battle, advanced, "TradeRatio", true)!.Value, 9);
    }

    [Fact]
    public void Controller_AcceptsResultAfterEntityFailure_WhileDiscardingUnreliableProcessMetrics()
    {
        BattleHistoryReplayController controller = new(null!, NullLogger<Entity>.Instance);
        ArenaInfo arena = JsonSerializer.Deserialize<ArenaInfo>("{\"clientVersionFromExe\":\"15,9,0,0\"}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        BattleHistoryReplay replay = controller.CreateUnpackedReplay(arena);
        replay.EntityParsingError = nameof(EndOfStreamException);
        replay.DamageStatsSeen = true;
        replay.EnemyDamageByType[1] = 116_995.91;
        replay.PotentialDamageByType[1] = 1_000_000;
        replay.DamageStatisticTypesSeen.Add(3);
        controller.HandleNetworkPacket(Packet(ReplayResultsFixture.Create((_, own, _) => own[201] = 25_097)), new ReplayUnpackerOptions());

        ReplayBattleResults? result = Validate(replay, out string error);
        Assert.NotNull(result);
        Assert.Empty(error);
        var basic = NodsoftReplayParserAdapter.FindResultMetrics(replay, result);
        BattleAdvancedMetrics advanced = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay, result);
        Assert.Equal(120_222, basic.Damage);
        Assert.Equal(BattleMetricSource.ReplayExact, basic.Source);
        Assert.Equal(25_097, advanced.DamageTaken);
        Assert.Equal(7_515, advanced.SpottingDamage);
        Assert.True(advanced.Survived);
        Assert.Equal(802, advanced.SurvivalSeconds);
        Assert.Null(advanced.PotentialDamage);
        Assert.Equal(MetricAvailability.Unavailable, advanced.PotentialDamageAvailability);
        Assert.Null(NodsoftReplayParserAdapter.FindDamage(replay));
        Assert.Empty(NodsoftReplayParserAdapter.FindDamageBreakdowns(replay));
    }

    [Fact]
    public void EntityFailureWithoutVerifiedResult_CannotLeakEarlierEventMetrics()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        replay.EntityParsingError = nameof(EndOfStreamException);
        Assert.Null(Validate(replay, out _));
        var basic = NodsoftReplayParserAdapter.FindResultMetrics(replay);
        BattleAdvancedMetrics advanced = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay);
        Assert.Null(basic.Damage);
        Assert.Null(basic.Frags);
        Assert.Equal(BattleResult.Unknown, basic.Result);
        Assert.Equal(BattleMetricSource.MetadataOnly, basic.Source);
        Assert.Null(advanced.SpottingDamage);
        Assert.Null(advanced.DamageTaken);
        Assert.Null(advanced.PotentialDamage);
        Assert.Null(advanced.Survived);
        Assert.Empty(NodsoftReplayParserAdapter.FindDamageBreakdowns(replay));
    }

    [Theory]
    [InlineData("missing", "")]
    [InlineData("invalid", "BattleResultsSchemaInvalid")]
    [InlineData("unsupported", "BattleResultsVersionUnsupported")]
    [InlineData("identity", "BattleResultsIdentityMismatch")]
    public void UnverifiedResult_KeepsEventSourceDerivedAndReceivedUnknown(string condition, string expectedError)
    {
        BattleHistoryReplay replay = WithEventSnapshot(condition == "unsupported" ? new Version(15, 10, 0) : null);
        if (condition == "invalid") AddResult(replay, JsonSerializer.SerializeToElement(new { damage = 999_999 }));
        if (condition is "unsupported" or "identity")
            AddResult(replay, ReplayResultsFixture.Create((_, own, _) => { if (condition == "identity") own[1] = "SomeoneElse"; }));

        ReplayBattleResults? result = Validate(replay, out string error);
        Assert.Null(result);
        Assert.Equal(expectedError, error);
        var basic = NodsoftReplayParserAdapter.FindResultMetrics(replay, result);
        BattleAdvancedMetrics advanced = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay, result);
        Assert.Equal(BattleMetricSource.ReplayDerived, basic.Source);
        Assert.Equal(116_996, basic.Damage);
        Assert.Equal(70_500, advanced.SpottingDamage);
        Assert.Null(advanced.DamageTaken);
        Assert.Equal(MetricAvailability.Unavailable, advanced.DamageTakenAvailability);
        Assert.Null(SessionAnalysisService.MetricValue(new BattleRecord { Damage = basic.Damage }, advanced, "TradeRatio", true));
    }

    [Fact]
    public void ArbitraryExtraJsonMatchingPlayerAndMetricNames_DoesNotBecomeAnExactResult()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        replay.ExtraJsonData.Add(JsonSerializer.SerializeToElement(new
        {
            playerName = "Self", damage = 999_999, frags = 9, result = "win", damageTaken = 1,
            nested = new { Self = new { damage = 999_999, isWinner = true } }
        }));
        Assert.Null(Validate(replay, out _));
        Assert.Equal(BattleMetricSource.ReplayDerived, NodsoftReplayParserAdapter.FindResultMetrics(replay).Source);
        Assert.Null(NodsoftReplayParserAdapter.FindAdvancedMetrics(replay).DamageTaken);
    }

    [Fact]
    public void ExactStructuredExtraJson_UsesTheSameValidationAsTheResultPacket()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        replay.ExtraJsonData.Add(ReplayResultsFixture.Create());
        Assert.NotNull(Validate(replay, out string error));
        Assert.Empty(error);
    }

    [Fact]
    public void IdenticalDuplicateResults_AreAcceptedWithoutAccumulatingDamage()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        JsonElement root = ReplayResultsFixture.Create((_, own, _) => own[201] = 25_097);
        AddResult(replay, root);
        AddResult(replay, root);
        ReplayBattleResults? result = Validate(replay, out string error);
        Assert.Empty(error);
        Assert.Equal(120_222, result!.Damage);
        Assert.Equal(25_097, result.DamageTaken);
    }

    [Fact]
    public void ConflictingValidResults_AreRejectedRatherThanChoosingTheLastPacket()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        AddResult(replay, ReplayResultsFixture.Create());
        AddResult(replay, ReplayResultsFixture.Create((_, own, _) => own[426] = 130_000));
        Assert.Null(Validate(replay, out string error));
        Assert.Equal("BattleResultsConflict", error);
        Assert.Null(NodsoftReplayParserAdapter.FindAdvancedMetrics(replay).DamageTaken);
    }

    [Fact]
    public void ResultPacketLimit_StaysInvalidEvenAfterAnotherGoodPacketArrives()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        for (int i = 0; i < 10; i++) AddResult(replay, ReplayResultsFixture.Create());
        Assert.Empty(replay.BattleResults);
        Assert.Null(Validate(replay, out string error));
        Assert.Equal("BattleResultsTooManyPackets", error);
        Assert.Null(NodsoftReplayParserAdapter.FindAdvancedMetrics(replay).DamageTaken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidPacketBody_DoesNotHideAnIndependentlyValidatedResult(bool invalidFirst)
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        BattleResultsPacket invalid = new(1, 900, new Version(15, 9, 0), null, "BattleResultsJsonInvalid");
        if (invalidFirst) BattleHistoryReplayController.ApplyBattleResults(replay, invalid);
        AddResult(replay, ReplayResultsFixture.Create());
        if (!invalidFirst) BattleHistoryReplayController.ApplyBattleResults(replay, invalid);
        Assert.NotNull(Validate(replay, out string error));
        Assert.Empty(error);
    }

    [Fact]
    public void InvalidReceivedSubtotal_DoesNotEraseVerifiedMainResultOrSubstituteSpotting()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        AddResult(replay, ReplayResultsFixture.Create((_, own, _) => own[201] = -1));
        ReplayBattleResults? result = Validate(replay, out string error);
        Assert.NotNull(result);
        Assert.Empty(error);
        Assert.Equal("BattleResultsReceivedDamageInvalid", result.DamageTakenError);
        Assert.Equal(BattleMetricSource.ReplayExact, NodsoftReplayParserAdapter.FindResultMetrics(replay, result).Source);
        BattleAdvancedMetrics advanced = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay, result);
        Assert.Equal(7_515, advanced.SpottingDamage);
        Assert.Null(advanced.DamageTaken);
        Assert.Equal(MetricAvailability.Unavailable, advanced.DamageTakenAvailability);
    }

    [Fact]
    public void KnownZeroReceivedDamage_RemainsZeroButCannotBeUsedAsTheRatioDenominator()
    {
        BattleHistoryReplay replay = WithEventSnapshot();
        AddResult(replay, ReplayResultsFixture.Create());
        ReplayBattleResults? result = Validate(replay, out _);
        BattleAdvancedMetrics advanced = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay, result);
        BattleRecord battle = new() { Damage = result!.Damage };
        Assert.Equal(0, SessionAnalysisService.MetricValue(battle, advanced, "DamageTaken", true));
        Assert.Null(SessionAnalysisService.MetricValue(battle, advanced, "TradeRatio", true));
        Assert.Null(SessionAnalysisService.MetricValue(battle, advanced, "DamageTaken", false));
    }

    private static BattleHistoryReplay WithEventSnapshot(Version? version = null)
    {
        BattleHistoryReplay replay = new()
        {
            ClientVersion = version ?? new Version(15, 9, 0), BattleEnded = true,
            BattleEndTime = 810, DamageStatsSeen = true
        };
        replay.EnemyDamageByType[1] = 116_995.91;
        replay.SpottingDamageByType[1] = 70_500;
        replay.PotentialDamageByType[1] = 1_000_000;
        replay.DamageStatisticTypesSeen.UnionWith(new[] { 0, 2, 3 });
        return replay;
    }

    private static BattleResultsPacket Packet(JsonElement root) => new(1, 811, new Version(15, 9, 0), root, "");

    private static void AddResult(BattleHistoryReplay replay, JsonElement root) =>
        BattleHistoryReplayController.ApplyBattleResults(replay, Packet(root));

    private static ReplayBattleResults? Validate(BattleHistoryReplay replay, out string error) =>
        NodsoftReplayParserAdapter.FindValidatedBattleResults(replay, "Self", "101", out error);
}
