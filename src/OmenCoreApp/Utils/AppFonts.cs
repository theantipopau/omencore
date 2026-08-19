using System.Windows;
using System.Windows.Media;

namespace OmenCore.Utils
{
    /// <summary>
    /// Resolves the app's two shared font resources (AppFontFamily/MonospaceFontFamily, declared
    /// in Styles/ModernStyles.xaml) for the code-behind call sites that build WPF visuals directly
    /// — tray icon rendering, native context menus, toast popups — and therefore can't reference a
    /// StaticResource the way XAML markup can. Keeping these in sync with the XAML resources here
    /// means a future font change (e.g. swapping AppFontFamily to an embedded typeface) only needs
    /// to touch the resource dictionary, not every hardcoded "Segoe UI" scattered through code-behind.
    /// </summary>
    internal static class AppFonts
    {
        private static readonly FontFamily FallbackApp = new("Segoe UI");
        private static readonly FontFamily FallbackMonospace = new("Cascadia Mono, Consolas, \"Courier New\", monospace");

        /// <summary>The app-wide text font. Falls back to Segoe UI if resources aren't loaded yet (e.g. very early startup, or a design-time context).</summary>
        public static FontFamily App => Resolve("AppFontFamily", FallbackApp);

        /// <summary>Log output, diagnostic dumps, and tabular/hex values where column alignment matters.</summary>
        public static FontFamily Monospace => Resolve("MonospaceFontFamily", FallbackMonospace);

        private static FontFamily Resolve(string key, FontFamily fallback)
        {
            if (Application.Current?.TryFindResource(key) is FontFamily resource)
            {
                return resource;
            }

            return fallback;
        }
    }
}
