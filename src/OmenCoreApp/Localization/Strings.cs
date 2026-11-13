using System.Globalization;
using System.Resources;

namespace OmenCore.Localization
{
    /// <summary>
    /// Typed access to <c>Localization/Strings.resx</c> and its satellite catalogs (roadmap WP-32).
    ///
    /// Written by hand rather than generated: the SDK only produces a designer class when Visual
    /// Studio's custom tool runs, so a CLI build (which is what CI is) would otherwise leave the
    /// class missing. Each property is one line, and <c>StringsResourceTests</c> fails if a key in
    /// the .resx has no property here or the other way round - that test is what replaces the
    /// generator's guarantee.
    ///
    /// A missing key returns its own name instead of throwing or rendering nothing: the user sees
    /// something obviously unfinished on that one label, and the app still starts. The parity test
    /// means that should never reach a release.
    /// </summary>
    public static class Strings
    {
        private static readonly ResourceManager Resources =
            new(typeof(Strings).FullName!, typeof(Strings).Assembly);

        private static string Get(string key) =>
            Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

        // Window chrome
        public static string WindowTitle => Get(nameof(WindowTitle));
        public static string WindowMinimize => Get(nameof(WindowMinimize));
        public static string WindowMaximize => Get(nameof(WindowMaximize));
        public static string WindowRestore => Get(nameof(WindowRestore));
        public static string WindowClose => Get(nameof(WindowClose));

        // Navigation rail
        public static string NavGeneral => Get(nameof(NavGeneral));
        public static string NavCustom => Get(nameof(NavCustom));
        public static string NavTuning => Get(nameof(NavTuning));
        public static string NavDiagnostics => Get(nameof(NavDiagnostics));
        public static string NavMonitoring => Get(nameof(NavMonitoring));
        public static string NavOptimizer => Get(nameof(NavOptimizer));
        public static string NavMemory => Get(nameof(NavMemory));
        public static string NavBloatware => Get(nameof(NavBloatware));
        public static string NavRgb => Get(nameof(NavRgb));
        public static string NavSettings => Get(nameof(NavSettings));
        public static string NavGames => Get(nameof(NavGames));

        // Settings page: window behaviour
        public static string SettingsWindowBehaviour => Get(nameof(SettingsWindowBehaviour));
        public static string SettingsStartWithWindows => Get(nameof(SettingsStartWithWindows));
        public static string SettingsStartWithWindowsHelp => Get(nameof(SettingsStartWithWindowsHelp));
        public static string SettingsStartMinimized => Get(nameof(SettingsStartMinimized));
        public static string SettingsStartMinimizedHelp => Get(nameof(SettingsStartMinimizedHelp));
        public static string SettingsLiteMode => Get(nameof(SettingsLiteMode));
        public static string SettingsLiteModeHelp => Get(nameof(SettingsLiteModeHelp));
        public static string SettingsHeadlessMode => Get(nameof(SettingsHeadlessMode));
        public static string SettingsHeadlessModeHelp => Get(nameof(SettingsHeadlessModeHelp));
        public static string SettingsSoftwareRendering => Get(nameof(SettingsSoftwareRendering));
        public static string SettingsSoftwareRenderingHelp => Get(nameof(SettingsSoftwareRenderingHelp));
        public static string SettingsReduceMotion => Get(nameof(SettingsReduceMotion));
        public static string SettingsReduceMotionHelp => Get(nameof(SettingsReduceMotionHelp));
        public static string SettingsLanguage => Get(nameof(SettingsLanguage));
        public static string SettingsLanguageHelp => Get(nameof(SettingsLanguageHelp));
        public static string SettingsMinimizeToTray => Get(nameof(SettingsMinimizeToTray));
        public static string SettingsMinimizeToTrayHelp => Get(nameof(SettingsMinimizeToTrayHelp));
        public static string SettingsStayOnTop => Get(nameof(SettingsStayOnTop));
        public static string SettingsStayOnTopHelp => Get(nameof(SettingsStayOnTopHelp));
        public static string SettingsTopmostBadge => Get(nameof(SettingsTopmostBadge));
        public static string SettingsActiveBadge => Get(nameof(SettingsActiveBadge));

        // Settings page: tray and interface
        public static string SettingsTrayAndUi => Get(nameof(SettingsTrayAndUi));
        public static string SettingsDisplayScale => Get(nameof(SettingsDisplayScale));
        public static string SettingsDisplayScaleHelp => Get(nameof(SettingsDisplayScaleHelp));
        public static string SettingsQuickAccess => Get(nameof(SettingsQuickAccess));
        public static string SettingsQuickAccessHelp => Get(nameof(SettingsQuickAccessHelp));
        public static string SettingsQuickPopup => Get(nameof(SettingsQuickPopup));
        public static string SettingsQuickPopupHelp => Get(nameof(SettingsQuickPopupHelp));
        public static string SettingsTrayTemperature => Get(nameof(SettingsTrayTemperature));
        public static string SettingsTrayTemperatureHelp => Get(nameof(SettingsTrayTemperatureHelp));
        public static string SettingsCorsairHidOnly => Get(nameof(SettingsCorsairHidOnly));
        public static string SettingsCorsairHidOnlyHelp => Get(nameof(SettingsCorsairHidOnlyHelp));
        public static string SettingsCorsairHidOnlyWarning => Get(nameof(SettingsCorsairHidOnlyWarning));
        public static string SettingsTelemetry => Get(nameof(SettingsTelemetry));
        public static string SettingsTelemetryHelp => Get(nameof(SettingsTelemetryHelp));
    }
}
