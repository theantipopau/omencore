using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// The write itself needs an AMD SMU and is verified by running it against an independent
    /// reader - see tools/SmuProbe --limits, which is the only thing here that can confirm a limit
    /// took. What is testable without hardware is everything that decides whether to write at all:
    /// the target, the supply, and the part. Those are the three ways this can be wrong on a
    /// machine nobody measured it on.
    ///
    /// The captures below are real replies from board 8D87.
    /// </summary>
    public class ApuPowerClampServiceTests
    {
        private static HpWmiBios.AdapterInfo Adapter(byte[] reply) =>
            HpWmiBios.DecodeAdapterData(reply)
                ?? throw new System.Exception("the capture is a valid 4-byte reply");

        // 280 W barrel, BelowRequirement - the case this feature exists for.
        private static HpWmiBios.AdapterInfo Barrel280W() =>
            Adapter(new byte[] { 0x02, 0xC2, 0x00, 0x38 });

        // 100 W dock, ConnectedTypeC.
        private static HpWmiBios.AdapterInfo UsbCDock100W() =>
            Adapter(new byte[] { 0x05, 0xC2, 0x00, 0x14 });

        // ── The target comes from the machine, not from here ──────────────────────────────────

        private static HpWmiBios.SystemDesignData? Board8D87Design()
        {
            byte[] reply = new byte[16];
            reply[0] = 0x4A; reply[1] = 0x01;   // 330 W shipping adapter
            reply[8] = 60;                       // DefaultCpuPowerLimitWithGpu
            return HpWmiBios.DecodeSystemDesignData(reply);
        }

        [Fact]
        public void The_Users_Own_Stapm_Setting_Wins()
        {
            // There is already a slider that means "the sustained package power I want". A second
            // feature holding a different number would make that slider a lie - it would read 51 W
            // while this re-asserted 60 W underneath it twice a minute.
            var target = ApuPowerClampService.TargetFor(
                new AmdPowerLimits { StapmLimitWatts = 51 }, Board8D87Design());

            target.Watts.Should().Be(51u);
            target.Source.Should().Be(ApuPowerClampService.TargetSource.UserSetting);
        }

        [Fact]
        public void The_Firmwares_Own_Budget_Is_The_Fallback()
        {
            // Null, not a default value: AmdPowerLimits reaches the config only when someone applies
            // the panel, so null is "nobody has chosen" and any value is a choice.
            var target = ApuPowerClampService.TargetFor(null, Board8D87Design());

            target.Watts.Should().Be(60u,
                because: "the firmware's own CPU-with-GPU figure is a number this machine writes " +
                         "itself, unlike a constant carried over from the board it was measured on");
            target.Source.Should().Be(ApuPowerClampService.TargetSource.Firmware);
        }

        [Fact]
        public void A_Deliberate_Low_Setting_Is_Still_The_Users_Choice()
        {
            // 25 W happens to be what the clamp already holds, so this makes the lift a no-op. That
            // is what the slider says to do, and second-guessing it would be the feature overriding
            // the control it is meant to serve.
            var target = ApuPowerClampService.TargetFor(
                new AmdPowerLimits { StapmLimitWatts = 25 }, Board8D87Design());

            target.Watts.Should().Be(25u);
            target.Source.Should().Be(ApuPowerClampService.TargetSource.UserSetting);
        }

        [Fact]
        public void A_Machine_That_States_No_Budget_Gets_No_Target()
        {
            ApuPowerClampService.TargetFor(null, null).Watts.Should().Be(0u);
            ApuPowerClampService.TargetFor(null, null).Source
                .Should().Be(ApuPowerClampService.TargetSource.None);

            byte[] silent = new byte[16];        // byte[8] left at zero
            ApuPowerClampService.TargetFor(null, HpWmiBios.DecodeSystemDesignData(silent))
                .Watts.Should().Be(0u, because: "no target means no offer, not a guessed one");
        }

        // ── Which supplies this is offered on ─────────────────────────────────────────────────

        [Fact]
        public void A_280W_Barrel_Against_A_330W_Requirement_Can_Carry_It()
        {
            // 85% of the requirement, and measured: the 200 W supply - a lower fraction still -
            // carried a full combined load with battery rate at exactly 0 W throughout.
            ApuPowerClampService.SupplyCanCarryIt(Barrel280W(), 330, out var reason)
                .Should().BeTrue();
            reason.Should().BeEmpty();
        }

        [Fact]
        public void A_UsbC_Source_Is_Refused_Outright()
        {
            // Not a judgement about the wattage. The dock negotiates 100 W and the platform budget
            // drops with it, so the GPU alone after a restart is already most of the source. This is
            // the one adapter state where the clamp is defensible on the numbers.
            ApuPowerClampService.SupplyCanCarryIt(UsbCDock100W(), 330, out var reason)
                .Should().BeFalse();

            reason.Should().Contain("USB-C");
            reason.Should().EndWith(".", because: "it is shown to a user as a sentence");
        }

        [Fact]
        public void An_Unreported_Rating_Is_Refused_Rather_Than_Assumed()
        {
            // byte[3] = 0: the wattage byte is not populated. Treating "not reported" as "fine"
            // would put the widest gate on the least information.
            ApuPowerClampService.SupplyCanCarryIt(Adapter(new byte[] { 0x02, 0xC2, 0x00, 0x00 }), 330,
                                                  out var reason)
                .Should().BeFalse();
            reason.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void A_Requirement_The_Machine_Never_Stated_Is_Refused()
        {
            ApuPowerClampService.SupplyCanCarryIt(Barrel280W(), 0, out _).Should().BeFalse(
                because: "with no requirement to compare against there is no ratio to judge");
        }

        // ── Which parts this is offered on ────────────────────────────────────────────────────

        [Fact]
        public void Only_Families_With_Confirmed_Message_Ids_Are_Supported()
        {
            // Strix Point is the part all of this was measured on.
            ApuPowerClampService.CpuIsSupported(RyzenFamily.StrixPoint).Should().BeTrue();

            // Raven and its relatives use a different STAPM scheme and have no confirmed APU-slow
            // message at all. A wrong message ID returns Ok and does nothing, so an unlisted family
            // must refuse rather than try.
            ApuPowerClampService.CpuIsSupported(RyzenFamily.Raven).Should().BeFalse();
            ApuPowerClampService.CpuIsSupported(RyzenFamily.Unknown).Should().BeFalse();

            // Renoir/Cezanne have the PPT limits but not the separate APU-slow domain, and raising
            // three of four moves the wall rather than removing it.
            ApuPowerClampService.CpuIsSupported(RyzenFamily.RenoirLucienne).Should().BeFalse();
        }
    }
}
