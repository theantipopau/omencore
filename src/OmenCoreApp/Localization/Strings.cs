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
    }
}
