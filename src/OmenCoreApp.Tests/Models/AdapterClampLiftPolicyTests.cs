using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Models
{
    /// <summary>
    /// These decide whether OmenCore restarts a display device with nobody watching, so they are
    /// worth more tests than the code is long. The captures are real replies from board 8D87.
    /// </summary>
    public class AdapterClampLiftPolicyTests
    {
        private static HpWmiBios.AdapterInfo Adapter(byte[] reply) =>
            HpWmiBios.DecodeAdapterData(reply)
                ?? throw new System.Exception("the capture is a valid 4-byte reply");

        // 280 W barrel, BelowRequirement.
        private static HpWmiBios.AdapterInfo Barrel280W() =>
            Adapter(new byte[] { 0x02, 0xC2, 0x00, 0x38 });

        // 330 W barrel, MeetsRequirement - nothing clamped, nothing to lift.
        private static HpWmiBios.AdapterInfo Barrel330W() =>
            Adapter(new byte[] { 0x01, 0xC2, 0x00, 0x42 });

        private static AdapterClampLiftSettings BothOn() =>
            new() { LiftCpuLimits = true, LiftGpuWhenParked = true };

        [Fact]
        public void Both_Halves_Are_Off_Until_Someone_Turns_Them_On()
        {
            var fresh = new AdapterClampLiftSettings();

            fresh.LiftCpuLimits.Should().BeFalse();
            fresh.LiftGpuWhenParked.Should().BeFalse();

            AdapterClampLiftPolicy.DecideCpu(fresh, Barrel280W(), cpuHalfIsOffered: true)
                .Should().Be(ClampLiftDecision.Disabled);

            AdapterClampLiftPolicy.DecideGpu(fresh, Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid)
                .Should().Be(ClampLiftDecision.Disabled,
                    because: "writing SMU limits and restarting a GPU are not things an app should " +
                             "start doing because it was installed");
        }

        [Fact]
        public void A_Parked_Gpu_On_An_Igpu_Driven_Panel_Is_The_Case_This_Can_Act_On()
        {
            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid)
                .Should().Be(ClampLiftDecision.Apply);

            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Optimus)
                .Should().Be(ClampLiftDecision.Apply,
                    because: "Optimus keeps the panel on the iGPU just as Hybrid does");
        }

        [Fact]
        public void An_Awake_Gpu_Is_Left_Alone()
        {
            // The restart destroys every graphics and compute context on the device. Whatever is
            // using it did not ask for that, and the clamp costs watts where this would cost work.
            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: false, displayMode: HpWmiBios.GpuMode.Hybrid)
                .Should().Be(ClampLiftDecision.DeferredGpuAwake);
        }

        [Fact]
        public void A_Panel_Driven_By_The_Dgpu_Is_Never_Restarted_Automatically()
        {
            // In Discrete mode the internal panel is on the dGPU, so pulling the device takes the
            // display with it. A parked dGPU should not be driving a panel at all, which is exactly
            // why this is checked: the automatic path runs with nobody watching.
            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Discrete)
                .Should().Be(ClampLiftDecision.DeferredDiscreteDisplay);

            // A mode that could not be read is treated as the dangerous one.
            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: null)
                .Should().Be(ClampLiftDecision.DeferredDiscreteDisplay,
                    because: "an unreadable mode is not evidence of a safe one");
        }

        [Fact]
        public void A_Supply_That_Meets_The_Requirement_Has_Nothing_To_Lift()
        {
            AdapterClampLiftPolicy.SupplyQualifies(BothOn(), Barrel330W()).Should().BeFalse();

            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel330W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid)
                .Should().Be(ClampLiftDecision.NotApplicable,
                    because: "restarting a GPU that is not clamped costs a restart and gains nothing");
        }

        [Fact]
        public void The_Watts_Ceiling_Narrows_The_Rule_Rather_Than_Widening_It()
        {
            var below200 = new AdapterClampLiftSettings
            {
                LiftCpuLimits = true,
                LiftGpuWhenParked = true,
                OnlyBelowAdapterWatts = 200
            };

            // 280 W is clamped, but the user asked for this only on supplies under 200 W.
            AdapterClampLiftPolicy.SupplyQualifies(below200, Barrel280W()).Should().BeFalse();

            // Zero means "whenever the firmware calls it under-rated", which is the default.
            AdapterClampLiftPolicy.SupplyQualifies(BothOn(), Barrel280W()).Should().BeTrue();
        }

        [Fact]
        public void A_Ceiling_Cannot_Be_Satisfied_By_A_Wattage_Nobody_Reported()
        {
            var below200 = new AdapterClampLiftSettings
            {
                LiftCpuLimits = true,
                OnlyBelowAdapterWatts = 200
            };

            // byte[3] = 0: no rating reported. Someone who set a ceiling asked for a narrower rule,
            // so an unknown wattage fails it rather than passing it.
            AdapterClampLiftPolicy
                .SupplyQualifies(below200, Adapter(new byte[] { 0x02, 0xC2, 0x00, 0x00 }))
                .Should().BeFalse();
        }

        [Fact]
        public void The_Cpu_Half_Turns_On_The_Setting_And_The_Supply_Only()
        {
            // No state of the machine makes an SMU power-limit message unsafe to send: it interrupts
            // nothing and takes nothing away. So there is no parked-or-awake condition here, and the
            // absence is deliberate rather than an oversight.
            AdapterClampLiftPolicy.DecideCpu(BothOn(), Barrel280W(), cpuHalfIsOffered: true)
                .Should().Be(ClampLiftDecision.Apply);

            AdapterClampLiftPolicy.DecideCpu(BothOn(), Barrel280W(), cpuHalfIsOffered: false)
                .Should().Be(ClampLiftDecision.NotApplicable,
                    because: "the offer already carries the CPU-family and supply-headroom gates");
        }
    }
}
