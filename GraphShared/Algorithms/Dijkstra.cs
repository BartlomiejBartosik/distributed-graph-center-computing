using System;
using System.Collections.Generic;
using System.Threading;
using GraphShared.Models;

namespace GraphShared.Algorithms
{
    public static class Dijkstra
    {
        public static double[] ComputeShortestPaths(Graph graph, int startVertex, CancellationToken cancellationToken = default)
        {
            int n = graph.VertexCount;
            double[] distances = new double[n];
            bool[] visited = new bool[n];

            for (int i = 0; i < n; i++)
            {
                distances[i] = double.MaxValue;
            }

            distances[startVertex] = 0;

            PriorityQueue<int, double> queue = new PriorityQueue<int, double>();
            queue.Enqueue(startVertex, 0);

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int current = queue.Dequeue();

                if (visited[current])
                    continue;

                visited[current] = true;

                foreach (var edge in graph.AdjacencyList[current])
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int neighbor = edge.Target;
                    double newDistance = distances[current] + edge.Weight;

                    if (!visited[neighbor] && newDistance < distances[neighbor])
                    {
                        distances[neighbor] = newDistance;
                        queue.Enqueue(neighbor, newDistance);
                    }
                }
            }

            return distances;
        }
    }
}