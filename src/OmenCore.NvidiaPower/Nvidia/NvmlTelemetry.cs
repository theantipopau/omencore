using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NvpwrControlBlackwell
{
    internal static class NvmlTelemetry
    {
        private const int NVML_SUCCESS = 0;
        private const uint NVML_TEMPERATURE_GPU = 0;
        private const uint NVML_CLOCK_GRAPHICS = 0;
        private const uint NVML_CLOCK_MEM = 2;
        private const uint NVML_FI_DEV_MEMORY_TEMP = 82;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetCountDelegate(ref uint count);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetHandleDelegate(uint index, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetUIntDelegate(IntPtr device, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetUIntArgDelegate(IntPtr device, uint arg, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetLimitsDelegate(IntPtr device, out uint min, out uint max);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetUtilDelegate(IntPtr device, ref NvmlUtilization util);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetFieldValuesDelegate(IntPtr device, int valuesCount, IntPtr values);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization
        {
            public uint gpu;
            public uint memory;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct NvmlValue
        {
            [FieldOffset(0)] public double dVal;
            [FieldOffset(0)] public uint uiVal;
            [FieldOffset(0)] public ulong ullVal;
            [FieldOffset(0)] public long sllVal;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlFieldValue
        {
            public uint fieldId;
            public uint scopeId;
            public long timestamp;
            public long latencyUsec;
            public int valueType;
            public int nvmlReturn;
            public NvmlValue value;
        }

        private static readonly object Sync = new object();
        private static bool _attempted;
        private static bool _ready;
        private static string _error = "";
        private static IntPtr _module;
        private static IntPtr _device;

        private static GetUIntDelegate _powerUsage;
        private static GetUIntArgDelegate _temperature;
        private static GetUtilDelegate _utilization;
        private static GetUIntArgDelegate _clockInfo;
        private static GetUIntDelegate _enforcedPowerLimit;
        private static GetUIntDelegate _powerManagementLimit;
        private static GetLimitsDelegate _limitConstraints;
        private static GetFieldValuesDelegate _fieldValues;

        private static IntPtr Proc(string name)
        {
            if (_module == IntPtr.Zero) return IntPtr.Zero;
            return GetProcAddress(_module, name);
        }

        private static Delegate D(string name, Type type)
        {
            IntPtr p = Proc(name);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(p, type);
        }

        private static bool EnsureReady(out string error)
        {
            lock (Sync)
            {
                if (_ready)
                {
                    error = "";
                    return true;
                }

                if (_attempted)
                {
                    error = _error;
                    return false;
                }

                _attempted = true;

                try
                {
                    string systemNvml = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "nvml.dll");

                    _module = LoadLibrary(systemNvml);
                    if (_module == IntPtr.Zero)
                        _module = LoadLibrary("nvml.dll");

                    if (_module == IntPtr.Zero)
                        throw new InvalidOperationException("nvml.dll could not be loaded.");

                    InitDelegate init =
                        (InitDelegate)D("nvmlInit_v2", typeof(InitDelegate));

                    if (init == null)
                        init = (InitDelegate)D("nvmlInit", typeof(InitDelegate));

                    GetCountDelegate getCount =
                        (GetCountDelegate)D("nvmlDeviceGetCount_v2", typeof(GetCountDelegate));

                    if (getCount == null)
                        getCount = (GetCountDelegate)D("nvmlDeviceGetCount", typeof(GetCountDelegate));

                    GetHandleDelegate getHandle =
                        (GetHandleDelegate)D("nvmlDeviceGetHandleByIndex_v2", typeof(GetHandleDelegate));

                    if (getHandle == null)
                        getHandle = (GetHandleDelegate)D("nvmlDeviceGetHandleByIndex", typeof(GetHandleDelegate));

                    if (init == null || getCount == null || getHandle == null)
                        throw new InvalidOperationException("Required NVML entry points are missing.");

                    int rc = init();
                    if (rc != NVML_SUCCESS)
                        throw new InvalidOperationException("nvmlInit failed rc=" + rc.ToString());

                    uint count = 0;
                    rc = getCount(ref count);
                    if (rc != NVML_SUCCESS || count == 0)
                        throw new InvalidOperationException("NVML returned no GPU. rc=" + rc.ToString());

                    rc = getHandle(0, out _device);
                    if (rc != NVML_SUCCESS || _device == IntPtr.Zero)
                        throw new InvalidOperationException("NVML GPU handle failed rc=" + rc.ToString());

                    _powerUsage =
                        (GetUIntDelegate)D("nvmlDeviceGetPowerUsage", typeof(GetUIntDelegate));
                    _temperature =
                        (GetUIntArgDelegate)D("nvmlDeviceGetTemperature", typeof(GetUIntArgDelegate));
                    _utilization =
                        (GetUtilDelegate)D("nvmlDeviceGetUtilizationRates", typeof(GetUtilDelegate));
                    _clockInfo =
                        (GetUIntArgDelegate)D("nvmlDeviceGetClockInfo", typeof(GetUIntArgDelegate));
                    _enforcedPowerLimit =
                        (GetUIntDelegate)D("nvmlDeviceGetEnforcedPowerLimit", typeof(GetUIntDelegate));
                    _powerManagementLimit =
                        (GetUIntDelegate)D("nvmlDeviceGetPowerManagementLimit", typeof(GetUIntDelegate));
                    _limitConstraints =
                        (GetLimitsDelegate)D("nvmlDeviceGetPowerManagementLimitConstraints", typeof(GetLimitsDelegate));
                    _fieldValues =
                        (GetFieldValuesDelegate)D("nvmlDeviceGetFieldValues", typeof(GetFieldValuesDelegate));

                    _ready = true;
                    error = "";
                    return true;
                }
                catch (Exception ex)
                {
                    _error = SafeError.Describe(ex);
                    error = _error;
                    return false;
                }
            }
        }

        private static double? ReadFieldUnsigned(uint fieldId)
        {
            if (_fieldValues == null || _device == IntPtr.Zero)
                return null;

            int size = Marshal.SizeOf(typeof(NvmlFieldValue));
            IntPtr p = Marshal.AllocHGlobal(size);

            try
            {
                NvmlFieldValue f = new NvmlFieldValue();
                f.fieldId = fieldId;
                f.scopeId = 0;

                Marshal.StructureToPtr(f, p, false);

                int rc = _fieldValues(_device, 1, p);
                if (rc != NVML_SUCCESS)
                    return null;

                f = (NvmlFieldValue)Marshal.PtrToStructure(
                    p,
                    typeof(NvmlFieldValue));

                if (f.nvmlReturn != NVML_SUCCESS)
                    return null;

                // nvmlValueType_t:
                // 0=double, 1=uint, 2=ulong, 3=ulonglong, 4=signed longlong.
                if (f.valueType == 0)
                    return f.value.dVal;
                if (f.valueType == 1)
                    return f.value.uiVal;
                if (f.valueType == 3)
                    return f.value.ullVal;
                if (f.valueType == 4)
                    return f.value.sllVal;

                // A number of NVIDIA drivers report integer sensor fields
                // with the union populated even when a newer type enum is used.
                if (f.value.uiVal != 0)
                    return f.value.uiVal;

                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        public static bool Fill(TelemetryState t, out string error)
        {
            error = "";

            if (t == null)
            {
                error = "Telemetry target is null.";
                return false;
            }

            if (!EnsureReady(out error))
                return false;

            lock (Sync)
            {
                uint u;

                if (_powerUsage != null &&
                    _powerUsage(_device, out u) == NVML_SUCCESS)
                    t.PowerW = u / 1000.0;

                if (_temperature != null &&
                    _temperature(_device, NVML_TEMPERATURE_GPU, out u) == NVML_SUCCESS)
                    t.GpuTempC = u;

                if (_utilization != null)
                {
                    NvmlUtilization util = new NvmlUtilization();
                    if (_utilization(_device, ref util) == NVML_SUCCESS)
                        t.UtilizationPct = util.gpu;
                }

                if (_clockInfo != null)
                {
                    if (_clockInfo(_device, NVML_CLOCK_GRAPHICS, out u) == NVML_SUCCESS)
                        t.CoreClockMHz = u;

                    if (_clockInfo(_device, NVML_CLOCK_MEM, out u) == NVML_SUCCESS)
                        t.MemoryClockMHz = u;
                }

                double? memTemp = ReadFieldUnsigned(NVML_FI_DEV_MEMORY_TEMP);
                if (memTemp.HasValue &&
                    memTemp.Value > 0 &&
                    memTemp.Value < 150)
                    t.MemoryTempC = memTemp.Value;

                if (t.Power == null)
                    t.Power = new PowerState();

                if (_enforcedPowerLimit != null &&
                    _enforcedPowerLimit(_device, out u) == NVML_SUCCESS &&
                    u > 0)
                    t.Power.CurrentW = u / 1000.0;

                if (_powerManagementLimit != null &&
                    _powerManagementLimit(_device, out u) == NVML_SUCCESS &&
                    u > 0)
                    t.Power.RequestedW = u / 1000.0;

                if (_limitConstraints != null)
                {
                    uint mn, mx;
                    if (_limitConstraints(_device, out mn, out mx) == NVML_SUCCESS &&
                        mx > 0)
                        t.Power.MaxW = mx / 1000.0;
                }

                return true;
            }
        }
    }
}
