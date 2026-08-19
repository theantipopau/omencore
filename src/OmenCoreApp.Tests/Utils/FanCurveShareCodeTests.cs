using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    public class FanCurveShareCodeTests
    {
        private static List<FanCurvePoint> SampleCurve() => new()
        {
            new FanCurvePoint { TemperatureC = 40, FanPercent = 20 },
            new FanCurvePoint { TemperatureC = 60, FanPercent = 50 },
            new FanCurvePoint { TemperatureC = 80, FanPercent = 100 },
        };

        [Fact]
        public void Generate_ThenParse_RoundTripsExactly()
        {
            var code = FanCurveShareCode.Generate(SampleCurve(), "My Silent Curve");

            code.Should().NotBeNullOrEmpty();
            code.Should().StartWith("OCFC1:");

            FanCurveShareCode.TryParse(code, out var points, out var name).Should().BeTrue();
            name.Should().Be("My Silent Curve");
            points.Should().HaveCount(3);
            points[0].TemperatureC.Should().Be(40);
            points[0].FanPercent.Should().Be(20);
            points[2].TemperatureC.Should().Be(80);
            points[2].FanPercent.Should().Be(100);
        }

        [Fact]
        public void Generate_SortsPointsByTemperature_RegardlessOfInputOrder()
        {
            var unsorted = new List<FanCurvePoint>
            {
                new() { TemperatureC = 80, FanPercent = 100 },
                new() { TemperatureC = 40, FanPercent = 20 },
                new() { TemperatureC = 60, FanPercent = 50 },
            };

            var code = FanCurveShareCode.Generate(unsorted, "Reordered");
            FanCurveShareCode.TryParse(code, out var points, out _);

            points.Should().BeInAscendingOrder(p => p.TemperatureC);
        }

        [Fact]
        public void Generate_FewerThanTwoPoints_ReturnsNull()
        {
            FanCurveShareCode.Generate(new List<FanCurvePoint> { new() { TemperatureC = 40, FanPercent = 20 } }, "Too Short")
                .Should().BeNull();

            FanCurveShareCode.Generate(new List<FanCurvePoint>(), "Empty").Should().BeNull();
        }

        [Fact]
        public void Generate_EmptyOrWhitespaceName_FallsBackToDefault()
        {
            var code = FanCurveShareCode.Generate(SampleCurve(), "   ");
            FanCurveShareCode.TryParse(code, out _, out var name);

            name.Should().Be("Custom Curve");
        }

        [Fact]
        public void Generate_NameContainingPipe_IsSanitizedSoItCannotBreakTheFormat()
        {
            var code = FanCurveShareCode.Generate(SampleCurve(), "Weird|Name|Here");
            FanCurveShareCode.TryParse(code, out var points, out var name).Should().BeTrue();

            name.Should().NotContain("|");
            points.Should().HaveCount(3, "a pipe in the name must not be mistaken for the name/points separator");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not base64!!!")]
        [InlineData("OCFC1:not base64either")]
        public void TryParse_MalformedInput_ReturnsFalseAndDoesNotThrow(string? input)
        {
            var act = () => FanCurveShareCode.TryParse(input, out _, out _);

            act.Should().NotThrow();
            FanCurveShareCode.TryParse(input, out var points, out var name).Should().BeFalse();
            points.Should().BeEmpty();
            name.Should().BeEmpty();
        }

        [Fact]
        public void TryParse_WithoutPrefix_StillParsesTheBase64Payload()
        {
            var code = FanCurveShareCode.Generate(SampleCurve(), "No Prefix Test")!;
            var withoutPrefix = code.Substring("OCFC1:".Length);

            FanCurveShareCode.TryParse(withoutPrefix, out var points, out var name).Should().BeTrue();
            name.Should().Be("No Prefix Test");
            points.Should().HaveCount(3);
        }

        [Fact]
        public void TryParse_DuplicateTemperaturePoints_IsRejected()
        {
            var duplicateTempCurve = new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 20 },
                new() { TemperatureC = 40, FanPercent = 30 },
            };
            // Bypass Generate's own sorting-only behavior by hand-crafting a code with a
            // duplicate, since Generate doesn't itself reject duplicates - TryParse must.
            var code = FanCurveShareCode.Generate(duplicateTempCurve, "Dup");

            FanCurveShareCode.TryParse(code, out var points, out _).Should().BeFalse();
            points.Should().BeEmpty();
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        public void TryParse_FanPercentOutOfRange_IsRejected(int badPercent)
        {
            var curve = new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 20 },
                new() { TemperatureC = 60, FanPercent = badPercent },
            };
            var code = FanCurveShareCode.Generate(curve, "OutOfRange");

            FanCurveShareCode.TryParse(code, out var points, out _).Should().BeFalse();
            points.Should().BeEmpty();
        }

        [Fact]
        public void TryParse_MissingPointsSeparator_IsRejected()
        {
            // A payload with a name but no '|' separator at all.
            var bytes = System.Text.Encoding.UTF8.GetBytes("JustAName");
            var code = "OCFC1:" + System.Convert.ToBase64String(bytes);

            FanCurveShareCode.TryParse(code, out var points, out var name).Should().BeFalse();
            points.Should().BeEmpty();
            name.Should().BeEmpty();
        }
    }
}
