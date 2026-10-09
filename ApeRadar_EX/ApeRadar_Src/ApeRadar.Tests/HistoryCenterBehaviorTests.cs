using ApeRadar.History;
using ApeRadar.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ApeRadar.Tests;

public sealed class HistoryCenterBehaviorTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),$"ApeRadar.HistoryCenter.{Guid.NewGuid():N}");
    private string DatabasePath=>Path.Combine(directory,"history.db");
    private static readonly DateTimeOffset Started=new(2026,10,7,12,0,0,TimeSpan.Zero);
    private static BattleRecord Battle(string key,int hour=0)=>new()
    {
        BattleKey=key,StartedAt=Started.AddHours(hour),Server="ASIA",AccountId="1",AccountName="Tester",
        ShipId="101",ShipName="Test ship",Mode="RandomBattle",MapName="Map"
    };
    private static ShipStatSnapshot Snapshot(int count)=>new()
    {
        CapturedAt=Started.AddMinutes(count-100),Provider="test",AccountId="1",ShipId="101",
        Battles=count,Wins=count-50,Damage=count*60_000,Frags=count
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApiIntervals_SurviveSingleReplayAndRepeatedChecksWithoutDoubleCounting(bool replayFirst)
    {
        SqliteHistoryRepository repository=new(DatabasePath);
        long id=await repository.UpsertDraftAsync(Battle("one"),Array.Empty<BattlePlayerRecord>(),Snapshot(100));
        ReplayParseResult replay=new()
        {
            Status=ReplayParseStatus.Parsed,FileHash="replay",ParserVersion="test",StartedAt=Started,
            Server="ASIA",AccountId="1",AccountName="Tester",ShipId="101",MapName="Map",
            Source=BattleMetricSource.ReplayExact,Damage=89_000,Frags=2,Result=BattleResult.Loss
        };
        if (replayFirst) await repository.CompleteFromReplayAsync(id,replay,"one.wowsreplay");
        await repository.ResolveFromApiAsync(id,Snapshot(100),Snapshot(103));
        if (!replayFirst) await repository.CompleteFromReplayAsync(id,replay,"one.wowsreplay");
        await repository.ResolveFromApiAsync(id,Snapshot(100),Snapshot(103));
        ApiBattleInterval interval=Assert.Single(await repository.GetApiIntervalsAsync(new HistoryQuery()));
        Assert.Equal(3,interval.BattleCount);
        Assert.Equal(180_000,interval.Damage);
        Assert.True(interval.HasCounterBounds);
        BattleRecord single=Assert.Single(await repository.GetBattlesAsync(new HistoryQuery { SingleBattlesOnly=true }));
        Assert.Equal(1,single.BattleCount);
        Assert.Equal(89_000,single.Damage);
        Assert.Equal(1,await repository.CountBattlesAsync(new HistoryQuery { SingleBattlesOnly=true }));
        Assert.Equal(1,new HistoryAnalysisService().CalculateSummary(new[] { single }).RecordedBattles);
    }

    [Fact]
    public async Task SingleBattleQuery_FiltersIntervalsBeforePagingFavoritesAndAnalysis()
    {
        SqliteHistoryRepository repository=new(DatabasePath);
        long intervalId=await repository.UpsertDraftAsync(Battle("interval"),Array.Empty<BattlePlayerRecord>(),Snapshot(100));
        await repository.ResolveFromApiAsync(intervalId,Snapshot(100),Snapshot(103));
        long singleId=await repository.UpsertDraftAsync(Battle("single",1),Array.Empty<BattlePlayerRecord>(),null);
        await repository.ResolveFromApiAsync(singleId,Snapshot(100),Snapshot(101));
        long pendingId=await repository.UpsertDraftAsync(Battle("pending",2),Array.Empty<BattlePlayerRecord>(),null);
        await repository.SetBattleFavoriteAsync(intervalId,true);
        await repository.SetBattleFavoriteAsync(singleId,true);
        HistoryQuery query=new() { SingleBattlesOnly=true,Descending=true,Limit=1 };
        Assert.Equal(2,await repository.CountBattlesAsync(query));
        Assert.Equal(pendingId,Assert.Single(await repository.GetBattlesAsync(query)).Id);
        Assert.Equal(singleId,Assert.Single(await repository.GetBattlesAsync(new HistoryQuery { SingleBattlesOnly=true,FavoritesOnly=true })).Id);
        var singles=await repository.GetBattlesAsync(new HistoryQuery { SingleBattlesOnly=true });
        HistorySummary summary=new HistoryAnalysisService().CalculateSummary(singles);
        Assert.Equal(2,summary.RecordedBattles);
        Assert.Equal(1,summary.ResultSampleCount);
        Assert.Equal(1,summary.DamageSampleCount);
        Assert.Equal(60_000,summary.AverageDamage);
        Assert.Equal(1,summary.Winrate);
    }

    [Fact]
    public async Task FavoriteChanges_PreserveSavedReviewAndUnsavedDraftAcrossRepositoryRestart()
    {
        SqliteHistoryRepository repository=new(DatabasePath);
        long id=await repository.UpsertDraftAsync(Battle("review"),Array.Empty<BattlePlayerRecord>(),null);
        await repository.SaveBattleReviewAsync(new BattleReview { BattleId=id,Note="Saved",Tags=new() { "Saved tag" } });
        await repository.SaveReviewDraftAsync(new BattleReview { BattleId=id,Note="Draft",Tags=new() { "Draft tag" } });
        await repository.SetBattleFavoriteAsync(id,true);
        repository=new(DatabasePath);
        BattleReview saved=(await repository.GetBattleReviewAsync(id))!;
        BattleReview draft=(await repository.GetReviewDraftAsync(id))!;
        Assert.Equal("Saved",saved.Note); Assert.Equal("Draft",draft.Note);
        Assert.True(saved.IsFavorite); Assert.True(draft.IsFavorite);
        Assert.Equal("Saved tag",Assert.Single(saved.Tags));
        Assert.Equal("Draft tag",Assert.Single(draft.Tags));
        await repository.SaveBattleReviewAsync(draft);
        Assert.Null(await repository.GetReviewDraftAsync(id));
        Assert.Equal("Draft",(await repository.GetBattleReviewAsync(id))!.Note);
    }

    [Fact]
    public async Task ReviewEdits_AreRestoredAfterWindowViewModelIsDisposed()
    {
        SqliteHistoryRepository repository=new(DatabasePath);
        long id=await repository.UpsertDraftAsync(Battle("draft"),Array.Empty<BattlePlayerRecord>(),null);
        HistoryViewModel vm=CreateViewModel(repository);
        await vm.ReloadAsync();
        vm.SelectedBattle=Assert.Single(vm.Rows);
        await vm.BattleDetailLoadTask;
        vm.ReviewNote="Continue editing after closing";
        vm.CustomReviewTag="Custom tag"; vm.AddCustomReviewTag();
        await vm.ToggleFavoriteAsync();
        vm.Dispose();
        await vm.FlushReviewDraftsAsync();
        using HistoryViewModel reopened=CreateViewModel(new SqliteHistoryRepository(DatabasePath));
        await reopened.ReloadAsync(); reopened.SelectedBattle=Assert.Single(reopened.Rows);
        await reopened.BattleDetailLoadTask;
        Assert.Equal("Continue editing after closing",reopened.ReviewNote);
        Assert.Contains(reopened.ReviewTags,x=>x.Key=="Custom tag" && x.IsSelected);
        Assert.True(reopened.IsFavorite);
        Assert.Equal("",(await repository.GetBattleReviewAsync(id))!.Note);
        await reopened.SaveReviewAsync();
        Assert.Null(await repository.GetReviewDraftAsync(id));
        Assert.Equal(reopened.ReviewNote,(await repository.GetBattleReviewAsync(id))!.Note);
    }

    [Fact]
    public async Task Initialization_DefaultsToSevenCalendarDaysAndMostRecentAccount()
    {
        SqliteHistoryRepository repository=new(DatabasePath);
        await repository.UpsertDraftAsync(Battle("account"),Array.Empty<BattlePlayerRecord>(),null);
        using HistoryViewModel vm=CreateViewModel(repository);
        await vm.InitializeAsync();
        Assert.Equal("ASIA",vm.SelectedServer?.Value); Assert.Equal("1",vm.SelectedAccount?.Value);
        Assert.Equal(DateTime.Today.AddDays(-6),vm.FromDate); Assert.Equal(DateTime.Today,vm.ToDate);
        vm.ApplyDateRange("week",new DateTime(2026,10,8));
        Assert.Equal(new DateTime(2026,10,2),vm.FromDate); Assert.Equal(new DateTime(2026,10,8),vm.ToDate);
    }

    [Fact]
    public async Task ClearHistory_WaitsForDraftWritesAndRemovesIntervals()
    {
        SqliteHistoryRepository repository=new(DatabasePath);
        long id=await repository.UpsertDraftAsync(Battle("review"),Array.Empty<BattlePlayerRecord>(),null);
        long interval=await repository.UpsertDraftAsync(Battle("interval",1),Array.Empty<BattlePlayerRecord>(),null);
        await repository.ResolveFromApiAsync(interval,Snapshot(100),Snapshot(103));
        using HistoryViewModel vm=CreateViewModel(repository);
        await vm.ReloadAsync(); vm.SelectedBattle=Assert.Single(vm.Rows); await vm.BattleDetailLoadTask;
        for (int i=0;i<20;i++) vm.ReviewNote=$"Draft {i}";
        await vm.ClearAsync(); await vm.FlushReviewDraftsAsync();
        Assert.Empty(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Empty(await repository.GetApiIntervalsAsync(new HistoryQuery()));
        Assert.Null(await repository.GetReviewDraftAsync(id));
    }

    private static HistoryViewModel CreateViewModel(IHistoryRepository repository)
    {
        HistoryAnalysisService analysis=new();
        return new(repository,analysis,new SessionAnalysisService(analysis),new ImprovementInsightService(analysis),
            new HistoryPagingTests.IdleTrackingCoordinator(),"Microsoft YaHei");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory,true);
    }
}
