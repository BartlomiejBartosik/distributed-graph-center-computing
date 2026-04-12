using Microsoft.Extensions.Hosting;

namespace gRPC.Services
{
    public class LeaseMonitorService : BackgroundService
    {
        private readonly ClusterState _clusterState;

        public LeaseMonitorService(ClusterState clusterState)
        {
            _clusterState = clusterState;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _clusterState.SweepTimeouts();
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}