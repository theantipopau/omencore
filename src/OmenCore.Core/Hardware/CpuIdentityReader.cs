using System;
using System.Management;
using Microsoft.Win32;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Reads this machine's CPU identity without touching WMI.
    ///
    /// 5.0.0, WP-31: <c>select * from Win32_Processor</c> measured ~1 s on a cold WMI service on the
    /// reference machine, and two separate startup paths used it just to read strings the kernel
    /// already publishes at <c>HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0</c>. One reader
    /// keeps both paths on the same fast value instead of one of them quietly reverting to WMI.
    ///
    /// WMI is still the fallback when the key is absent, so behaviour on a system whose registry
    /// does not carry this data is unchanged.
    /// </summary>
    internal static class CpuIdentityReader
    {
        private const string ProcessorKeyPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

        /// <summary>
        /// Returns the processor name ("AMD Ryzen 7 9800X3D 8-Core Processor"), the identifier
        /// ("AMD64 Family 26 Model 68 Stepping 0", the same text WMI exposes as Caption) and the
        /// vendor identifier ("AuthenticAMD" / "GenuineIntel"). Returns false when neither source
        /// produced anything usable.
        /// </summary>
        public static bool TryRead(out string processorName, out string identifier, out string vendorIdentifier)
        {
            if (TryReadRegistry(out processorName, out identifier, out vendorIdentifier))
            {
                return true;
            }

            return TryReadWmi(out processorName, out identifier, out vendorIdentifier);
        }

        private static bool TryReadRegistry(out string processorName, out string identifier, out string vendorIdentifier)
        {
            processorName = string.Empty;
            identifier = string.Empty;
            vendorIdentifier = string.Empty;

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(ProcessorKeyPath);
                if (key == null)
                {
                    return false;
                }

                processorName = (key.GetValue("ProcessorNameString") as string)?.Trim() ?? string.Empty;
                identifier = (key.GetValue("Identifier") as string)?.Trim() ?? string.Empty;
                vendorIdentifier = (key.GetValue("VendorIdentifier") as string)?.Trim() ?? string.Empty;

                return !string.IsNullOrEmpty(processorName) || !string.IsNullOrEmpty(identifier);
            }
            catch
            {
                processorName = string.Empty;
                identifier = string.Empty;
                vendorIdentifier = string.Empty;
                return false;
            }
        }

        private static bool TryReadWmi(out string processorName, out string identifier, out string vendorIdentifier)
        {
            processorName = string.Empty;
            identifier = string.Empty;
            vendorIdentifier = string.Empty;

            try
            {
                using var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    processorName = obj["Name"]?.ToString()?.Trim() ?? string.Empty;
                    identifier = obj["Caption"]?.ToString()?.Trim() ?? string.Empty;
                    vendorIdentifier = obj["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                    return true;
                }
            }
            catch
            {
                // Fall through to the empty result below.
            }

            processorName = string.Empty;
            identifier = string.Empty;
            vendorIdentifier = string.Empty;
            return false;
        }
    }
}
