using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Coverage for BiosUpdateService's version-comparison and URL-construction logic.
    ///
    /// Note: contrary to how this class was described in prior roadmap notes, it does not
    /// write firmware at all — it only checks HP's web API/support pages for a newer BIOS
    /// softpaq and hands off to HP's own tools (browser links) for the user to install any
    /// update. The real risk surface here is "reports the wrong version/update state",
    /// not an unsafe write path, so these tests focus on the version-comparison logic
    /// (the part most likely to silently misreport) rather than firmware I/O, which
    /// doesn't exist in this class.
    /// </summary>
    public class BiosUpdateServiceTests
    {
        private static BiosUpdateService CreateService() => new(new LoggingService());

        [Theory]
        [InlineData("F.19", "F.20", true)]   // newer minor -> update available
        [InlineData("F.20", "F.19", false)]  // current is newer -> no update
        [InlineData("F.20", "F.20", false)]  // identical -> no update
        [InlineData("F.9", "F.10", true)]    // numeric compare, not lexicographic ("9" < "10")
        [InlineData("1.15.0", "1.15.1", true)]
        [InlineData("1.15.1", "1.15.0", false)]
        public void CompareBiosVersions_NumericComparison_ReturnsExpected(string current, string latest, bool expectedUpdateAvailable)
        {
            InvokeCompareBiosVersions(current, latest).Should().Be(expectedUpdateAvailable);
        }

        [Fact]
        public void CompareBiosVersions_MoreVersionComponentsOnLatest_TreatsAsNewer()
        {
            // "1.15" vs "1.15.1" — latest has an extra trailing component with equal
            // prefix, should be treated as newer (more specific / later point release).
            InvokeCompareBiosVersions("1.15", "1.15.1").Should().BeTrue();
        }

        [Fact]
        public void CompareBiosVersions_NullOrEmptyInputs_ReturnsFalse()
        {
            InvokeCompareBiosVersions(null, "F.20").Should().BeFalse();
            InvokeCompareBiosVersions("F.19", null).Should().BeFalse();
            InvokeCompareBiosVersions("", "").Should().BeFalse();
        }

        [Fact]
        public void CompareBiosVersions_NonNumericVersions_FallsBackToStringComparison()
        {
            // Neither string contains digits, so the numeric-extraction path yields no
            // components and the method must fall back to ordinal string comparison
            // instead of throwing or silently returning false for every input.
            InvokeCompareBiosVersions("Alpha", "Beta").Should().BeTrue();
            InvokeCompareBiosVersions("Beta", "Alpha").Should().BeFalse();
        }

        [Theory]
        [InlineData("F.20", new[] { 20 })]
        [InlineData("1.15.3", new[] { 1, 15, 3 })]
        [InlineData("no digits here", new int[0])]
        public void ExtractVersionNumbers_ParsesDigitGroups(string version, int[] expected)
        {
            var result = InvokeExtractVersionNumbers(version);
            result.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
        }

        [Theory]
        [InlineData("6Y7K8PA#ABG", "6Y7K8PA")]
        [InlineData("8A43", "8A43")]
        [InlineData(null, null)]
        [InlineData("", null)]
        public void ExtractProductId_HandlesTypicalSkuFormats(string? sku, string? expected)
        {
            InvokeExtractProductId(sku).Should().Be(expected);
        }

        [Fact]
        public void ConstructSupportUrl_PrefersSerialNumberWhenPresent()
        {
            var info = new SystemInfo { SerialNumber = "5CD9442HQ9", Model = "OMEN by HP Laptop 15-dc1xxx" };

            var url = InvokeConstructSupportUrl(info);

            url.Should().Contain("serialnumber=5CD9442HQ9");
        }

        [Fact]
        public void ConstructSupportUrl_FallsBackToModelWhenNoSerial()
        {
            var info = new SystemInfo { SerialNumber = "", Model = "OMEN by HP Laptop 15-dc1xxx" };

            var url = InvokeConstructSupportUrl(info);

            url.Should().NotContain("serialnumber=");
            url.Should().Contain(Uri.EscapeDataString(info.Model));
        }

        [Fact]
        public async Task CheckForUpdatesAsync_MissingSkuAndProductName_ReturnsEarlyWithoutNetworkCall()
        {
            using var service = CreateService();
            var info = new SystemInfo
            {
                BiosVersion = "F.19",
                BiosDate = "2024-01-01",
                SystemSku = "",
                ProductName = ""
            };

            var result = await service.CheckForUpdatesAsync(info);

            result.UpdateAvailable.Should().BeFalse();
            result.Message.Should().Contain("Unable to determine HP product ID");
            result.CurrentBiosVersion.Should().Be("F.19");
        }

        // ---- reflection helpers -------------------------------------------------

        private static bool InvokeCompareBiosVersions(string? current, string? latest)
        {
            var method = typeof(BiosUpdateService).GetMethod(
                "CompareBiosVersions", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            using var service = CreateService();
            var value = method!.Invoke(service, new object?[] { current, latest });
            return (bool)value!;
        }

        private static List<int> InvokeExtractVersionNumbers(string version)
        {
            var method = typeof(BiosUpdateService).GetMethod(
                "ExtractVersionNumbers", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            using var service = CreateService();
            return (List<int>)method!.Invoke(service, new object[] { version })!;
        }

        private static string? InvokeExtractProductId(string? sku)
        {
            var method = typeof(BiosUpdateService).GetMethod(
                "ExtractProductId", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            using var service = CreateService();
            return (string?)method!.Invoke(service, new object?[] { sku });
        }

        private static string InvokeConstructSupportUrl(SystemInfo info)
        {
            var method = typeof(BiosUpdateService).GetMethod(
                "ConstructSupportUrl", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            using var service = CreateService();
            return (string)method!.Invoke(service, new object[] { info })!;
        }
    }
}
