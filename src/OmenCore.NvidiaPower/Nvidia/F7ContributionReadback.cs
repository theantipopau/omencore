using System;
using System.Runtime.InteropServices;

namespace NvpwrControlBlackwell
{
    /// <summary>
    /// Reads the driver's own source request for the F7 power client (the value an E633 CURRENT write authors), from
    /// the legacy board status buffer (NVAPI interface 0x31B7A4CD, version 0x106E0). Ported from Prophecy Power Unlocker
    /// 1.2.9 (bobshmo), used with permission. The public power ceiling is not this value: the two can differ (a 140 W
    /// ceiling over a 115 W request), which is why a no-op must replay this one and not the ceiling.
    /// </summary>
    internal static class F7ContributionReadback
    {
        internal const int BufferSize = 1760;

        /// <summary>Null when the buffer is the wrong version, lacks the mask bits, has no F7 entry, has two, or the value is out of range.</summary>
        internal static uint? Decode(byte[] data)
        {
            if (data == null || data.Length < BufferSize ||
                BitConverter.ToUInt32(data, 0) != 0x106E0 ||
                (BitConverter.ToUInt32(data, 4) & 4) == 0 ||
                (BitConverter.ToUInt32(data, 8) & 4) == 0)
                return null;

            uint count = BitConverter.ToUInt32(data, 260);
            if (count == 0 || count > 32) return null;

            uint? result = null;
            bool seen = false;
            for (int i = 0; i < count; i++)
            {
                int offset = 264 + i * 8;
                if (BitConverter.ToUInt32(data, offset) != 0xF7) continue;
                if (seen) return null; // ambiguous source
                seen = true;
                uint value = BitConverter.ToUInt32(data, offset + 4);
                if (value > 0 && value <= 1000000) result = value;
            }
            return result;
        }

        internal static uint? Read()
        {
            try
            {
                using (NvApiSession nv = new NvApiSession())
                {
                    string err;
                    if (!nv.Open(out err)) return null;
                    var get = nv.GetGpuBuffer(0x31B7A4CD);
                    if (get == null) return null;

                    byte[] bytes = new byte[BufferSize];
                    BitConverter.GetBytes(0x106E0u).CopyTo(bytes, 0);
                    BitConverter.GetBytes(4u).CopyTo(bytes, 4);
                    IntPtr mem = Marshal.AllocHGlobal(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, mem, bytes.Length);
                        if (get(nv.Gpu, mem) != 0) return null;
                        Marshal.Copy(mem, bytes, 0, bytes.Length);
                        return Decode(bytes);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(mem);
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                AppLog.Write("F7 contribution readback failed: " + SafeError.Describe(ex));
                return null;
            }
        }
    }

    /// <summary>
    /// Picks the value a driver-validation no-op sends: the retained F7 source request, bounded by the live MAX. Never
    /// inferred from the public ceiling. Ported from Prophecy Power Unlocker 1.2.9 (bobshmo), used with permission.
    /// </summary>
    internal static class DriverValidationGuard
    {
        internal static int NoopTarget(uint? sourceF7Mw, double? liveMaxW)
        {
            if (!sourceF7Mw.HasValue || sourceF7Mw.Value == 0 || sourceF7Mw.Value > 1000000 ||
                !liveMaxW.HasValue || double.IsNaN(liveMaxW.Value) || double.IsInfinity(liveMaxW.Value) ||
                liveMaxW.Value <= 0 || liveMaxW.Value > 1000 ||
                sourceF7Mw.Value > Math.Round(liveMaxW.Value * 1000) + 10)
                throw new InvalidOperationException("A bounded source F7 contribution and live NVIDIA MAX are required for driver no-op validation. No request was inferred from the public power ceiling.");
            return checked((int)sourceF7Mw.Value);
        }
    }

    /// <summary>
    /// Error text that never calls back into a failing or unloaded NVIDIA driver (an exception's Message can do that).
    /// Ported from Prophecy Power Unlocker 1.2.9 (bobshmo), used with permission; this assembly does not reference
    /// NvAPIWrapper, so its exception is recognised by type name.
    /// </summary>
    internal static class SafeError
    {
        public static string Describe(Exception error) => Describe(error, 0);

        private static string Describe(Exception error, int depth)
        {
            if (depth >= 8) return error.GetType().Name;
            if (error.GetType().Name == "NVIDIAApiException")
                return "NVIDIA API error (HRESULT 0x" + error.HResult.ToString("X8") + ")";
            if (error is AggregateException aggregate)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var inner in aggregate.InnerExceptions) parts.Add(Describe(inner, depth + 1));
                return "Multiple errors: " + string.Join("; ", parts);
            }
            if (error is System.Reflection.TargetInvocationException && error.InnerException != null)
                return "Invocation failed: " + Describe(error.InnerException, depth + 1);
            try { return error.Message; }
            catch (Exception) { return error.GetType().Name + " (HRESULT 0x" + error.HResult.ToString("X8") + ")"; }
        }
    }
}
