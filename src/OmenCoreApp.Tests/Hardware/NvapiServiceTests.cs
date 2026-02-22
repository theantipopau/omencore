using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class NvapiServiceTests
    {
        [Fact]
        public void Initialize_DoesNotThrow_AndReflectsSupportsOverclocking()
        {
            var logging = new LoggingService(); logging.Initialize();
            var svc = new NvapiService(logging);
            // initialization should not throw regardless of environment
            var result = svc.Initialize();
            svc.SupportsOverclocking.Should().Be(result, "SupportsOverclocking flag matches result");
            // result may be true on machines with NVIDIA hardware, but should never crash
        }
    }
}
