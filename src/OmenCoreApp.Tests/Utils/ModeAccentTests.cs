using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    public class ModeAccentTests
    {
        [Theory]
        [InlineData("Performance", 0xFF, 0x6B, 0x35)]
        [InlineData("Balanced", 0x00, 0xD4, 0xFF)]
        [InlineData("Quiet", 0x00, 0xFF, 0x88)]
        [InlineData("Custom", 0xA8, 0x55, 0xF7)]
        [InlineData(null, 0xFF, 0x00, 0x5C)]
        [InlineData("SomethingElse", 0xFF, 0x00, 0x5C)]
        public void For_MatchesTheProfileCardColours_AndFallsBackToTheBrandRed(string? profile, byte r, byte g, byte b)
        {
            ModeAccent.For(profile).Should().Be(Color.FromRgb(r, g, b));
        }

        [Fact]
        public void Apply_RecoloursTheBrushInPlace()
        {
            var brush = new SolidColorBrush(Colors.Black);
            var resources = new ResourceDictionary { [ModeAccent.BrushKey] = brush };

            ModeAccent.Apply(resources, "Quiet", highContrast: false);

            resources[ModeAccent.BrushKey].Should().BeSameAs(brush, "consumers hold this exact instance");
            brush.Color.Should().Be(ModeAccent.Quiet);
        }

        [Fact]
        public void Apply_InHighContrast_UsesTheSystemHighlightNotAModeColour()
        {
            var brush = new SolidColorBrush(Colors.Black);
            var resources = new ResourceDictionary { [ModeAccent.BrushKey] = brush };

            ModeAccent.Apply(resources, "Performance", highContrast: true);

            brush.Color.Should().Be(SystemColors.HighlightColor);
        }

        [Fact]
        public void Apply_LeavesAFrozenOrMissingBrushAlone()
        {
            var frozen = new SolidColorBrush(Colors.Black);
            frozen.Freeze();

            var apply = () =>
            {
                ModeAccent.Apply(new ResourceDictionary { [ModeAccent.BrushKey] = frozen }, "Quiet", false);
                ModeAccent.Apply(new ResourceDictionary(), "Quiet", false);
            };

            apply.Should().NotThrow();
            frozen.Color.Should().Be(Colors.Black);
        }

        [Fact]
        public void ModeAccentBrush_IsDeclaredUnfrozen()
        {
            var styles = FindRepoFile("src/OmenCoreApp/Styles/ModernStyles.xaml");
            styles.Should().NotBeNull();
            var lines = File.ReadAllLines(styles!);

            var declaration = lines.FirstOrDefault(l => l.Contains("x:Key=\"ModeAccentBrush\"", StringComparison.Ordinal));
            declaration.Should().NotBeNull();
            declaration!.Should().Contain("Freeze=\"False\"", "ModeAccent recolours it at runtime");
        }

        [Theory]
        [InlineData("Default", "Balanced")]
        [InlineData("balanced", "Balanced")]
        [InlineData("Normal", "Balanced")]
        [InlineData("Turbo", "Performance")]
        [InlineData("Silent", "Quiet")]
        [InlineData("My Gaming Mode", "My Gaming Mode")]   // a user's own saved mode keeps its name
        [InlineData("", "--")]
        [InlineData(null, "--")]
        public void ToDisplayName_UsesTheCardNames_AndKeepsUserModeNames(string? raw, string expected)
        {
            PerformanceModeNameResolver.ToDisplayName(raw).Should().Be(expected);
        }

        private static string? FindRepoFile(string relative)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            return null;
        }
    }
}
