using ApeRadar.History;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class BattleResultsPresentationTests
{
    [Theory]
    [InlineData(true, 50_000, "2.00")]
    [InlineData(true, 0, "-")]
    [InlineData(false, 50_000, null)]
    public async Task BattleDetails_ShowVerifiedTakenAndRatioOnlyWhenEnabled(bool enabled, long taken, string? expectedRatio)
    {
        bool previous = ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics;
        try
        {
            ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics = enabled;
            HistoryPagingTests.ReadRepository repository = new(1);
            repository.Records[0].Damage = 100_000;
            repository.AdvancedMetrics[1] = new BattleAdvancedMetrics
            {
                BattleId = 1, SpottingDamage = 12_345, SpottingDamageAvailability = MetricAvailability.Stable,
                DamageTaken = taken, DamageTakenAvailability = MetricAvailability.Experimental, ParserSchemaVersion = "5"
            };
            HistoryAnalysisService analysis = new();
            using HistoryViewModel vm = new(repository, analysis, new SessionAnalysisService(analysis),
                new ImprovementInsightService(analysis), new HistoryPagingTests.IdleTrackingCoordinator(), "Microsoft YaHei");
            vm.SelectedBattle = new HistoryRowViewModel(repository.Records[0], null, null, null);
            await vm.BattleDetailLoadTask;

            Assert.Contains("Spotting damage: 12345", vm.BattleDetailAdvancedSummary);
            if (enabled)
            {
                Assert.Contains($"Damage taken (experimental): {taken}", vm.BattleDetailAdvancedSummary);
                Assert.Contains($"Trade ratio (experimental): {expectedRatio}", vm.BattleDetailAdvancedSummary);
                Assert.Contains($"Trade ratio (experimental): {expectedRatio}", vm.BattleDetailMetrics);
            }
            else
            {
                Assert.DoesNotContain("Damage taken", vm.BattleDetailAdvancedSummary);
                Assert.DoesNotContain("Trade ratio", vm.BattleDetailAdvancedSummary);
                Assert.DoesNotContain("Trade ratio", vm.BattleDetailMetrics);
            }
        }
        finally { ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics = previous; }
    }
}
