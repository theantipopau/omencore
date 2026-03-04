using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class SystemInfoServiceTests
    {
        [Theory]
        [InlineData("Intel(R) Core(TM) i9-14900H", false)]
        [InlineData("Intel(R) Core(TM) i7-14750H Strix Point", true)]
        [InlineData("14th Gen Intel(R) Core(TM) - some string", true)]
        [InlineData("AMD Ryzen 9 7940HS", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsStrixPointCpu_DetectsCorrectly(string name, bool expected)
        {
            var result = SystemInfoService.IsStrixPointCpu(name ?? string.Empty);
            result.Should().Be(expected);
        }
    }
}
