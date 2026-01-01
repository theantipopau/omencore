using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OmenCore.Hardware
{
    /// <summary>
    /// WinRing0-based MSR (Model-Specific Register) access for CPU voltage control.
    /// Requires WinRing0 kernel driver to be installed and running.
    /// 
    /// DEPRECATED: Use PawnIOMsrAccess instead. WinRing0 is blocked by Secure Boot.
    /// This class will be removed in a future version.
    /// </summary>
    [Obsolete("Use PawnIOMsrAccess instead. WinRing0 is blocked by Secure Boot.")]
    public class WinRing0MsrAccess : IMsrAccess
    {
        // Try multiple device paths for different WinRing0 versions
        private static readonly string[] DevicePaths = new[]
        {
            "\\\\.\\WinRing0_1_2_0",
            "\\\\.\\WinRing0_1_2",
            "\\\\.\\WinRing0"
        };
        
        private const uint IOCTL_MSR_READ = 0x9C402084;  // Standard WinRing0 IOCTL
        private const uint IOCTL_MSR_WRITE = 0x9C402088; // Standard WinRing0 IOCTL
        
        // Intel MSR addresses for voltage control
        private const uint MSR_IA32_VOLTAGE_PLANE_0 = 0x150; // Core voltage plane
        private const uint MSR_IA32_VOLTAGE_PLANE_2 = 0x152; // Cache voltage plane
        
        private readonly SafeFileHandle _handle;
        private readonly object _lock = new();

        public WinRing0MsrAccess()
        {
            // Try each device path until one works
            foreach (var devicePath in DevicePaths)
            {
                _handle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                    0,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (!_handle.IsInvalid)
                {
                    return; // Successfully opened
                }
            }

            throw new InvalidOperationException($"Failed to open WinRing0 device. Tried: {string.Join(", ", DevicePaths)}. Ensure driver is installed.");
        }

        public bool IsAvailable => !_handle.IsInvalid && !_handle.IsClosed;

        /// <summary>
        /// Read a Model-Specific Register
        /// </summary>
        public ulong ReadMsr(uint msrAddress)
        {
            lock (_lock)
            {
                if (!IsAvailable)
                    throw new InvalidOperationException("WinRing0 driver not available");

                var request = new MsrRequest { Register = msrAddress };
                var response = new MsrResponse();
                
                uint bytesReturned = 0;
                bool success = NativeMethods.DeviceIoControl(
                    _handle,
                    IOCTL_MSR_READ,
                    ref request,
                    Marshal.SizeOf<MsrRequest>(),
                    ref response,
                    Marshal.SizeOf<MsrResponse>(),
                    ref bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    throw new InvalidOperationException($"Failed to read MSR 0x{msrAddress:X}");
                }

                return response.Value;
            }
        }

        /// <summary>
        /// Write to a Model-Specific Register
        /// </summary>
        public void WriteMsr(uint msrAddress, ulong value)
        {
            lock (_lock)
            {
                if (!IsAvailable)
                    throw new InvalidOperationException("WinRing0 driver not available");

                var request = new MsrWriteRequest 
                { 
                    Register = msrAddress,
                    Value = value
                };
                
                uint bytesReturned = 0;
                bool success = NativeMethods.DeviceIoControl(
                    _handle,
                    IOCTL_MSR_WRITE,
                    ref request,
                    Marshal.SizeOf<MsrWriteRequest>(),
                    IntPtr.Zero,
                    0,
                    ref bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    throw new InvalidOperationException($"Failed to write MSR 0x{msrAddress:X}");
                }
            }
        }

        /// <summary>
        /// Apply voltage offset to CPU core (in millivolts)
        /// Negative values = undervolt, Positive values = overvolt (dangerous!)
        /// </summary>
        public void ApplyCoreVoltageOffset(int millivolts)
        {
            if (millivolts > 0)
            {
                throw new ArgumentException("Overvolting (positive offset) is extremely dangerous and not supported");
            }

            if (millivolts < -250)
            {
                throw new ArgumentException("Offset below -250mV is too aggressive and likely unstable");
            }

            // Convert millivolts to Intel's voltage format
            // Intel uses 1.024mV units in a 21-bit signed field
            // Formula: offset_units = (millivolts / 1.024)
            long offsetUnits = (long)(millivolts / 1.024);
            
            // Pack into voltage plane format (bits 31:21 = voltage offset)
            ulong voltageValue = (ulong)((offsetUnits & 0x7FF) << 21);
            
            WriteMsr(MSR_IA32_VOLTAGE_PLANE_0, voltageValue);
        }

        /// <summary>
        /// Apply voltage offset to CPU cache (in millivolts)
        /// </summary>
        public void ApplyCacheVoltageOffset(int millivolts)
        {
            if (millivolts > 0)
            {
                throw new ArgumentException("Overvolting (positive offset) is extremely dangerous and not supported");
            }

            if (millivolts < -250)
            {
                throw new ArgumentException("Offset below -250mV is too aggressive and likely unstable");
            }

            long offsetUnits = (long)(millivolts / 1.024);
            ulong voltageValue = (ulong)((offsetUnits & 0x7FF) << 21);
            
            WriteMsr(MSR_IA32_VOLTAGE_PLANE_2, voltageValue);
        }

        /// <summary>
        /// Read current core voltage offset (in millivolts)
        /// Returns 0 if unable to read or parse
        /// </summary>
        public int ReadCoreVoltageOffset()
        {
            try
            {
                ulong value = ReadMsr(MSR_IA32_VOLTAGE_PLANE_0);
                long offsetUnits = (long)((value >> 21) & 0x7FF);
                
                // Sign extend from 11 bits
                if ((offsetUnits & 0x400) != 0)
                {
                    offsetUnits |= unchecked((long)0xFFFFFFFFFFFFF800);
                }
                
                return (int)(offsetUnits * 1.024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Read current cache voltage offset (in millivolts)
        /// </summary>
        public int ReadCacheVoltageOffset()
        {
            try
            {
                ulong value = ReadMsr(MSR_IA32_VOLTAGE_PLANE_2);
                long offsetUnits = (long)((value >> 21) & 0x7FF);
                
                if ((offsetUnits & 0x400) != 0)
                {
                    offsetUnits |= unchecked((long)0xFFFFFFFFFFFFF800);
                }
                
                return (int)(offsetUnits * 1.024);
            }
            catch
            {
                return 0;
            }
        }
        
        // ==========================================
        // TCC Offset (Thermal Control Circuit)
        // ==========================================
        
        /// <summary>
        /// MSR 0x1A2 - IA32_TEMPERATURE_TARGET
        /// Bits 29:24 contain the TCC activation temperature offset (0-63°C reduction)
        /// </summary>
        private const uint MSR_IA32_TEMPERATURE_TARGET = 0x1A2;
        
        /// <summary>
        /// Read the current TCC offset (temperature limit reduction).
        /// Returns 0-63, where 0 = no limit, 63 = max 63°C below TjMax.
        /// </summary>
        public int ReadTccOffset()
        {
            try
            {
                ulong value = ReadMsr(MSR_IA32_TEMPERATURE_TARGET);
                // Bits 29:24 contain the TCC offset
                int offset = (int)((value >> 24) & 0x3F);
                return offset;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Read the TjMax (maximum junction temperature) from MSR.
        /// This is the base temperature before TCC offset is applied.
        /// </summary>
        public int ReadTjMax()
        {
            try
            {
                ulong value = ReadMsr(MSR_IA32_TEMPERATURE_TARGET);
                // Bits 23:16 contain TjMax
                int tjMax = (int)((value >> 16) & 0xFF);
                return tjMax > 0 ? tjMax : 100; // Default to 100°C if not readable
            }
            catch
            {
                return 100; // Default TjMax
            }
        }
        
        /// <summary>
        /// Set the TCC offset to limit maximum CPU temperature.
        /// Offset of N means CPU will throttle at (TjMax - N)°C.
        /// </summary>
        /// <param name="offset">Offset in degrees (0-63). 0 = no limit, 15 = throttle 15°C below TjMax</param>
        public void SetTccOffset(int offset)
        {
            if (offset < 0 || offset > 63)
            {
                throw new ArgumentException("TCC offset must be between 0 and 63");
            }
            
            // Read current value to preserve other bits
            ulong currentValue = ReadMsr(MSR_IA32_TEMPERATURE_TARGET);
            
            // Clear bits 29:24 and set new offset
            ulong newValue = (currentValue & ~(0x3FUL << 24)) | ((ulong)offset << 24);
            
            WriteMsr(MSR_IA32_TEMPERATURE_TARGET, newValue);
        }
        
        /// <summary>
        /// Get the effective temperature limit (TjMax - TCC offset).
        /// </summary>
        public int GetEffectiveTempLimit()
        {
            int tjMax = ReadTjMax();
            int offset = ReadTccOffset();
            return tjMax - offset;
        }

        public void Dispose()
        {
            _handle?.Dispose();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MsrRequest
        {
            public uint Register;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MsrResponse
        {
            public uint Register;
            public ulong Value;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MsrWriteRequest
        {
            public uint Register;
            public ulong Value;
        }

        private static class NativeMethods
        {
            public const uint GENERIC_READ = 0x80000000;
            public const uint GENERIC_WRITE = 0x40000000;
            public const uint OPEN_EXISTING = 3;

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeFileHandle CreateFile(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeviceIoControl(
                SafeFileHandle hDevice,
                uint dwIoControlCode,
                ref MsrRequest lpInBuffer,
                int nInBufferSize,
                ref MsrResponse lpOutBuffer,
                int nOutBufferSize,
                ref uint lpBytesReturned,
                IntPtr lpOverlapped);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeviceIoControl(
                SafeFileHandle hDevice,
                uint dwIoControlCode,
                ref MsrWriteRequest lpInBuffer,
                int nInBufferSize,
                IntPtr lpOutBuffer,
                int nOutBufferSize,
                ref uint lpBytesReturned,
                IntPtr lpOverlapped);
        }
    }
}
