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

            AdapterClampLiftPolicy.DecideCpu(fresh, Barrel280W(), cpuHalfIsOffered: true, onAcPower: true)
                .Should().Be(ClampLiftDecision.Disabled);

            AdapterClampLiftPolicy.DecideGpu(fresh, Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid, onAcPower: true)
                .Should().Be(ClampLiftDecision.Disabled,
                    because: "writing SMU limits and restarting a GPU are not things an app should " +
                             "start doing because it was installed");
        }

        [Fact]
        public void A_Parked_Gpu_On_An_Igpu_Driven_Panel_Is_The_Case_This_Can_Act_On()
        {
            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid, onAcPower: true)
                .Should().Be(ClampLiftDecision.Apply);

            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Optimus, onAcPower: true)
                .Should().Be(ClampLiftDecision.Apply,
                    because: "Optimus keeps the panel on the iGPU just as Hybrid does");
        }

        [Fact]
        public void An_Awake_Gpu_Is_Left_Alone()
        {
            // The restart destroys every graphics and compute context on the device. Whatever is
            // using it did not ask for that, and the clamp costs watts where this would cost work.
            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: false, displayMode: HpWmiBios.GpuMode.Hybrid, onAcPower: true)
                .Should().Be(ClampLiftDecision.DeferredGpuAwake);
        }

        [Fact]
        public void A_Panel_Driven_By_The_Dgpu_Is_Never_Restarted_Automatically()
        {
            // In Discrete mode the internal panel is on the dGPU, so pulling the device takes the
            // display with it. A parked dGPU should not be driving a panel at all, which is exactly
            // why this is checked: the automatic path runs with nobody watching.
            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Discrete, onAcPower: true)
                .Should().Be(ClampLiftDecision.DeferredDiscreteDisplay);

            // A mode that could not be read is treated as the dangerous one.
            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel280W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: null, onAcPower: true)
                .Should().Be(ClampLiftDecision.DeferredDiscreteDisplay,
                    because: "an unreadable mode is not evidence of a safe one");
        }

        [Fact]
        public void A_Supply_That_Meets_The_Requirement_Has_Nothing_To_Lift()
        {
            AdapterClampLiftPolicy.SupplyQualifies(BothOn(), Barrel330W()).Should().BeFalse();

            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel330W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid, onAcPower: true)
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
            // Nothing about the GPU's state makes an SMU power-limit message unsafe to send: it
            // interrupts nothing and takes nothing away. So there is no parked-or-awake condition
            // here, and the absence is deliberate rather than an oversight. Being plugged in is a
            // separate matter - see the battery tests below.
            AdapterClampLiftPolicy.DecideCpu(BothOn(), Barrel280W(), cpuHalfIsOffered: true, onAcPower: true)
                .Should().Be(ClampLiftDecision.Apply);

            AdapterClampLiftPolicy.DecideCpu(BothOn(), Barrel280W(), cpuHalfIsOffered: false, onAcPower: true)
                .Should().Be(ClampLiftDecision.NotApplicable,
                    because: "the offer already carries the CPU-family and supply-headroom gates");
        }

        // ── On battery ───────────────────────────────────────────────────────────────────────────
        //
        // Both halves undo a clamp the firmware applies because the attached adapter is under-rated.
        // Unplugged there is no attached adapter, so there is nothing to undo and nothing either
        // half should be doing - least of all the GPU half, which restarts the display device.

        // The reply board 8D87 actually gives on battery: NotSupported, which has no verdict at all.
        private static HpWmiBios.AdapterInfo OnBattery8D87() =>
            Adapter(new byte[] { 0x00, 0xC2, 0x00, 0x00 });

        // The reply HP documents for the same situation, and the dangerous one: BatteryPower has a
        // verdict, and it is not MeetsRequirement, so the low-wattage rule reads it as under-rated.
        private static HpWmiBios.AdapterInfo OnBatteryReportedAsSuch() =>
            Adapter(new byte[] { 0x03, 0xC2, 0x00, 0x00 });

        [Fact]
        public void Neither_Half_Runs_On_Battery()
        {
            AdapterClampLiftPolicy.DecideGpu(BothOn(), OnBattery8D87(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid,
                                             onAcPower: false)
                .Should().Be(ClampLiftDecision.DeferredOnBattery);

            AdapterClampLiftPolicy.DecideCpu(BothOn(), OnBattery8D87(), cpuHalfIsOffered: true,
                                             onAcPower: false)
                .Should().Be(ClampLiftDecision.DeferredOnBattery,
                    because: "raised SMU power limits on battery are held against the battery");
        }

        [Fact]
        public void A_Firmware_That_Calls_Battery_Power_A_Verdict_Does_Not_Get_A_Gpu_Restart()
        {
            // This is the whole reason the power state is checked rather than left to the adapter
            // verdict. BatteryPower passes HasVerdict and is not MeetsRequirement, so the supply
            // rule alone calls it under-rated and would hand back Apply - a GPU restart, unattended,
            // on a machine running off its battery.
            AdapterClampLiftPolicy.SupplyQualifies(BothOn(), OnBatteryReportedAsSuch())
                .Should().BeTrue(because: "the supply rule on its own cannot tell this from a weak adapter");

            AdapterClampLiftPolicy.DecideGpu(BothOn(), OnBatteryReportedAsSuch(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid,
                                             onAcPower: false)
                .Should().Be(ClampLiftDecision.DeferredOnBattery,
                    because: "the power state refuses before the adapter verdict is consulted");
        }

        [Fact]
        public void Board_8D87_Refuses_On_Battery_By_Luck_And_That_Is_Not_The_Guard()
        {
            // 8D87 answers NotSupported on battery, which fails HasVerdict, so this board would have
            // refused with or without the power-state check. Recorded so nobody reads the measured
            // behaviour on this one machine as evidence that the check is redundant.
            OnBattery8D87().HasVerdict.Should().BeFalse();
            OnBattery8D87().IsLowWattage.Should().BeFalse();
            AdapterClampLiftPolicy.SupplyQualifies(BothOn(), OnBattery8D87()).Should().BeFalse();
        }

        [Fact]
        public void Being_Plugged_In_Is_Not_On_Its_Own_A_Reason_To_Act()
        {
            // The guard refuses on battery; it does not turn into permission on AC. A supply that
            // meets the requirement still has nothing to lift.
            AdapterClampLiftPolicy.PowerStateQualifies(onAcPower: true).Should().BeTrue();
            AdapterClampLiftPolicy.PowerStateQualifies(onAcPower: false).Should().BeFalse();

            AdapterClampLiftPolicy.DecideGpu(BothOn(), Barrel330W(), gpuHalfIsOffered: true,
                                             gpuIsParked: true, displayMode: HpWmiBios.GpuMode.Hybrid,
                                             onAcPower: true)
                .Should().Be(ClampLiftDecision.NotApplicable);
        }
    }
}
