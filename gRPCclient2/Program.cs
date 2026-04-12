using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using GraphShared.Algorithms;
using GraphShared.Models;
using PSR_GraphCenter.Contracts;
using System.Diagnostics;

namespace gRPCclient2
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            Console.Write("Podaj adres serwera [http://localhost:5000]: ");
            string? address = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(address))
            {
                address = "http://localhost:5000";
            }

            Console.Write($"Podaj liczbę lokalnych wątków [{Environment.ProcessorCount}]: ");
            string? threadInput = Console.ReadLine()?.Trim();

            int preferredThreads = Environment.ProcessorCount;
            if (!string.IsNullOrWhiteSpace(threadInput) &&
                int.TryParse(threadInput, out int parsedThreads) &&
                parsedThreads > 0)
            {
                preferredThreads = parsedThreads;
            }

            string workerId = $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}";

            Console.WriteLine();
            Console.WriteLine($"Worker uruchomiony. ID: {workerId}");
            Console.WriteLine();

            DateTime reconnectStartTime = DateTime.MinValue;

            while (true)
            {
                try
                {
                    await RunWorkerAsync(address, workerId, preferredThreads);

                    reconnectStartTime = DateTime.MinValue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WORKER] Błąd połączenia: {ex.Message}");

                    if (reconnectStartTime == DateTime.MinValue)
                    {
                        reconnectStartTime = DateTime.UtcNow;
                    }

                    TimeSpan disconnectedTime = DateTime.UtcNow - reconnectStartTime;

                    if (disconnectedTime >= TimeSpan.FromSeconds(30))
                    {
                        Console.WriteLine("[WORKER] Brak połączenia z serwerem przez 30 sekund. Zamykanie klienta.");
                        return;
                    }
                }

                Console.WriteLine("[WORKER] Próba ponownego połączenia za 3 sekundy...");
                await Task.Delay(3000);
            }
        }

        private static async Task RunWorkerAsync(string address, string workerId, int preferredThreads)
        {
            using var channel = GrpcChannel.ForAddress(address);
            var client = new ClusterCoordinator.ClusterCoordinatorClient(channel);

            using var call = client.Connect();

            var writeLock = new SemaphoreSlim(1, 1);
            var sessionCts = new CancellationTokenSource();

            object stateLock = new object();
            string currentJobId = string.Empty;
            string currentChunkId = string.Empty;

            await SendAsync(call.RequestStream, writeLock, new WorkerMessage
            {
                Hello = new WorkerHello
                {
                    WorkerId = workerId,
                    PreferredThreads = preferredThreads
                }
            });

            Console.WriteLine("[WORKER] Połączono z serwerem.");

            var heartbeatTask = Task.Run(async () =>
            {
                while (!sessionCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(2000, sessionCts.Token);

                        string jobId;
                        string chunkId;

                        lock (stateLock)
                        {
                            jobId = currentJobId;
                            chunkId = currentChunkId;
                        }

                        await SendAsync(call.RequestStream, writeLock, new WorkerMessage
                        {
                            Heartbeat = new Heartbeat
                            {
                                WorkerId = workerId,
                                JobId = jobId,
                                ChunkId = chunkId,
                                UnixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            }
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, sessionCts.Token);

            try
            {
                await foreach (var message in call.ResponseStream.ReadAllAsync(sessionCts.Token))
                {
                    if (message.PayloadCase != ServerMessage.PayloadOneofCase.Task)
                    {
                        continue;
                    }

                    ChunkTask task = message.Task;

                    lock (stateLock)
                    {
                        currentJobId = task.JobId;
                        currentChunkId = task.ChunkId;
                    }

                    Console.WriteLine($"[WORKER] Otrzymano chunk {task.ChunkId}");
                    Console.WriteLine($"[WORKER] Zakres: {task.StartVertexInclusive} - {task.EndVertexExclusive - 1}");
                    Console.WriteLine($"[WORKER] Wątki lokalne: {task.LocalThreadCount}");

                    Graph graph = ProtoMapper.FromDto(task.Graph);

                    Stopwatch stopwatch = Stopwatch.StartNew();

                    RangeResult rangeResult = GraphCenterRangeFinder.FindRangeParallel(
                        graph,
                        task.StartVertexInclusive,
                        task.EndVertexExclusive,
                        task.LocalThreadCount > 0 ? task.LocalThreadCount : preferredThreads,
                        sessionCts.Token);

                    stopwatch.Stop();

                    ChunkResult result = ProtoMapper.ToChunkResult(
                        workerId,
                        task.JobId,
                        task.ChunkId,
                        task.LeaseId,
                        rangeResult,
                        stopwatch.ElapsedMilliseconds);

                    await SendAsync(call.RequestStream, writeLock, new WorkerMessage
                    {
                        Result = result
                    });

                    Console.WriteLine($"[WORKER] Odesłano wynik chunku {task.ChunkId}");
                    Console.WriteLine($"[WORKER] Czas liczenia chunku: {stopwatch.ElapsedMilliseconds} ms");
                    Console.WriteLine();
                    lock (stateLock)
                    {
                        currentJobId = string.Empty;
                        currentChunkId = string.Empty;
                    }
                }
            }
            finally
            {
                sessionCts.Cancel();

                try
                {
                    await call.RequestStream.CompleteAsync();
                }
                catch
                {
                }

                try
                {
                    await heartbeatTask;
                }
                catch
                {
                }

                Console.WriteLine("[WORKER] Rozłączono z serwerem.");
            }
        }

        private static async Task SendAsync(
            IClientStreamWriter<WorkerMessage> stream,
            SemaphoreSlim writeLock,
            WorkerMessage message)
        {
            await writeLock.WaitAsync();

            try
            {
                await stream.WriteAsync(message);
            }
            finally
            {
                writeLock.Release();
            }
        }
    }

    internal static class ProtoMapper
    {
        public static Graph FromDto(GraphDto dto)
        {
            Graph graph = new Graph(dto.VertexCount);

            foreach (var node in dto.Nodes)
            {
                graph.AddNode(node.Id, node.X, node.Y);
            }

            foreach (var edge in dto.Edges)
            {
                graph.AddUndirectedEdge(edge.Source, edge.Target);
            }

            return graph;
        }

        public static ChunkResult ToChunkResult(
            string workerId,
            string jobId,
            string chunkId,
            string leaseId,
            RangeResult rangeResult,
            long executionTimeMs)
        {
            ChunkResult result = new ChunkResult
            {
                WorkerId = workerId,
                JobId = jobId,
                ChunkId = chunkId,
                LeaseId = leaseId,
                ExecutionTimeMs = executionTimeMs
            };

            foreach (var item in rangeResult.DistanceSums.OrderBy(x => x.Vertex))
            {
                result.Sums.Add(new VertexSum
                {
                    Vertex = item.Vertex,
                    Sum = item.Sum
                });
            }

            return result;
        }
    }
}