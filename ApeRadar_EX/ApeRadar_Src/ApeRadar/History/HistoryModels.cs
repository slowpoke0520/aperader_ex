using ApeRadar.Models;
using System;

namespace ApeRadar.History
{
    internal enum BattleMetricSource { ReplayExact, ReplayDerived, ApiExact, ApiMerged, MetadataOnly }
    internal enum BattleResult { Win, Loss, Draw, UnknownNonWin, Unknown }
    internal enum BattleCompleteness { Complete, Partial, Pending, Unsupported, Failed }
    internal enum ReplayParseStatus { Parsed, Partial, Unsupported, Invalid, Pending }

    internal sealed class BattleRecord
    {
        public long Id { get; set; }
        public string BattleKey { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public string Server { get; set; } = "";
        public string Mode { get; set; } = "";
        public string MapName { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string ShipId { get; set; } = "";
        public string ShipName { get; set; } = "";
        public BattleResult Result { get; set; } = BattleResult.Unknown;
        public double? WinCount { get; set; }
        public long? Damage { get; set; }
        public double? Frags { get; set; }
        public int BattleCount { get; set; } = 1;
        public BattleMetricSource Source { get; set; } = BattleMetricSource.MetadataOnly;
        public BattleCompleteness Completeness { get; set; } = BattleCompleteness.Pending;
        public string? ReplayHash { get; set; }
        public string? ReplayVersion { get; set; }
        public string? StatusMessage { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    internal sealed class BattlePlayerRecord
    {
        public string PlayerKey { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string Relation { get; set; } = "";
        public string ShipId { get; set; } = "";
        public string ShipName { get; set; } = "";
        public string ShipType { get; set; } = "";
        public int ShipTier { get; set; }
        public bool IsHidden { get; set; }
        public bool IsDataStale { get; set; }
        public double? AccountBattles { get; set; }
        public double? AccountWinrate { get; set; }
        public double? AccountPr { get; set; }
        public double? ShipBattles { get; set; }
        public double? ShipWinrate { get; set; }
        public double? ShipPr { get; set; }

        public static BattlePlayerRecord FromPlayer(Player player)
        {
            string server = ServerExt.GetNameByServer(player.Server);
            string identity = player.ID != "-1" ? player.ID : $"name:{player.Name.ToLowerInvariant()}";
            return new BattlePlayerRecord
            {
                PlayerKey = $"{server}:{identity}", AccountId = player.ID, AccountName = player.Name,
                Relation = player.Relation, ShipId = player.ShipID, ShipName = player.ShipName,
                ShipType = player.ShipType, ShipTier = player.ShipTier, IsHidden = player.IsHidden,
                IsDataStale = player.IsDataStale, AccountBattles = ValueOrNull(player.Battles),
                AccountWinrate = ValueOrNull(player.AccountWinrate), AccountPr = ValueOrNull(player.PR),
                ShipBattles = ValueOrNull(player.ShipBattles), ShipWinrate = ValueOrNull(player.ShipWinrate),
                ShipPr = ValueOrNull(player.ShipPR)
            };
        }

        private static double? ValueOrNull(double value) => value < 0 ? null : value;
    }

    internal sealed class ShipStatSnapshot
    {
        public long Id { get; set; }
        public long BattleId { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
        public string Provider { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string ShipId { get; set; } = "";
        public double Battles { get; set; }
        public double Wins { get; set; }
        public double? Losses { get; set; }
        public double Damage { get; set; }
        public double Frags { get; set; }
    }

    internal sealed class PendingResultCheck
    {
        public long BattleId { get; set; }
        public int Attempt { get; set; }
        public DateTimeOffset NextAttemptAt { get; set; }
        public string LastError { get; set; } = "";
    }

    internal sealed class ReplayParseResult
    {
        public ReplayParseStatus Status { get; init; }
        public string FileHash { get; init; } = "";
        public string GameVersion { get; init; } = "";
        public string ParserVersion { get; init; } = "";
        public string BattleKey { get; init; } = "";
        public DateTimeOffset? StartedAt { get; init; }
        public string Mode { get; init; } = "";
        public string MapName { get; init; } = "";
        public string AccountName { get; init; } = "";
        public string ShipId { get; init; } = "";
        public BattleResult Result { get; init; } = BattleResult.Unknown;
        public long? Damage { get; init; }
        public double? Frags { get; init; }
        public BattleMetricSource Source { get; init; } = BattleMetricSource.MetadataOnly;
        public string ErrorCode { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
        public bool HasCompleteMetrics => Status == ReplayParseStatus.Parsed && Damage.HasValue && Frags.HasValue && Result != BattleResult.Unknown;
    }

    internal sealed class HistoryQuery
    {
        public string? Server { get; init; }
        public string? AccountId { get; init; }
        public string? ShipId { get; init; }
        public DateTimeOffset? From { get; init; }
        public DateTimeOffset? To { get; init; }
    }

    internal sealed class HistoryFilterOption
    {
        public string Value { get; init; } = "";
        public string Display { get; init; } = "";
    }

    internal sealed class HistorySummary
    {
        public int RecordedBattles { get; init; }
        public int EffectiveBattles { get; init; }
        public double? Winrate { get; init; }
        public double? AverageDamage { get; init; }
        public double? AverageFrags { get; init; }
        public double? AveragePr { get; init; }
        public double CompletenessRate { get; init; }
    }

    internal sealed class HistoryTrendPoint
    {
        public long BattleId { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public string Label { get; init; } = "";
        public double Value { get; init; }
    }

    internal sealed class ReplayImportProgress
    {
        public int Total { get; init; }
        public int Processed { get; init; }
        public int Imported { get; init; }
        public int Skipped { get; init; }
        public int Failed { get; init; }
        public bool Cancelled { get; init; }
    }
}
