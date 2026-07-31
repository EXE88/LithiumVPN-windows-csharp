using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using System;

namespace Lithiumvpn.Pages
{
    public sealed partial class CoinsPage : Page
    {
        private const string AdminId = "@Devamirkaj";
        private const string TelegramDeepLink = "https://t.me/Devamirkaj";

        public CoinsPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;

            Services.ConnectivityService.Instance.StateChanged +=
                _ => DispatcherQueue.TryEnqueue(ApplyConnectivity);

            OfflinePanel.WentOnline += (_, _) => ApplyConnectivity();
            OfflinePanel.NeedLogin += (_, _) => Frame?.Navigate(typeof(LoginPage));
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ApplyConnectivity();
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(() => StartTour());
            }
        }

        private void ApplyConnectivity()
        {
            bool online = Services.ConnectivityService.Instance.IsOnline;
            ContentScroll.Visibility = online ? Visibility.Visible : Visibility.Collapsed;
            OfflinePanel.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
            if (online) PopulateBalance();
        }

        private void PopulateBalance()
        {
            var status = Services.AppState.Instance.Status;
            if (status is not null)
                CoinBalanceText.Text = status.CoinCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void StartTour()
        {
            TourTip1.ActionButtonClick += (s, e) => { TourTip1.IsOpen = false; TourTip2.IsOpen = true; };
            TourTip2.ActionButtonClick += (s, e) => { TourTip2.IsOpen = false; TourTip3.IsOpen = true; };
            TourTip1.IsOpen = true;
        }

        private void CopyId_Click(object sender, RoutedEventArgs e)
        {
            var dp = new DataPackage();
            dp.SetText(AdminId);
            Clipboard.SetContent(dp);
        }

        private async void OpenTelegram_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(TelegramDeepLink));
        }
    }
}
