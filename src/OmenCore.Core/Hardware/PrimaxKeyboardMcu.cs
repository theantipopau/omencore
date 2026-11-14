using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Per-key keyboard MCU on 2021-2024 OMEN 16/17 per-key boards: Primax <c>0461:4E9A</c>
    /// ("Cybug", OMEN 17) and <c>0461:4E9B</c> ("Ralph", OMEN 16), vendor interface <c>mi_02</c>,
    /// usage page <c>0xFF13</c>.
    ///
    /// WHY THIS EXISTS. On these boards the BIOS reports keyboard type 3 (per-key) and still
    /// answers the four-zone ColorTable commands, but nothing written there reaches the keys.
    /// The keyboard is only reachable through its own USB MCU.
    ///
    /// PROVENANCE. The wire format is the same McuSDK2 frame <see cref="DojoKeyboardMcu"/> speaks
    /// (<c>[cmd][index][len lo][len hi][60-byte payload]</c>, SETs acked <c>EC AC</c>), which this
    /// project measured on board 8D87. The Primax-specific facts - interface <c>mi_02</c>, report
    /// id 0, LED counts, which boards OGH routes here, and that a static map needs no flash write -
    /// come from the Ohman project's published research notes (github.com/P4R1H/ohman,
    /// docs/research.md section 7b), which read them out of OMEN Gaming Hub's own assemblies and
    /// confirmed them on board 8BAD. Only the documented facts were used; no Ohman code (GPL-3)
    /// was copied. The transport below is this project's own, shared with DojoKeyboardMcu.
    ///
    /// SAFETY. Only the commands that were observed to be harmless are ever sent, enforced by
    /// <see cref="IsAllowedCommand"/>: device-info GETs, lighting on/off, and the three colour
    /// pages. Store-to-flash (0x0A) and restore/firmware-update mode (0x10) are refused in code.
    ///
    /// SCOPE. Uniform colour only. The per-key index map for these keyboards is not in this
    /// project yet, so four-zone colour cannot be placed on the right keys.
    /// </summary>
    public sealed class PrimaxKeyboardMcu : IDisposable
    {
        public const ushort PrimaxVid = 0x0461;
        public const ushort CybugPid = 0x4E9A;   // OMEN 17 per-key
        public const ushort RalphPid = 0x4E9B;   // OMEN 16 per-key
        // OMEN 16-wf1xxx (2024, board 8C77) per-key, from a Discord user's log (2026-11-14): firmware
        // keyboard type 3, VID_0461&PID_4F1E with an MI_02 interface, and the four-zone colour table
        // accepted but ignored. LED count unknown. Still has to pass the device-info handshake in Open()
        // before anything else is sent, so a different MCU behind this PID gets nothing written.
        public const ushort Wf1Pid = 0x4F1E;

        private static readonly ushort[] PerKeyPids = { CybugPid, RalphPid, Wf1Pid };

        private const string VendorInterface = "MI_02";
        private const ushort VendorUsagePage = 0xFF13;
        private const int ReportLength = 65;
        private const byte ReportId = 0x00;
        private const int PayloadAt = 5;
        private const int PayloadCapacity = 60;
        private const int ColorPages = 3;

        private const byte AckHigh = 0xEC;
        private const byte AckOk = 0xAC;

        internal const byte CmdGetInfo = 0x80;
        internal const byte CmdGetEffect = 0x83;
        internal const byte CmdSetLightingOnOff = 0x09;
        internal const byte CmdSetKeyR = 0x05;
        internal const byte CmdSetKeyG = 0x06;
        internal const byte CmdSetKeyB = 0x07;
        internal const byte CmdStoreToFlash = 0x0A;
        internal const byte CmdRestoreDefault = 0x10;

        private readonly IntPtr _handle;
        private bool _disposed;

        public ushort ProductId { get; private init; }
        public string ProductName { get; private init; } = string.Empty;
        public string DevicePath { get; private init; } = string.Empty;
        public string FirmwareVersion { get; private set; } = "unknown";

        /// <summary>Real LEDs per keyboard (OGH's own key tables): 168 Cybug, 167 Ralph.</summary>
        public int LedCount => GetLedCount(ProductId);

        private PrimaxKeyboardMcu(IntPtr handle) => _handle = handle;

        /// <summary>168 Cybug, 167 Ralph; every slot for a PID whose LED count is not known, so no key stays dark.</summary>
        internal static int GetLedCount(ushort pid) => pid switch
        {
            CybugPid => 168,
            RalphPid => 167,
            _ => ColorPages * PayloadCapacity
        };

        /// <summary>
        /// The only frames this class will put on the wire. Everything else, including the flash
        /// store and the restore/firmware-update command, throws before a byte is written.
        /// </summary>
        internal static bool IsAllowedCommand(byte command, byte index, byte[] payload) => command switch
        {
            CmdGetInfo => index is 0x01 or 0x02,
            CmdGetEffect => index == 0x00,
            CmdSetLightingOnOff => index == 0x00 && payload.Length >= 1 && payload[0] is 0 or 1,
            CmdSetKeyR or CmdSetKeyG or CmdSetKeyB => index < ColorPages,
            _ => false
        };

        /// <summary>
        /// Build the three colour channels for a uniform colour: every real LED set, the padding
        /// slots past <paramref name="ledCount"/> left at zero as OGH leaves them.
        /// </summary>
        internal static (byte[] R, byte[] G, byte[] B) BuildUniformMap(int ledCount, byte r, byte g, byte b)
        {
            int slots = ColorPages * PayloadCapacity;
            var rc = new byte[slots];
            var gc = new byte[slots];
            var bc = new byte[slots];
            int n = Math.Clamp(ledCount, 0, slots);
            Array.Fill(rc, r, 0, n);
            Array.Fill(gc, g, 0, n);
            Array.Fill(bc, b, 0, n);
            return (rc, gc, bc);
        }

        internal static bool IsPrimaxPerKeyPath(string path) =>
            path.IndexOf($"VID_{PrimaxVid:X4}", StringComparison.OrdinalIgnoreCase) >= 0 &&
            path.IndexOf(VendorInterface, StringComparison.OrdinalIgnoreCase) >= 0 &&
            PerKeyPids.Any(pid => path.IndexOf($"PID_{pid:X4}", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Open the keyboard's vendor interface, or null when this machine has none.</summary>
        public static PrimaxKeyboardMcu? Open()
        {
            foreach (string path in EnumerateHidPaths())
            {
                if (!IsPrimaxPerKeyPath(path)) continue;
                var device = TryOpen(path);
                if (device == null) continue;

                // A device-info GET is the protocol handshake: a keyboard that does not echo it is
                // not this MCU, whatever its path says, and gets nothing else sent to it.
                if (device.TryReadDeviceInfo()) return device;
                device.Dispose();
            }
            return null;
        }

        /// <summary>Every Primax path seen, with why it was or was not taken - for diagnostics.</summary>
        public static IReadOnlyList<string> DescribeCandidates()
        {
            var lines = new List<string>();
            foreach (string path in EnumerateHidPaths())
            {
                if (path.IndexOf($"VID_{PrimaxVid:X4}", StringComparison.OrdinalIgnoreCase) < 0) continue;
                lines.Add($"{(IsPrimaxPerKeyPath(path) ? "candidate" : "not mi_02 / unknown PID")} : {path}");
            }
            return lines;
        }

        private bool TryReadDeviceInfo()
        {
            var reply = Exchange(CmdGetInfo, 0x01, Array.Empty<byte>());
            if (reply == null || reply[1] != CmdGetInfo || reply[2] != 0x01) return false;

            FirmwareVersion = $"{reply[PayloadAt]:X2}.{reply[PayloadAt + 1]:X2}.{reply[PayloadAt + 2]:X2}.{reply[PayloadAt + 3]:X2}";
            return true;
        }

        public bool SetLightingEnabled(bool enabled) =>
            WithDeviceMutex(() => SendSet(CmdSetLightingOnOff, 0, new byte[] { (byte)(enabled ? 1 : 0) }));

        /// <summary>
        /// Light every key one colour through the MCU's static map: lighting on, then R, G and B
        /// pages 0..2. Volatile - nothing is stored to flash.
        /// </summary>
        public bool SetStaticColor(byte r, byte g, byte b)
        {
            var (rc, gc, bc) = BuildUniformMap(LedCount, r, g, b);
            return WithDeviceMutex(() =>
                SendSet(CmdSetLightingOnOff, 0, new byte[] { 0x01 }) &&
                SendPages(CmdSetKeyR, rc) && SendPages(CmdSetKeyG, gc) && SendPages(CmdSetKeyB, bc));
        }

        private bool SendPages(byte command, byte[] channel)
        {
            for (byte page = 0; page < ColorPages; page++)
            {
                var payload = new byte[PayloadCapacity];
                Array.Copy(channel, page * PayloadCapacity, payload, 0, PayloadCapacity);
                if (!SendSet(command, page, payload, declaredLength: 0)) return false;
            }
            return true;
        }

        /// <summary>
        /// OGH serialises access to this keyboard with a named mutex per exchange. Taking the same
        /// one keeps a map write from interleaving with OGH's if both are running. If it cannot be
        /// had in time the write goes ahead anyway - a stuck OGH must not lock the user out.
        /// </summary>
        private bool WithDeviceMutex(Func<bool> action)
        {
            string name = $"omen_device_vid[{PrimaxVid:x4}]_pid[{ProductId:x4}]_interface_string[mi_02]";
            Mutex? mutex = null;
            bool owned = false;
            try
            {
                mutex = new Mutex(false, name);
                try { owned = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
                catch (AbandonedMutexException) { owned = true; }
                return action();
            }
            catch (UnauthorizedAccessException)
            {
                return action();
            }
            finally
            {
                if (owned) mutex!.ReleaseMutex();
                mutex?.Dispose();
            }
        }

        private bool SendSet(byte command, byte index, byte[] payload, int? declaredLength = null)
        {
            var reply = Exchange(command, index, payload, declaredLength);
            return reply != null && reply[PayloadAt] == AckHigh && reply[PayloadAt + 1] == AckOk;
        }

        private byte[]? Exchange(byte command, byte index, byte[] payload, int? declaredLength = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PrimaxKeyboardMcu));
            if (!IsAllowedCommand(command, index, payload))
                throw new InvalidOperationException($"Command 0x{command:X2}/{index} is not allowed on the Primax keyboard MCU.");
            if (payload.Length > PayloadCapacity)
                throw new ArgumentException($"payload is {payload.Length} bytes, max {PayloadCapacity}", nameof(payload));

            int bLength = declaredLength ?? payload.Length;
            var report = new byte[ReportLength];
            report[0] = ReportId;
            report[1] = command;
            report[2] = index;
            report[3] = (byte)(bLength & 0xFF);
            report[4] = (byte)(bLength >> 8);
            payload.CopyTo(report, PayloadAt);

            if (Transfer(report, write: true) <= 0) return null;
            var reply = new byte[ReportLength];
            return Transfer(reply, write: false) > PayloadAt + 1 ? reply : null;
        }

        private static PrimaxKeyboardMcu? TryOpen(string path)
        {
            IntPtr h = Native.CreateFileW(path, Native.GenericRead | Native.GenericWrite,
                                          Native.ShareReadWrite, IntPtr.Zero, Native.OpenExisting,
                                          Native.FileFlagOverlapped, IntPtr.Zero);
            if (h == Native.InvalidHandle) return null;

            try
            {
                var attrs = new Native.HiddAttributes { Size = Marshal.SizeOf<Native.HiddAttributes>() };
                if (!Native.HidD_GetAttributes(h, ref attrs) ||
                    attrs.VendorID != PrimaxVid ||
                    !PerKeyPids.Contains(attrs.ProductID) ||
                    !Native.HidD_GetPreparsedData(h, out IntPtr preparsed))
                {
                    Native.CloseHandle(h);
                    return null;
                }

                var caps = new Native.HidpCaps();
                try { Native.HidP_GetCaps(preparsed, ref caps); }
                finally { Native.HidD_FreePreparsedData(preparsed); }

                // Structural checks before any vendor byte is sent: the documented vendor usage
                // page and a 65-byte output report.
                if (caps.UsagePage != VendorUsagePage || caps.OutputReportByteLength != ReportLength)
                {
                    Native.CloseHandle(h);
                    return null;
                }

                string product = string.Empty;
                var nameBuffer = new byte[256];
                if (Native.HidD_GetProductString(h, nameBuffer, nameBuffer.Length))
                    product = Encoding.Unicode.GetString(nameBuffer).Split('\0')[0];

                return new PrimaxKeyboardMcu(h) { ProductId = attrs.ProductID, ProductName = product, DevicePath = path };
            }
            catch
            {
                Native.CloseHandle(h);
                return null;
            }
        }

        private int Transfer(byte[] buffer, bool write)
        {
            using var completed = new ManualResetEvent(false);
            var overlapped = new NativeOverlapped { EventHandle = completed.SafeWaitHandle.DangerousGetHandle() };

            bool started = write
                ? Native.WriteFile(_handle, buffer, (uint)buffer.Length, out uint transferred, ref overlapped)
                : Native.ReadFile(_handle, buffer, (uint)buffer.Length, out transferred, ref overlapped);

            if (!started)
            {
                if (Marshal.GetLastWin32Error() != Native.ErrorIoPending) return 0;
                if (!completed.WaitOne(IoTimeout))
                {
                    Native.CancelIo(_handle);
                    return 0;
                }
                if (!Native.GetOverlappedResult(_handle, ref overlapped, out transferred, true)) return 0;
            }
            return (int)transferred;
        }

        private static readonly TimeSpan IoTimeout = TimeSpan.FromMilliseconds(1500);

        private static IEnumerable<string> EnumerateHidPaths()
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'HID%'");

            foreach (System.Management.ManagementObject o in searcher.Get())
            {
                string? id = o["PNPDeviceID"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;
                yield return $@"\\?\{id.Replace('\\', '#')}#{{4d1e55b2-f16f-11cf-88cb-001111000030}}";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero && _handle != Native.InvalidHandle) Native.CloseHandle(_handle);
        }

        private static class Native
        {
            internal const uint GenericRead = 0x80000000;
            internal const uint GenericWrite = 0x40000000;
            internal const uint ShareReadWrite = 0x03;
            internal const uint OpenExisting = 3;
            internal const uint FileFlagOverlapped = 0x40000000;
            internal const int ErrorIoPending = 997;
            internal static readonly IntPtr InvalidHandle = new(-1);

            [StructLayout(LayoutKind.Sequential)]
            internal struct HiddAttributes { public int Size; public ushort VendorID, ProductID, VersionNumber; }

            [StructLayout(LayoutKind.Sequential)]
            internal struct HidpCaps
            {
                public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
                public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps,
                              NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps,
                              NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps,
                              NumberFeatureDataIndices;
            }

            [DllImport("hid.dll")] internal static extern bool HidD_GetAttributes(IntPtr h, ref HiddAttributes a);
            [DllImport("hid.dll")] internal static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr p);
            [DllImport("hid.dll")] internal static extern bool HidD_FreePreparsedData(IntPtr p);
            [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr p, ref HidpCaps c);
            [DllImport("hid.dll")] internal static extern bool HidD_GetProductString(IntPtr h, byte[] b, int len);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr sa,
                                                      uint disposition, uint flags, IntPtr template);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool WriteFile(IntPtr h, byte[] b, uint len, out uint written, ref NativeOverlapped o);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool ReadFile(IntPtr h, byte[] b, uint len, out uint read, ref NativeOverlapped o);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped o, out uint transferred, bool wait);
            [DllImport("kernel32.dll")] internal static extern bool CancelIo(IntPtr h);
            [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
        }
    }
}
