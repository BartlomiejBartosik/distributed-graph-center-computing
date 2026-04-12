using System.Threading.Channels;
using GraphShared.Algorithms;
using GraphShared.Models;
using PSR_GraphCenter.Contracts;

namespace gRPC.Services
{
    public class ClusterState
    {
        private readonly object _lock = new object();

        private readonly Dictionary<string, WorkerSession> _workers = new();
        private readonly Dictionary<string, DistributedJob> _jobs = new();

        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _leaseDuration = TimeSpan.FromSeconds(15);

        public int ActiveWorkerCount
        {
            get
            {
                lock (_lock)
                {
                    return _workers.Count;
                }
            }
        }

        public IReadOnlyList<string> GetWorkerIds()
        {
            lock (_lock)
            {
                return _workers.Keys.OrderBy(x => x).ToList();
            }
        }

        public void RegisterWorker(string workerId, int preferredThreads, Channel<ServerMessage> outgoingChannel)
        {
            lock (_lock)
            {
                if (_workers.ContainsKey(workerId))
                {
                    RemoveWorkerLocked(workerId);
                }

                _workers[workerId] = new WorkerSession
                {
                    WorkerId = workerId,
                    PreferredThreads = preferredThreads <= 0 ? 1 : preferredThreads,
                    Outgoing = outgoingChannel,
                    LastSeenUtc = DateTime.UtcNow
                };

                Console.WriteLine($"[SERWER] Zarejestrowano workera: {workerId}, wątki lokalne: {preferredThreads}");
                TryDispatchLocked();
            }
        }

        public void UpdateHeartbeat(Heartbeat heartbeat)
        {
            lock (_lock)
            {
                if (!_workers.TryGetValue(heartbeat.WorkerId, out var worker))
                {
                    return;
                }

                worker.LastSeenUtc = DateTime.UtcNow;

                if (!string.IsNullOrWhiteSpace(heartbeat.JobId) &&
                    !string.IsNullOrWhiteSpace(heartbeat.ChunkId) &&
                    _jobs.TryGetValue(heartbeat.JobId, out var job) &&
                    job.Chunks.TryGetValue(heartbeat.ChunkId, out var chunk))
                {
                    if (chunk.Status == ChunkStatus.Assigned &&
                        chunk.AssignedWorkerId == heartbeat.WorkerId)
                    {
                        chunk.LeaseExpiresAtUtc = DateTime.UtcNow + _leaseDuration;
                    }
                }
            }
        }

        public void AcceptChunkResult(ChunkResult result)
        {
            lock (_lock)
            {
                if (!_workers.TryGetValue(result.WorkerId, out var worker))
                {
                    return;
                }

                worker.LastSeenUtc = DateTime.UtcNow;

                if (!_jobs.TryGetValue(result.JobId, out var job))
                {
                    return;
                }

                if (!job.Chunks.TryGetValue(result.ChunkId, out var chunk))
                {
                    return;
                }

                if (chunk.Status != ChunkStatus.Assigned)
                {
                    return;
                }

                if (chunk.AssignedWorkerId != result.WorkerId)
                {
                    return;
                }

                if (chunk.LeaseId != result.LeaseId)
                {
                    Console.WriteLine($"[SERWER] Odrzucono spóźniony wynik workera {result.WorkerId} dla chunku {result.ChunkId}");
                    return;
                }

                foreach (var item in result.Sums)
                {
                    if (item.Vertex >= 0 && item.Vertex < job.DistanceSums.Length)
                    {
                        job.DistanceSums[item.Vertex] = item.Sum;
                    }
                }

                chunk.Status = ChunkStatus.Completed;
                chunk.AssignedWorkerId = null;
                chunk.LeaseId = string.Empty;

                worker.Busy = false;
                worker.CurrentJobId = null;
                worker.CurrentChunkId = null;

                Console.WriteLine($"[SERWER] Odebrano wynik: worker={result.WorkerId}, chunk={result.ChunkId}, czas={result.ExecutionTimeMs} ms");

                if (job.Chunks.Values.All(c => c.Status == ChunkStatus.Completed) && !job.Completed)
                {
                    job.Completed = true;

                    int bestVertex = -1;
                    double minSum = double.MaxValue;

                    for (int i = 0; i < job.DistanceSums.Length; i++)
                    {
                        if (job.DistanceSums[i] < minSum)
                        {
                            minSum = job.DistanceSums[i];
                            bestVertex = i;
                        }
                    }

                    var finalResult = new GraphCenterResult(bestVertex, minSum, job.DistanceSums.ToArray());
                    job.Completion.TrySetResult(finalResult);

                    Console.WriteLine($"[SERWER] Zadanie {job.JobId} zakończone. Centrum grafu: {bestVertex}");
                }

                TryDispatchLocked();
            }
        }

