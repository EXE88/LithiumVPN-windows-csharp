using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace Lithiumvpn.Pages
{
    public sealed partial class HelpPage : Page
    {
        public HelpPage()
        {
            this.InitializeComponent();
        }

        private void StartTour_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var tag = btn.Tag?.ToString();
            System.Type? pageType = tag switch
            {
                "Dashboard" => typeof(DashboardPage),
                "Configs"   => typeof(ServersPage),
                "Account"   => typeof(AccountPage),
                "Coins"     => typeof(CoinsPage),
                "Plans"     => typeof(PlansPage),
                _           => null
            };

            if (pageType is not null)
                TourManager.RequestTour(pageType);
        }
    }
}
