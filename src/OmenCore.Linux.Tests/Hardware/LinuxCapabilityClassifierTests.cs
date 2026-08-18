using FluentAssertions;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Tests.Hardware;

/// <summary>
/// LinuxCapabilityClassifier had no test coverage at all before this file, despite being pure,
/// deterministic logic with zero hardware/filesystem dependency - there was no OmenCore.Linux
/// test project of any kind. Covers the classification matrix directly, plus a dedicated
/// regression test for GitHub #137 (board 8BCD, ACPI WMAA/WHCM aborts) - the exact real-world
/// report that drove the WMAA-abort-prone-board downgrade this class already implements.
/// </summary>
public class LinuxCapabilityClassifierTests
{
    private static LinuxCapabilityAssessment Assess(
        bool isRoot = true,
        bool hasEcAccess = false,
        bool hasHpWmiPath = false,
        bool hasThermalProfile = false,
        bool hasPlatformProfile = false,
        bool hasAcpiPlatformProfile = false,
        bool hasFan1Output = false,
        bool hasFan2Output = false,
        bool hasFan1Target = false,
        bool hasFan2Target = false,
        bool hasHwmonFanAccess = false,
        bool hasTelemetryPaths = false,
        bool isUnsafeEcModel = false,
        string? model = null,
        string? boardId = null) =>
        LinuxCapabilityClassifier.Assess(
            isRoot, hasEcAccess, hasHpWmiPath, hasThermalProfile, hasPlatformProfile,
            hasAcpiPlatformProfile, hasFan1Output, hasFan2Output, hasFan1Target, hasFan2Target,
            hasHwmonFanAccess, hasTelemetryPaths, isUnsafeEcModel, model, boardId);

    [Fact]
    public void Board8BCD_WithManualFanTargetAccess_IsDowngradedToProfileOnly_NotFullControl()
    {
        // GitHub #137: OMEN 16-xd0xxx (board 8BCD) reports a genuine per-fan hwmon target write
        // path present - which would otherwise classify as FullControl - but every WMI call
        // actually aborts in the kernel (dmesg: "ACPI Error: Aborting method _SB.WMID.WMAA due
        // to previous error"). Fan profile commands return success with zero hardware effect,
        // RPM never changes, keyboard RGB writes revert immediately, and battery status reads
        // back wrong. The classifier must not claim FullControl here just because the sysfs
        // paths exist.
        var assessment = Assess(hasFan1Target: true, hasHwmonFanAccess: true, boardId: "8BCD");

        assessment.CapabilityClass.Should().Be(LinuxCapabilityClass.ProfileOnly,
            "board 8BCD's hwmon paths exist on paper but field reports confirm WMI writes silently no-op");
        assessment.SupportsManualFanControl.Should().BeFalse();
        assessment.SupportsProfileControl.Should().BeTrue();
        assessment.Reason.Should().Contain("8BCD").And.Contain("WMAA");
    }

    [Fact]
    public void Board8BCD_WithNoProfileControlEither_FallsToTelemetryOnly()
    {
        var assessment = Assess(hasHpWmiPath: true, boardId: "8BCD");

        assessment.CapabilityClass.Should().Be(LinuxCapabilityClass.TelemetryOnly);
        assessment.SupportsManualFanControl.Should().BeFalse();
        assessment.SupportsProfileControl.Should().BeFalse();
    }

    [Fact]
    public void Board8BCD_IsCaseInsensitiveAndTrimmed()
    {
        Assess(hasFan1Target: true, hasHwmonFanAccess: true, boardId: " 8bcd ").CapabilityClass
            .Should().Be(LinuxCapabilityClass.ProfileOnly, "board ID matching must not be case- or whitespace-sensitive");
    }

    [Fact]
    public void OtherBoard_WithSameManualFanTargetAccess_IsNotDowngraded()
    {
        // Same inputs as the 8BCD test above, different board - proves the downgrade is
        // specific to 8BCD's known-bad firmware, not a general distrust of this input shape.
        var assessment = Assess(hasFan1Target: true, hasHwmonFanAccess: true, boardId: "8BCA");

        assessment.CapabilityClass.Should().Be(LinuxCapabilityClass.FullControl);
        assessment.SupportsManualFanControl.Should().BeTrue();
    }