        public void UnregisterWorker(string workerId)
        {
            lock (_lock)
            {
                RemoveWorkerLocked(workerId);
                TryDispatchLocked();
            }
        }

        public string CreateJob(Graph graph, int chunkSize)
        {
            lock (_lock)
            {
                if (chunkSize <= 0)
                {
                    chunkSize = 1;
                }

                string jobId = Guid.NewGuid().ToString("N");

                var job = new DistributedJob
                {
                    JobId = jobId,
                    Graph = graph,
                    DistanceSums = Enumerable.Repeat(double.MaxValue, graph.VertexCount).ToArray()
                };

                int chunkNumber = 0;

                for (int start = 0; start < graph.VertexCount; start += chunkSize)
                {
                    int end = Math.Min(start + chunkSize, graph.VertexCount);
                    string chunkId = $"{jobId}-chunk-{chunkNumber++}";

                    job.Chunks[chunkId] = new ChunkAssignment
                    {
                        JobId = jobId,
                        ChunkId = chunkId,
                        StartVertexInclusive = start,
                        EndVertexExclusive = end,
                        Status = ChunkStatus.Pending
                    };
                }

                _jobs[jobId] = job;

                Console.WriteLine($"[SERWER] Utworzono zadanie {jobId}. Chunków: {job.Chunks.Count}");
                TryDispatchLocked();

                return jobId;
            }
        }

        public Task<GraphCenterResult> WaitForJobResultAsync(string jobId)
        {
            lock (_lock)
            {
                if (!_jobs.TryGetValue(jobId, out var job))
                {
                    throw new InvalidOperationException($"Nie znaleziono zadania: {jobId}");
                }

                return job.Completion.Task;
            }
        }

        public void SweepTimeouts()
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;

                var silentWorkers = _workers.Values
                    .Where(w => now - w.LastSeenUtc > _heartbeatTimeout)
                    .Select(w => w.WorkerId)
                    .ToList();

                foreach (string workerId in silentWorkers)
                {
                    Console.WriteLine($"[SERWER] Worker {workerId} przestał odpowiadać. Usuwam i zlecam zadanie innemu.");
                    RemoveWorkerLocked(workerId);
                }

                foreach (var job in _jobs.Values.Where(j => !j.Completed))
                {
                    foreach (var chunk in job.Chunks.Values)
                    {
                        if (chunk.Status == ChunkStatus.Assigned &&
                            chunk.LeaseExpiresAtUtc <= now)
                        {
                            Console.WriteLine($"[SERWER] Lease wygasł dla chunku {chunk.ChunkId}. Ponowne przydzielenie.");
                            RequeueChunkLocked(job, chunk);
                        }
                    }
                }

