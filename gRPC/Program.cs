using Microsoft.AspNetCore.Server.Kestrel.Core;
using gRPC.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<ClusterState>();
builder.Services.AddSingleton<ConsoleJobRunner>();
builder.Services.AddHostedService<LeaseMonitorService>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

var app = builder.Build();

app.MapGrpcService<ClusterCoordinatorGrpcService>();

await app.StartAsync();

Console.WriteLine("Serwer gRPC uruchomiony na porcie 5000.");
Console.WriteLine("Adres: http://localhost:5000");
Console.WriteLine();

var runner = app.Services.GetRequiredService<ConsoleJobRunner>();
await runner.RunAsync();

await app.StopAsync();