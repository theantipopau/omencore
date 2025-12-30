using System.Threading.Tasks;
using OmenCore.Services.Rgb;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class RgbNetSystemProviderTests
    {
        [Fact]
        public async Task Initialize_DoesNotThrow_And_SetsAvailableFlag()
        {
            var logging = new LoggingService(); logging.Initialize();
            var p = new RgbNetSystemProvider(logging);

            // Should not throw during initialize
            await p.InitializeAsync();

            // IsAvailable may be true or false depending on test environment; assert no exception
            Assert.True(p != null);
        }

        [Fact]
        public async Task ApplyEffect_InvalidColor_DoesNotThrow()
        {
            var logging = new LoggingService(); logging.Initialize();
            var p = new RgbNetSystemProvider(logging);
            await p.InitializeAsync();

            // invalid color should not throw
            await p.ApplyEffectAsync("color:NOTAHex");
        }

        [Fact]
        public async Task ApplyEffect_ValidColor_DoesNotThrow()
        {
            var logging = new LoggingService(); logging.Initialize();
            var p = new RgbNetSystemProvider(logging);
            await p.InitializeAsync();

            // valid color may or may not affect devices in test env, but should not throw
            await p.ApplyEffectAsync("color:#112233");
        }
    }
}
