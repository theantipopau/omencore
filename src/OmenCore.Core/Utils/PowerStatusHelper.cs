using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OmenCore.Utils
{
    /// <summary>
    /// Minimal P/Invoke replacement for System.Windows.Forms.SystemInformation.PowerStatus -
    /// AC/battery status was the only thing three services (PowerAutomationService,
    /// AutomationService, WmiBiosMonitor) needed from System.Windows.Forms, and pulling in the
    /// whole WinForms runtime pack for one Win32 struct isn't worth it in a library meant to
    /// stay usable from a lean future CLI host (docs/ROADMAP_v4.2.1.md, "v4.3.0 candidate slate"
    /// item 1). Wraps the same kernel32 GetSystemPowerStatus call WinForms itself calls.
    ///
    /// Deliberately does not swallow a failed GetSystemPowerStatus call (throws Win32Exception
    /// instead, same as the WinForms property would surface one) - every original call site
    /// already wraps this in its own try/catch with a WMI/WinRT fallback path, and swallowing
    /// the failure here would silently skip that fallback instead of triggering it.
    /// </summary>
    public static class PowerStatusHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        private static SYSTEM_POWER_STATUS Query()
        {
            if (!GetSystemPowerStatus(out var status))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return status;
        }

        /// <summary>True if AC power is confirmed connected (raw ACLineStatus == 1); false for
        /// an "unknown" (255) reading. Throws Win32Exception if the query itself fails.</summary>
        public static bool IsAcPowerOnline() => Query().ACLineStatus == 1;

        /// <summary>Battery charge percent (0-100), or null if unknown (raw byte 255) rather
        /// than a sentinel value a caller could mistake for real data. Throws Win32Exception if
        /// the query itself fails.</summary>
        public static int? GetBatteryLifePercent()
        {
            var status = Query();
            return status.BatteryLifePercent == 255 ? null : status.BatteryLifePercent;
        }
    }
}
