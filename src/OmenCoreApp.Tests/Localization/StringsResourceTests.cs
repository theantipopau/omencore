using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using OmenCore.Localization;
using Xunit;

namespace OmenCoreApp.Tests.Localization
{
    /// <summary>
    /// Replaces the designer-file guarantee that a CLI build does not give us: every key in the
    /// neutral catalog has a property, and every key exists and is filled in for every shipped
    /// language. Without this, adding a string and forgetting Russian is silent until a user sees
    /// the English word in a Russian window.
    /// </summary>
    public class StringsResourceTests
    {
        private static readonly string[] Languages = { "en", "ru", "tr" };

        private static string LocalizationDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "src/OmenCoreApp/Localization");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find src/OmenCoreApp/Localization from the test output folder");
        }

        private static string CataloguePath(string language) =>
            Path.Combine(LocalizationDirectory(), language == "en" ? "Strings.resx" : $"Strings.{language}.resx");

        private static Dictionary<string, string> ReadCatalogue(string language)
        {
            XNamespace none = XNamespace.None;
            return XDocument.Load(CataloguePath(language))
                .Root!
                .Elements(none + "data")
                .ToDictionary(
                    element => element.Attribute("name")!.Value,
                    element => element.Element(none + "value")?.Value ?? string.Empty);
        }

        [Fact]
        public void EveryLanguageHasTheSameKeysAsTheNeutralCatalogue()
        {
            var english = ReadCatalogue("en");

            foreach (string language in Languages.Where(l => l != "en"))
            {
                var translated = ReadCatalogue(language);

                translated.Keys.Should().BeEquivalentTo(english.Keys,
                    $"'{language}' must be a complete translation, not a partial one");
            }
        }

        [Fact]
        public void NoTranslationIsEmpty()
        {
            foreach (string language in Languages)
            {
                foreach (var pair in ReadCatalogue(language))
                {
                    pair.Value.Should().NotBeNullOrWhiteSpace(
                        $"'{pair.Key}' in '{language}' must carry a value");
                }
            }
        }

        [Fact]
        public void EveryCatalogueKeyHasTypedAccessor()
        {
            var properties = typeof(Strings)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string key in ReadCatalogue("en").Keys)
            {
                properties.Should().Contain(key,
                    $"Strings.{key} is what XAML and code use to reach that catalogue entry");
            }
        }

        [Fact]
        public void Resolve_MapsEverySupportedCode()
        {
            Language.Resolve("en").TwoLetterISOLanguageName.Should().Be("en");
            Language.Resolve("ru").TwoLetterISOLanguageName.Should().Be("ru");
            Language.Resolve("tr").TwoLetterISOLanguageName.Should().Be("tr");
        }

        [Fact]
        public void Resolve_FallsBackToTheSystemCultureForAutoAndUnknownCodes()
        {
            var system = CultureInfo.GetCultureInfo("de-DE");

            Language.Resolve("auto", system).Should().Be(system);
            Language.Resolve("klingon", system).Should().Be(system);
            Language.Resolve(null, system).Should().Be(system);
        }

        [Fact]
        public void Normalize_ClampsToWhatWeShip()
        {
            Language.Normalize(null).Should().Be(Language.SystemDefault);
            Language.Normalize("  ").Should().Be(Language.SystemDefault);
            Language.Normalize("RU").Should().Be("ru");
            Language.Normalize("de").Should().Be(Language.SystemDefault);
        }

        [Fact]
        public void Strings_ResolveFromTheSatelliteCatalogs_NotJustTheNeutralOne()
        {
            // The parity tests read the .resx files; this one proves the built satellites are
            // actually loaded at runtime, which is the difference between "translated" and
            // "ships translated".
            CultureInfo original = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
                Strings.NavSettings.Should().Be("Настройки");
                Strings.WindowMinimize.Should().Be("Свернуть");

                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr");
                Strings.NavGames.Should().Be("Oyunlar");

                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                Strings.NavGames.Should().Be("Games");
            }
            finally
            {
                CultureInfo.CurrentUICulture = original;
            }
        }

        [Fact]
        public void Apply_ChangesOnlyTheUiCulture()
        {
            // Numbers must keep the invariant formatting App installs on every thread; a UI
            // language that also moved CurrentCulture would reintroduce the comma-decimal bug.
            CultureInfo originalUi = CultureInfo.CurrentUICulture;
            CultureInfo originalCulture = CultureInfo.CurrentCulture;

            try
            {
                Language.Apply("ru");

                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Should().Be("ru");
                CultureInfo.CurrentCulture.Should().Be(originalCulture);
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentUICulture = originalUi;
                CultureInfo.CurrentUICulture = originalUi;
                CultureInfo.CurrentCulture = originalCulture;
            }
        }
    }
}
