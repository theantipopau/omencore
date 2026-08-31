namespace OmenCore.Models
{
    public enum FanMode
    {
        Max,
        Auto,
        Manual,
        Performance,  // Aggressive thermal policy for gaming
        Quiet,        // Silent mode with conservative fan speeds
        Constant      // Fixed percentage - OmenMon-style constant speed mode
    }
}
