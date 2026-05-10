using System.Threading.Tasks;
using gRPC.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GraphGui.Services
{
    public class GrpcServerHost
    {
        private WebApplication? _app;

        public ClusterState? ClusterState { get; private set; }

        public async Task StartAsync()
        {
            if (_app != null)
            {
                return;
            }

            var builder = WebApplication.CreateBuilder();

            builder.Services.AddGrpc();
            builder.Services.AddSingleton<ClusterState>();
            builder.Services.AddHostedService<LeaseMonitorService>();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(5000, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            });

            _app = builder.Build();

            _app.MapGrpcService<ClusterCoordinatorGrpcService>();

            await _app.StartAsync();

            ClusterState = _app.Services.GetRequiredService<ClusterState>();
        }

        public async Task StopAsync()
        {
            if (_app == null)
            {
                return;
            }

            await _app.StopAsync();
            await _app.DisposeAsync();

            _app = null;
            ClusterState = null;
        }
    }
}