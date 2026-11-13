using OmenCore.Hardware;

namespace OmenCoreApp.Tests
{
    /// <summary>
    /// Temperatures for tests without touching the machine. The real LibreHardwareMonitor bridge opens PawnIO and the GPU
    /// vendor libraries on whatever computer runs the suite; on a desktop with an AMD Radeon the ADL driver faulted inside
    /// a test (2026-11-12) and took the whole test host down. Zero is what the CI runners report, so tests written
    /// against those runners see the same values here.
    /// </summary>
    internal static class TestThermals
    {
        public static ThermalSensorProvider NoHardware() => ThermalSensorProvider.FromReader(() => (0, 0));
    }
}
