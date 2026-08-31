namespace OmenCore.Models
{
    /// <summary>
    /// Status of TCC (Thermal Control Circuit) offset - CPU temperature limit.
    /// </summary>
    public class TccOffsetStatus
    {
        /// <summary>
        /// Whether TCC offset is supported on this CPU.
        /// </summary>
        public bool IsSupported { get; set; }
        
        /// <summary>
        /// Current TCC offset in degrees Celsius (0-63).
        /// </summary>
        public int CurrentOffset { get; set; }
        
        /// <summary>
        /// TjMax - maximum junction temperature before any offset.
        /// </summary>
        public int TjMax { get; set; } = 100;
        
        /// <summary>
        /// Effective temperature limit (TjMax - CurrentOffset).
        /// </summary>
        public int EffectiveLimit => TjMax - CurrentOffset;
        
        /// <summary>
        /// Status message for display.
        /// </summary>
        public string StatusMessage { get; set; } = "";
        
        /// <summary>
        /// Create an unsupported status.
        /// </summary>
        public static TccOffsetStatus CreateUnsupported(string reason = "TCC offset not supported")
        {
            return new TccOffsetStatus
            {
                IsSupported = false,
                StatusMessage = reason
            };
        }
        
        /// <summary>
        /// Create a supported status with current values.
        /// </summary>
        public static TccOffsetStatus CreateSupported(int tjMax, int currentOffset)
        {
            return new TccOffsetStatus
            {
                IsSupported = true,
                TjMax = tjMax,
                CurrentOffset = currentOffset,
                StatusMessage = currentOffset > 0 
                    ? $"Temp limit: {tjMax - currentOffset}°C (TjMax {tjMax}°C - {currentOffset}°C offset)"
                    : $"No limit (TjMax {tjMax}°C)"
            };
        }
    }
}
