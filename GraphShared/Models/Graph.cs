using System;
using System.Collections.Generic;

namespace GraphShared.Models
{
    public class Graph
    {
        public int VertexCount { get; }
        public List<Edge>[] AdjacencyList { get; }
        public Node[] Nodes { get; }

        public Graph(int vertexCount)
        {
            VertexCount = vertexCount;
            Nodes = new Node[vertexCount];
            AdjacencyList = new List<Edge>[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                AdjacencyList[i] = new List<Edge>();
            }
        }

        public void AddNode(int id, double x, double y)
        {
            Nodes[id] = new Node(id, x, y);
        }

        public void AddUndirectedEdge(int source, int target)
        {
            if (source == target)
                return;

            if (source < 0 || source >= VertexCount || target < 0 || target >= VertexCount)
                return;

            if (Nodes[source] == null || Nodes[target] == null)
                return;

            if (EdgeExists(source, target))
                return;

            double weight = CalculateEuclideanDistance(Nodes[source], Nodes[target]);

            AdjacencyList[source].Add(new Edge(target, weight));
            AdjacencyList[target].Add(new Edge(source, weight));
        }

        public bool EdgeExists(int source, int target)
        {
            foreach (var edge in AdjacencyList[source])
            {
                if (edge.Target == target)
                    return true;
            }

            return false;
        }

        private double CalculateEuclideanDistance(Node a, Node b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public int GetEdgeCount()
        {
            int count = 0;

            for (int i = 0; i < VertexCount; i++)
            {
                count += AdjacencyList[i].Count;
            }

            return count / 2;
        }

        public void PrintGraphSummary()
        {
            Console.WriteLine($"Liczba wierzchołków: {VertexCount}");
            Console.WriteLine($"Liczba krawędzi: {GetEdgeCount()}");
        }

        public void PrintGraph()
        {
            for (int i = 0; i < VertexCount; i++)
            {
                Console.Write($"Wierzchołek {i} ({Nodes[i].X:F2}, {Nodes[i].Y:F2}): ");

                foreach (var edge in AdjacencyList[i])
                {
                    Console.Write($"-> {edge.Target} [waga: {edge.Weight:F2}] ");
                }

                Console.WriteLine();
            }
        }
    }
}