using System;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class RyzenControlTests
    {
        [Fact]
        public void IsRyzenAi9CurveOptimizerUnsupported_WhenFamily26Model64Plus_ReturnsTrue()
        {
            var result = InvokeIsRyzenAi9Unsupported(
                "AMD Ryzen AI 9 HX 375",
                "AMD64 Family 26 Model 68 Stepping 0");

            result.Should().BeTrue();
        }

        [Fact]
        public void IsRyzenAi9CurveOptimizerUnsupported_WhenModelBelowThreshold_ReturnsFalse()
        {
            var result = InvokeIsRyzenAi9Unsupported(
                "AMD Ryzen AI 9 HX 370",
                "AMD64 Family 26 Model 36 Stepping 0");

            result.Should().BeFalse();
        }

        [Fact]
        public void IsRyzenAi9CurveOptimizerUnsupported_WhenHexModelFormat_ReturnsTrue()
        {
            var result = InvokeIsRyzenAi9Unsupported(
                "AMD Ryzen AI 9 HX 375",
                "AMD64 Family 0x1A Model 0x44 Stepping 0");

            result.Should().BeTrue();
        }

        private static bool InvokeIsRyzenAi9Unsupported(string cpuName, string cpuModel)
        {
            var method = typeof(RyzenControl).GetMethod(
                "IsRyzenAi9CurveOptimizerUnsupported",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string) },
                null);

            method.Should().NotBeNull();

            var value = method!.Invoke(null, new object[] { cpuName, cpuModel });
            value.Should().BeOfType<bool>();
            return (bool)value!;
        }

        [Fact]
        public void DetectFamily_Family26Model96_DoesNotMisidentifyAsRenoirLucienne()
        {
            // GitHub #171 (board 8E10): "AMD Ryzen AI 7 350" reports as
            // "AMD64 Family 26 Model 96 Stepping 0" - a genuinely new Family 26 part that happens
            // to share a model number with the much older Renoir/Lucienne generation (Family 23
            // Model 96/104). The Renoir/Lucienne fallback check used to match on Model alone with
            // no Family qualifier, misidentifying this CPU. Model numbers are not unique across
            // families, so the fallback must not fire outside the family it was written for.
            var family = InvokeDetectFamily("AMD Ryzen AI 7 350 w/ Radeon 860M", "AMD64 Family 26 Model 96 Stepping 0");

            family.Should().NotBe(RyzenFamily.RenoirLucienne);
        }

        [Fact]
        public void DetectFamily_Family23Model96_StillResolvesToRenoirLucienne()
        {
            // Regression guard for the fix above: a genuine Renoir-generation part (Family 23
            // Model 96) must still resolve correctly once the Family qualifier is added.
            var family = InvokeDetectFamily("AMD Ryzen 7 4800H", "AMD64 Family 23 Model 96 Stepping 1");

            family.Should().Be(RyzenFamily.RenoirLucienne);
        }

        private static RyzenFamily InvokeDetectFamily(string cpuName, string cpuModel)
        {
            var nameProperty = typeof(RyzenControl).GetProperty(
                nameof(RyzenControl.CpuName), BindingFlags.Public | BindingFlags.Static);
            var modelProperty = typeof(RyzenControl).GetProperty(
                nameof(RyzenControl.CpuModel), BindingFlags.Public | BindingFlags.Static);
            var method = typeof(RyzenControl).GetMethod(
                "DetectFamily", BindingFlags.NonPublic | BindingFlags.Static);

            nameProperty.Should().NotBeNull();
            modelProperty.Should().NotBeNull();
            method.Should().NotBeNull();

            var previousName = RyzenControl.CpuName;
            var previousModel = RyzenControl.CpuModel;
            try
            {
                nameProperty!.SetValue(null, cpuName);
                modelProperty!.SetValue(null, cpuModel);

                var value = method!.Invoke(null, null);
                value.Should().BeOfType<RyzenFamily>();
                return (RyzenFamily)value!;
            }
            finally
            {
                nameProperty!.SetValue(null, previousName);
                modelProperty!.SetValue(null, previousModel);
            }
        }
    }
}