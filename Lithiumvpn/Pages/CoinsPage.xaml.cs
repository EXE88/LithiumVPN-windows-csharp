using Microsoft.UI.Xaml.Controls;

namespace Lithiumvpn.Pages
{
    public sealed partial class CoinsPage : Page
    {
        public CoinsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(() =>
                {
                    TourTip1.CloseButtonClick += (s, ev) => TourTip1.IsOpen = false;
                    TourTip1.IsOpen = true;
                });
            }
        }
    }
}
