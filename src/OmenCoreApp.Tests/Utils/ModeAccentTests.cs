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
        public void Apply_PutsAModeBrushUnderTheKey()
        {
            var resources = new ResourceDictionary { [ModeAccent.BrushKey] = new SolidColorBrush(Colors.Black) };

            ModeAccent.Apply(resources, "Quiet", highContrast: false);

            resources[ModeAccent.BrushKey].Should().BeOfType<SolidColorBrush>()
                .Which.Color.Should().Be(ModeAccent.Quiet);
        }

        [Fact]
        public void Apply_InHighContrast_UsesTheSystemHighlightNotAModeColour()
        {
            var resources = new ResourceDictionary();

            ModeAccent.Apply(resources, "Performance", highContrast: true);

            ((SolidColorBrush)resources[ModeAccent.BrushKey]).Color.Should().Be(SystemColors.HighlightColor);
        }

        [Fact]
        public void ModeAccentBrush_IsOnlyReferencedDynamically()
        {
            // A StaticResource consumer would keep the startup brush forever, because Apply replaces the brush rather
            // than recolouring it (a brush used as a style Setter value is frozen when the style seals).
            var root = FindRepoFile("src/OmenCoreApp/Styles/ModernStyles.xaml");
            root.Should().NotBeNull();
            var app = Directory.GetParent(root!)!.Parent!.FullName;

            var staticUses = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .Where(f => File.ReadAllText(f).Contains("StaticResource ModeAccentBrush", StringComparison.Ordinal));

            staticUses.Should().BeEmpty();
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
