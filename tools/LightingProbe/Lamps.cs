// Reads the HID LampArray descriptors off every attached lighting device.
//
// HID usage page 0x59 ("Lighting And Illumination") is the standardised per-key RGB surface -
// the same one Windows Dynamic Lighting drives. A keyboard that implements it needs no protocol
// reverse engineering at all, which is a very different starting point from guessing at a
// vendor command set.
//
// READ ONLY. Feature reports 1 and 3 are read; report 2 is a request that must be written
// before 3 can be read, and it selects which lamp to describe - it changes no lighting state.
// Reports 4, 5 and 6 (the ones that actually set colour) are not touched.

using System.Runtime.InteropServices;

namespace OmenCore.Tools.LightingProbe;

internal static class Lamps
{
    // HID Lighting And Illumination report ids.
    private const byte ReportLampArrayAttributes = 1;
    private const byte ReportLampAttributesRequest = 2;
    private const byte ReportLampAttributesResponse = 3;

    private const ushort LightingUsagePage = 0x59;
    private const ushort LampArrayUsage = 0x01;

    internal static int Run()
    {
        Console.WriteLine("=== HID LampArray ===\n");

        var found = 0;
        foreach (string path in Native.EnumerateHidPaths())
        {
            using var dev = HidDevice.TryOpen(path, forWrite: true);
            if (dev == null) continue;
            if (dev.UsagePage != LightingUsagePage || dev.Usage != LampArrayUsage) continue;

            found++;
            Console.WriteLine($"  {dev.Product}  (VID 0x{dev.VendorId:X4} PID 0x{dev.ProductId:X4})");
            Console.WriteLine($"    path            : {path}");
            Console.WriteLine($"    feature report  : {dev.FeatureReportLength} bytes");

            var attrs = dev.ReadArrayAttributes();
            if (attrs == null)
            {
                Console.WriteLine("    attributes      : unreadable\n");
                continue;
            }

            Console.WriteLine($"    LampCount       : {attrs.Value.LampCount}");
            Console.WriteLine($"    Kind            : {KindName(attrs.Value.Kind)} ({attrs.Value.Kind})");
            Console.WriteLine($"    BoundingBox     : {attrs.Value.WidthUm / 1000.0:F0} x " +
                              $"{attrs.Value.HeightUm / 1000.0:F0} x {attrs.Value.DepthUm / 1000.0:F0} mm");
            Console.WriteLine($"    MinUpdateInterval: {attrs.Value.MinUpdateIntervalUs / 1000.0:F0} ms");

            // Sample a few lamps. Note the returned LampId is printed rather than assumed: on the
            // board this was written against, the response ignored the requested id and free-ran,
            // so pairing a response with a request without checking would mislabel every lamp.
            Console.WriteLine("    sample lamps (id is what the DEVICE reported, not what was asked):");
            int sampleCount = Math.Min(4, (int)attrs.Value.LampCount);
            for (int i = 0; i < sampleCount; i++)
            {
                var lamp = dev.ReadLampAttributes((ushort)i);
                if (lamp == null) { Console.WriteLine($"      request {i}: unreadable"); continue; }

                var l = lamp.Value;
                Console.WriteLine($"      request {i,3} -> id {l.LampId,3}  pos ({l.XUm / 1000.0:F0},{l.YUm / 1000.0:F0},{l.ZUm / 1000.0:F0}) mm  " +
                                  $"levels r={l.Red} g={l.Green} b={l.Blue} i={l.Intensity}  " +
                                  $"programmable={l.IsProgrammable}  key usage 0x{l.InputBinding:X2}");
            }

            Console.WriteLine();
        }

        if (found == 0)
        {
            Console.WriteLine("  No HID LampArray found.");
            Console.WriteLine("  Either nothing here implements usage page 0x59, or the device is held");
            Console.WriteLine("  exclusively by another process.\n");
            return 0;
        }

        Console.WriteLine("  Writing colour is NOT attempted here, and that is a decision rather than an");
        Console.WriteLine("  omission. Windows Dynamic Lighting takes ownership of LampArray devices when it");
        Console.WriteLine("  is enabled, and it refreshes them continuously - so a write can land and be");
        Console.WriteLine("  overwritten before anyone sees it. There is also no colour readback anywhere in");
        Console.WriteLine("  the LampArray spec, so 'did that key turn red' has no software answer: only a");
        Console.WriteLine("  person looking at the keyboard can confirm it.");
        return 0;
    }

