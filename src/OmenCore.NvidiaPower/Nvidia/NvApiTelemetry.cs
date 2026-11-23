using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NvpwrControlBlackwell
{
    internal static class NvApiTelemetry
    {
        private const uint ID_GET_FULL_NAME = 0xCEEE8E9F;
        private const uint ID_GET_VBIOS_STRING = 0xA561FD7D;
        private const uint ID_GET_PCI_IDENTIFIERS = 0x2DDFB66E;
        private const uint ID_GET_CURRENT_VOLTAGE = 0x465F9BCF;
        private const uint ID_QUERY_THERMAL_SENSORS = 0x65FE3AAD;

        private const int VOLTAGE_SIZE = 0x4C;
        private const uint VOLTAGE_VERSION = 0x0001004C;

        // NV_GPU_THERMAL_EX V2:
        // version + mask + 8 reserved DWORDs + 32 Q8.8 temperature sensors.
        private const int THERMAL_EX_SIZE = 0xA8;
        private const uint THERMAL_EX_VERSION = 0x000200A8;
        private const int THERMAL_SENSOR_BASE = 0x28;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int GpuStringDelegate(
            IntPtr gpu,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PciIdentifiersDelegate(
            IntPtr gpu,
            ref uint deviceId,
            ref uint subsystemId,
            ref uint revisionId,
            ref uint extDeviceId);

        private static void PutU32(byte[] b, int off, uint v)
        {
            Array.Copy(BitConverter.GetBytes(v), 0, b, off, 4);
        }

        private static int CallGpuBuffer(
            NvApiSession.GpuBufferDelegate fn,
            IntPtr gpu,
            byte[] data)
        {
            IntPtr p = Marshal.AllocHGlobal(data.Length);

            try
            {
                Marshal.Copy(data, 0, p, data.Length);
                int rc = fn(gpu, p);
                Marshal.Copy(p, data, 0, data.Length);
                return rc;
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        public static bool TryGetIdentity(
            out string name,
            out string vbios,
            out uint deviceId,
            out uint subsystemId,
            out string error)
        {
            name = "";
            vbios = "";
            deviceId = 0;
            subsystemId = 0;
            error = "";

            try
            {
                using (NvApiSession nv = new NvApiSession())
                {
                    string e;
                    if (!nv.Open(out e))
                    {
                        error = e;
                        return false;
                    }

                    IntPtr fullNamePtr = nv.QI(ID_GET_FULL_NAME);
                    IntPtr vbiosPtr = nv.QI(ID_GET_VBIOS_STRING);
                    IntPtr pciPtr = nv.QI(ID_GET_PCI_IDENTIFIERS);

                    if (fullNamePtr != IntPtr.Zero)
                    {
                        GpuStringDelegate fn =
                            (GpuStringDelegate)Marshal.GetDelegateForFunctionPointer(
                                fullNamePtr,
                                typeof(GpuStringDelegate));

                        StringBuilder sb = new StringBuilder(64);
                        if (fn(nv.Gpu, sb) == 0)
                            name = sb.ToString().Trim();
                    }

                    if (vbiosPtr != IntPtr.Zero)
                    {
                        GpuStringDelegate fn =
                            (GpuStringDelegate)Marshal.GetDelegateForFunctionPointer(
                                vbiosPtr,
                                typeof(GpuStringDelegate));

                        StringBuilder sb = new StringBuilder(64);
                        if (fn(nv.Gpu, sb) == 0)
                            vbios = sb.ToString().Trim();
                    }

                    if (pciPtr != IntPtr.Zero)
                    {
                        PciIdentifiersDelegate fn =
                            (PciIdentifiersDelegate)Marshal.GetDelegateForFunctionPointer(
                                pciPtr,
                                typeof(PciIdentifiersDelegate));

                        uint rev = 0;
                        uint ext = 0;
                        fn(nv.Gpu, ref deviceId, ref subsystemId, ref rev, ref ext);
                    }

                    return !String.IsNullOrEmpty(name) ||
                           !String.IsNullOrEmpty(vbios);
                }
            }
            catch (Exception ex)
            {
                error = SafeError.Describe(ex);
                return false;
            }
        }

        public static void Fill(TelemetryState t)
        {
            if (t == null) return;

            try
            {
                using (NvApiSession nv = new NvApiSession())
                {
                    string error;
                    if (!nv.Open(out error))
                        return;

                    TryVoltage(nv, t);
                    TryExtendedThermals(nv, t);
                }
            }
            catch
            {
                // Telemetry is opportunistic. One unavailable private NVAPI
                // interface must never blank the rest of the monitoring page.
            }
        }

        private static void TryVoltage(
            NvApiSession nv,
            TelemetryState t)
        {
            NvApiSession.GpuBufferDelegate fn =
                nv.GetGpuBuffer(ID_GET_CURRENT_VOLTAGE);

            if (fn == null)
                return;

            byte[] b = new byte[VOLTAGE_SIZE];
            PutU32(b, 0, VOLTAGE_VERSION);

            if (CallGpuBuffer(fn, nv.Gpu, b) != 0)
                return;

            uint uv = BitConverter.ToUInt32(b, 0x28);

            if (uv >= 200000 && uv <= 2000000)
                t.VoltageV = uv / 1000000.0;
        }

        private static void TryExtendedThermals(
            NvApiSession nv,
            TelemetryState t)
        {
            NvApiSession.GpuBufferDelegate fn =
                nv.GetGpuBuffer(ID_QUERY_THERMAL_SENSORS);

            if (fn == null)
                return;

            List<double> values = new List<double>();

            for (int count = 32; count >= 2; count--)
            {
                byte[] b = new byte[THERMAL_EX_SIZE];
                PutU32(b, 0, THERMAL_EX_VERSION);

                uint mask;
                if (count >= 32)
                    mask = 0xFFFFFFFFu;
                else
                    mask = (1u << count) - 1u;

                PutU32(b, 4, mask);

                if (CallGpuBuffer(fn, nv.Gpu, b) != 0)
                    continue;

                values.Clear();

                for (int i = 0; i < 32; i++)
                {
                    uint raw = BitConverter.ToUInt32(
                        b,
                        THERMAL_SENSOR_BASE + i * 4);

                    if (raw == 0)
                        continue;

                    double c = raw / 256.0;

                    if (c >= 0.0 && c <= 150.0)
                        values.Add(c);
                }

                if (values.Count != 0)
                    break;
            }

            if (values.Count == 0)
                return;

            // QueryThermalSensors returns internal die sensor readings without
            // public labels. We identify the hotspot conservatively as the
            // hottest plausible die sensor, excluding readings that match the
            // independently reported memory-temperature sensor.
            double hottest = Double.MinValue;

            foreach (double c in values)
            {
                if (t.MemoryTempC.HasValue &&
                    Math.Abs(c - t.MemoryTempC.Value) < 0.75)
                    continue;

                if (t.GpuTempC.HasValue &&
                    c + 0.25 < t.GpuTempC.Value)
                    continue;

                if (c > hottest)
                    hottest = c;
            }

            if (hottest != Double.MinValue)
            {
                if (!t.GpuTempC.HasValue ||
                    hottest >= t.GpuTempC.Value)
                    t.HotspotTempC = hottest;
            }
        }
    }
}
