using System;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
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
    }
}