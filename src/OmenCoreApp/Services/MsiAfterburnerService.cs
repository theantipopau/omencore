using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services
{
    /// <summary>
    /// Result of checking for conflicts between OmenCore and MSI Afterburner.
    /// </summary>
    public class ConflictCheckResult
    {
        public DateTime Timestamp { get; set; }
        public List<string> Conflicts { get; set; } = new();
        public bool HasConflicts { get; set; }
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event arguments for conflict detection.
    /// </summary>
    public class ConflictDetectedEventArgs : EventArgs
    {
        public ConflictCheckResult ConflictResult { get; }

        public ConflictDetectedEventArgs(ConflictCheckResult conflictResult)
        {
            ConflictResult = conflictResult;
        }
    }

    /// <summary>
    /// Service for reading MSI Afterburner shared memory and detecting conflicts.
    /// Provides GPU temperature and other monitoring data from Afterburner.
    /// </summary>
    public class MsiAfterburnerService : IDisposable
    {
        private readonly LoggingService _logging;
        private MemoryMappedFile? _sharedMemory;
        private bool _isConnected;
        private bool _disposed;
        private Timer? _monitoringTimer;
        private readonly object _lock = new();

        // Afterburner shared memory structure (simplified)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AfterburnerData
        {
            public uint Signature;        // 'RTSS'
            public uint Version;          // Version
            public uint GpuEntryCount;    // Number of GPU entries
            public uint GpuEntrySize;     // Size of each GPU entry

            // GPU entries follow...
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct GpuEntry
        {
            public uint Flags;            // Entry flags
            public uint GpuId;            // GPU identifier
            public uint Temp;             // GPU temperature
            public uint TempMin;          // Minimum temperature
            public uint TempMax;          // Maximum temperature
            public uint FanSpeed;         // Fan speed %
            public uint FanSpeedRpm;      // Fan speed RPM
            public uint CoreClock;        // Core clock MHz
            public uint MemoryClock;      // Memory clock MHz
            public uint CoreVoltage;      // Core voltage mV
            public uint Power;            // Power consumption %
            public uint PerfLimitReason;  // Performance limit reasons

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public byte[] GpuName;        // GPU name
        }

        public bool IsAvailable => _isConnected;
        public bool IsAfterburnerRunning => CheckAfterburnerRunning();

        public event EventHandler<AfterburnerDataEventArgs>? AfterburnerDataReceived;
        public event EventHandler<ConflictDetectedEventArgs>? ConflictDetected;

        public MsiAfterburnerService(LoggingService logging)
        {
            _logging = logging;
            _logging.Info("MsiAfterburnerService initialized");
        }

        /// <summary>
        /// Initialize connection to MSI Afterburner shared memory.
        /// </summary>
        public bool Initialize()
        {
            lock (_lock)
            {
                if (_isConnected)
                    return true;

                try
                {
                    if (!CheckAfterburnerRunning())
                    {
                        _logging.Info("MSI Afterburner not detected running");
                        return false;
                    }

                    // Try to open Afterburner shared memory
                    _sharedMemory = MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2");
                    _isConnected = true;

                    _logging.Info("Successfully connected to MSI Afterburner shared memory");

                    // Start monitoring timer
                    _monitoringTimer = new Timer(MonitorAfterburner, null, 1000, 2000); // Check every 2 seconds

                    return true;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to connect to MSI Afterburner shared memory: {ex.Message}");
                    _isConnected = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Read current GPU data from Afterburner.
        /// </summary>
        public AfterburnerGpuData? ReadGpuData()
        {
            lock (_lock)
            {
                if (!_isConnected || _sharedMemory == null)
                    return null;

                try
                {
                    using var accessor = _sharedMemory.CreateViewAccessor(0, Marshal.SizeOf<AfterburnerData>());
                    accessor.Read(0, out AfterburnerData header);

                    // Verify signature
                    if (header.Signature != 0x52545353) // 'RTSS'
                    {
                        _logging.Warn("Invalid Afterburner shared memory signature");
                        return null;
                    }

                    // Read first GPU entry
                    if (header.GpuEntryCount > 0)
                    {
                        long entryOffset = Marshal.SizeOf<AfterburnerData>();
                        accessor.Dispose(); // Close first accessor

                        using var entryAccessor = _sharedMemory.CreateViewAccessor(entryOffset, (long)header.GpuEntrySize);
                        entryAccessor.Read(0, out GpuEntry gpuEntry);

                        var gpuData = new AfterburnerGpuData
                        {
                            GpuId = (int)gpuEntry.GpuId,
                            TemperatureC = gpuEntry.Temp,
                            TemperatureMinC = gpuEntry.TempMin,
                            TemperatureMaxC = gpuEntry.TempMax,
                            FanSpeedPercent = gpuEntry.FanSpeed,
                            FanSpeedRpm = (int)gpuEntry.FanSpeedRpm,
                            CoreClockMhz = gpuEntry.CoreClock,
                            MemoryClockMhz = gpuEntry.MemoryClock,
                            CoreVoltageMv = gpuEntry.CoreVoltage,
                            PowerPercent = gpuEntry.Power,
                            PerfLimitReason = (PerformanceLimitReason)gpuEntry.PerfLimitReason,
                            GpuName = GetStringFromBytes(gpuEntry.GpuName),
                            Timestamp = DateTime.Now
                        };

                        return gpuData;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to read Afterburner data: {ex.Message}");

                    // Check if Afterburner is still running
                    if (!CheckAfterburnerRunning())
                    {
                        _logging.Info("MSI Afterburner no longer running, disconnecting");
                        Disconnect();
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Check for conflicts between OmenCore and Afterburner.
        /// </summary>
        public ConflictCheckResult CheckForConflicts()
        {
            var result = new ConflictCheckResult
            {
                Timestamp = DateTime.Now,
                Conflicts = new List<string>()
            };

            try
            {
                // Check if Afterburner is controlling fan speed
                var gpuData = ReadGpuData();
                if (gpuData != null && gpuData.FanSpeedPercent > 0)
                {
                    // If Afterburner is controlling fan speed, it might conflict
                    result.Conflicts.Add("MSI Afterburner is actively controlling GPU fan speed");
                }

                // Check for performance limit reasons that might indicate conflicts
                if (gpuData != null && gpuData.PerfLimitReason != PerformanceLimitReason.None)
                {
                    result.Conflicts.Add($"GPU performance limited by: {gpuData.PerfLimitReason}");
                }

                // Check if both applications are trying to control the same hardware
                if (IsAfterburnerRunning && IsOmenCoreControllingGpu())
                {
                    result.Conflicts.Add("Both OmenCore and MSI Afterburner may be controlling GPU settings");
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Error checking for conflicts: {ex.Message}", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            result.HasConflicts = result.Conflicts.Count > 0;
            if (result.HasConflicts)
            {
                result.Success = false;
            }

            return result;
        }

        /// <summary>
        /// Get recommendations for resolving conflicts.
        /// </summary>
        public List<string> GetConflictResolutionRecommendations()
        {
            var recommendations = new List<string>();

            if (!IsAfterburnerRunning)
            {
                recommendations.Add("MSI Afterburner is not running - no conflicts detected");
                return recommendations;
            }

            var conflictResult = CheckForConflicts();

            if (conflictResult.HasConflicts)
            {
                recommendations.Add("⚠️ Conflicts detected between OmenCore and MSI Afterburner:");
                recommendations.AddRange(conflictResult.Conflicts);

                recommendations.Add("");
                recommendations.Add("Recommended solutions:");
                recommendations.Add("1. Close MSI Afterburner if not needed for GPU monitoring");
                recommendations.Add("2. In MSI Afterburner, disable fan control (set to 'Auto')");
                recommendations.Add("3. Use either OmenCore OR Afterburner for GPU control, not both");
                recommendations.Add("4. Consider using OmenCore's GPU monitoring instead of Afterburner");
            }
            else
            {
                recommendations.Add("✓ No conflicts detected - safe to run both applications");
            }

            return recommendations;
        }

        /// <summary>
        /// Attempt to resolve conflicts automatically.
        /// </summary>
        public Task<ConflictResolutionResult> ResolveConflictsAsync()
        {
            var result = new ConflictResolutionResult
            {
                Timestamp = DateTime.Now,
                ActionsTaken = new List<string>(),
                Success = false
            };

            try
            {
                var conflicts = CheckForConflicts();

                if (!conflicts.HasConflicts)
                {
                    result.Success = true;
                    result.Message = "No conflicts detected - no action needed";
                    return Task.FromResult(result);
                }

                // For now, we can only provide recommendations
                // Automatic resolution would require more complex integration
                result.Message = "Conflicts detected but cannot be resolved automatically. Please follow manual resolution steps.";
                result.ActionsTaken.Add("Generated conflict resolution recommendations");
                result.Success = false; // Cannot resolve automatically yet

                _logging.Info("Conflict resolution attempted - manual intervention required");
            }
            catch (Exception ex)
            {
                _logging.Error($"Error during conflict resolution: {ex.Message}", ex);
                result.Message = $"Error during resolution: {ex.Message}";
            }

            return Task.FromResult(result);
        }

        private void MonitorAfterburner(object? state)
        {
            try
            {
                var gpuData = ReadGpuData();
                if (gpuData != null)
                {
                    AfterburnerDataReceived?.Invoke(this, new AfterburnerDataEventArgs(gpuData));
                }

                // Check for conflicts periodically
                var conflictResult = CheckForConflicts();
                if (conflictResult.HasConflicts)
                {
                    ConflictDetected?.Invoke(this, new ConflictDetectedEventArgs(conflictResult));
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Error in Afterburner monitoring: {ex.Message}", ex);
            }
        }

        private bool CheckAfterburnerRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName("MSIAfterburner");
                var rtssProcesses = Process.GetProcessesByName("RTSS");

                return processes.Length > 0 || rtssProcesses.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool IsOmenCoreControllingGpu()
        {
            // This would need to check if OmenCore is actively controlling GPU settings
            // For now, return false - would need integration with GPU control services
            return false;
        }

        private void Disconnect()
        {
            lock (_lock)
            {
                _monitoringTimer?.Dispose();
                _monitoringTimer = null;

                _sharedMemory?.Dispose();
                _sharedMemory = null;

                _isConnected = false;
                _logging.Info("Disconnected from MSI Afterburner shared memory");
            }
        }

        private static string GetStringFromBytes(byte[] bytes)
        {
            var nullIndex = Array.IndexOf(bytes, (byte)0);
            var length = nullIndex >= 0 ? nullIndex : bytes.Length;
            return Encoding.UTF8.GetString(bytes, 0, length);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Disconnect();
            _disposed = true;
        }
    }

    /// <summary>
    /// Result of conflict resolution attempt.
    /// </summary>
    public class ConflictResolutionResult
    {
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<string> ActionsTaken { get; set; } = new();
    }

    /// <summary>
    /// Event args for Afterburner data updates.
    /// </summary>
    public class AfterburnerDataEventArgs : EventArgs
    {
        public AfterburnerGpuData GpuData { get; }

        public AfterburnerDataEventArgs(AfterburnerGpuData gpuData)
        {
            GpuData = gpuData;
        }
    }
}