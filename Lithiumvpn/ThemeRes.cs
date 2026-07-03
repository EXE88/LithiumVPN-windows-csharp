using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Lithiumvpn
{
    /// <summary>
    /// Resolves theme resources against an element's <see cref="FrameworkElement.ActualTheme"/>.
    ///
    /// The app theme is applied per-window by DevWinUI's ThemeService, so
    /// <c>Application.Current.Resources[key]</c> resolves against the OS theme
    /// instead of the theme the user actually sees — dynamic UI built in
    /// code-behind must use this helper instead.
    /// </summary>
    public static class ThemeRes
    {
        public static Brush Brush(FrameworkElement element, string key) =>
            (Brush)Get(element.ActualTheme, key);

        public static object Get(ElementTheme theme, string key)
        {
            // WinUI theme dictionaries use "Default" for dark and "Light" for light.
            string dictKey = theme == ElementTheme.Dark ? "Default" : "Light";
            if (TryFind(Application.Current.Resources, dictKey, key, out var value))
                return value;
            return Application.Current.Resources[key];
        }

        private static bool TryFind(ResourceDictionary dict, string dictKey, string key, out object value)
        {
            if (dict.ThemeDictionaries.TryGetValue(dictKey, out var themed) &&
                themed is ResourceDictionary themedDict &&
                themedDict.ContainsKey(key))
            {
                value = themedDict[key];
                return true;
            }

            foreach (var merged in dict.MergedDictionaries)
                if (TryFind(merged, dictKey, key, out value))
                    return true;

            value = null!;
            return false;
        }
    }
}
