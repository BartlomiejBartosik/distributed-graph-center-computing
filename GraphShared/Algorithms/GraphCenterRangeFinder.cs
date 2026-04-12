using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GraphShared.Models;

namespace GraphShared.Algorithms
{
    public class VertexDistanceSum
    {
        public int Vertex { get; set; }
        public double Sum { get; set; }

        public VertexDistanceSum(int vertex, double sum)
        {
            Vertex = vertex;
            Sum = sum;
        }
    }

    public class RangeResult
    {
        public int BestVertex { get; set; }
        public double MinimumDistanceSum { get; set; }
        public List<VertexDistanceSum> DistanceSums { get; set; } = new();
    }

    public class GraphCenterResult
    {
        public int BestVertex { get; set; }
        public double MinimumDistanceSum { get; set; }
        public double[] DistanceSums { get; set; }

        public GraphCenterResult(int bestVertex, double minimumDistanceSum, double[] distanceSums)
        {
            BestVertex = bestVertex;
            MinimumDistanceSum = minimumDistanceSum;
            DistanceSums = distanceSums;
        }
    }

    public static class GraphCenterRangeFinder
    {
        public static RangeResult FindRangeParallel(
            Graph graph,
            int startInclusive,
            int endExclusive,
            int threadCount,
            CancellationToken cancellationToken = default)
        {
            if (threadCount <= 0)
                threadCount = 1;

            object lockObject = new object();
            List<VertexDistanceSum> sums = new List<VertexDistanceSum>();

            int bestVertex = -1;
            double minSum = double.MaxValue;

            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = threadCount,
                CancellationToken = cancellationToken
            };

            Parallel.For(startInclusive, endExclusive, options, i =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                double[] distances = Dijkstra.ComputeShortestPaths(graph, i, cancellationToken);
                double sum = 0;

                for (int j = 0; j < graph.VertexCount; j++)
                {
                    if (distances[j] == double.MaxValue)
                    {
                        sum = double.MaxValue;
                        break;
                    }

                    sum += distances[j];
                }

                lock (lockObject)
                {
                    sums.Add(new VertexDistanceSum(i, sum));

                    if (sum < minSum)
                    {
                        minSum = sum;
                        bestVertex = i;
                    }
                }
            });

            return new RangeResult
            {
                BestVertex = bestVertex,
                MinimumDistanceSum = minSum,
                DistanceSums = sums
            };
        }
    }
}