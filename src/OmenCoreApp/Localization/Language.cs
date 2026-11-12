using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace OmenCore.Localization
{
    /// <summary>One selectable UI language. <c>Code</c> is what goes in the config file.</summary>
    public sealed record LanguageOption(string Code, string Name);

    /// <summary>
    /// Chooses the UI culture (roadmap WP-32).
    ///
    /// Deliberately UI-only. <c>App</c> forces invariant number formatting because every display
    /// site writes "95.0%" and every one of them would otherwise follow the OS locale; this changes
    /// only <see cref="CultureInfo.CurrentUICulture"/>, which is what <c>ResourceManager</c> reads
    /// for string lookup and what nothing else here depends on. So a Russian user gets Russian
    /// labels and still gets "95.0%" rather than "95,0%".
    /// </summary>
    public static class Language
    {
        public const string SystemDefault = "auto";

        public static IReadOnlyList<LanguageOption> Supported { get; } = new[]
        {
            new LanguageOption(SystemDefault, "System default"),
            new LanguageOption("en", "English"),
            new LanguageOption("ru", "Русский"),
            new LanguageOption("tr", "Türkçe")
        };

        /// <summary>
        /// Clamps whatever is in the config to a language we ship, so a hand-edited or
        /// forward-dated "de" falls back to the system default instead of loading a culture whose
        /// catalogs do not exist.
        /// </summary>
        public static string Normalize(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return SystemDefault;
            }

            string trimmed = code.Trim().ToLowerInvariant();
            return Supported.Any(option => option.Code == trimmed) ? trimmed : SystemDefault;
        }

        /// <summary>
        /// The culture to render strings in. "auto" is the language Windows is installed in; a
        /// culture we have no catalog for simply falls back through the neutral (English) resource,
        /// which is what ResourceManager does when a satellite is missing.
        /// </summary>
        public static CultureInfo Resolve(string? code, CultureInfo? systemUiCulture = null)
        {
            string normalized = Normalize(code);
            if (normalized == SystemDefault)
            {
                return systemUiCulture ?? CultureInfo.InstalledUICulture;
            }

            try
            {
                return CultureInfo.GetCultureInfo(normalized);
            }
            catch (CultureNotFoundException)
            {
                // A broken ICU install should not stop the app from starting in English.
                return CultureInfo.InstalledUICulture;
            }
        }

        /// <summary>
        /// Applies the configured language to the UI culture. Called once at startup, before any
        /// window is created, because XAML resolves <c>x:Static</c> strings as it loads and the
        /// window would otherwise be built in the previous culture.
        /// </summary>
        public static void Apply(string? code)
        {
            CultureInfo culture = Resolve(code);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
    }
}
