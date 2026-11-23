using System;
using System.Runtime.InteropServices;

namespace NvpwrControlBlackwell
{
    internal static class NvmlBridge
    {
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        private static extern int nvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        private static extern int nvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        private static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceSetGpuLockedClocks")]
        private static extern int nvmlDeviceSetGpuLockedClocks(IntPtr device, uint minClockMHz, uint maxClockMHz);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceResetGpuLockedClocks")]
        private static extern int nvmlDeviceResetGpuLockedClocks(IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")]
        private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint clockMHz);

        public static bool SetLockedClocks(uint minMHz, uint maxMHz, out string error)
        {
            error = "";
            try
            {
                int rc = nvmlInit();
                if (rc != 0) { error = "NVML init failed rc=" + rc; return false; }

                IntPtr dev;
                rc = nvmlDeviceGetHandleByIndex(0, out dev);
                if (rc != 0) { error = "NVML get device failed rc=" + rc; return false; }

                rc = nvmlDeviceSetGpuLockedClocks(dev, minMHz, maxMHz);
                if (rc != 0)
                {
                    if (rc == 4) error = "NVML Administrator permission required to lock clocks.";
                    else error = "nvmlDeviceSetGpuLockedClocks failed rc=" + rc;
                    return false;
                }
                AppLog.Write("NVML Locked Clocks set to " + minMHz + ".." + maxMHz + " MHz");
                return true;
            }
            catch (Exception ex)
            {
                error = "NVML exception: " + SafeError.Describe(ex);
                return false;
            }
        }

        public static bool ResetLockedClocks(out string error)
        {
            error = "";
            try
            {
                int rc = nvmlInit();
                if (rc != 0) { error = "NVML init failed rc=" + rc; return false; }

                IntPtr dev;
                rc = nvmlDeviceGetHandleByIndex(0, out dev);
                if (rc != 0) { error = "NVML get device failed rc=" + rc; return false; }

                rc = nvmlDeviceResetGpuLockedClocks(dev);
                if (rc != 0)
                {
                    error = "nvmlDeviceResetGpuLockedClocks failed rc=" + rc;
                    return false;
                }
                AppLog.Write("NVML Locked Clocks reset to stock");
                return true;
            }
            catch (Exception ex)
            {
                error = "NVML exception: " + SafeError.Describe(ex);
                return false;
            }
        }
    }
}
