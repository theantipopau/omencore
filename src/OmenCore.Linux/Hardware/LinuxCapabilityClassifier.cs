namespace OmenCore.Linux.Hardware;

public enum LinuxCapabilityClass
{
    FullControl,
    ProfileOnly,
    TelemetryOnly,
    UnsupportedControl
}

public sealed class LinuxCapabilityAssessment
{
    public LinuxCapabilityClass CapabilityClass { get; init; }
    public bool SupportsManualFanControl { get; init; }
    public bool SupportsProfileControl { get; init; }
    public bool SupportsTelemetry { get; init; }
    public string Reason { get; init; } = string.Empty;

    public string CapabilityKey => CapabilityClass switch
    {
        LinuxCapabilityClass.FullControl => "full-control",
        LinuxCapabilityClass.ProfileOnly => "profile-only",
        LinuxCapabilityClass.TelemetryOnly => "telemetry-only",
        _ => "unsupported-control"
    };
}

public static class LinuxCapabilityClassifier
{
    public static LinuxCapabilityAssessment Assess(
        bool isRoot,
        bool hasEcAccess,
        bool hasHpWmiPath,
        bool hasThermalProfile,
        bool hasPlatformProfile,
        bool hasAcpiPlatformProfile,
        bool hasFan1Output,
        bool hasFan2Output,
        bool hasFan1Target,
        bool hasFan2Target,
        bool hasHwmonFanAccess,
        bool hasTelemetryPaths,
        bool isUnsafeEcModel,
        string? model,
        string? boardId)
    {
        var hasManualFanControl = hasEcAccess || hasFan1Output || hasFan2Output || hasFan1Target || hasFan2Target || hasHwmonFanAccess;
        var hasProfileControl = hasThermalProfile || hasPlatformProfile || hasAcpiPlatformProfile;
        var hasTelemetry = hasTelemetryPaths || hasHpWmiPath || hasManualFanControl || hasProfileControl;

        if (hasManualFanControl)
        {
            var reason = hasHwmonFanAccess
                ? "Manual fan control is available through hp-wmi hwmon pwm/fan targets."
                : hasFan1Target || hasFan2Target
                    ? "Manual fan control is available through hp-wmi hwmon fan target files."
                    : hasFan1Output || hasFan2Output
                        ? "Manual fan control is available through hp-wmi fan output files."
                        : "Manual fan control is available through legacy EC access.";

            if (!isRoot)
            {
                reason += " Run with sudo to use write-capable controls.";
            }

            return new LinuxCapabilityAssessment
            {
                CapabilityClass = LinuxCapabilityClass.FullControl,
                SupportsManualFanControl = true,
                SupportsProfileControl = hasProfileControl,
                SupportsTelemetry = true,
                Reason = reason
            };
        }

        if (hasProfileControl)
        {
            var reason = isUnsafeEcModel
                ? $"Board '{boardId ?? "unknown"}' on model '{model ?? "unknown"}' is classified profile-only because direct EC writes are blocked for safety and only thermal/platform profile control is exposed."
                : "Thermal/platform profile control is available, but firmware does not expose manual fan target/output interfaces on this board.";

            if (!isRoot)
            {
                reason += " Run with sudo to apply profile changes.";
            }

            return new LinuxCapabilityAssessment
            {
                CapabilityClass = LinuxCapabilityClass.ProfileOnly,
                SupportsManualFanControl = false,
                SupportsProfileControl = true,
                SupportsTelemetry = true,
                Reason = reason
            };
        }

        if (hasTelemetry)
        {
            return new LinuxCapabilityAssessment
            {
                CapabilityClass = LinuxCapabilityClass.TelemetryOnly,
                SupportsManualFanControl = false,
                SupportsProfileControl = false,
                SupportsTelemetry = true,
                Reason = "Telemetry paths are present, but no writable EC, hp-wmi, hwmon target, or platform profile control interface is exposed for this board/kernel combination."
            };
        }

        return new LinuxCapabilityAssessment
        {
            CapabilityClass = LinuxCapabilityClass.UnsupportedControl,
            SupportsManualFanControl = false,
            SupportsProfileControl = false,
            SupportsTelemetry = false,
            Reason = "No supported Linux control interface was detected. This is usually a kernel exposure gap, unsupported firmware path, or missing hp-wmi/ec_sys support for the current board."
        };
    }
}