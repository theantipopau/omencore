using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmenCore.Hardware
{
    public sealed class WinRing0EcAccess : IEcAccess
    {
        private SafeFileHandle? _handle;
        private string _devicePath = string.Empty;

        public bool IsAvailable => _handle is { IsInvalid: false };

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
            return IsAvailable;
        }

        public byte ReadByte(ushort address)
        {
            EnsureHandle();
            var payload = new EcRegister { Address = address, Value = 0 };
            var ok = Native.DeviceIoControl(_handle!, Native.IOCTL_EC_READ,
                ref payload, Marshal.SizeOf<EcRegister>(),
                ref payload, Marshal.SizeOf<EcRegister>(),
                out _, IntPtr.Zero);
            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"EC read failed at 0x{address:X4}");
            }
            return payload.Value;
        }

        public void WriteByte(ushort address, byte value)
        {
            EnsureHandle();
            var payload = new EcRegister { Address = address, Value = value };
            var ok = Native.DeviceIoControl(_handle!, Native.IOCTL_EC_WRITE,
                ref payload, Marshal.SizeOf<EcRegister>(),
                IntPtr.Zero, 0, out _, IntPtr.Zero);
            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"EC write failed at 0x{address:X4}");
            }
            Thread.Sleep(1);
        }

        private void EnsureHandle()
        {
            if (!IsAvailable)
            {
                throw new InvalidOperationException($"EC bridge {_devicePath} is not ready");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EcRegister
        {
            public ushort Address;
            public byte Value;
        }

        private static class Native
        {
            public const uint FILE_GENERIC_READ = 0x80000000;
            public const uint FILE_GENERIC_WRITE = 0x40000000;
            public const uint FILE_SHARE_READ = 0x00000001;
            public const uint FILE_SHARE_WRITE = 0x00000002;
            public const uint OPEN_EXISTING = 3;
            public const uint IOCTL_EC_READ = 0x80862007; // TODO replace with actual driver codes
            public const uint IOCTL_EC_WRITE = 0x8086200B;

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
                ref EcRegister inBuffer,
                int nInBufferSize,
                ref EcRegister outBuffer,
                int nOutBufferSize,
                out int bytesReturned,
                IntPtr overlapped);

            [DllImport("kernel32", SetLastError = true)]
            public static extern bool DeviceIoControl(
                SafeFileHandle hDevice,
                uint dwIoControlCode,
                ref EcRegister inBuffer,
                int nInBufferSize,
                IntPtr outBuffer,
                int nOutBufferSize,
                out int bytesReturned,
                IntPtr overlapped);
        }
    }
}
