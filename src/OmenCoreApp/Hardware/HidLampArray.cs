using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Per-key RGB over the standardised HID LampArray surface (usage page 0x59, "Lighting And
    /// Illumination") - the same protocol Windows Dynamic Lighting speaks.
    ///
    /// This is worth stating plainly because it changes what per-key RGB costs: on boards whose
    /// keyboard implements this, there is no vendor command set to reverse engineer. Board 8D87
    /// (OMEN MAX 16, 2025 AMD) reports 120 addressable lamps of kind Keyboard across 342 x 125 mm,
    /// 8-bit per channel, each carrying the HID usage of the key it sits under.
    ///
    /// TWO THINGS THIS CANNOT DO, both of which matter before trusting it:
    ///
    /// 1. It cannot confirm its own work. The LampArray spec has no colour readback anywhere, so
    ///    "did that key turn red" has no software answer - only a person looking at the keyboard.
    ///    Every method here reports whether the WRITE was accepted, which is not the same claim.
    ///
    /// 2. It does not own the device. Windows Dynamic Lighting takes LampArray devices when it is
    ///    enabled and refreshes them continuously, so a colour written here can be overwritten
    ///    within one refresh interval (33 ms on 8D87). <see cref="SetAutonomousMode"/> asks the
    ///    device to stop running its own effects; it does not ask Windows to stop running its.
    ///    Arbitrating with Dynamic Lighting is a policy decision, not a transport one.
    /// </summary>
    public sealed class HidLampArray : IDisposable
    {
        // Report ids from the HID Lighting And Illumination page.
        private const byte ReportArrayAttributes = 1;
        private const byte ReportLampAttributesRequest = 2;
        private const byte ReportLampAttributesResponse = 3;
        private const byte ReportMultiUpdate = 4;
        private const byte ReportRangeUpdate = 5;
        private const byte ReportControl = 6;

        /// <summary>LampMultiUpdateReport carries at most this many lamps per transaction.</summary>
        public const int MaxLampsPerUpdate = 8;

        /// <summary>Shortest feature report whose attribute fields this class can parse.</summary>
        private const int MinReadableReportLength = 29;

        /// <summary>
        /// Bytes a LampMultiUpdateReport needs: report id, lamp count, flags, eight u16 ids, then
        /// eight RGBI quads. The id and channel arrays are sized for eight lamps whether or not
        /// all eight are used, so a shorter report cannot carry this message at all.
        /// </summary>
        private const int MultiUpdateReportLength = 3 + (MaxLampsPerUpdate * 2) + (MaxLampsPerUpdate * 4);

        private const ushort LightingUsagePage = 0x59;
        private const ushort LampArrayUsage = 0x01;

        private readonly IntPtr _handle;
        private readonly int _reportLength;
        private bool _disposed;

        /// <summary>Number of addressable lamps.</summary>
        public ushort LampCount { get; private init; }

        /// <summary>Device kind. 1 is Keyboard.</summary>
        public uint Kind { get; private init; }

        /// <summary>Shortest interval the device will accept updates at.</summary>
        public TimeSpan MinUpdateInterval { get; private init; }

        public ushort VendorId { get; private init; }
        public ushort ProductId { get; private init; }
        public string DevicePath { get; private init; } = string.Empty;

        /// <summary>Physical extent of the whole array, in micrometres.</summary>
        public uint BoundingBoxWidthUm { get; private init; }
        public uint BoundingBoxHeightUm { get; private init; }
        public uint BoundingBoxDepthUm { get; private init; }

        private HidLampArray(IntPtr handle, int reportLength)
        {
            _handle = handle;
            _reportLength = reportLength;
        }

        /// <summary>
        /// Open every HID LampArray on the system. Callers pick by <see cref="Kind"/> and
        /// <see cref="LampCount"/> - a machine can expose more than one, and on 8D87 the
        /// four-zone backlight appears as a separate virtual "Scene" array alongside the keyboard.
        /// </summary>
        public static IReadOnlyList<HidLampArray> OpenAll()
        {
            var found = new List<HidLampArray>();

            foreach (string path in Native.EnumerateHidPaths())
            {
                var device = TryOpen(path);
                if (device != null) found.Add(device);
            }

            return found;
        }

        /// <summary>Device kind 1, Keyboard.</summary>
        public const uint KindKeyboard = 1;

        /// <summary>Device kind 5, Scene. What the light bar enumerates as.</summary>
        public const uint KindScene = 5;

        /// <summary>
        /// Open the light bar, which enumerates as a Scene-kind LampArray of its own - on 8D87
        /// "HID VHF Driver" 0461:0001, four lamps across a 323 x 5 mm strip, ids 0..3 left to right.
        ///
        /// THIS IS THE PATH THAT DOES NOT NEED ADMINISTRATOR. HP's WMI BIOS surface, which is the
        /// only way to reach the bar's brightness and its nine built-in animations, requires
        /// elevation because invoking methods in root\wmi does. HID feature reports do not: Windows
        /// Dynamic Lighting is an unelevated user-mode component that drives exactly these devices,
        /// which is the existence proof.
        ///
        /// What is given up by coming in this way is real - colour only, no brightness, no
        /// animations, and no readback - so this is a fallback with a cost, not a free win.
        /// </summary>
        /// <param name="board">
        /// Baseboard product id to gate on, or null to read the current machine's. Only boards where
        /// this has been confirmed on hardware are served - see <see cref="OmenBoard.SupportsHidLightBar"/>
        /// for why that is an allowlist and not a probe.
        /// </param>
        public static HidLampArray? OpenLightBar(string? board = null)
        {
            if (!OmenBoard.SupportsHidLightBar(board)) return null;

            HidLampArray? bar = null;

            foreach (var candidate in OpenAll())
            {
                if (bar == null && LooksLikeALightBar(candidate))
                    bar = candidate;
                else
                    candidate.Dispose();
            }

            return bar;
        }

        /// <summary>
        /// Whether a LampArray is shaped like a light bar.
        ///
        /// Kind and lamp count alone are NOT enough, and this is the whole safety property of
        /// <see cref="OpenLightBar"/>: "Scene, four lamps" would also match a dock, an external
        /// strip, a mousepad, or a fan hub - and matching one of those means writing colour to a
        /// device the user never asked about. The geometry is what actually identifies a bar. On
        /// 8D87 it measures 323 x 5 mm, a ratio of 65:1, which nothing else plausibly is.
        ///
        /// Only ever consulted on hardware this code has not seen, so it is deliberately a shape
        /// test rather than a VID:PID allowlist - an allowlist of one entry would be a claim about
        /// every other OMEN, made from a sample of one machine.
        /// </summary>
        private static bool LooksLikeALightBar(HidLampArray a)
        {
            const uint MinWidthUm = 100_000;  // 100 mm - a bar spans the chassis
            const uint MaxHeightUm = 20_000;  //  20 mm - and is a strip, not a panel
            const uint MinAspect = 10;

            return a.Kind == KindScene
                && a.LampCount is >= 2 and <= 8
                && a.BoundingBoxWidthUm >= MinWidthUm
                && a.BoundingBoxHeightUm is > 0 and <= MaxHeightUm
                && a.BoundingBoxWidthUm / a.BoundingBoxHeightUm >= MinAspect;
        }

        private static HidLampArray? TryOpen(string path)
        {
            // Identify before asking for anything. This runs over EVERY HID device on the machine,
            // most of which are mice, keyboards and touchpads that have nothing to do with
            // lighting, so requesting read/write up front would be asking for write access to the
            // user's input devices just to read a usage page off them. A zero-access handle is
            // enough for the descriptor queries and cannot write by construction.
            IntPtr probe = Native.CreateFileW(path, 0, Native.ShareReadWrite, IntPtr.Zero,
                                              Native.OpenExisting, 0, IntPtr.Zero);
            if (probe == Native.InvalidHandle) return null;

            ushort usagePage, usage, featureLength;
            try
            {
                if (!Native.HidD_GetPreparsedData(probe, out IntPtr preparsed)) return null;
                try
                {
                    var caps = new Native.HidpCaps();
                    Native.HidP_GetCaps(preparsed, ref caps);
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    featureLength = caps.FeatureReportByteLength;
                }
                finally { Native.HidD_FreePreparsedData(preparsed); }
            }
            finally { Native.CloseHandle(probe); }

            // The attributes reports this class reads reach byte 28, so anything shorter than
            // that cannot be parsed without reading past the buffer. A real LampArray is at
            // least 51 bytes - that is LampMultiUpdateReport's size - but the check is against
            // what is actually indexed rather than what is expected.
            if (usagePage != LightingUsagePage || usage != LampArrayUsage ||
                featureLength < MinReadableReportLength)
            {
                return null;
            }

            // Only now, and only for a device that really is a LampArray.
            IntPtr h = Native.CreateFileW(path, Native.GenericReadWrite, Native.ShareReadWrite,
                                          IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
            if (h == Native.InvalidHandle) return null;

            try
            {
                var attrs = new Native.HiddAttributes { Size = Marshal.SizeOf<Native.HiddAttributes>() };
                Native.HidD_GetAttributes(h, ref attrs);

                var buffer = new byte[featureLength];
                buffer[0] = ReportArrayAttributes;
                if (!Native.HidD_GetFeature(h, buffer, buffer.Length))
                {
                    Native.CloseHandle(h);
                    return null;
                }

                return new HidLampArray(h, featureLength)
                {
                    LampCount = BitConverter.ToUInt16(buffer, 1),
                    BoundingBoxWidthUm = BitConverter.ToUInt32(buffer, 3),
                    BoundingBoxHeightUm = BitConverter.ToUInt32(buffer, 7),
                    BoundingBoxDepthUm = BitConverter.ToUInt32(buffer, 11),
                    Kind = BitConverter.ToUInt32(buffer, 15),
                    MinUpdateInterval = TimeSpan.FromMilliseconds(BitConverter.ToUInt32(buffer, 19) / 1000.0),
                    VendorId = attrs.VendorID,
                    ProductId = attrs.ProductID,
                    DevicePath = path
                };
            }
            catch
            {
                Native.CloseHandle(h);
                return null;
            }
        }

        /// <summary>
        /// Read one lamp's attributes: where it is, and which key it sits under.
        ///
        /// The returned LampId is included deliberately rather than assumed to match
        /// <paramref name="lampId"/>. On board 8D87's keyboard the response ignores the request
        /// and free-runs - asking for 0, 1, 2, 3 returns ids 41, 42, 43, 44 - while the virtual
        /// four-zone array on the same machine pairs correctly. Callers that need a specific
        /// lamp must check the id they got back.
        /// </summary>
        public LampInfo? GetLampInfo(ushort lampId)
        {
            ThrowIfDisposed();

            var request = new byte[_reportLength];
            request[0] = ReportLampAttributesRequest;
            request[1] = (byte)(lampId & 0xFF);
            request[2] = (byte)(lampId >> 8);
            if (!Native.HidD_SetFeature(_handle, request, request.Length)) return null;

            var response = new byte[_reportLength];
            response[0] = ReportLampAttributesResponse;
            if (!Native.HidD_GetFeature(_handle, response, response.Length)) return null;

            return new LampInfo(
                BitConverter.ToUInt16(response, 1),
                BitConverter.ToUInt32(response, 3),
                BitConverter.ToUInt32(response, 7),
                BitConverter.ToUInt32(response, 11),
                response[28]);
        }

        /// <summary>
        /// Take the lamps away from the device's own effect engine, or hand them back.
        ///
        /// Ask for host control before writing colours: a device still running its own animation
        /// will overwrite them. This says nothing about Windows Dynamic Lighting, which is a
        /// separate owner - see the remarks on the class.
        /// </summary>
        public bool SetAutonomousMode(bool autonomous)
        {
            ThrowIfDisposed();

            var report = new byte[_reportLength];
            report[0] = ReportControl;
            report[1] = (byte)(autonomous ? 1 : 0);
            return Native.HidD_SetFeature(_handle, report, report.Length);
        }

        /// <summary>
        /// Read the current autonomous-mode setting back, or null if the device will not answer.
        ///
        /// The HID spec does not require the control report to be readable, so null is a normal
        /// answer rather than a failure. Where it IS readable it is the only way to tell an
        /// accepted control report from an applied one - a distinction that matters here, because
        /// <see cref="SetAutonomousMode"/> returning true means the device took the message, not
        /// that it acted on it.
        /// </summary>
        public bool? TryReadAutonomousMode()
        {
            ThrowIfDisposed();

            var report = new byte[_reportLength];
            report[0] = ReportControl;
            return Native.HidD_GetFeature(_handle, report, report.Length)
                ? report[1] != 0
                : null;
        }

        /// <summary>
        /// Set a contiguous run of lamps to one colour, inclusive of both ends.
        /// Cheaper than <see cref="SetLamps"/> when the colour is uniform - one transaction
        /// instead of one per eight lamps.
        /// </summary>
        public bool SetRange(ushort firstLampId, ushort lastLampId, byte r, byte g, byte b, byte intensity = 255)
        {
            ThrowIfDisposed();

            var report = BuildRangeUpdateReport(_reportLength, firstLampId, lastLampId, r, g, b, intensity);
            return Native.HidD_SetFeature(_handle, report, report.Length);
        }

        /// <summary>Set every lamp to one colour.</summary>
        public bool SetAll(byte r, byte g, byte b, byte intensity = 255) =>
            LampCount > 0 && SetRange(0, (ushort)(LampCount - 1), r, g, b, intensity);

        /// <summary>
        /// Set individually coloured lamps. Sent in batches of <see cref="MaxLampsPerUpdate"/>,
        /// which is all one LampMultiUpdateReport holds.
        ///
        /// Only the final batch is flagged complete, so the device applies the whole set at once
        /// rather than tearing partway through a frame.
        ///
        /// Returns true when every batch was accepted. That means accepted, not visible - this
        /// class cannot see the keyboard.
        /// </summary>
        public bool SetLamps(IReadOnlyList<LampColor> lamps)
        {
            ThrowIfDisposed();
            if (lamps == null) throw new ArgumentNullException(nameof(lamps));
            if (lamps.Count == 0) return true;

            // A device whose feature reports are shorter than this cannot carry a multi-update.
            // Refusing is the honest answer - writing a truncated one would either overrun the
            // buffer or send a malformed report the device might partly act on.
            if (_reportLength < MultiUpdateReportLength) return false;

            bool allAccepted = true;

            for (int offset = 0; offset < lamps.Count; offset += MaxLampsPerUpdate)
            {
                int count = Math.Min(MaxLampsPerUpdate, lamps.Count - offset);
                bool isLastBatch = offset + count >= lamps.Count;

                var report = BuildMultiUpdateReport(_reportLength, lamps, offset, count, isLastBatch);
                if (!Native.HidD_SetFeature(_handle, report, report.Length))
                {
                    allAccepted = false;
                }
            }

            return allAccepted;
        }

        /// <summary>
        /// Lay out one LampMultiUpdateReport. Separated from the write so the byte offsets can be
        /// tested: a wrong offset here does not fail loudly, it lights the wrong key.
        /// </summary>
        internal static byte[] BuildMultiUpdateReport(
            int reportLength, IReadOnlyList<LampColor> lamps, int offset, int count, bool isLastBatch)
        {
            var report = new byte[reportLength];
            report[0] = ReportMultiUpdate;
            report[1] = (byte)count;
            report[2] = (byte)(isLastBatch ? 0x01 : 0x00); // LampUpdateComplete

            for (int i = 0; i < count; i++)
            {
                var lamp = lamps[offset + i];

                // Ids are a packed u16 array at byte 3; channels are a packed RGBI array starting
                // after ALL EIGHT id slots, whether or not they are all used. Packing the channels
                // immediately after the ids in use would misalign every lamp in a partial batch.
                int idAt = 3 + (i * 2);
                report[idAt] = (byte)(lamp.LampId & 0xFF);
                report[idAt + 1] = (byte)(lamp.LampId >> 8);

                int channelAt = 3 + (MaxLampsPerUpdate * 2) + (i * 4);
                report[channelAt] = lamp.R;
                report[channelAt + 1] = lamp.G;
                report[channelAt + 2] = lamp.B;
                report[channelAt + 3] = lamp.Intensity;
            }

            return report;
        }

        /// <summary>
        /// Lay out one LampRangeUpdateReport. Separated for the same reason as
        /// <see cref="BuildMultiUpdateReport"/>.
        /// </summary>
        internal static byte[] BuildRangeUpdateReport(
            int reportLength, ushort firstLampId, ushort lastLampId, byte r, byte g, byte b, byte intensity)
        {
            var report = new byte[reportLength];
            report[0] = ReportRangeUpdate;
            report[1] = 0x01; // LampUpdateComplete - apply on receipt
            report[2] = (byte)(firstLampId & 0xFF);
            report[3] = (byte)(firstLampId >> 8);
            report[4] = (byte)(lastLampId & 0xFF);
            report[5] = (byte)(lastLampId >> 8);
            report[6] = r;
            report[7] = g;
            report[8] = b;
            report[9] = intensity;
            return report;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HidLampArray));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero && _handle != Native.InvalidHandle) Native.CloseHandle(_handle);
        }

        /// <summary>One lamp's physical placement and the key it lights.</summary>
        public readonly record struct LampInfo(ushort LampId, uint XMicrometres, uint YMicrometres,
                                               uint ZMicrometres, byte KeyUsage);

        /// <summary>A colour destined for one lamp.</summary>
        public readonly record struct LampColor(ushort LampId, byte R, byte G, byte B, byte Intensity = 255);

        private static class Native
        {
            internal const uint GenericReadWrite = 0xC0000000;
            internal const uint ShareReadWrite = 0x03;
            internal const uint OpenExisting = 3;
            internal static readonly IntPtr InvalidHandle = new(-1);

            private static readonly Guid HidInterfaceGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

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
            [DllImport("hid.dll")] internal static extern bool HidD_GetFeature(IntPtr h, byte[] b, int len);
            [DllImport("hid.dll")] internal static extern bool HidD_SetFeature(IntPtr h, byte[] b, int len);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr sa,
                                                      uint disposition, uint flags, IntPtr template);
            [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);

            internal static IEnumerable<string> EnumerateHidPaths()
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'HID%'");

                foreach (System.Management.ManagementObject o in searcher.Get())
                {
                    string? id = o["PNPDeviceID"]?.ToString();
                    if (string.IsNullOrEmpty(id)) continue;
                    yield return $@"\\?\{id.Replace('\\', '#')}#{{{HidInterfaceGuid}}}";
                }
            }
        }
    }
}
