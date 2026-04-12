using System.Threading.Channels;
using Grpc.Core;
using PSR_GraphCenter.Contracts;

namespace gRPC.Services
{
    public class ClusterCoordinatorGrpcService : ClusterCoordinator.ClusterCoordinatorBase
    {
        private readonly ClusterState _clusterState;

        public ClusterCoordinatorGrpcService(ClusterState clusterState)
        {
            _clusterState = clusterState;
        }

        public override async Task Connect(
            IAsyncStreamReader<WorkerMessage> requestStream,
            IServerStreamWriter<ServerMessage> responseStream,
            ServerCallContext context)
        {
            if (!await requestStream.MoveNext(context.CancellationToken))
            {
                return;
            }

            WorkerMessage firstMessage = requestStream.Current;

            if (firstMessage.PayloadCase != WorkerMessage.PayloadOneofCase.Hello)
            {
                throw new RpcException(
                    new Status(StatusCode.InvalidArgument, "Pierwsza wiadomość musi być typu Hello."));
            }

            WorkerHello hello = firstMessage.Hello;
            var outgoingChannel = Channel.CreateUnbounded<ServerMessage>();

            _clusterState.RegisterWorker(
                hello.WorkerId,
                hello.PreferredThreads,
                outgoingChannel);

            var sendTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in outgoingChannel.Reader.ReadAllAsync(context.CancellationToken))
                    {
                        await responseStream.WriteAsync(message);
                    }
                }
                catch
                {
                    // po rozłączeniu workera nie trzeba nic robić
                }
            }, context.CancellationToken);

            try
            {
                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    WorkerMessage message = requestStream.Current;

                    switch (message.PayloadCase)
                    {
                        case WorkerMessage.PayloadOneofCase.Heartbeat:
                            _clusterState.UpdateHeartbeat(message.Heartbeat);
                            break;

                        case WorkerMessage.PayloadOneofCase.Result:
                            _clusterState.AcceptChunkResult(message.Result);
                            break;
                    }
                }
            }
            catch
            {
                // rozłączenie workera
            }
            finally
            {
                _clusterState.UnregisterWorker(hello.WorkerId);
                outgoingChannel.Writer.TryComplete();

                try
                {
                    await sendTask;
                }
                catch
                {
                }

                Console.WriteLine($"[SERWER] Rozłączono workera: {hello.WorkerId}");
            }
        }
    }
}