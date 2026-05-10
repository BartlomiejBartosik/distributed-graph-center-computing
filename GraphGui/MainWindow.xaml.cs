using GraphShared.Algorithms;
using GraphShared.IO;
using GraphShared.Models;
using gRPC.Services;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GraphGui
{
    public partial class MainWindow : Window
    {
        private readonly ClusterState _clusterState;
        private Graph? _currentGraph;
        private int? _bestVertex;

        public MainWindow(ClusterState clusterState)
        {
            InitializeComponent();

            _clusterState = clusterState;

            RefreshWorkers();
        }

        private void LoadCsv_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                _currentGraph = GraphCsv.LoadFromCsv(dialog.FileName);
                _bestVertex = null;

                DrawGraph();

                StatusTextBlock.Text = "Status: wczytano graf z pliku CSV";

                ResultTextBlock.Text =
                    $"Wczytany graf:\n" +
                    $"Wierzchołki: {_currentGraph.VertexCount}\n" +
                    $"Krawędzie: {_currentGraph.GetEdgeCount()}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Nie udało się wczytać grafu.\n\n" + ex.Message,
                    "Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void GenerateGraph_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(VertexCountTextBox.Text, out int vertexCount) || vertexCount <= 0)
            {
                MessageBox.Show("Podaj poprawną liczbę wierzchołków.");
                return;
            }

            if (!int.TryParse(ExtraEdgesTextBox.Text, out int extraEdges) || extraEdges < 0)
            {
                MessageBox.Show("Podaj poprawną liczbę dodatkowych krawędzi.");
                return;
            }

            try
            {
                _currentGraph = GraphGenerator.GenerateConnectedGeometricGraph(vertexCount, extraEdges);
                _bestVertex = null;

                DrawGraph();

                StatusTextBlock.Text = "Status: wygenerowano graf";

                ResultTextBlock.Text =
                    $"Wygenerowany graf:\n" +
                    $"Wierzchołki: {_currentGraph.VertexCount}\n" +
                    $"Krawędzie: {_currentGraph.GetEdgeCount()}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Nie udało się wygenerować grafu.\n\n" + ex.Message,
                    "Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SaveCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGraph == null)
            {
                MessageBox.Show(
                    "Nie ma grafu do zapisania. Najpierw wczytaj albo wygeneruj graf.",
                    "Brak grafu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                FileName = "graph.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                GraphCsv.SaveToCsv(_currentGraph, dialog.FileName);

                StatusTextBlock.Text = "Status: zapisano graf do pliku CSV";

                MessageBox.Show(
                    "Graf został zapisany do pliku CSV.",
                    "Zapis zakończony",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Nie udało się zapisać grafu.\n\n" + ex.Message,
                    "Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void CalculateCenter_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGraph == null)
            {
                MessageBox.Show(
                    "Najpierw wczytaj albo wygeneruj graf.",
                    "Brak grafu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (_clusterState.ActiveWorkerCount == 0)
            {
                MessageBox.Show(
                    "Brak aktywnych workerów. Uruchom projekt gRPCclient2.",
                    "Brak workerów",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!int.TryParse(ChunkSizeTextBox.Text, out int chunkSize) || chunkSize <= 0)
            {
                MessageBox.Show(
                    "Podaj poprawny rozmiar chunku.",
                    "Niepoprawne dane",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                StatusTextBlock.Text = "Status: trwa obliczanie...";
                ResultTextBlock.Text = "Wynik: oczekiwanie na workery...";

                Stopwatch stopwatch = Stopwatch.StartNew();

                string jobId = _clusterState.CreateJob(_currentGraph, chunkSize);
                GraphCenterResult result = await _clusterState.WaitForJobResultAsync(jobId);

                stopwatch.Stop();

                _bestVertex = result.BestVertex;

                DrawGraph();

                StatusTextBlock.Text = "Status: zakończono obliczenia";

                ResultTextBlock.Text =
                    $"WYNIK:\n" +
                    $"Centrum grafu: {result.BestVertex}\n" +
                    $"Minimalna suma odległości: {result.MinimumDistanceSum:F2}\n" +
                    $"Czas wykonania: {stopwatch.ElapsedMilliseconds} ms";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Wystąpił błąd podczas obliczania.\n\n" + ex.Message,
                    "Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusTextBlock.Text = "Status: błąd obliczeń";
            }
        }

        private void RefreshWorkers_Click(object sender, RoutedEventArgs e)
        {
            RefreshWorkers();
        }

        private void RefreshWorkers()
        {
            WorkersListBox.Items.Clear();

            var workers = _clusterState.GetWorkerIds();

            if (workers.Count == 0)
            {
                WorkersListBox.Items.Add("Brak workerów");
                return;
            }

            foreach (string workerId in workers)
            {
                WorkersListBox.Items.Add(workerId);
            }
        }

        private void DrawGraph()
        {
            GraphCanvas.Children.Clear();

            if (_currentGraph == null)
            {
                return;
            }

            double canvasWidth = GraphCanvas.ActualWidth;
            double canvasHeight = GraphCanvas.ActualHeight;

            if (canvasWidth <= 0)
            {
                canvasWidth = 750;
            }

            if (canvasHeight <= 0)
            {
                canvasHeight = 550;
            }

            double margin = 35;

            double minX = _currentGraph.Nodes.Min(node => node.X);
            double maxX = _currentGraph.Nodes.Max(node => node.X);
            double minY = _currentGraph.Nodes.Min(node => node.Y);
            double maxY = _currentGraph.Nodes.Max(node => node.Y);

            double rangeX = maxX - minX;
            double rangeY = maxY - minY;

            if (rangeX == 0)
            {
                rangeX = 1;
            }

            if (rangeY == 0)
            {
                rangeY = 1;
            }

            double ScaleX(double x)
            {
                return margin + ((x - minX) / rangeX) * (canvasWidth - 2 * margin);
            }

            double ScaleY(double y)
            {
                return margin + ((y - minY) / rangeY) * (canvasHeight - 2 * margin);
            }

            for (int source = 0; source < _currentGraph.VertexCount; source++)
            {
                foreach (var edge in _currentGraph.AdjacencyList[source])
                {
                    if (source > edge.Target)
                    {
                        continue;
                    }

                    Node sourceNode = _currentGraph.Nodes[source];
                    Node targetNode = _currentGraph.Nodes[edge.Target];

                    Line line = new Line
                    {
                        X1 = ScaleX(sourceNode.X),
                        Y1 = ScaleY(sourceNode.Y),
                        X2 = ScaleX(targetNode.X),
                        Y2 = ScaleY(targetNode.Y),
                        Stroke = Brushes.DimGray,
                        StrokeThickness = 1
                    };

                    GraphCanvas.Children.Add(line);
                }
            }

            for (int i = 0; i < _currentGraph.VertexCount; i++)
            {
                Node node = _currentGraph.Nodes[i];

                bool isBestVertex = _bestVertex.HasValue && _bestVertex.Value == i;

                double size = isBestVertex ? 22 : 12;

                Ellipse ellipse = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = isBestVertex ? Brushes.Red : Brushes.DodgerBlue,
                    Stroke = Brushes.White,
                    StrokeThickness = isBestVertex ? 2 : 1,
                    ToolTip = $"Wierzchołek: {i}"
                };

                Canvas.SetLeft(ellipse, ScaleX(node.X) - size / 2);
                Canvas.SetTop(ellipse, ScaleY(node.Y) - size / 2);

                GraphCanvas.Children.Add(ellipse);
            }
        }
    }
}