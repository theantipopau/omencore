using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.Services.SystemOptimizer;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class SystemOptimizerServiceAdminTests
    {
        [Fact]
        public async Task ApplyOptimizationAsync_NonAdmin_ReturnsAdminError()
        {
            var logger = new LoggingService();
            var service = new SystemOptimizerService(logger, () => false);

            var result = await service.ApplyOptimizationAsync("power_game_mode");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Administrator privileges");
        }

        [Fact]
        public async Task ApplyOptimizationAsync_Admin_UnknownOptimization_ReturnsUnknownNotAdminError()
        {
            var logger = new LoggingService();
            var service = new SystemOptimizerService(logger, () => true);

            var result = await service.ApplyOptimizationAsync("unknown_toggle");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Unknown optimization");
            result.ErrorMessage.Should().NotContain("Administrator privileges");
        }

        [Fact]
        public async Task ApplyBalancedAsync_NonAdmin_ReturnsSinglePreflightFailure()
        {
            var logger = new LoggingService();
            var service = new SystemOptimizerService(logger, () => false);

            var results = await service.ApplyBalancedAsync();

            results.Should().HaveCount(1);
            results[0].Success.Should().BeFalse();
            results[0].ErrorMessage.Should().Contain("Administrator privileges");
        }
    }
}
