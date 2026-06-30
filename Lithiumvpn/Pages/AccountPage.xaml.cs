using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Lithiumvpn.Localization;

namespace Lithiumvpn.Pages
{
    public sealed partial class AccountPage : Page
    {
        public AccountPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(StartTour);
            }
        }

        private bool _tourAttached = false;
        private void StartTour()
        {
            if (!_tourAttached)
            {
                _tourAttached = true;
                TourTip1.ActionButtonClick += (s, e) => { TourTip1.IsOpen = false; TourTip2.IsOpen = true; };
                TourTip1.CloseButtonClick  += (s, e) => TourTip1.IsOpen = false;
                TourTip2.ActionButtonClick += (s, e) => { TourTip2.IsOpen = false; TourTip3.IsOpen = true; };
                TourTip2.CloseButtonClick  += (s, e) => TourTip2.IsOpen = false;
                TourTip3.CloseButtonClick  += (s, e) => TourTip3.IsOpen = false;
            }
            TourTip1.IsOpen = true;
        }

        private async void SignOut_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationManager.Instance;
            var dialog = new ContentDialog
            {
                Title = loc.Get("Account_SignOutDialogTitle"),
                Content = loc.Get("Account_SignOutDialogBody"),
                PrimaryButtonText = loc.Get("Account_SignOut"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                this.Frame?.Navigate(typeof(LoginPage));
        }

        private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationManager.Instance;
            await new ContentDialog
            {
                Title = loc.Get("Account_DeleteDialogTitle"),
                Content = loc.Get("Account_DeleteDialogBody"),
                PrimaryButtonText = loc.Get("Account_DeleteForever"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }
    }
}
