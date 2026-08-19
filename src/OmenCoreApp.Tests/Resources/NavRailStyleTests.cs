using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using FluentAssertions;
using Xunit;

namespace OmenCoreApp.Tests.Resources
{
    /// <summary>
    /// The main window's navigation rail is a pure retemplate of the existing TabControl, and a
    /// clean build does not prove a ControlTemplate is well-formed - MSBuild compiles XAML to BAML
    /// without resolving StaticResource references or validating that a template's TargetType
    /// matches what it templates. This environment also cannot launch the real app to look at it.
    /// So these tests do the strongest check available here: actually parse the shared resource
    /// dictionary with XamlReader and assert the two new styles resolve, target the right types,
    /// and carry the pieces the rail depends on. A malformed template throws during that parse.
    ///
    /// Deliberately narrow: this verifies the styles are structurally valid and wired to the right
    /// control types. It cannot verify that the rail *looks* right - spacing, contrast, and whether
    /// 164px is a sensible width are still open until someone runs the app.
    ///
    /// Everything touching the loaded dictionary runs inside the STA worker, because WPF objects
    /// are thread-affine: reading Style.TargetType from the test thread throws even though the
    /// object exists. Only plain values cross back.
    /// </summary>
    [Collection("NonParallel")]
    public class NavRailStyleTests
    {
        private static string GetRepoRoot()
        {
            var di = new DirectoryInfo(AppContext.BaseDirectory);
            while (di != null)
            {
                if (Directory.Exists(Path.Combine(di.FullName, "src")))
                {
                    return di.FullName;
                }

                di = di.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root");
        }

        private static string StylesPath =>
            Path.Combine(GetRepoRoot(), "src", "OmenCoreApp", "Styles", "ModernStyles.xaml");

        /// <summary>
        /// Loads the dictionary on a short-lived STA thread (WPF requires it) and runs
        /// <paramref name="inspect"/> there too, returning only a plain value. Avoids taking a
        /// dependency on Xunit.StaFact for this one file.
        /// </summary>
        private static T Inspect<T>(Func<ResourceDictionary, T> inspect)
        {
            var path = StylesPath;
            File.Exists(path).Should().BeTrue($"expected shared resource dictionary at {path}");

            T result = default!;
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var dictionary = (ResourceDictionary)XamlReader.Load(stream);
                    result = inspect(dictionary);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new InvalidOperationException(
                    $"Loading or inspecting ModernStyles.xaml failed: {failure.Message}", failure);
            }

            return result;
        }

        private static ControlTemplate? GetTemplate(Style style) =>
            style.Setters
                .OfType<Setter>()
                .Where(s => s.Property == Control.TemplateProperty)
                .Select(s => s.Value)
                .OfType<ControlTemplate>()
                .FirstOrDefault();

        [Fact]
        public void ModernStyles_ParsesAsAValidResourceDictionary()
        {
            // The load itself is the assertion: a malformed style anywhere in this file throws
            // here rather than failing at runtime in the real app.
            var count = Inspect(d => d.Count);

            count.Should().BeGreaterThan(0);
        }

        [Fact]
        public void NavRailTabControlStyle_ExistsAndTargetsTabControl()
        {
            var (exists, targetsTabControl) = Inspect(d =>
            {
                if (!d.Contains("Omen.NavRailTabControl"))
                {
                    return (false, false);
                }

                var style = d["Omen.NavRailTabControl"] as Style;
                return (true, style?.TargetType == typeof(TabControl));
            });

            exists.Should().BeTrue(
                "MainWindow's TabControl references this style by key; a rename or typo would fail only at runtime");
            targetsTabControl.Should().BeTrue();
        }

        [Fact]
        public void NavRailTabItemStyle_ExistsAndTargetsTabItem()
        {
            var (exists, targetsTabItem, hasTemplate) = Inspect(d =>
            {
                if (!d.Contains("Omen.NavRailTabItem"))
                {
                    return (false, false, false);
                }

                var style = d["Omen.NavRailTabItem"] as Style;
                var template = style == null ? null : GetTemplate(style);
                return (true, style?.TargetType == typeof(TabItem), template?.TargetType == typeof(TabItem));
            });

            exists.Should().BeTrue();
            targetsTabItem.Should().BeTrue();
            hasTemplate.Should().BeTrue("the rail item style must supply a ControlTemplate targeting TabItem");
        }

        [Fact]
        public void NavRailTabItemStyle_HasAGroupStartTriggerForTheSeparator()
        {
            // Group separators between related sections are driven entirely by a Tag trigger on
            // this template, which is what lets MainWindow mark group boundaries without
            // reordering tabs or touching any index-dependent code. Drop the trigger and the tabs
            // still work but silently lose all visual grouping.
            var hasGroupStartTrigger = Inspect(d =>
            {
                var style = (Style)d["Omen.NavRailTabItem"];
                var template = GetTemplate(style);

                return template != null && template.Triggers
                    .OfType<Trigger>()
                    .Any(t => t.Property == FrameworkElement.TagProperty && Equals(t.Value, "GroupStart"));
            });

            hasGroupStartTrigger.Should().BeTrue(
                "group separators are driven by Tag=\"GroupStart\" on the first TabItem of each group");
        }

        [Fact]
        public void OriginalHorizontalTabStyles_AreStillPresentForSettingsInnerTabs()
        {
            // SettingsView hosts its own inner TabControl on the original horizontal styles and
            // should keep them. Guards against a future "cleanup" deleting them on the assumption
            // the rail replaced them everywhere.
            var (hasControl, hasItem) = Inspect(d =>
                (d.Contains("ModernTabControl"), d.Contains("ModernTabItem")));

            hasControl.Should().BeTrue();
            hasItem.Should().BeTrue();
        }
    }
}
