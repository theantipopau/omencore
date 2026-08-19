using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// AppFonts.App/Monospace resolve the app's two shared font resources for code-behind call
    /// sites (tray icon, native context menus, toasts) that can't use a XAML StaticResource. In
    /// this headless test host there is no Application.Current (same as DashboardViewModel's
    /// dispatcher fallback elsewhere in this suite), so these tests exercise exactly the guard
    /// that matters most: no WPF Application loaded yet must never throw or return null.
    /// </summary>
    public class AppFontsTests
    {
        [Fact]
        public void App_NoApplicationLoaded_ReturnsNonNullFallback()
        {
            AppFonts.App.Should().NotBeNull();
            AppFonts.App.Source.Should().Be("Segoe UI");
        }

        [Fact]
        public void Monospace_NoApplicationLoaded_ReturnsNonNullFallback()
        {
            AppFonts.Monospace.Should().NotBeNull();
            AppFonts.Monospace.Source.Should().Contain("Cascadia Mono").And.Contain("Consolas");
        }

        [Fact]
        public void App_And_Monospace_AreDistinctFallbacks()
        {
            AppFonts.App.Source.Should().NotBe(AppFonts.Monospace.Source);
        }
    }
}
