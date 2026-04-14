using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services
{
    /// <summary>
    /// Detects MSI Afterburner and other conflicting software that may interfere with OmenCore.
    /// Also provides GPU telemetry from Afterburner when available.
    /// </summary>
    public class ConflictDetectionService : IDisposable
    {
        private readonly LoggingService _logging;
        private bool _disposed;
        
        // MSI Afterburner shared memory
        private const string MAB_SHARED_MEMORY_NAME = "MAHMSharedMemory";
        private MemoryMappedFile? _mabMmf;
        private MemoryMappedViewAccessor? _mabAccessor;
        
        /// <summary>
        /// Known conflicting applications.
        /// </summary>
        public static readonly Dictionary<string, ConflictInfo> KnownConflicts = new()
        {
            ["MSIAfterburner"] = new ConflictInfo
            {
                Name = "MSI Afterburner",
                ProcessNames = new[] { "MSIAfterburner" },
                Impact = ConflictSeverity.Low,
                Description = "Coexistence active — OmenCore reads GPU data from Afterburner shared memory",
                Mitigation = "No action needed — apps share GPU data automatically"
            },
            ["RTSS"] = new ConflictInfo
            {
                Name = "RivaTuner Statistics Server",
                ProcessNames = new[] { "RTSS", "RTSSHooksLoader", "RTSSHooksLoader64" },
                Impact = ConflictSeverity.High,
                Description = "RTSS injects D3D/DXGI hooks that corrupt WPF's render channel and can cause repeated UCEERR_RENDERTHREADFAILURE crashes",
                Mitigation = "Close RTSS, or enable Software Rendering in OmenCore Settings → General to avoid render-thread conflicts"
            },
            ["OmenHub"] = new ConflictInfo
            {
                Name = "OMEN Gaming Hub",
                ProcessNames = new[] { "OGHAgent", "HPOmenCommandCenter", "OMEN Gaming Hub", 
                    "OmenLightingService", "OmenLighting", "HP.OMEN.GameHub" },
                Impact = ConflictSeverity.Medium,
                Description = "Both apps may try to control fans simultaneously",
                Mitigation = "Use OmenCore OR OMEN Gaming Hub, not both for fan control"
            },
            ["XTU"] = new ConflictInfo
            {
                Name = "Intel XTU",
                ProcessNames = new[] { "XTU", "IntelXTU", "XTU3Service" },
                Impact = ConflictSeverity.High,
                Description = "XTU loads its own WinRing0 driver which conflicts with OmenCore",
                Mitigation = "Close XTU before using OmenCore EC features"
            },
            ["ThrottleStop"] = new ConflictInfo
            {
                Name = "ThrottleStop",
                ProcessNames = new[] { "ThrottleStop" },
                Impact = ConflictSeverity.Medium,
                Description = "May conflict with power/undervolt settings",
                Mitigation = "Avoid using both apps for the same settings"
            },
            ["HWiNFO"] = new ConflictInfo
            {
                Name = "HWiNFO",
                ProcessNames = new[] { "HWiNFO32", "HWiNFO64" },
                Impact = ConflictSeverity.Low,
                Description = "Generally compatible, but sensor polling may cause minor delays",
                Mitigation = "Usually no action needed"
            },
            ["FanControl"] = new ConflictInfo
            {
                Name = "FanControl",
                ProcessNames = new[] { "FanControl" },
                Impact = ConflictSeverity.High,
                Description = "Both apps will try to control fans - unpredictable behavior",
                Mitigation = "Close FanControl before using OmenCore"
            }
        };
        
        public bool IsMsiAfterburnerRunning { get; private set; }
        public bool IsMsiAfterburnerSharedMemoryAvailable { get; private set; }
        
        /// <summary>
        /// True when Afterburner shared memory is providing GPU data to WmiBiosMonitor,
        /// eliminating NVAPI polling contention.
        /// </summary>
        public bool AfterburnerCoexistenceActive { get; set; }
        public List<DetectedConflict> DetectedConflicts { get; private set; } = new();
        
        public event Action<List<DetectedConflict>>? OnConflictsDetected;
        
        public ConflictDetectionService(LoggingService logging)
        {
            _logging = logging;
        }
        
        /// <summary>
        /// Scan for conflicting applications.
        /// </summary>
        public async Task<List<DetectedConflict>> ScanForConflictsAsync()
        {
            return await Task.Run(() =>
            {
                var conflicts = new List<DetectedConflict>();
                
                try
                {
                    var runningProcesses = Process.GetProcesses()
                        .Select(p => { try { return p.ProcessName; } catch { return ""; } })
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var kvp in KnownConflicts)
                    {
                        var matchedProcesses = kvp.Value.ProcessNames
                            .Where(p => runningProcesses.Contains(p))
                            .ToList();
                        
                        if (matchedProcesses.Any())
                        {
                            conflicts.Add(new DetectedConflict
                            {
                                Id = kvp.Key,
                                Info = kvp.Value,
                                RunningProcesses = matchedProcesses
                            });
                            
                            _logging.Info($"[ConflictDetection] Detected: {kvp.Value.Name} ({string.Join(", ", matchedProcesses)})");
                        }
                    }
                    
                    // Check MSI Afterburner specifically
                    IsMsiAfterburnerRunning = conflicts.Any(c => c.Id == "MSIAfterburner");
                    if (IsMsiAfterburnerRunning)
                    {
                        TryConnectToAfterburnerSharedMemory();
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"[ConflictDetection] Scan failed: {ex.Message}", ex);
                }
                
                DetectedConflicts = conflicts;
                OnConflictsDetected?.Invoke(conflicts);
                
                return conflicts;
            });
        }

        /// <summary>
        /// Periodically scan for conflicts asynchronously until cancellation.
        /// </summary>
        public async Task MonitorConflictsAsync(TimeSpan interval, CancellationToken ct)
        {
            if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(10);

            while (!ct.IsCancellationRequested)
            {
                await ScanForConflictsAsync();
                try
                {
                    await Task.Delay(interval, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
        
        /// <summary>
        /// Try to connect to MSI Afterburner's shared memory for GPU telemetry.
        /// </summary>
        private bool TryConnectToAfterburnerSharedMemory()
        {
            try
            {
                _mabMmf?.Dispose();
                _mabAccessor?.Dispose();
                
                _mabMmf = MemoryMappedFile.OpenExisting(MAB_SHARED_MEMORY_NAME, MemoryMappedFileRights.Read);
                _mabAccessor = _mabMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                
                // Verify signature
                uint signature = _mabAccessor.ReadUInt32(0);
                if (signature == 0x4D41484D) // "MAHM"
                {
                    IsMsiAfterburnerSharedMemoryAvailable = true;
                    _logging.Info("[ConflictDetection] MSI Afterburner shared memory connected");
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                // Afterburner running but shared memory not enabled
                _logging.Info("[ConflictDetection] MSI Afterburner shared memory not available");
            }
            catch (Exception ex)
            {
                _logging.Warn($"[ConflictDetection] Afterburner shared memory error: {ex.Message}");
            }
            
            IsMsiAfterburnerSharedMemoryAvailable = false;
            return false;
        }
        
        /// <summary>
        /// Read GPU data from MSI Afterburner shared memory.
        /// </summary>
        private bool _mahmDumpLogged = false;

        public AfterburnerGpuData? ReadAfterburnerGpuData()
        {
            if (!IsMsiAfterburnerSharedMemoryAvailable || _mabAccessor == null)
            {
                // Try to reconnect
                if (IsMsiAfterburnerRunning && !TryConnectToAfterburnerSharedMemory())
                    return null;
                    
                if (_mabAccessor == null)
                    return null;
            }
            
            try
            {
                // MAHM shared memory header
                var header = new MAHMSharedMemoryHeader();
                _mabAccessor.Read(0, out header);
                
                if (header.dwSignature != 0x4D41484D || header.dwNumEntries == 0)
                    return null;

                if (!_mahmDumpLogged)
                    _logging.Info($"[MAHM] Header: version={header.dwVersion}, headerSize={header.dwHeaderSize}, numEntries={header.dwNumEntries}, entrySize={header.dwEntrySize}");

                var result = new AfterburnerGpuData();
                
                // Use dwHeaderSize from the file as the authoritative entry array offset,
                // fallback to our struct size if it's clearly wrong (0 or absurdly large).
                int entryOffset = (header.dwHeaderSize >= 16 && header.dwHeaderSize <= 512)
                    ? (int)header.dwHeaderSize
                    : Marshal.SizeOf<MAHMSharedMemoryHeader>();
                int entrySize = (int)header.dwEntrySize;
                
                // MAHM v2 entry layout:
                //   szSrcName[MAX_PATH]       = offset 0    (260 bytes)
                //   szSrcUnits[MAX_PATH]      = offset 260  (260 bytes)
                //   szLocSrcName[MAX_PATH]    = offset 520  (260 bytes)
                //   szLocSrcUnits[MAX_PATH]   = offset 780  (260 bytes)
                //   dwSrcId                   = offset 1040 (4 bytes)
                //   dwSrcFlags                = offset 1044 (4 bytes)
                //   data (float)              = offset 1048 (4 bytes)
                // For older v1 format (no localized names): data at offset 528
                // MAHM v2 (entrySize ~1072): 4 * MAX_PATH(260) + 8 = offset 1048
                // MAHM v3 (entrySize ~1324): 5 * MAX_PATH(260) + 8 = offset 1308
                int dataOffset = entrySize >= 1300 ? 1308 :
                                 entrySize >= 1072 ? 1048 : 528;
                
                for (int i = 0; i < header.dwNumEntries && i < 256; i++)
                {
                    int offset = entryOffset + (i * entrySize);
                    
                    // Read source name + units (first 2 x MAX_PATH chars)
                    byte[] nameBytes = new byte[260];
                    _mabAccessor.ReadArray(offset, nameBytes, 0, 260);
                    string name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                    byte[] unitBytes = new byte[260];
                    _mabAccessor.ReadArray(offset + 260, unitBytes, 0, 260);
                    string units = System.Text.Encoding.ASCII.GetString(unitBytes).TrimEnd('\0');
                    
                    // Read data value at the correct offset within the entry
                    float value = _mabAccessor.ReadSingle(offset + dataOffset);
                    
                    // Strip any GPU index prefix wherever it appears in the name so both
                    // "GPU temperature" and "GPU1 temperature" / "    0   GPU1 temperature"
                    // all normalise to "GPU temperature" for matching.
                    var normName = System.Text.RegularExpressions.Regex
                        .Replace(name, @"(?i)GPU\d+\s+", "GPU ").Trim();

                    // First-time diagnostic: dump a subset of MAHM entries so logs stay readable.
                    if (!_mahmDumpLogged && i < 24)
                        _logging.Info($"[MAHM] Entry[{i}] raw='{name}' norm='{normName}' val={value:F1} (entrySize={entrySize}, dataOffset={dataOffset})");

                    if (normName.Contains("GPU temperature", StringComparison.OrdinalIgnoreCase) && !normName.Contains("limit", StringComparison.OrdinalIgnoreCase))
                        result.GpuTemperature = value;
                    else if (normName.Contains("GPU usage", StringComparison.OrdinalIgnoreCase) &&
                             !normName.Contains("FB usage", StringComparison.OrdinalIgnoreCase) &&
                             !normName.Contains("VID usage", StringComparison.OrdinalIgnoreCase) &&
                             !normName.Contains("BUS usage", StringComparison.OrdinalIgnoreCase))
                        result.GpuLoadPercent = value;
                    else if (normName.Contains("GPU memory usage", StringComparison.OrdinalIgnoreCase))
                        result.VramUsedPercent = value;
                    else if (normName.Contains("Memory usage", StringComparison.OrdinalIgnoreCase) && !normName.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        result.VramUsedPercent = value; // fallback for older format
                    else if (normName.Contains("GPU fan", StringComparison.OrdinalIgnoreCase) && normName.Contains("RPM", StringComparison.OrdinalIgnoreCase))
                        result.FanSpeedRpm = (int)value;
                    else if (normName.Contains("GPU fan", StringComparison.OrdinalIgnoreCase) && normName.Contains("%", StringComparison.OrdinalIgnoreCase))
                        result.FanSpeedPercent = value;
                    else if (normName.Contains("GPU power", StringComparison.OrdinalIgnoreCase) && !normName.Contains("limit", StringComparison.OrdinalIgnoreCase))
                    {
                        result.GpuPower = value;
                        result.GpuPowerUnit = units;
                    }
                    else if (normName.Contains("GPU core clock", StringComparison.OrdinalIgnoreCase) || normName.Contains("Core clock", StringComparison.OrdinalIgnoreCase))
                        result.CoreClockMhz = value;
                    else if (normName.Contains("Memory clock", StringComparison.OrdinalIgnoreCase))
                        result.MemoryClockMhz = value;
                }

                if (!_mahmDumpLogged)
                {
                    string powerUnit = string.IsNullOrWhiteSpace(result.GpuPowerUnit) ? "(unit unknown)" : result.GpuPowerUnit;
                    _logging.Info($"[MAHM] First-read result: GpuTemp={result.GpuTemperature:F1}°C, Load={result.GpuLoadPercent:F0}%, Clock={result.CoreClockMhz:F0}MHz, Power={result.GpuPower:F1} {powerUnit}");
                    _mahmDumpLogged = true;
                }

                result.Timestamp = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                _logging.Warn($"[ConflictDetection] Failed to read Afterburner data: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Check if MSI Afterburner is installed (even if not running).
        /// </summary>
        public static bool IsAfterburnerInstalled()
        {
            try
            {
                // Check common install paths
                string[] paths =
                {
                    @"C:\Program Files (x86)\MSI Afterburner\MSIAfterburner.exe",
                    @"C:\Program Files\MSI Afterburner\MSIAfterburner.exe"
                };
                
                if (paths.Any(File.Exists))
                    return true;
                
                // Check registry
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\MSI\Afterburner");
                if (key != null)
                    return true;
                    
                using var key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\MSI\Afterburner");
                return key32 != null;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Get a user-friendly summary of detected conflicts.
        /// </summary>
        public string GetConflictSummary()
        {
            if (DetectedConflicts.Count == 0)
                return "No conflicting applications detected.";
            
            var critical = DetectedConflicts.Where(c => c.Info.Impact == ConflictSeverity.High).ToList();
            var medium = DetectedConflicts.Where(c => c.Info.Impact == ConflictSeverity.Medium).ToList();
            var low = DetectedConflicts.Where(c => c.Info.Impact == ConflictSeverity.Low).ToList();
            
            var sb = new System.Text.StringBuilder();
            
            if (critical.Any())
            {
                sb.AppendLine("⚠️ HIGH IMPACT conflicts detected:");
                foreach (var c in critical)
                    sb.AppendLine($"  • {c.Info.Name}: {c.Info.Mitigation}");
            }
            
            if (medium.Any())
            {
                sb.AppendLine("⚡ Medium impact conflicts:");
                foreach (var c in medium)
                    sb.AppendLine($"  • {c.Info.Name}: {c.Info.Description}");
            }
            
            if (low.Any())
            {
                sb.AppendLine("ℹ️ Low impact (usually OK):");
                foreach (var c in low)
                    sb.AppendLine($"  • {c.Info.Name}");
            }
            
            return sb.ToString().TrimEnd();
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _mabAccessor?.Dispose();
            _mabMmf?.Dispose();
        }
        
        // MSI Afterburner shared memory structures
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MAHMSharedMemoryHeader
        {
            public uint dwSignature;     // 'MAHM' = 0x4D41484D
            public uint dwVersion;
            public uint dwHeaderSize;
            public uint dwNumEntries;
            public uint dwEntrySize;
            public int dwTime;           // Timestamp
        }
    }
    
    public enum ConflictSeverity
    {
        Low,
        Medium,
        High
    }
    
    public class ConflictInfo
    {
        public string Name { get; set; } = "";
        public string[] ProcessNames { get; set; } = Array.Empty<string>();
        public ConflictSeverity Impact { get; set; }
        public string Description { get; set; } = "";
        public string Mitigation { get; set; } = "";
    }
    
    public class DetectedConflict
    {
        public string Id { get; set; } = "";
        public ConflictInfo Info { get; set; } = new();
        public List<string> RunningProcesses { get; set; } = new();
    }
    
    public class AfterburnerGpuData
    {
        public DateTime Timestamp { get; set; }
        public float GpuTemperature { get; set; }
        public float GpuLoadPercent { get; set; }
        public float VramUsedPercent { get; set; }
        public int FanSpeedRpm { get; set; }
        public float FanSpeedPercent { get; set; }
        public float GpuPower { get; set; }
        public string GpuPowerUnit { get; set; } = string.Empty;
        public float CoreClockMhz { get; set; }
        public float MemoryClockMhz { get; set; }
        public int GpuId { get; set; }
        public float TemperatureC { get; set; }
        public float TemperatureMinC { get; set; }
        public float TemperatureMaxC { get; set; }
        public float CoreVoltageMv { get; set; }
        public float PowerPercent { get; set; }
        public PerformanceLimitReason PerfLimitReason { get; set; }
        public string GpuName { get; set; } = string.Empty;
    }

    public enum PerformanceLimitReason
    {
        None,
        Thermal,
        Power,
        Voltage,
        Unknown
    }
}