    private static string KindName(uint kind) => kind switch
    {
        1 => "Keyboard", 2 => "Mouse", 3 => "GameController", 4 => "Peripheral",
        5 => "Scene", 6 => "Notification", 7 => "Chassis", 8 => "Wearable",
        9 => "Furniture", 10 => "Art", _ => "unknown"
    };

    internal readonly record struct ArrayAttributes(
        ushort LampCount, uint WidthUm, uint HeightUm, uint DepthUm, uint Kind, uint MinUpdateIntervalUs);

    internal readonly record struct LampAttributes(
        ushort LampId, uint XUm, uint YUm, uint ZUm, uint LatencyUs, uint Purposes,
        byte Red, byte Green, byte Blue, byte Intensity, bool IsProgrammable, byte InputBinding);

    private sealed class HidDevice : IDisposable
    {
        private readonly IntPtr _handle;
        internal ushort VendorId, ProductId, UsagePage, Usage, FeatureReportLength;
        internal string Product = string.Empty;

        private HidDevice(IntPtr handle) => _handle = handle;

        internal static HidDevice? TryOpen(string path, bool forWrite)
        {
            // Feature reports need write access only for the lamp-attributes REQUEST, which
            // selects a lamp to describe. Fall back to read-only if the device is busy.
            IntPtr h = Native.CreateFileW(path, forWrite ? Native.GenericReadWrite : Native.GenericRead,
                                          Native.ShareReadWrite, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
            if (h == Native.InvalidHandle && forWrite)
                h = Native.CreateFileW(path, Native.GenericRead, Native.ShareReadWrite,
                                       IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
            if (h == Native.InvalidHandle) return null;

            var dev = new HidDevice(h);
            var attr = new Native.HiddAttributes { Size = Marshal.SizeOf<Native.HiddAttributes>() };
            if (!Native.HidD_GetAttributes(h, ref attr)) { dev.Dispose(); return null; }
            dev.VendorId = attr.VendorID;
            dev.ProductId = attr.ProductID;

            if (!Native.HidD_GetPreparsedData(h, out IntPtr pp)) { dev.Dispose(); return null; }
            try
            {
                var caps = new Native.HidpCaps();
                Native.HidP_GetCaps(pp, ref caps);
                dev.UsagePage = caps.UsagePage;
                dev.Usage = caps.Usage;
                dev.FeatureReportLength = caps.FeatureReportByteLength;
            }
            finally { Native.HidD_FreePreparsedData(pp); }

            var buf = new byte[256];
            if (Native.HidD_GetProductString(h, buf, buf.Length))
                dev.Product = System.Text.Encoding.Unicode.GetString(buf).Split('\0')[0];

            return dev;
        }

        internal ArrayAttributes? ReadArrayAttributes()
        {
            int len = Math.Max((int)FeatureReportLength, 32);
            var b = new byte[len];
            b[0] = ReportLampArrayAttributes;
            if (!Native.HidD_GetFeature(_handle, b, len)) return null;

            return new ArrayAttributes(
                BitConverter.ToUInt16(b, 1),
                BitConverter.ToUInt32(b, 3),
                BitConverter.ToUInt32(b, 7),
                BitConverter.ToUInt32(b, 11),
                BitConverter.ToUInt32(b, 15),
                BitConverter.ToUInt32(b, 19));
        }

        internal LampAttributes? ReadLampAttributes(ushort lampId)
        {
            int len = Math.Max((int)FeatureReportLength, 32);

            var req = new byte[len];
            req[0] = ReportLampAttributesRequest;
            req[1] = (byte)(lampId & 0xFF);
            req[2] = (byte)(lampId >> 8);
            if (!Native.HidD_SetFeature(_handle, req, len)) return null;

            var b = new byte[len];
            b[0] = ReportLampAttributesResponse;
            if (!Native.HidD_GetFeature(_handle, b, len)) return null;

            return new LampAttributes(
                BitConverter.ToUInt16(b, 1),
                BitConverter.ToUInt32(b, 3),
                BitConverter.ToUInt32(b, 7),
                BitConverter.ToUInt32(b, 11),
                BitConverter.ToUInt32(b, 15),
                BitConverter.ToUInt32(b, 19),
                b[23], b[24], b[25], b[26], b[27] != 0, b[28]);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero && _handle != Native.InvalidHandle) Native.CloseHandle(_handle);
        }
    }

    private static class Native
    {
        internal const uint GenericRead = 0x80000000;
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
        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        internal static extern bool HidD_GetProductString(IntPtr h, byte[] buf, int len);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr sa,
                                                  uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);

        /// <summary>
        /// HID interface paths, built from the PnP device ids rather than SetupAPI - it is far
        /// less P/Invoke for the same result, and this is a diagnostic tool.
        /// </summary>
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