    [Fact]
    public void HwmonPwmEnableAlone_WithoutAPerFanTarget_IsProfileOnly_NotFullControl()
    {
        // hasHwmonFanAccess on its own (pwm_enable present, matching issue #137's own
        // diagnostics table: "HP-WMI pwm1_enable: Present" but "Fan 1/2 Target Control: Missing")
        // is coarse policy control, not a reliable manual per-fan write path - the classifier's
        // own comment says exactly this. It must land in ProfileOnly, not FullControl, on any
        // board, independent of the 8BCD-specific downgrade.
        var assessment = Assess(hasHwmonFanAccess: true, boardId: "8BCA");

        assessment.CapabilityClass.Should().Be(LinuxCapabilityClass.ProfileOnly);
        assessment.SupportsManualFanControl.Should().BeFalse();
    }

    [Fact]
    public void EcAccess_WithoutWmaaAbortBoard_IsFullControl()
    {
        var assessment = Assess(hasEcAccess: true, boardId: "8D41");

        assessment.CapabilityClass.Should().Be(LinuxCapabilityClass.FullControl);
        assessment.SupportsManualFanControl.Should().BeTrue();
        assessment.Reason.Should().Contain("legacy EC access");
    }

    [Fact]
    public void ThermalProfileOnly_NoManualFanControl_IsProfileOnly()
    {
        var assessment = Assess(hasThermalProfile: true);

        assessment.CapabilityClass.Should().Be(LinuxCapabilityClass.ProfileOnly);
        assessment.SupportsManualFanControl.Should().BeFalse();
        assessment.SupportsProfileControl.Should().BeTrue();
    }

    [Fact]
    public void UnsafeEcModel_ProfileOnlyReason_NamesTheBoard()
    {
        var assessment = Assess(hasThermalProfile: true, isUnsafeEcModel: true, boardId: "8E41", model: "Transcend 14");

        assessment.Reason.Should().Contain("8E41").And.Contain("Transcend 14").And.Contain("blocked for safety");
    }

    [Fact]
    public void TelemetryPathsOnly_NoControlInterfaces_IsTelemetryOnly()
    {
        var assessment = Assess(hasTelemetryPaths: true);

        assessment.CapabilityClass.Should().Be(LinuxCapabilityClass.TelemetryOnly);
        assessment.SupportsTelemetry.Should().BeTrue();
        assessment.SupportsManualFanControl.Should().BeFalse();
        assessment.SupportsProfileControl.Should().BeFalse();
    }

    [Fact]
    public void NothingDetected_IsUnsupportedControl()
    {
        var assessment = Assess();

        assessment.CapabilityClass.Should().Be(LinuxCapabilityClass.UnsupportedControl);
        assessment.SupportsTelemetry.Should().BeFalse();
        assessment.CapabilityKey.Should().Be("unsupported-control");
    }

    [Fact]
    public void NotRoot_AppendsSudoGuidanceToReason()
    {
        var assessment = Assess(isRoot: false, hasEcAccess: true);

        assessment.Reason.Should().Contain("sudo");
    }

    [Theory]
    [InlineData(LinuxCapabilityClass.FullControl, "full-control")]
    [InlineData(LinuxCapabilityClass.ProfileOnly, "profile-only")]
    [InlineData(LinuxCapabilityClass.TelemetryOnly, "telemetry-only")]
    [InlineData(LinuxCapabilityClass.UnsupportedControl, "unsupported-control")]
    public void CapabilityKey_MapsEachClassToItsExpectedStringKey(LinuxCapabilityClass capabilityClass, string expectedKey)
    {
        var assessment = new LinuxCapabilityAssessment { CapabilityClass = capabilityClass };

        assessment.CapabilityKey.Should().Be(expectedKey);
    }
}
