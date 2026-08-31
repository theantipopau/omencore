using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Diagnostic service for capturing raw EC register dumps and system state for troubleshooting.
    /// Opt-in only - user must explicitly enable this for privacy.
    /// </summary>
    public class DiagnosticLoggingService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly Hardware.IEcAccess? _ecAccess;
        private bool _isEnabled;
        private bool _disposed;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private readonly List<EcSnapshot> _snapshots = new();
        private readonly object _snapshotLock = new();
        
        // Configuration
        private int _captureIntervalMs = 1000;
        private bool _captureEcRegisters = true;
        private bool _captureProcessList = false;
        
        /// <summary>
        /// EC registers to capture for fan control diagnostics.
        /// </summary>
        private static readonly ushort[] FanControlRegisters = new ushort[]
        {
            0x2C, 0x2D, 0x2E, 0x2F, // Fan speed %
            0x34, 0x35,              // Fan RPM (100 RPM units)
            0x44, 0x45, 0x46,        // Fan duty/mode
            0x62, 0x63,              // OMCC control
            0xCE, 0xCF,              // Performance/power
            0xEC, 0xF4               // Fan boost/state
        };
        
        /// <summary>
        /// Temperature registers
        /// </summary>
        private static readonly ushort[] ThermalRegisters = new ushort[]
        {
            0x57, 0x58, // CPU temps
            0x59, 0x5A, // GPU temps
            0x68, 0x69, // Memory temps
        };
        
        public bool IsEnabled => _isEnabled;
        public int SnapshotCount => _snapshots.Count;
        
        public event Action<string>? OnDiagnosticEvent;
        
        public DiagnosticLoggingService(LoggingService logging, Hardware.IEcAccess? ecAccess = null)
        {
            _logging = logging;
            _ecAccess = ecAccess;
        }
        
        /// <summary>
        /// Enable diagnostic capture mode.
        /// </summary>
        public void Enable(int captureIntervalMs = 1000, bool captureEc = true, bool captureProcesses = false)
        {
            if (_isEnabled) return;
            
            _captureIntervalMs = captureIntervalMs;
            _captureEcRegisters = captureEc;
            _captureProcessList = captureProcesses;
            _isEnabled = true;
            
            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
            
            _logging.Info($"[Diagnostics] Enabled - Interval: {captureIntervalMs}ms, EC: {captureEc}, Processes: {captureProcesses}");
            OnDiagnosticEvent?.Invoke("Diagnostic capture started");
        }
        
        /// <summary>
        /// Disable diagnostic capture.
        /// </summary>
        public void Disable()
        {
            if (!_isEnabled) return;
            
            _cts?.Cancel();
            try
            {
                _captureTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                // Expected when the capture task observes the cancellation above; surface
                // anything unexpected to the debugger without recursing into this service.
                Debug.WriteLine($"[DiagnosticLogging] Capture task did not stop cleanly: {ex.Message}");
            }
            
            _cts?.Dispose();
            _cts = null;
            _captureTask = null;
            _isEnabled = false;
            
            _logging.Info("[Diagnostics] Disabled");
            OnDiagnosticEvent?.Invoke("Diagnostic capture stopped");
        }
        
        /// <summary>
        /// Take a single snapshot manually.
        /// </summary>
        public EcSnapshot? CaptureSnapshot(string? label = null)
        {
            try
            {
                var snapshot = new EcSnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    Label = label ?? $"Manual-{_snapshots.Count + 1}"
                };
                
                if (_captureEcRegisters && _ecAccess?.IsAvailable == true)
                {
                    // Capture fan control registers
                    foreach (var reg in FanControlRegisters)
                    {
                        try
                        {
                            snapshot.FanRegisters[reg] = _ecAccess.ReadByte(reg);
                        }
                        catch
                        {
                            snapshot.FanRegisters[reg] = 0xFF; // Error marker
                        }
                    }
                    
                    // Capture thermal registers
                    foreach (var reg in ThermalRegisters)
                    {
                        try
                        {
                            snapshot.ThermalRegisters[reg] = _ecAccess.ReadByte(reg);
                        }
                        catch
                        {
                            snapshot.ThermalRegisters[reg] = 0xFF;
                        }
                    }
                }
                
                if (_captureProcessList)
                {
                    snapshot.ProcessList = GetRelevantProcesses();
                }
                
                lock (_snapshotLock)
                {
                    _snapshots.Add(snapshot);
                    
                    // Keep max 1000 snapshots
                    while (_snapshots.Count > 1000)
                    {
                        _snapshots.RemoveAt(0);
                    }
                }
                
                return snapshot;
            }
            catch (Exception ex)
            {
                _logging.Error($"[Diagnostics] Snapshot capture failed: {ex.Message}", ex);
                return null;
            }
        }
        
        /// <summary>
        /// Get all captured snapshots.
        /// </summary>
        public List<EcSnapshot> GetSnapshots()
        {
            lock (_snapshotLock)
            {
                return new List<EcSnapshot>(_snapshots);
            }
        }
        
        /// <summary>
        /// Clear all snapshots.
        /// </summary>
        public void ClearSnapshots()
        {
            lock (_snapshotLock)
            {
                _snapshots.Clear();
            }
            _logging.Info("[Diagnostics] Snapshots cleared");
        }
        
        /// <summary>
        /// Export diagnostic data to a file.
        /// </summary>
        public string ExportDiagnostics(string? outputPath = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== OmenCore Diagnostic Export ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Snapshots: {_snapshots.Count}");
            sb.AppendLine();
            
            // System info
            sb.AppendLine("=== System Information ===");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"64-bit: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"EC Backend: {(_ecAccess?.IsAvailable == true ? "Available" : "Not available")}");
            sb.AppendLine();
            
            // Conflicting processes
            sb.AppendLine("=== Relevant Running Processes ===");
            foreach (var proc in GetRelevantProcesses())
            {
                sb.AppendLine($"  - {proc}");
            }
            sb.AppendLine();
            
            // Snapshots
            sb.AppendLine("=== EC Snapshots ===");
            List<EcSnapshot> snapshots;
            lock (_snapshotLock)
            {
                snapshots = new List<EcSnapshot>(_snapshots);
            }
            
            foreach (var snap in snapshots)
            {
                sb.AppendLine($"--- {snap.Label} @ {snap.Timestamp:HH:mm:ss.fff} ---");
                
                sb.AppendLine("Fan Registers:");
                foreach (var kvp in snap.FanRegisters.OrderBy(k => k.Key))
                {
                    string regName = GetRegisterName(kvp.Key);
                    sb.AppendLine($"  0x{kvp.Key:X2} ({regName}): 0x{kvp.Value:X2} ({kvp.Value})");
                }
                
                sb.AppendLine("Thermal Registers:");
                foreach (var kvp in snap.ThermalRegisters.OrderBy(k => k.Key))
                {
                    sb.AppendLine($"  0x{kvp.Key:X2}: {kvp.Value}°C");
                }
                
                sb.AppendLine();
            }
            
            // Analysis hints
            sb.AppendLine("=== Analysis Hints ===");
            if (snapshots.Count >= 2)
            {
                var first = snapshots.First();
                var last = snapshots.Last();
                
                // Check if fan registers changed
                foreach (var reg in FanControlRegisters)
                {
                    if (first.FanRegisters.TryGetValue(reg, out var firstVal) &&
                        last.FanRegisters.TryGetValue(reg, out var lastVal))
                    {
                        if (firstVal != lastVal)
                        {
                            sb.AppendLine($"  Register 0x{reg:X2} changed: 0x{firstVal:X2} -> 0x{lastVal:X2}");
                        }
                    }
                }
            }
            
            string content = sb.ToString();
            
            if (outputPath == null)
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OmenCore", "diagnostics");
                Directory.CreateDirectory(logDir);
                outputPath = Path.Combine(logDir, $"diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            }
            
            File.WriteAllText(outputPath, content);
            _logging.Info($"[Diagnostics] Exported to {outputPath}");
            
            return outputPath;
        }
        
        private async Task CaptureLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    CaptureSnapshot($"Auto-{_snapshots.Count + 1}");
                    await Task.Delay(_captureIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"[Diagnostics] Capture error: {ex.Message}");
                    await Task.Delay(1000, ct);
                }
            }
        }
        
        private List<string> GetRelevantProcesses()
        {
            var relevant = new[] 
            { 
                "OmenCore", "HPOmenCommandCenter", "OGHAgent", "omen", 
                "MSIAfterburner", "RTSS", "RivaTuner", "HWiNFO", "HWMonitor",
                "GPU-Z", "ThrottleStop", "XTU", "IntelXTU", "FanControl",
                "SpeedFan", "AIDA64", "CoreTemp", "LibreHardwareMonitor"
            };
            
            var result = new List<string>();
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (relevant.Any(r => proc.ProcessName.Contains(r, StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Add($"{proc.ProcessName} (PID: {proc.Id})");
                        }
                    }
                    catch (Exception)
                    {
                        // Process exited between enumeration and inspection, or access is
                        // denied for a protected/system process — skip it. Logging per-process
                        // would be noise across dozens of normal system processes.
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiagnosticLogging] Relevant-process enumeration failed: {ex.Message}");
            }
            
            return result;
        }
        
        private static string GetRegisterName(ushort reg)
        {
            return reg switch
            {
                0x2C => "FAN1_XSS",
                0x2D => "FAN2_XSS",
                0x2E => "FAN1_PCT",
                0x2F => "FAN2_PCT",
                0x34 => "FAN1_RPM",
                0x35 => "FAN2_RPM",
                0x44 => "FAN1_DUTY",
                0x45 => "FAN2_DUTY",
                0x46 => "FAN_MODE",
                0x62 => "OMCC",
                0x63 => "TIMER",
                0xCE => "PERF_MODE",
                0xCF => "PWR_LIMIT",
                0xEC => "FAN_BOOST",
                0xF4 => "FAN_STATE",
                _ => "UNKNOWN"
            };
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            Disable();
        }
    }
    
    /// <summary>
    /// Represents a single EC register snapshot.
    /// </summary>
    public class EcSnapshot
    {
        public DateTime Timestamp { get; set; }
        public string Label { get; set; } = "";
        public Dictionary<ushort, byte> FanRegisters { get; set; } = new();
        public Dictionary<ushort, byte> ThermalRegisters { get; set; } = new();
        public List<string> ProcessList { get; set; } = new();
    }
}
