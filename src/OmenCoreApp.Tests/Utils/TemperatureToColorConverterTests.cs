using System.Globalization;
using System.Windows.Media;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    public class TemperatureToColorConverterTests
    {
        [Theory]
        [InlineData(0.0, "gpu")]
        [InlineData(-1.0, "cpu")]
        public void NoReading_IsGrey_NotTheCoolGreen(double temp, string mode)
        {
            new TemperatureToColorConverter().Convert(temp, typeof(Brush), mode, CultureInfo.InvariantCulture)
                .Should().BeSameAs(TemperatureToColorConverter.NoReadingBrush);
        }

        [Fact]
        public void ARealReading_StillGetsAThresholdColour()
        {
            new TemperatureToColorConverter().Convert(45.0, typeof(Brush), "cpu", CultureInfo.InvariantCulture)
                .Should().NotBeSameAs(TemperatureToColorConverter.NoReadingBrush);
        }
    }
}
