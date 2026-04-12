using System;
using System.Globalization;
using System.IO;
using System.Linq;
using GraphShared.Models;

namespace GraphShared.IO
{
    public static class GraphCsv
    {
        public static void SaveToCsv(Graph graph, string path)
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using StreamWriter writer = new StreamWriter(path);

            writer.WriteLine("TYPE,ID,X,Y,NEIGHBORS");

            for (int i = 0; i < graph.VertexCount; i++)
            {
                string neighbors = string.Join("|",
                    graph.AdjacencyList[i]
                        .Select(e => e.Target)
                        .Distinct()
                        .OrderBy(x => x));

                string line =
                    $"NODE,{i}," +
                    $"{graph.Nodes[i].X.ToString(CultureInfo.InvariantCulture)}," +
                    $"{graph.Nodes[i].Y.ToString(CultureInfo.InvariantCulture)}," +
                    $"{neighbors}";

                writer.WriteLine(line);
            }
        }

        public static Graph LoadFromCsv(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Nie znaleziono pliku: {path}");

            string[] lines = File.ReadAllLines(path);

            if (lines.Length < 2)
                throw new Exception("Plik CSV jest pusty albo nie zawiera danych.");

            var dataLines = lines
                .Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            int vertexCount = dataLines.Length;
            Graph graph = new Graph(vertexCount);

            foreach (string line in dataLines)
            {
                string[] parts = line.Split(',');

                if (parts.Length < 5)
                    throw new Exception($"Niepoprawny wiersz CSV: {line}");

                if (parts[0] != "NODE")
                    throw new Exception($"Niepoprawny typ rekordu: {parts[0]}");

                int id = int.Parse(parts[1]);
                double x = double.Parse(parts[2], CultureInfo.InvariantCulture);
                double y = double.Parse(parts[3], CultureInfo.InvariantCulture);

                graph.AddNode(id, x, y);
            }

            foreach (string line in dataLines)
            {
                string[] parts = line.Split(',');

                int id = int.Parse(parts[1]);
                string neighborsPart = parts[4];

                if (string.IsNullOrWhiteSpace(neighborsPart))
                    continue;

                string[] neighbors = neighborsPart.Split('|', StringSplitOptions.RemoveEmptyEntries);

                foreach (string neighborText in neighbors)
                {
                    int neighbor = int.Parse(neighborText);
                    graph.AddUndirectedEdge(id, neighbor);
                }
            }

            return graph;
        }
    }
}