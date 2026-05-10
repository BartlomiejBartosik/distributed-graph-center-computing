using GraphGui.Services;
using System;
using System.Windows;

namespace GraphGui
{
    public partial class App : Application
    {
        private readonly GrpcServerHost _serverHost = new GrpcServerHost();

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                await _serverHost.StartAsync();

                if (_serverHost.ClusterState == null)
                {
                    MessageBox.Show("Nie udało się uruchomić stanu klastra.");
                    Shutdown();
                    return;
                }

                MainWindow window = new MainWindow(_serverHost.ClusterState);
                window.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Nie udało się uruchomić serwera gRPC.\n\n" + ex.Message,
                    "Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await _serverHost.StopAsync();

            base.OnExit(e);
        }
    }
}