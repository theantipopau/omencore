using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Safe memory optimizer using Windows Native API (NtSetSystemInformation).
    /// Cleans working sets, standby lists, modified page lists, file cache, and more.
    /// Requires admin privileges (OmenCore already runs elevated).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class MemoryOptimizerService : IDisposable
    {
        private readonly LoggingService _logger;
        private Timer? _autoCleanTimer;
        private Timer? _intervalCleanTimer;
        private bool _autoCleanEnabled;
        private int _autoCleanThresholdPercent = 80;
        private bool _intervalCleanEnabled;
        private int _intervalCleanMinutes = 10;
        private readonly object _cleanLock = new();
        private bool _isCleaning;

        public event Action<string>? StatusChanged;
        public event Action<MemoryCleanResult>? CleanCompleted;

        public MemoryOptimizerService(LoggingService logger)
        {
            _logger = logger;
        }

        // ========== MEMORY INFO ==========

        /// <summary>
        /// Gets current system memory information.
        /// </summary>
        public MemoryInfo GetMemoryInfo()
        {
            var memStatus = new NativeMethods.MEMORYSTATUSEX { dwLength = 64 };
            NativeMethods.GlobalMemoryStatusEx(ref memStatus);

            // Get performance info for cache/standby details
            var perfInfo = new NativeMethods.PERFORMANCE_INFORMATION
            {
                cb = (uint)Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>()
            };
            NativeMethods.GetPerformanceInfo(out perfInfo, perfInfo.cb);

            var pageSize = (long)perfInfo.PageSize;

            return new MemoryInfo
            {
                TotalPhysicalMB = (long)(memStatus.ullTotalPhys / 1048576),
                AvailablePhysicalMB = (long)(memStatus.ullAvailPhys / 1048576),
                UsedPhysicalMB = (long)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) / 1048576),
                MemoryLoadPercent = (int)memStatus.dwMemoryLoad,

                TotalPageFileMB = (long)(memStatus.ullTotalPageFile / 1048576),
                UsedPageFileMB = (long)((memStatus.ullTotalPageFile - memStatus.ullAvailPageFile) / 1048576),

                SystemCacheMB = (long)perfInfo.SystemCache * pageSize / 1048576,
                KernelTotalMB = (long)perfInfo.KernelTotal * pageSize / 1048576,
                KernelPagedMB = (long)perfInfo.KernelPaged * pageSize / 1048576,
                KernelNonPagedMB = (long)perfInfo.KernelNonpaged * pageSize / 1048576,

                CommitTotalMB = (long)perfInfo.CommitTotal * pageSize / 1048576,
                CommitLimitMB = (long)perfInfo.CommitLimit * pageSize / 1048576,
                CommitPeakMB = (long)perfInfo.CommitPeak * pageSize / 1048576,

                ProcessCount = (int)perfInfo.ProcessCount,
                ThreadCount = (int)perfInfo.ThreadCount,
                HandleCount = (int)perfInfo.HandleCount,
            };
        }

        // ========== MEMORY CLEANING ==========

        /// <summary>
        /// Performs a comprehensive memory clean with all safe operations.
        /// </summary>
        public async Task<MemoryCleanResult> CleanMemoryAsync(MemoryCleanFlags flags = MemoryCleanFlags.AllSafe)
        {
            return await Task.Run(() =>
            {
                lock (_cleanLock)
                {
                    if (_isCleaning)
                    {
                        return new MemoryCleanResult
                        {
                            Success = false,
                            ErrorMessage = "A memory clean operation is already in progress"
                        };
                    }

                    _isCleaning = true;
                    try
                    {
                        return CleanMemoryInternal(flags);
                    }
                    finally
                    {
                        _isCleaning = false;
                    }
                }
            });
        }

        private MemoryCleanResult CleanMemoryInternal(MemoryCleanFlags flags)
        {
            var result = new MemoryCleanResult();
            var beforeInfo = GetMemoryInfo();
            result.BeforeUsedMB = beforeInfo.UsedPhysicalMB;

            _logger.Info($"Starting memory clean (flags: {flags})...");
            StatusChanged?.Invoke("Cleaning memory...");

            // Ensure we have the required privileges
            if (!EnableRequiredPrivileges())
            {
                result.Success = false;
                result.ErrorMessage = "Failed to acquire required privileges. Run as administrator.";
                _logger.Error(result.ErrorMessage);
                return result;
            }

            int operationsSucceeded = 0;
            int operationsFailed = 0;

            // 1. Empty working sets of all processes
            if (flags.HasFlag(MemoryCleanFlags.WorkingSets))
            {
                StatusChanged?.Invoke("Trimming process working sets...");
                if (EmptyWorkingSets())
                {
                    operationsSucceeded++;
                    _logger.Info("Working sets cleaned");
                }
                else
                {
                    operationsFailed++;
                    _logger.Warn("Failed to clean working sets");
                }
            }

            // 2. Flush system file cache
            if (flags.HasFlag(MemoryCleanFlags.SystemFileCache))
            {
                StatusChanged?.Invoke("Flushing system file cache...");
                if (FlushSystemFileCache())
                {
                    operationsSucceeded++;
                    _logger.Info("System file cache flushed");
                }
                else
                {
                    operationsFailed++;
                    _logger.Warn("Failed to flush system file cache");
                }
            }

            // 3. Purge standby list (low priority first, then full)
            if (flags.HasFlag(MemoryCleanFlags.StandbyListLowPriority))
            {
                StatusChanged?.Invoke("Purging low-priority standby list...");
                if (PurgeStandbyList(lowPriorityOnly: true))
                {
                    operationsSucceeded++;
                    _logger.Info("Low-priority standby list purged");
                }
                else
                {
                    operationsFailed++;
                    _logger.Warn("Failed to purge low-priority standby list");
                }
            }

            if (flags.HasFlag(MemoryCleanFlags.StandbyList))
            {
                StatusChanged?.Invoke("Purging standby list...");
                if (PurgeStandbyList(lowPriorityOnly: false))
                {
                    operationsSucceeded++;
                    _logger.Info("Standby list purged");
                }
                else
                {
                    operationsFailed++;
                    _logger.Warn("Failed to purge standby list");
                }
            }

            // 4. Flush modified page list
            if (flags.HasFlag(MemoryCleanFlags.ModifiedPageList))
            {
                StatusChanged?.Invoke("Flushing modified page list...");
                if (FlushModifiedPageList())
                {
                    operationsSucceeded++;
                    _logger.Info("Modified page list flushed");
                }
                else
                {
                    operationsFailed++;
                    _logger.Warn("Failed to flush modified page list");
                }
            }

            // 5. Combine memory pages (Win10+)
            if (flags.HasFlag(MemoryCleanFlags.CombinePages))
            {
                StatusChanged?.Invoke("Combining memory pages...");
                if (CombineMemoryPages())
                {
                    operationsSucceeded++;
                    _logger.Info("Memory pages combined");
                }
                else
                {
                    operationsFailed++;
                    _logger.Warn("Failed to combine memory pages");
                }
            }

            // Calculate result
            // Small delay to let OS update counters
            Thread.Sleep(200);
            var afterInfo = GetMemoryInfo();
            result.AfterUsedMB = afterInfo.UsedPhysicalMB;
            result.FreedMB = Math.Max(0, result.BeforeUsedMB - result.AfterUsedMB);
            result.OperationsSucceeded = operationsSucceeded;
            result.OperationsFailed = operationsFailed;
            result.Success = operationsSucceeded > 0;
            result.Timestamp = DateTime.Now;

            var statusMsg = $"Freed {result.FreedMB} MB ({operationsSucceeded} operations, {operationsFailed} failed)";
            StatusChanged?.Invoke(statusMsg);
            _logger.Info(statusMsg);
            CleanCompleted?.Invoke(result);

            return result;
        }

        // ========== NATIVE MEMORY OPERATIONS ==========

        /// <summary>
        /// Empties working sets of all processes (NtSetSystemInformation with MemoryEmptyWorkingSets).
        /// This is the safest operation — just trims unused memory from process working sets.
        /// </summary>
        private bool EmptyWorkingSets()
        {
            try
            {
                int command = (int)NativeMethods.SYSTEM_MEMORY_LIST_COMMAND.MemoryEmptyWorkingSets;
                int status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SYSTEM_INFORMATION_CLASS.SystemMemoryListInformation,
                    ref command,
                    sizeof(int));

                return status == 0; // STATUS_SUCCESS
            }
            catch (Exception ex)
            {
                _logger.Error($"EmptyWorkingSets failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Flushes the system file cache by setting min/max to MAXSIZE_T.
        /// </summary>
        private bool FlushSystemFileCache()
        {
            try
            {
                var cacheInfo = new NativeMethods.SYSTEM_FILECACHE_INFORMATION
                {
                    MinimumWorkingSet = new IntPtr(-1), // MAXSIZE_T
                    MaximumWorkingSet = new IntPtr(-1)  // MAXSIZE_T
                };

                int status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SYSTEM_INFORMATION_CLASS.SystemFileCacheInformationEx,
                    ref cacheInfo,
                    Marshal.SizeOf<NativeMethods.SYSTEM_FILECACHE_INFORMATION>());

                return status == 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"FlushSystemFileCache failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Purges standby memory pages. lowPriorityOnly=true only purges priority-0 pages (safer).
        /// </summary>
        private bool PurgeStandbyList(bool lowPriorityOnly)
        {
            try
            {
                int command = lowPriorityOnly
                    ? (int)NativeMethods.SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeLowPriorityStandbyList
                    : (int)NativeMethods.SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeStandbyList;

                int status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SYSTEM_INFORMATION_CLASS.SystemMemoryListInformation,
                    ref command,
                    sizeof(int));

                return status == 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"PurgeStandbyList(lowPriority={lowPriorityOnly}) failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Flushes modified page list to disk.
        /// </summary>
        private bool FlushModifiedPageList()
        {
            try
            {
                int command = (int)NativeMethods.SYSTEM_MEMORY_LIST_COMMAND.MemoryFlushModifiedList;
                int status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SYSTEM_INFORMATION_CLASS.SystemMemoryListInformation,
                    ref command,
                    sizeof(int));

                return status == 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"FlushModifiedPageList failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Combines identical memory pages (Win10+ only).
        /// </summary>
        private bool CombineMemoryPages()
        {
            try
            {
                var combineInfo = new NativeMethods.MEMORY_COMBINE_INFORMATION_EX();
                int size = Marshal.SizeOf<NativeMethods.MEMORY_COMBINE_INFORMATION_EX>();

                int status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SYSTEM_INFORMATION_CLASS.SystemCombinePhysicalMemoryInformation,
                    ref combineInfo,
                    size);

                return status == 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"CombineMemoryPages failed: {ex.Message}");
                return false;
            }
        }

        // ========== PRIVILEGE MANAGEMENT ==========

        /// <summary>
        /// Enables SE_PROF_SINGLE_PROCESS_PRIVILEGE and SE_INCREASE_QUOTA_PRIVILEGE
        /// required for NtSetSystemInformation memory operations.
        /// </summary>
        private bool EnableRequiredPrivileges()
        {
            try
            {
                bool result = true;
                result &= EnablePrivilege("SeProfileSingleProcessPrivilege");
                result &= EnablePrivilege("SeIncreaseQuotaPrivilege");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"EnableRequiredPrivileges failed: {ex.Message}");
                return false;
            }
        }

        private static bool EnablePrivilege(string privilegeName)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!NativeMethods.OpenProcessToken(
                    Process.GetCurrentProcess().Handle,
                    NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                    out tokenHandle))
                {
                    return false;
                }

                if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out var luid))
                {
                    return false;
                }

                var tp = new NativeMethods.TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
                };

                return NativeMethods.AdjustTokenPrivileges(
                    tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                    NativeMethods.CloseHandle(tokenHandle);
            }
        }

        // ========== AUTO-CLEAN ==========

        /// <summary>
        /// Enables automatic memory cleaning when usage exceeds threshold.
        /// </summary>
        public void SetAutoClean(bool enabled, int thresholdPercent = 80)
        {
            _autoCleanEnabled = enabled;
            _autoCleanThresholdPercent = Math.Clamp(thresholdPercent, 50, 95);

            if (enabled)
            {
                _autoCleanTimer?.Dispose();
                // Check every 30 seconds
                _autoCleanTimer = new Timer(AutoCleanCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                _logger.Info($"Auto-clean enabled at {_autoCleanThresholdPercent}% threshold");
            }
            else
            {
                _autoCleanTimer?.Dispose();
                _autoCleanTimer = null;
                _logger.Info("Auto-clean disabled");
            }
        }

        /// <summary>
        /// Enables periodic automatic memory cleaning every N minutes.
        /// </summary>
        public void SetIntervalClean(bool enabled, int intervalMinutes = 10)
        {
            _intervalCleanEnabled = enabled;
            _intervalCleanMinutes = Math.Clamp(intervalMinutes, 1, 120);

            if (enabled)
            {
                _intervalCleanTimer?.Dispose();
                var interval = TimeSpan.FromMinutes(_intervalCleanMinutes);
                _intervalCleanTimer = new Timer(IntervalCleanCallback, null, interval, interval);
                _logger.Info($"Interval clean enabled every {_intervalCleanMinutes} minute(s)");
            }
            else
            {
                _intervalCleanTimer?.Dispose();
                _intervalCleanTimer = null;
                _logger.Info("Interval clean disabled");
            }
        }

        private void AutoCleanCallback(object? state)
        {
            try
            {
                var info = GetMemoryInfo();
                if (info.MemoryLoadPercent >= _autoCleanThresholdPercent)
                {
                    _logger.Info($"Auto-clean triggered: memory at {info.MemoryLoadPercent}% (threshold: {_autoCleanThresholdPercent}%)");
                    TryRunScheduledClean($"Auto-clean: memory at {info.MemoryLoadPercent}%...");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Auto-clean check failed: {ex.Message}");
            }
        }

        private void IntervalCleanCallback(object? state)
        {
            try
            {
                _logger.Info($"Interval clean triggered (every {_intervalCleanMinutes} minute(s))");
                TryRunScheduledClean($"Auto-clean: scheduled every {_intervalCleanMinutes} minute(s)...");
            }
            catch (Exception ex)
            {
                _logger.Error($"Interval clean failed: {ex.Message}");
            }
        }

        private void TryRunScheduledClean(string statusMessage)
        {
            lock (_cleanLock)
            {
                if (_isCleaning)
                    return;

                _isCleaning = true;
                try
                {
                    StatusChanged?.Invoke(statusMessage);
                    CleanMemoryInternal(MemoryCleanFlags.AllSafe);
                }
                finally
                {
                    _isCleaning = false;
                }
            }
        }

        public bool AutoCleanEnabled => _autoCleanEnabled;
        public int AutoCleanThreshold => _autoCleanThresholdPercent;
        public bool IntervalCleanEnabled => _intervalCleanEnabled;
        public int IntervalCleanMinutes => _intervalCleanMinutes;
        public bool IsCleaning => _isCleaning;

        public void Dispose()
        {
            _autoCleanTimer?.Dispose();
            _autoCleanTimer = null;
            _intervalCleanTimer?.Dispose();
            _intervalCleanTimer = null;
        }

        // ========== P/INVOKE DECLARATIONS ==========

        private static class NativeMethods
        {
            // ===== ntdll.dll =====

            [DllImport("ntdll.dll")]
            public static extern int NtSetSystemInformation(
                SYSTEM_INFORMATION_CLASS infoClass,
                ref int info,
                int length);

            [DllImport("ntdll.dll")]
            public static extern int NtSetSystemInformation(
                SYSTEM_INFORMATION_CLASS infoClass,
                ref SYSTEM_FILECACHE_INFORMATION info,
                int length);

            [DllImport("ntdll.dll")]
            public static extern int NtSetSystemInformation(
                SYSTEM_INFORMATION_CLASS infoClass,
                ref MEMORY_COMBINE_INFORMATION_EX info,
                int length);

            // ===== kernel32.dll =====

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);

            // ===== psapi.dll =====

            [DllImport("psapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetPerformanceInfo(
                out PERFORMANCE_INFORMATION pPerformanceInformation,
                uint cb);

            // ===== advapi32.dll (Privilege management) =====

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool OpenProcessToken(
                IntPtr processHandle,
                uint desiredAccess,
                out IntPtr tokenHandle);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool LookupPrivilegeValue(
                string? lpSystemName,
                string lpName,
                out LUID lpLuid);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AdjustTokenPrivileges(
                IntPtr tokenHandle,
                [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
                ref TOKEN_PRIVILEGES newState,
                uint bufferLength,
                IntPtr previousState,
                IntPtr returnLength);

            // ===== Constants =====

            public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
            public const uint TOKEN_QUERY = 0x0008;
            public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

            // ===== Enums =====

            public enum SYSTEM_INFORMATION_CLASS
            {
                SystemFileCacheInformationEx = 81,
                SystemMemoryListInformation = 80,
                SystemCombinePhysicalMemoryInformation = 130,
            }

            public enum SYSTEM_MEMORY_LIST_COMMAND
            {
                MemoryEmptyWorkingSets = 0,
                MemoryFlushModifiedList = 3,
                MemoryPurgeStandbyList = 4,
                MemoryPurgeLowPriorityStandbyList = 5,
            }

            // ===== Structs =====

            [StructLayout(LayoutKind.Sequential)]
            public struct MEMORYSTATUSEX
            {
                public uint dwLength;
                public uint dwMemoryLoad;
                public ulong ullTotalPhys;
                public ulong ullAvailPhys;
                public ulong ullTotalPageFile;
                public ulong ullAvailPageFile;
                public ulong ullTotalVirtual;
                public ulong ullAvailVirtual;
                public ulong ullAvailExtendedVirtual;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct SYSTEM_FILECACHE_INFORMATION
            {
                public IntPtr CurrentSize;
                public IntPtr PeakSize;
                public uint PageFaultCount;
                public IntPtr MinimumWorkingSet;
                public IntPtr MaximumWorkingSet;
                public IntPtr CurrentSizeIncludingTransitionInPages;
                public IntPtr PeakSizeIncludingTransitionInPages;
                public uint TransitionRePurposeCount;
                public uint Flags;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MEMORY_COMBINE_INFORMATION_EX
            {
                public IntPtr Handle;
                public UIntPtr PagesCombined;
                public uint Flags;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct PERFORMANCE_INFORMATION
            {
                public uint cb;
                public UIntPtr CommitTotal;
                public UIntPtr CommitLimit;
                public UIntPtr CommitPeak;
                public UIntPtr PhysicalTotal;
                public UIntPtr PhysicalAvailable;
                public UIntPtr SystemCache;
                public UIntPtr KernelTotal;
                public UIntPtr KernelPaged;
                public UIntPtr KernelNonpaged;
                public UIntPtr PageSize;
                public uint HandleCount;
                public uint ProcessCount;
                public uint ThreadCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct LUID
            {
                public uint LowPart;
                public int HighPart;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct TOKEN_PRIVILEGES
            {
                public uint PrivilegeCount;
                public LUID Luid;
                public uint Attributes;
            }
        }
    }

    // ========== PUBLIC MODELS ==========

    /// <summary>
    /// Current system memory information.
    /// </summary>
    public class MemoryInfo
    {
        public long TotalPhysicalMB { get; set; }
        public long AvailablePhysicalMB { get; set; }
        public long UsedPhysicalMB { get; set; }
        public int MemoryLoadPercent { get; set; }

        public long TotalPageFileMB { get; set; }
        public long UsedPageFileMB { get; set; }

        public long SystemCacheMB { get; set; }
        public long KernelTotalMB { get; set; }
        public long KernelPagedMB { get; set; }
        public long KernelNonPagedMB { get; set; }

        public long CommitTotalMB { get; set; }
        public long CommitLimitMB { get; set; }
        public long CommitPeakMB { get; set; }

        public int ProcessCount { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
    }

    /// <summary>
    /// Result of a memory clean operation.
    /// </summary>
    public class MemoryCleanResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public long BeforeUsedMB { get; set; }
        public long AfterUsedMB { get; set; }
        public long FreedMB { get; set; }
        public int OperationsSucceeded { get; set; }
        public int OperationsFailed { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Flags to select which memory cleaning operations to perform.
    /// </summary>
    [Flags]
    public enum MemoryCleanFlags
    {
        None = 0,

        /// <summary>Trim working sets of all processes. Safest operation.</summary>
        WorkingSets = 1 << 0,

        /// <summary>Flush the system file cache.</summary>
        SystemFileCache = 1 << 1,

        /// <summary>Purge low-priority standby pages only.</summary>
        StandbyListLowPriority = 1 << 2,

        /// <summary>Purge all standby pages. May cause brief stutter.</summary>
        StandbyList = 1 << 3,

        /// <summary>Flush modified pages to disk.</summary>
        ModifiedPageList = 1 << 4,

        /// <summary>Combine identical memory pages (Win10+).</summary>
        CombinePages = 1 << 5,

        /// <summary>All safe operations (working sets + low-priority standby + combine).</summary>
        AllSafe = WorkingSets | StandbyListLowPriority | CombinePages,

        /// <summary>All operations including aggressive ones.</summary>
        All = WorkingSets | SystemFileCache | StandbyListLowPriority | StandbyList | ModifiedPageList | CombinePages,
    }
}