                TryDispatchLocked();
            }
        }

        private void RemoveWorkerLocked(string workerId)
        {
            if (!_workers.TryGetValue(workerId, out var worker))
            {
                return;
            }

            if (worker.Busy &&
                !string.IsNullOrWhiteSpace(worker.CurrentJobId) &&
                !string.IsNullOrWhiteSpace(worker.CurrentChunkId) &&
                _jobs.TryGetValue(worker.CurrentJobId, out var job) &&
                job.Chunks.TryGetValue(worker.CurrentChunkId, out var chunk) &&
                chunk.Status == ChunkStatus.Assigned &&
                chunk.AssignedWorkerId == workerId)
            {
                RequeueChunkLocked(job, chunk);
            }

            worker.Outgoing.Writer.TryComplete();
            _workers.Remove(workerId);
        }

        private void RequeueChunkLocked(DistributedJob job, ChunkAssignment chunk)
        {
            if (!string.IsNullOrWhiteSpace(chunk.AssignedWorkerId) &&
                _workers.TryGetValue(chunk.AssignedWorkerId, out var worker))
            {
                worker.Busy = false;
                worker.CurrentJobId = null;
                worker.CurrentChunkId = null;
            }

            chunk.Status = ChunkStatus.Pending;
            chunk.AssignedWorkerId = null;
            chunk.LeaseId = string.Empty;
            chunk.LeaseExpiresAtUtc = DateTime.MinValue;
        }

        private void TryDispatchLocked()
        {
            var freeWorkers = _workers.Values
                .Where(w => !w.Busy)
                .OrderBy(w => w.WorkerId)
                .ToList();

            foreach (var worker in freeWorkers)
            {
                var pending = FindNextPendingChunkLocked();

                if (pending == null)
                {
                    break;
                }

                var job = pending.Job;
                var chunk = pending.Chunk;

                chunk.Status = ChunkStatus.Assigned;
                chunk.AssignedWorkerId = worker.WorkerId;
                chunk.LeaseId = Guid.NewGuid().ToString("N");
                chunk.LeaseExpiresAtUtc = DateTime.UtcNow + _leaseDuration;

                worker.Busy = true;
                worker.CurrentJobId = job.JobId;
                worker.CurrentChunkId = chunk.ChunkId;

                var taskMessage = new ServerMessage
                {
                    Task = BuildChunkTask(job, chunk, worker.PreferredThreads)
                };

                if (!worker.Outgoing.Writer.TryWrite(taskMessage))
                {
                    RequeueChunkLocked(job, chunk);
                }
                else
                {
                    Console.WriteLine($"[SERWER] Przydzielono chunk {chunk.ChunkId} workerowi {worker.WorkerId}");
                }
            }
        }

        private PendingChunkInfo? FindNextPendingChunkLocked()
        {
            foreach (var job in _jobs.Values.Where(j => !j.Completed))
            {
                foreach (var chunk in job.Chunks.Values.OrderBy(c => c.StartVertexInclusive))
                {
                    if (chunk.Status == ChunkStatus.Pending)
                    {
                        return new PendingChunkInfo
                        {
                            Job = job,
                            Chunk = chunk
                        };
                    }
                }
            }

            return null;
        }

        private static ChunkTask BuildChunkTask(DistributedJob job, ChunkAssignment chunk, int localThreadCount)
        {
            GraphDto graphDto = new GraphDto
            {
                VertexCount = job.Graph.VertexCount
            };

            for (int i = 0; i < job.Graph.VertexCount; i++)
            {
                graphDto.Nodes.Add(new NodeDto
                {
                    Id = job.Graph.Nodes[i].Id,
                    X = job.Graph.Nodes[i].X,
                    Y = job.Graph.Nodes[i].Y
                });
            }

            for (int i = 0; i < job.Graph.VertexCount; i++)
            {
                foreach (var edge in job.Graph.AdjacencyList[i])
                {
                    if (i < edge.Target)
                    {
                        graphDto.Edges.Add(new EdgeDto
                        {
                            Source = i,
                            Target = edge.Target
                        });
                    }
                }
            }

            return new ChunkTask
            {
                JobId = job.JobId,
                ChunkId = chunk.ChunkId,
                LeaseId = chunk.LeaseId,
                StartVertexInclusive = chunk.StartVertexInclusive,
                EndVertexExclusive = chunk.EndVertexExclusive,
                LocalThreadCount = localThreadCount,
                Graph = graphDto
            };
        }

        private class WorkerSession
        {
            public string WorkerId { get; set; } = string.Empty;
            public int PreferredThreads { get; set; }
            public Channel<ServerMessage> Outgoing { get; set; } = default!;
            public DateTime LastSeenUtc { get; set; }
            public bool Busy { get; set; }
            public string? CurrentJobId { get; set; }
            public string? CurrentChunkId { get; set; }
        }

        private class DistributedJob
        {
            public string JobId { get; set; } = string.Empty;
            public Graph Graph { get; set; } = default!;
            public Dictionary<string, ChunkAssignment> Chunks { get; set; } = new();
            public double[] DistanceSums { get; set; } = Array.Empty<double>();
            public bool Completed { get; set; }

            public TaskCompletionSource<GraphCenterResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private class ChunkAssignment
        {
            public string JobId { get; set; } = string.Empty;
            public string ChunkId { get; set; } = string.Empty;
            public int StartVertexInclusive { get; set; }
            public int EndVertexExclusive { get; set; }
            public ChunkStatus Status { get; set; }
            public string? AssignedWorkerId { get; set; }
            public string LeaseId { get; set; } = string.Empty;
            public DateTime LeaseExpiresAtUtc { get; set; }
        }

        private class PendingChunkInfo
        {
            public DistributedJob Job { get; set; } = default!;
            public ChunkAssignment Chunk { get; set; } = default!;
        }

        private enum ChunkStatus
        {
            Pending,
            Assigned,
            Completed
        }
    }
}