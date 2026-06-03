using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Lithiumvpn.Pages
{
    public sealed partial class DashboardPage : Page
    {
        private enum ConnectionState { Disconnected, Connecting, Connected }
        private ConnectionState currentState = ConnectionState.Disconnected;
        private DispatcherTimer connectionTimer;

        public DashboardPage()
        {
            this.InitializeComponent();

            // Keep page instance cached so UI state (connection button visual state) persists across navigation
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

            connectionTimer = new DispatcherTimer();
            connectionTimer.Interval = TimeSpan.FromSeconds(2.5);
            connectionTimer.Tick += ConnectionTimer_Tick;

            UpdateVisualState();
        }

        private void MainConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentState == ConnectionState.Disconnected)
            {
                currentState = ConnectionState.Connecting;
                connectionTimer.Start();
                UpdateVisualState();
            }
            else if (currentState == ConnectionState.Connected)
            {
                currentState = ConnectionState.Disconnected;
                UpdateVisualState();
            }
        }

        private void ConnectionTimer_Tick(object? sender, object e)
        {
            connectionTimer.Stop();
            currentState = ConnectionState.Connected;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            string stateName = currentState switch
            {
                ConnectionState.Disconnected => "Disconnected",
                ConnectionState.Connecting => "Connecting",
                ConnectionState.Connected => "Connected",
                _ => "Disconnected"
            };

            VisualStateManager.GoToState(MainConnectButton, stateName, true);
        }
    }
}