using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmenCore.Hardware
{
    /// <summary>
    /// WinRing0-based EC access provider for systems without PawnIO.
    /// Uses the WinRing0 driver's port I/O capabilities with ACPI EC protocol.
    /// Requires Secure Boot and Memory Integrity to be disabled.
    /// </summary>
    public sealed class WinRing0EcAccess : IEcAccess
    {
        private SafeFileHandle? _handle;
        private string _devicePath = string.Empty;
        private bool _disposed;
        
        // ACPI EC standard ports
        private const ushort EC_DATA_PORT = 0x62;
        private const ushort EC_CMD_PORT = 0x66;

        // EC commands (ACPI standard)
        private const byte EC_CMD_READ = 0x80;   // RD_EC
        private const byte EC_CMD_WRITE = 0x81;  // WR_EC

        // EC status bits
        private const byte EC_STATUS_OBF = 0x01;  // Output Buffer Full
        private const byte EC_STATUS_IBF = 0x02;  // Input Buffer Full

        // Timeout for EC operations
        private const int EC_TIMEOUT_MS = 100;
        private const int EC_POLL_INTERVAL_US = 10;
        
        // Mutex for EC access synchronization
        private static readonly Mutex EcMutex = new(false, @"Global\Access_EC");
        
        /// <summary>
        /// Enable experimental keyboard EC writes. Set this to true when ExperimentalEcKeyboardEnabled is on.
        /// </summary>
        public static bool EnableExperimentalKeyboardWrites { get; set; } = false;
        
        /// <summary>
        /// Enable exclusive EC access diagnostic mode.
        /// When enabled, acquires and holds the EC mutex exclusively for diagnostic purposes.
        /// </summary>
        public static bool EnableExclusiveEcAccessDiagnostics { get; set; } = false;

        /// <summary>
        /// Allowlist of EC addresses that are safe to write (fan control only).
        /// Prevents accidental writes to critical hardware registers like VRM control, battery charger, etc.
        /// IMPORTANT: Keyboard RGB EC addresses (0xB0-0xBE) are NOT included because
        /// they vary by model and can cause system crashes on some hardware (e.g., OMEN 17-ck2xxx).
        /// </summary>
        private static readonly HashSet<ushort> AllowedWriteAddresses = new()
        {
            // Fan control registers (HP Omen typical addresses - adjust for your hardware)
            0x2C, // Fan 1 set speed % (XSS1) - OmenMon-style, newer models
            0x2D, // Fan 2 set speed % (XSS2) - OmenMon-style, newer models
            0x2E, // Fan 1 speed % (legacy)
            0x2F, // Fan 2 speed % (legacy)
            0x34, // Fan 1 speed in 100 RPM units (0-55)
            0x35, // Fan 2 speed in 100 RPM units (0-55)
            0x44, // Fan 1 duty cycle
            0x45, // Fan 2 duty cycle
            0x46, // Fan control mode
            0x4A, // Fan 1 speed low byte
            0x4B, // Fan 1 speed high byte
            0x4C, // Fan 2 speed low byte
            0x4D, // Fan 2 speed high byte
            0x62, // OMCC - BIOS manual/auto control (0x06=Manual, 0x00=Auto)
            0x63, // Timer register
            0x95, // Performance mode register for throttling mitigation (write 0x31 for performance)
            0xB0, // Fan speed target CPU
            0xB1, // Fan speed target GPU
            0xEC, // Fan boost (0x00=OFF, 0x0C=ON)
            0xF4, // Fan state (0x00=Enable, 0x02=Disable)
            
            // Note: 0x6C (dust cleaning/fan reversal) is NOT included because true fan reversal
            // requires OMEN Max hardware with omnidirectional BLDC fans. Writing to this register
            // on unsupported hardware could be dangerous.
            
            // NOTE: Keyboard backlight EC addresses (0xB2-0xBE) are NOT safe to write!
            // These registers vary by model and caused hard crashes on OMEN 17-ck2xxx.
            // Use WMI BIOS SetColorTable() for keyboard lighting instead.
            
            // Performance modes
            0xCE, // Performance mode register
            0xCF, // Power limit control
        };

        public bool IsAvailable
        {
            get
            {
                try
                {
                    return _handle != null && !_handle.IsInvalid && !_handle.IsClosed;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        public bool Initialize(string devicePath)
        {
            _devicePath = devicePath;
            _handle?.Dispose();
            _handle = Native.CreateFile(devicePath,
                Native.FILE_GENERIC_READ | Native.FILE_GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Native.OPEN_EXISTING,
                0,
                IntPtr.Zero);
            
            if (!IsAvailable)
            {
                System.Diagnostics.Debug.WriteLine($"[WinRing0] Failed to open device {devicePath}: {Marshal.GetLastWin32Error()}");
                return false;
            }
            
            // Verify we can actually do port I/O by reading the EC status
            try
            {
                byte status = ReadPortByte(EC_CMD_PORT);
                System.Diagnostics.Debug.WriteLine($"[WinRing0] EC status port read successful: 0x{status:X2}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WinRing0] EC port read test failed: {ex.Message}");
                _handle?.Dispose();
                _handle = null;
                return false;
            }
        }

        public byte ReadByte(ushort address)
        {
            EnsureHandle();
            
            bool acquired = false;
            try
            {
                acquired = EcMutex.WaitOne(EC_TIMEOUT_MS * 2);
                if (!acquired)
                {
                    if (EnableExclusiveEcAccessDiagnostics)
                    {
                        // In exclusive mode, log contention and throw
                        throw new TimeoutException("EC mutex acquisition failed - another application holds EC access (exclusive diagnostics mode)");
                    }
                    else
                    {
                        throw new TimeoutException("EC mutex acquisition timed out");
                    }
                }
                
                // Wait for input buffer empty
                if (!WaitForInputBufferEmpty())
                {
                    throw new TimeoutException($"EC read timeout waiting for IBF clear at address 0x{address:X2}");
                }
                
                // Send read command
                WritePortByte(EC_CMD_PORT, EC_CMD_READ);
                
                // Wait for input buffer empty
                if (!WaitForInputBufferEmpty())
                {
                    throw new TimeoutException($"EC read timeout after command at address 0x{address:X2}");
                }
                
                // Send address
                WritePortByte(EC_DATA_PORT, (byte)address);
                
                // Wait for output buffer full
                if (!WaitForOutputBufferFull())
                {
                    throw new TimeoutException($"EC read timeout waiting for OBF at address 0x{address:X2}");
                }
                
                // Read data
                return ReadPortByte(EC_DATA_PORT);
            }
            finally
            {
                if (acquired)
                    EcMutex.ReleaseMutex();
            }
        }

        public void WriteByte(ushort address, byte value)
        {
            EnsureHandle();
            
            // CRITICAL SAFETY CHECK: Only allow writes to pre-approved addresses
            if (!AllowedWriteAddresses.Contains(address))
            {
                var allowedList = string.Join(", ", AllowedWriteAddresses.Select(a => $"0x{a:X2}"));
                throw new UnauthorizedAccessException(
                    $"EC write to address 0x{address:X2} is blocked for safety. " +
                    $"Only approved addresses can be written to prevent hardware damage.");
            }
            
            bool acquired = false;
            try
            {
                acquired = EcMutex.WaitOne(EC_TIMEOUT_MS * 2);
                if (!acquired)
                {
                    if (EnableExclusiveEcAccessDiagnostics)
                    {
                        // In exclusive mode, log contention and throw
                        throw new TimeoutException("EC mutex acquisition failed - another application holds EC access (exclusive diagnostics mode)");
                    }
                    else
                    {
                        throw new TimeoutException("EC mutex acquisition timed out");
                    }
                }
                
                // Wait for input buffer empty
                if (!WaitForInputBufferEmpty())
                {
                    throw new TimeoutException($"EC write timeout waiting for IBF clear at address 0x{address:X2}");
                }
                
                // Send write command
                WritePortByte(EC_CMD_PORT, EC_CMD_WRITE);
                
                // Wait for input buffer empty
                if (!WaitForInputBufferEmpty())
                {
                    throw new TimeoutException($"EC write timeout after command at address 0x{address:X2}");
                }
                
                // Send address
                WritePortByte(EC_DATA_PORT, (byte)address);
                
                // Wait for input buffer empty
                if (!WaitForInputBufferEmpty())
                {
                    throw new TimeoutException($"EC write timeout after address at 0x{address:X2}");
                }
                
                // Send data
                WritePortByte(EC_DATA_PORT, value);
                
                // Small delay for EC to process
                Thread.Sleep(1);
            }
            finally
            {
                if (acquired)
                    EcMutex.ReleaseMutex();
            }
        }
        
        /// <summary>
        /// Wait for EC input buffer to be empty (IBF=0).
        /// </summary>
        private bool WaitForInputBufferEmpty()
        {
            int elapsed = 0;
            while (elapsed < EC_TIMEOUT_MS * 1000)
            {
                byte status = ReadPortByte(EC_CMD_PORT);
                if ((status & EC_STATUS_IBF) == 0)
                    return true;
                    
                // Spin wait ~10us
                SpinWait.SpinUntil(() => false, TimeSpan.FromTicks(EC_POLL_INTERVAL_US * 10));
                elapsed += EC_POLL_INTERVAL_US;
            }
            return false;
        }
        
        /// <summary>
        /// Wait for EC output buffer to be full (OBF=1).
        /// </summary>
        private bool WaitForOutputBufferFull()
        {
            int elapsed = 0;
            while (elapsed < EC_TIMEOUT_MS * 1000)
            {
                byte status = ReadPortByte(EC_CMD_PORT);
                if ((status & EC_STATUS_OBF) != 0)
                    return true;
                    
                SpinWait.SpinUntil(() => false, TimeSpan.FromTicks(EC_POLL_INTERVAL_US * 10));
                elapsed += EC_POLL_INTERVAL_US;
            }
            return false;
        }
        
        /// <summary>
        /// Read a byte from an I/O port using WinRing0.
        /// </summary>
        private byte ReadPortByte(ushort port)
        {
            try
            {
                var inBuf = new PortByteInput { Port = (uint)port };
                var outBuf = new PortByteOutput { Value = 0 };
                
                bool ok = Native.DeviceIoControl(_handle!,
                    Native.IOCTL_OLS_READ_IO_PORT_BYTE,
                    ref inBuf, Marshal.SizeOf<PortByteInput>(),
                    ref outBuf, Marshal.SizeOf<PortByteOutput>(),
                    out _, IntPtr.Zero);
                    
                if (!ok)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Port read failed at 0x{port:X4}");
                }
                
                return (byte)outBuf.Value;
            }
            catch (ObjectDisposedException)
            {
                throw new InvalidOperationException("WinRing0 handle has been disposed");
            }
        }
        
        /// <summary>
        /// Write a byte to an I/O port using WinRing0.
        /// </summary>
        private void WritePortByte(ushort port, byte value)
        {
            try
            {
                var inBuf = new PortByteInOut { Port = (uint)port, Value = value };
                
                bool ok = Native.DeviceIoControl(_handle!,
                    Native.IOCTL_OLS_WRITE_IO_PORT_BYTE,
                    ref inBuf, Marshal.SizeOf<PortByteInOut>(),
                    IntPtr.Zero, 0,
                    out _, IntPtr.Zero);
                    
                if (!ok)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Port write failed at 0x{port:X4}");
                }
            }
            catch (ObjectDisposedException)
            {
                throw new InvalidOperationException("WinRing0 handle has been disposed");
            }
        }

        private void EnsureHandle()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WinRing0EcAccess));
            }
            if (!IsAvailable)
            {
                throw new InvalidOperationException($"WinRing0 driver {_devicePath} is not ready");
            }
            // Additional check for disposed handle
            if (_handle == null || _handle.IsClosed)
            {
                throw new InvalidOperationException($"WinRing0 driver {_devicePath} handle is closed");
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle?.Dispose();
            _handle = null;
        }

        // WinRing0 IOCTL structures
        [StructLayout(LayoutKind.Sequential)]
        private struct PortByteInput
        {
            public uint Port;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct PortByteOutput
        {
            public uint Value;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct PortByteInOut
        {
            public uint Port;
            public byte Value;
        }

        private static class Native
        {
            public const uint FILE_GENERIC_READ = 0x80000000;
            public const uint FILE_GENERIC_WRITE = 0x40000000;
            public const uint FILE_SHARE_READ = 0x00000001;
            public const uint FILE_SHARE_WRITE = 0x00000002;
            public const uint OPEN_EXISTING = 3;
            
            // WinRing0 IOCTL codes (from OLS - OpenLibSys)
            // CTL_CODE(OLS_TYPE, Function, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA)
            // OLS_TYPE = 0x9C40
            public const uint IOCTL_OLS_READ_IO_PORT_BYTE = 0x9C402480;
            public const uint IOCTL_OLS_WRITE_IO_PORT_BYTE = 0x9C402488;

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeFileHandle CreateFile(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);

            [DllImport("kernel32", SetLastError = true)]
            public static extern bool DeviceIoControl(
                SafeFileHandle hDevice,
                uint dwIoControlCode,
                ref PortByteInput inBuffer,
                int nInBufferSize,
                ref PortByteOutput outBuffer,
                int nOutBufferSize,
                out int bytesReturned,
                IntPtr overlapped);

            [DllImport("kernel32", SetLastError = true)]
            public static extern bool DeviceIoControl(
                SafeFileHandle hDevice,
                uint dwIoControlCode,
                ref PortByteInOut inBuffer,
                int nInBufferSize,
                IntPtr outBuffer,
                int nOutBufferSize,
                out int bytesReturned,
                IntPtr overlapped);
        }
    }
}
