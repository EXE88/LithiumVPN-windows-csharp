using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;

namespace Lithiumvpn
{
    public sealed partial class MainWindow : Window
    {
        private enum ConnectionState { Disconnected, Connecting, Connected }
        private ConnectionState currentState = ConnectionState.Disconnected;
        private DispatcherTimer connectionTimer;

        public MainWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;

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
