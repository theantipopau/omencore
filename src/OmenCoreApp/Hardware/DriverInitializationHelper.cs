using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Centralized helper for driver post-installation state detection.
    /// Detects when drivers are installed but need reboot to fully activate.
    /// </summary>
    public static class DriverInitializationHelper
    {
        /// <summary>
        /// Check if PawnIO driver is installed on the system.
        /// This is a static utility used by multiple PawnIO initialization paths.
        /// </summary>
        public static bool IsPawnIOInstalled()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return false;
                }

                // Check registry first (most reliable)
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key != null)
                {
                    string? installLocation = key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                    {
                        return true;
                    }
                }

                // Check default installation path
                string defaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO");
                if (Directory.Exists(defaultPath))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if WinRing0 driver is installed on the system.
        /// Checks registry for driver installation.
        /// </summary>
        public static bool IsWinRing0Installed()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return false;
                }

                // Check WinRing0 registry keys
                using var key1 = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\WinRing0_1_2_0");
                if (key1 != null) return true;

                using var key2 = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\WinRing0");
                if (key2 != null) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detect if a driver module failed to load due to pending reboot requirement.
        /// Called when a driver is installed but module loading failed.
        /// Guides user to restart for full activation.
        /// </summary>
        /// <param name="driverName">e.g. "PawnIO", "WinRing0"</param>
        /// <param name="moduleName">e.g. "IntelMSR", "LpcACPIEC"</param>
        /// <param name="logging">Logging service (optional)</param>
        /// <returns>Warning message for user if reboot recommended</returns>
        public static string? GetPostInstallationRebootWarning(
            string driverName,
            string? moduleName,
            Services.LoggingService? logging = null)
        {
            if (moduleName == null)
            {
                return $"⚠️  {driverName} is installed but failed to fully initialize. " +
                       $"Please restart your computer to activate the driver.";
            }
            else
            {
                return $"⚠️  {driverName} {moduleName} module failed to load. " +
                       $"Restart your computer to fully activate the driver after installation.";
            }
        }
    }
}
