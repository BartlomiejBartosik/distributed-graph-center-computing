using System.Diagnostics;
using GraphShared.Algorithms;
using GraphShared.IO;
using GraphShared.Models;

namespace gRPC.Services
{
    public class ConsoleJobRunner
    {
        private readonly ClusterState _clusterState;

        public ConsoleJobRunner(ClusterState clusterState)
        {
            _clusterState = clusterState;
        }

        public async Task RunAsync()
        {
            EnsureGraphsFolderExists();

            while (true)
            {
                Console.WriteLine("======================================");
                Console.WriteLine("AKTYWNI WORKERZY:");

                var workers = _clusterState.GetWorkerIds();

                if (workers.Count == 0)
                {
                    Console.WriteLine("Brak podłączonych workerów.");
                }
                else
                {
                    foreach (var worker in workers)
                    {
                        Console.WriteLine($"- {worker}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("1 - Wczytaj graf z pliku CSV i policz centrum");
                Console.WriteLine("2 - Wygeneruj nowy graf, zapisz do CSV i policz centrum");
                Console.WriteLine("0 - Zakończ serwer");
                Console.Write("Wybierz opcję: ");

                string? option = Console.ReadLine()?.Trim();

                if (option == "0")
                {
                    return;
                }

                if (_clusterState.ActiveWorkerCount == 0)
                {
                    Console.WriteLine("Najpierw uruchom przynajmniej jednego workera.");
                    Console.WriteLine();
                    continue;
                }

                Graph graph;

                try
                {
                    if (option == "1")
                    {
                        string? path = SelectCsvFile();

                        if (path == null)
                        {
                            Console.WriteLine("Nie udało się wybrać pliku.");
                            Console.WriteLine();
                            continue;
                        }

                        graph = GraphCsv.LoadFromCsv(path);
                        Console.WriteLine($"Graf został wczytany z pliku: {Path.GetFileName(path)}");
                    }
                    else if (option == "2")
                    {
                        Console.Write("Podaj liczbę wierzchołków: ");
                        int vertexCount = ReadPositiveInt();

                        if (vertexCount < 2)
                        {
                            Console.WriteLine("Liczba wierzchołków musi być >= 2.");
                            Console.WriteLine();
                            continue;
                        }

                        Console.Write("Podaj liczbę dodatkowych krawędzi: ");
                        int extraEdges = ReadNonNegativeInt();

                        Console.Write("Podaj nazwę pliku wyjściowego CSV: ");
                        string? fileName = Console.ReadLine()?.Trim();

                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            Console.WriteLine("Nazwa pliku nie może być pusta.");
                            Console.WriteLine();
                            continue;
                        }

                        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            fileName += ".csv";
                        }

                        string savePath = Path.Combine("graphs", fileName);

                        graph = GraphGenerator.GenerateConnectedGeometricGraph(vertexCount, extraEdges);
                        GraphCsv.SaveToCsv(graph, savePath);

                        Console.WriteLine($"Graf został wygenerowany i zapisany do pliku: {savePath}");
                    }
                    else
                    {
                        Console.WriteLine("Niepoprawna opcja.");
                        Console.WriteLine();
                        continue;
                    }

                    Console.WriteLine();
                    Console.WriteLine($"Liczba wierzchołków: {graph.VertexCount}");
                    Console.WriteLine($"Liczba krawędzi: {graph.GetEdgeCount()}");

                    Console.WriteLine();
                    Console.Write("Czy wyświetlić cały graf? (t/n): ");
                    string? showGraph = Console.ReadLine()?.Trim().ToLower();

                    if (showGraph == "t")
                    {
                        PrintGraph(graph);
                    }

                    int workerCount = _clusterState.ActiveWorkerCount;

                    int chunkSize = Math.Min(1000, Math.Max(100, graph.VertexCount / (workerCount * 4)));
                    if (chunkSize <= 0)
                    {
                        chunkSize = 1;
                    }

                    Console.WriteLine();
                    Console.WriteLine($"Aktywni workerzy: {workerCount}");
                    Console.WriteLine($"Rozmiar chunku: {chunkSize}");

                    Stopwatch stopwatch = Stopwatch.StartNew();

                    string jobId = _clusterState.CreateJob(graph, chunkSize);
                    GraphCenterResult result = await _clusterState.WaitForJobResultAsync(jobId);

                    stopwatch.Stop();

                    Console.WriteLine();
                    Console.WriteLine("WYNIK");
                    Console.WriteLine($"Najlepszy węzeł: {result.BestVertex}");
                    Console.WriteLine($"Minimalna suma odległości: {result.MinimumDistanceSum:F2}");
                    Console.WriteLine($"Czas wykonania: {stopwatch.ElapsedMilliseconds} ms");

                    Console.WriteLine();
                    Console.Write("Czy wyświetlić sumy odległości dla wszystkich wierzchołków? (t/n): ");
                    string? showSums = Console.ReadLine()?.Trim().ToLower();

                    if (showSums == "t")
                    {
                        for (int i = 0; i < result.DistanceSums.Length; i++)
                        {
                            Console.WriteLine($"Wierzchołek {i}: {result.DistanceSums[i]:F2}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine("Wystąpił błąd:");
                    Console.WriteLine(ex.Message);
                }

                Console.WriteLine();
            }
        }

        private static void EnsureGraphsFolderExists()
        {
            if (!Directory.Exists("graphs"))
            {
                Directory.CreateDirectory("graphs");
            }
        }

        private static string? SelectCsvFile()
        {
            string folderPath = "graphs";

            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine("Folder 'graphs' nie istnieje.");
                return null;
            }

            string[] files = Directory
                .GetFiles(folderPath, "*.csv")
                .OrderBy(f => f)
                .ToArray();

            if (files.Length == 0)
            {
                Console.WriteLine("Brak plików CSV w folderze 'graphs'.");
                return null;
            }

            Console.WriteLine();
            Console.WriteLine("Dostępne pliki:");

            for (int i = 0; i < files.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {Path.GetFileName(files[i])}");
            }

            Console.Write("Wybierz numer pliku: ");
            int choice = ReadPositiveInt();

            if (choice < 1 || choice > files.Length)
            {
                Console.WriteLine("Niepoprawny wybór.");
                return null;
            }

            return files[choice - 1];
        }

        private static int ReadPositiveInt()
        {
            while (true)
            {
                string? input = Console.ReadLine();

                if (int.TryParse(input, out int value) && value > 0)
                {
                    return value;
                }

                Console.Write("Podaj poprawną dodatnią liczbę całkowitą: ");
            }
        }

        private static int ReadNonNegativeInt()
        {
            while (true)
            {
                string? input = Console.ReadLine();

                if (int.TryParse(input, out int value) && value >= 0)
                {
                    return value;
                }

                Console.Write("Podaj poprawną liczbę całkowitą >= 0: ");
            }
        }

        private static void PrintGraph(Graph graph)
        {
            for (int i = 0; i < graph.VertexCount; i++)
            {
                Console.Write($"Wierzchołek {i} ({graph.Nodes[i].X:F2}, {graph.Nodes[i].Y:F2}): ");

                foreach (var edge in graph.AdjacencyList[i])
                {
                    Console.Write($"-> {edge.Target} [waga: {edge.Weight:F2}] ");
                }

                Console.WriteLine();
            }
        }
    }
}