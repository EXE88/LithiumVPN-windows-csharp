using System;

namespace Lithiumvpn
{
    public static class TourManager
    {
        public static event Action<Type>? TourRequested;
        public static bool IsTourPending { get; set; }

        public static void RequestTour(Type pageType)
        {
            IsTourPending = true;
            TourRequested?.Invoke(pageType);
        }
    }
}
