namespace OmenCore.Models
{
    /// <summary>
    /// Pure helper for GPU OC safety checks.
    /// Separates increase-detection logic from UI so we can unit test policy decisions.
    /// </summary>
    public static class GpuOcSafetyGuard
    {
        /// <summary>
        /// Returns true when requested values represent a net increase above the current applied values.
        /// Any increase in core clock, memory clock, power limit, or voltage offset counts.
        /// </summary>
        public static bool IsIncreaseRequest(
            int requestedCore,
            int requestedMemory,
            int requestedPowerLimit,
            int requestedVoltage,
            int currentCore,
            int currentMemory,
            int currentPowerLimit,
            int currentVoltage)
        {
            return requestedCore > currentCore
                || requestedMemory > currentMemory
                || requestedPowerLimit > currentPowerLimit
                || requestedVoltage > currentVoltage;
        }
    }
}
