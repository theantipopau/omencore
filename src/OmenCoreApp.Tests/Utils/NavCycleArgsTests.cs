using FluentAssertions;
using OmenCore;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// --nav-cycle turns a normal launch into a measurement run that ends by closing the window, so
    /// misreading the argument would either leave an unattended instance running or profile a
    /// different number of passes than the CLI asked for.
    /// </summary>
    public class NavCycleArgsTests
    {
        [Fact]
        public void AbsentFlag_ReturnsNull()
        {
            App.ParseNavCyclePasses(new[] { "--minimized" }).Should().BeNull();
            App.ParseNavCyclePasses(System.Array.Empty<string>()).Should().BeNull();
        }

        [Fact]
        public void BareFlag_DefaultsToThreePasses()
        {
            App.ParseNavCyclePasses(new[] { "--nav-cycle" }).Should().Be(3);
        }

        [Fact]
        public void EqualsForm_KeepsTheRequestedCount()
        {
            App.ParseNavCyclePasses(new[] { "--nav-cycle=5" }).Should().Be(5);
        }

        [Fact]
        public void NonsenseOrOutOfRangeCounts_AreClampedRatherThanRejected()
        {
            App.ParseNavCyclePasses(new[] { "--nav-cycle=0" }).Should().Be(1);
            App.ParseNavCyclePasses(new[] { "--nav-cycle=999" }).Should().Be(20);
            App.ParseNavCyclePasses(new[] { "--nav-cycle=many" }).Should().Be(3);
        }

        [Fact]
        public void OtherArgumentsAroundIt_AreIgnored()
        {
            App.ParseNavCyclePasses(new[] { "--headless", "--nav-cycle=2" }).Should().Be(2);
        }
    }
}
