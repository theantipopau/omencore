using System;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    [Collection("NonParallel")]
    public class MotionPreferenceTests : IDisposable
    {
        private readonly Func<bool> _originalOverride = MotionPreference.OsAnimationsEnabledOverride;

        public void Dispose()
        {
            MotionPreference.OsAnimationsEnabledOverride = _originalOverride;
        }

        [Fact]
        public void ShouldReduceMotion_UserOverrideTrue_ReducesRegardlessOfOsPreference()
        {
            MotionPreference.OsAnimationsEnabledOverride = () => true; // OS animations ON

            MotionPreference.ShouldReduceMotion(userReduceMotionOverride: true).Should().BeTrue(
                "an explicit in-app override should not need the OS setting to agree");
        }

        [Fact]
        public void ShouldReduceMotion_OsAnimationsDisabled_ReducesEvenWithoutUserOverride()
        {
            MotionPreference.OsAnimationsEnabledOverride = () => false; // OS animations OFF

            MotionPreference.ShouldReduceMotion(userReduceMotionOverride: false).Should().BeTrue(
                "OmenCore's own motion should follow the Windows-wide 'Show animations' preference");
        }

        [Fact]
        public void ShouldReduceMotion_OsAnimationsEnabledAndNoOverride_DoesNotReduce()
        {
            MotionPreference.OsAnimationsEnabledOverride = () => true;

            MotionPreference.ShouldReduceMotion(userReduceMotionOverride: false).Should().BeFalse();
        }

        [Fact]
        public void ShouldReduceMotion_OsReadThrows_DefaultsToNotReducing()
        {
            // A read failure must not silently disable animation for everyone - default open.
            MotionPreference.OsAnimationsEnabledOverride = () => throw new InvalidOperationException("no display");

            MotionPreference.ShouldReduceMotion(userReduceMotionOverride: false).Should().BeFalse();
        }
    }
}
