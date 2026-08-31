using System;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class PowerLimitApplyResult
    {
        public PerformanceMode RequestedMode { get; set; } = new();
        public int AppliedPerformanceMode { get; set; }
        public int ReadBackPerformanceMode { get; set; }
        public int ReadBackCpuPl1 { get; set; }
        public int ReadBackCpuPl2 { get; set; }
        public int ReadBackGpuTgp { get; set; }
        public bool EcWriteSucceeded { get; set; }
        public bool VerificationPassed { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// True if the power limits were successfully applied and verified.
        /// </summary>
        public bool Success => EcWriteSucceeded && VerificationPassed;

        /// <summary>
        /// Check if read-back values match expected values.
        /// </summary>
        public bool ValuesMatch => ReadBackPerformanceMode == AppliedPerformanceMode;
    }
}