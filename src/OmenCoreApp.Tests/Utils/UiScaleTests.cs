using System.Linq;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// The scale is stored as a plain int in a user-editable file, so the two things that must hold
    /// are that any value maps to a step the UI can show, and that 100 % is exactly the identity.
    /// </summary>
    public class UiScaleTests
    {
        [Fact]
        public void Steps_AreAscending_AndContainTheDefault()
        {
            UiScale.Steps.Should().BeInAscendingOrder();
            UiScale.Steps.Should().Contain(UiScale.Default);
            UiScale.Steps.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void Normalize_KeepsSupportedValues()
        {
            foreach (int step in UiScale.Steps)
            {
                UiScale.Normalize(step).Should().Be(step);
            }
        }

        [Fact]
        public void Normalize_SnapsToTheNearestStep()
        {
            UiScale.Normalize(133).Should().Be(140);
            UiScale.Normalize(101).Should().Be(100);
            UiScale.Normalize(149).Should().Be(150);
        }

        [Fact]
        public void Normalize_RejectsNonsenseWithoutThrowing()
        {
            UiScale.Normalize(0).Should().Be(UiScale.Default);
            UiScale.Normalize(-70).Should().Be(UiScale.Default);
        }

        [Fact]
        public void Factor_IsTheIdentityAtOneHundred()
        {
            UiScale.Factor(100).Should().Be(1.0);
            UiScale.Factor(125).Should().Be(1.25);
            UiScale.Factor(80).Should().Be(0.8);
        }

        [Fact]
        public void NextAndPrevious_WalkTheStepsAndStopAtTheEnds()
        {
            UiScale.Next(100).Should().Be(110);
            UiScale.Previous(100).Should().Be(90);

            int top = UiScale.Steps.Last();
            UiScale.Next(top).Should().Be(top, "Ctrl+ at the ceiling must not invent a step");

            int bottom = UiScale.Steps.First();
            UiScale.Previous(bottom).Should().Be(bottom);
        }

        [Fact]
        public void NextAndPrevious_NormalizeTheirInputFirst()
        {
            UiScale.Next(101).Should().Be(110);
            UiScale.Previous(133).Should().Be(125);
        }
    }
}
