using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// The high-contrast palette only works by mutating shared brushes in place, so the two things
    /// that can silently break it are a key that no longer exists in the style sheet and a brush
    /// that got frozen. The last two tests check the real XAML for both.
    /// </summary>
    public class HighContrastThemeTests
    {
        private static ResourceDictionary Palette()
        {
            var resources = new ResourceDictionary();
            foreach (string key in HighContrastTheme.HighContrastColors().Keys)
            {
                resources[key] = new SolidColorBrush(Colors.Black);
            }

            return resources;
        }

        private static HighContrastTheme Theme(ResourceDictionary resources, bool highContrast) =>
            new(resources, () => highContrast);

        [Fact]
        public void Apply_WhenHighContrastIsOn_RepaintsEveryChromeBrush()
        {
            var resources = Palette();
            var theme = Theme(resources, highContrast: true);

            theme.ApplyIfNeeded();

            foreach (var pair in HighContrastTheme.HighContrastColors())
            {
                ((SolidColorBrush)resources[pair.Key]).Color.Should().Be(
                    pair.Value, $"'{pair.Key}' should follow the system palette");
            }

            theme.IsApplied.Should().BeTrue();
        }

        [Fact]
        public void Apply_WhenHighContrastIsOff_RestoresTheOriginalColours()
        {
            var resources = Palette();
            var theme = new HighContrastTheme(resources);

            theme.Apply(true);
            theme.Apply(false);

            foreach (string key in HighContrastTheme.HighContrastColors().Keys)
            {
                ((SolidColorBrush)resources[key]).Color.Should().Be(Colors.Black);
            }

            theme.IsApplied.Should().BeFalse();
        }

        [Fact]
        public void Apply_Repeatedly_KeepsTheOriginalsForRestore()
        {
            var resources = Palette();
            var theme = new HighContrastTheme(resources);

            // A second "on" must not snapshot the high-contrast colours as if they were the theme.
            theme.Apply(true);
            theme.Apply(true);
            theme.Apply(false);

            ((SolidColorBrush)resources[HighContrastTheme.Keys.TextPrimary]).Color.Should().Be(Colors.Black);
        }

        [Fact]
        public void Apply_WithAFrozenBrush_DoesNotThrow_AndLeavesItAlone()
        {
            var resources = Palette();
            var frozen = new SolidColorBrush(Colors.Black);
            frozen.Freeze();
            resources[HighContrastTheme.Keys.Border] = frozen;

            var theme = new HighContrastTheme(resources);

            Action apply = () => theme.Apply(true);

            apply.Should().NotThrow();
            frozen.Color.Should().Be(Colors.Black);
        }

        [Fact]
        public void Apply_WithAMissingKey_DoesNotThrow()
        {
            var resources = Palette();
            resources.Remove(HighContrastTheme.Keys.Accent);

            var theme = new HighContrastTheme(resources);

            Action apply = () => theme.Apply(true);

            apply.Should().NotThrow();
        }

        [Fact]
        public void EveryPaletteKey_ExistsInModernStylesAndIsNotFrozen()
        {
            string? stylesPath = FindStylesFile();
            stylesPath.Should().NotBeNull("the test needs the app's style sheet to check the contract");

            var lines = File.ReadAllLines(stylesPath!);

            foreach (string key in HighContrastTheme.HighContrastColors().Keys)
            {
                string? declaration = lines.FirstOrDefault(
                    line => line.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal));

                declaration.Should().NotBeNull($"'{key}' must stay declared in ModernStyles.xaml");

                // Mutation only reaches brush instances WPF has not frozen, and the style sheet is
                // the only place that guarantee can be made.
                declaration!.Should().Contain("Freeze=\"False\"",
                    $"'{key}' is repainted at runtime, so it must opt out of freezing");
            }
        }

        /// <summary>
        /// Walks up from the test binaries to the repo root. Kept as a search rather than a
        /// relative "../../.." because the depth differs between Debug and Release output.
        /// </summary>
        private static string? FindStylesFile()
        {
            const string relative = "src/OmenCoreApp/Styles/ModernStyles.xaml";
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
