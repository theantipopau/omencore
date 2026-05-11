using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class PowerAutomationServiceTests
    {
        [Fact]
        public void BuiltInPerformanceCurve_ReservesMaxForHighThermals()
        {
            var method = typeof(PowerAutomationService).GetMethod("GetBuiltInCurve", BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var curve = ((List<FanCurvePoint>)method!.Invoke(null, new object[] { FanMode.Performance })!)
                .OrderBy(p => p.TemperatureC)
                .ToList();

            curve.Where(p => p.TemperatureC <= 80).Should().OnlyContain(p => p.FanPercent < 90,
                "power automation fallback Performance/Gaming should not silently behave like Max at moderate temperatures");
            curve.Single(p => p.FanPercent == 100).TemperatureC.Should().BeGreaterThanOrEqualTo(90);
        }
    }
}
