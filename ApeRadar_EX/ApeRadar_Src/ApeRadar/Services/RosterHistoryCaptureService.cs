using ApeRadar.History;
using ApeRadar.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.Services
{
    internal interface IRosterHistoryCaptureService
    {
        Task CaptureAsync(RosterHistoryCaptureRequest request, CancellationToken cancellationToken = default);
    }

    internal sealed class RosterHistoryCaptureService : IRosterHistoryCaptureService
    {
        private readonly Func<IBattleTrackingCoordinator> coordinatorAccessor;

        public RosterHistoryCaptureService(Func<IBattleTrackingCoordinator> coordinatorAccessor) =>
            this.coordinatorAccessor = coordinatorAccessor ?? throw new ArgumentNullException(nameof(coordinatorAccessor));

        public Task CaptureAsync(RosterHistoryCaptureRequest request, CancellationToken cancellationToken = default)
        {
            Player? self = request.Players.FirstOrDefault(player => player.Relation == "0");
            if (self == null) return Task.CompletedTask;
            BattleRecord battle = new()
            {
                BattleKey = request.BattleId,
                StartedAt = request.BattleStartTime,
                Server = ServerExt.GetNameByServer(request.Server),
                Mode = request.BattleType,
                MapName = request.MapName,
                AccountId = self.ID,
                AccountName = self.Name,
                ShipId = self.ShipID,
                ShipName = self.ShipName,
                ShipType = self.ShipType,
                Completeness = BattleCompleteness.Pending,
                Source = BattleMetricSource.MetadataOnly,
                StatusMessage = "WaitingForReplay"
            };
            return coordinatorAccessor().CapturePreBattleAsync(
                battle,
                request.Players.Select(BattlePlayerRecord.FromPlayer).ToList(),
                null,
                cancellationToken);
        }
    }
}
