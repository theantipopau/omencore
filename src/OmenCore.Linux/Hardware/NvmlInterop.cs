using System.Runtime.InteropServices;
using System.Text;

namespace OmenCore.Linux.Hardware;

/// <summary>
/// One NVML query's worth of GPU state, from the single primary (index 0) NVIDIA GPU.
/// Any field being null means that specific NVML call failed even though the library loaded
/// and initialized fine - the whole snapshot isn't discarded just because e.g. power reporting
/// isn't supported on a given board while temperature/utilization are.
/// </summary>
public sealed class NvmlGpuSnapshot
{
    public string Name { get; init; } = string.Empty;
    public int? TemperatureC { get; init; }
    public double? PowerWatts { get; init; }
    public int? UtilizationPercent { get; init; }
}

/// <summary>
/// GitHub #186: OmenCore's Linux GPU telemetry never queried NVML at all - only hwmon (which the
/// proprietary NVIDIA driver typically doesn't register a "nvidia" hwmon device for, unlike
/// amdgpu/nouveau) and the OMEN EC's own GPU thermal register. On an NVIDIA laptop GPU with no
/// hwmon exposure, both of those come back empty and the whole GPU telemetry surface silently
/// reports "unavailable"/0, even though `nvidia-smi` reads the exact same GPU correctly via NVML.
///
/// This wraps libnvidia-ml.so.1 (the NVIDIA Management Library nvidia-smi itself is built on) via
/// P/Invoke. Deliberately narrow scope: single primary GPU (index 0) only - correct for every
/// actual OMEN/Victus laptop, which never ships more than one NVIDIA GPU, and "primary" here means
/// "the only one NVML can see" since NVML never enumerates Intel/AMD iGPUs anyway.
///
/// Library loading needs a custom resolver rather than a bare DllImport name: .NET's default
/// Linux native-library probing does not try the versioned SONAME (`libnvidia-ml.so.1`), and many
/// systems that only installed the runtime driver package (not a `-dev` package) have ONLY that
/// versioned file, no unversioned `libnvidia-ml.so` dev symlink to fall back on.
///
/// Thread-safety: not locked. Every real call site in this project (StatusCommand, MonitorCommand,
/// DiagnoseCommand) invokes this from a single thread; add locking if a future caller changes that.
/// </summary>
public static class NvmlInterop
{
    private const string LibraryName = "nvidia-ml";
    private const int NvmlSuccess = 0;
    private const int NvmlTemperatureGpu = 0;
    // NVML_DEVICE_NAME_V2_BUFFER_SIZE (96) - the current, larger buffer size; using it instead of
    // the older 64-byte NVML_DEVICE_NAME_BUFFER_SIZE covers both old and new driver builds safely.
    private const int NvmlDeviceNameBufferSize = 96;

    private static bool _resolverRegistered;
    private static bool _initAttempted;
    private static bool _initialized;

    /// <summary>
    /// Why the most recent NVML operation failed, or null if the most recent attempt succeeded or
    /// nothing has been attempted yet this process. Intended for diagnose-output surfacing (GitHub
    /// #186's own suggestion) rather than for control flow - callers should treat a null
    /// TryGetPrimaryGpu() result as "unavailable" regardless of whether this is set.
    /// </summary>
    public static string? LastFailureReason { get; private set; }

    static NvmlInterop()
    {
        RegisterResolver();
    }

    private static void RegisterResolver()
    {
        if (_resolverRegistered)
        {
            return;
        }

        _resolverRegistered = true;

        NativeLibrary.SetDllImportResolver(typeof(NvmlInterop).Assembly, (name, assembly, searchPath) =>
        {
            if (!string.Equals(name, LibraryName, StringComparison.Ordinal))
            {
                return IntPtr.Zero;
            }

            if (NativeLibrary.TryLoad("libnvidia-ml.so.1", out var handle))
            {
                return handle;
            }

            if (NativeLibrary.TryLoad("libnvidia-ml.so", out handle))
            {
                return handle;
            }

            return IntPtr.Zero;
        });
    }

    [DllImport(LibraryName)]
    private static extern int nvmlInit_v2();

    [DllImport(LibraryName)]
    private static extern int nvmlShutdown();

    [DllImport(LibraryName)]
    private static extern int nvmlDeviceGetCount_v2(out uint deviceCount);

