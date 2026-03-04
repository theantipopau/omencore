using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace OmenCoreApp.Tests.Resources
{
    public class ResourceDictionaryTests
    {
        private static string GetRepoRoot()
        {
            var dir = AppContext.BaseDirectory ?? throw new Exception("AppContext.BaseDirectory is null");
#pragma warning disable CS8600 // Repo root discovery uses runtime checks for nulls in test environment
            DirectoryInfo di = new(dir!);
            while (di != null)
            {
                if (Directory.Exists(Path.Combine(di.FullName, "src")))
                {
                    return di.FullName;
                }
                di = di.Parent;
            }
#pragma warning restore CS8600
            throw new Exception("Could not locate repository root (missing 'src' folder)");
        }

        [Fact]
        public void ResourceDictionariesContainRequiredKeys()
        {
            var rootPath = GetRepoRoot();
            var path = Path.Combine(rootPath, "src", "OmenCoreApp", "Styles", "ModernStyles.xaml");
            Assert.True(File.Exists(path), $"Expected resource file not found: {path}");

            var content = File.ReadAllText(path);

            Assert.Contains("x:Key=\"AccentGreenBrush\"", content);
            Assert.Contains("x:Key=\"AccentBrush\"", content);
            Assert.Contains("x:Key=\"TextPrimaryBrush\"", content);
            Assert.Contains("x:Key=\"OutlineButtonSmall\"", content);
        }

        [Fact]
        public void ViewsShouldNotIncludeModernStylesMerge()
        {
            // Prevent accidentally merging the shared resource dictionary in individual views, which
            // previously caused duplicate-key runtime crashes when App.xaml already included it.
            var rootPath = GetRepoRoot();
            var views = new[]
            {
                Path.Combine(rootPath, "src", "OmenCoreApp", "Views", "SettingsView.xaml"),
                Path.Combine(rootPath, "src", "OmenCoreApp", "Views", "DiagnosticsView.xaml"),
            };

            foreach (var view in views)
            {
                Assert.True(File.Exists(view), $"View XAML not found: {view}");
                var text = File.ReadAllText(view);
                Assert.DoesNotContain("ModernStyles.xaml", text);
            }
        }

        [Fact]
        public void ModernStylesContainsNoDuplicateKeys()
        {
            // parse the XAML and ensure each x:Key is unique within the file itself.
            var rootPath = GetRepoRoot();
            var fullPath = Path.Combine(rootPath, "src", "OmenCoreApp", "Styles", "ModernStyles.xaml");
            var doc = System.Xml.Linq.XDocument.Load(fullPath);
            var ns = "http://schemas.microsoft.com/winfx/2006/xaml";
            var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var elem in doc.Descendants())
            {
                var keyAttr = elem.Attribute(System.Xml.Linq.XName.Get("Key", ns));
                if (keyAttr != null)
                {
                    var key = keyAttr.Value;
                    Assert.True(keys.Add(key), $"Duplicate resource key found in ModernStyles.xaml: {key}");
                }
            }
        }
    }
}
