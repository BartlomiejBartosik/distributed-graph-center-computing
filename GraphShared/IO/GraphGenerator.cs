using System;
using GraphShared.Models;

namespace GraphShared.IO
{
    public static class GraphGenerator
    {
        public static Graph GenerateConnectedGeometricGraph(int vertexCount, int extraEdges)
        {
            Random random = new Random();
            Graph graph = new Graph(vertexCount);

            for (int i = 0; i < vertexCount; i++)
            {
                double x = random.NextDouble() * 1000.0;
                double y = random.NextDouble() * 1000.0;
                graph.AddNode(i, x, y);
            }

            for (int i = 0; i < vertexCount - 1; i++)
            {
                graph.AddUndirectedEdge(i, i + 1);
            }

            int added = 0;
            while (added < extraEdges)
            {
                int a = random.Next(vertexCount);
                int b = random.Next(vertexCount);

                if (a == b)
                    continue;

                int before = graph.GetEdgeCount();
                graph.AddUndirectedEdge(a, b);
                int after = graph.GetEdgeCount();

                if (after > before)
                {
                    added++;
                }
            }

            return graph;
        }
    }
}