    [DllImport(LibraryName)]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport(LibraryName)]
    private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

    [DllImport(LibraryName)]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport(LibraryName)]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);

    [DllImport(LibraryName)]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [DllImport(LibraryName)]
    private static extern IntPtr nvmlErrorString(int result);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    /// <summary>
    /// Queries the primary NVIDIA GPU's name, temperature, power draw, and utilization in one
    /// call. Returns null if NVML isn't loadable, won't initialize, or reports zero devices -
    /// check <see cref="LastFailureReason"/> for why. A non-null result can still have individual
    /// null fields if only some of the underlying NVML calls succeeded.
    /// </summary>
    public static NvmlGpuSnapshot? TryGetPrimaryGpu()
    {
        if (!EnsureInitialized())
        {
            return null;
        }

        try
        {
            var countRc = nvmlDeviceGetCount_v2(out var count);
            if (countRc != NvmlSuccess)
            {
                LastFailureReason = $"nvmlDeviceGetCount_v2 failed: {DescribeError(countRc)}";
                return null;
            }

            if (count == 0)
            {
                LastFailureReason = "nvmlDeviceGetCount_v2 reported 0 devices";
                return null;
            }

            var handleRc = nvmlDeviceGetHandleByIndex_v2(0, out var device);
            if (handleRc != NvmlSuccess)
            {
                LastFailureReason = $"nvmlDeviceGetHandleByIndex_v2 failed: {DescribeError(handleRc)}";
                return null;
            }

            var name = string.Empty;
            var nameBuffer = new byte[NvmlDeviceNameBufferSize];
            if (nvmlDeviceGetName(device, nameBuffer, (uint)nameBuffer.Length) == NvmlSuccess)
            {
                var nullIndex = Array.IndexOf(nameBuffer, (byte)0);
                var length = nullIndex >= 0 ? nullIndex : nameBuffer.Length;
                name = Encoding.ASCII.GetString(nameBuffer, 0, length);
            }

            int? temperatureC = null;
            if (nvmlDeviceGetTemperature(device, NvmlTemperatureGpu, out var temp) == NvmlSuccess)
            {
                temperatureC = (int)temp;
            }

            double? powerWatts = null;
            if (nvmlDeviceGetPowerUsage(device, out var milliwatts) == NvmlSuccess)
            {
                powerWatts = milliwatts / 1000.0;
            }

            int? utilizationPercent = null;
            if (nvmlDeviceGetUtilizationRates(device, out var utilization) == NvmlSuccess)
            {
                utilizationPercent = (int)utilization.Gpu;
            }

            LastFailureReason = null;
            return new NvmlGpuSnapshot
            {
                Name = name,
                TemperatureC = temperatureC,
                PowerWatts = powerWatts,
                UtilizationPercent = utilizationPercent
            };
        }
        catch (Exception ex)
        {
            LastFailureReason = $"NVML query threw: {ex.Message}";
            return null;
        }
    }

    private static bool EnsureInitialized()
    {
        if (_initialized)
        {
            return true;
        }

        // Only try once per process. NVML init/dlopen isn't free, and every real caller here is
        // either a one-shot CLI command or a MonitorCommand/daemon loop that would otherwise retry
        // every single tick for the rest of its run - a real driver/library absence doesn't
        // resolve itself mid-process.
        if (_initAttempted)
        {
            return false;
        }

        _initAttempted = true;

        try
        {
            var rc = nvmlInit_v2();
            if (rc != NvmlSuccess)
            {
                LastFailureReason = $"nvmlInit_v2 failed: {DescribeError(rc)}";
                return false;
            }

            _initialized = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => TryShutdown();
            return true;
        }
        catch (DllNotFoundException)
        {
            LastFailureReason = "libnvidia-ml.so.1 not found - the NVIDIA driver's userspace library isn't installed or isn't in the loader path";
            return false;
        }
        catch (Exception ex)
        {
            LastFailureReason = $"NVML load/init threw: {ex.Message}";
            return false;
        }
    }

    private static void TryShutdown()
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            nvmlShutdown();
        }
        catch
        {
            // Best effort on process exit - nothing meaningful to do with a shutdown failure here.
        }

        _initialized = false;
    }

    private static string DescribeError(int rc)
    {
        try
        {
            var ptr = nvmlErrorString(rc);
            var text = ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
            return string.IsNullOrEmpty(text) ? $"rc={rc}" : $"{text} (rc={rc})";
        }
        catch
        {
            return $"rc={rc}";
        }
    }
}
