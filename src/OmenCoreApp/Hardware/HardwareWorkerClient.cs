using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Client for the out-of-process hardware worker.
    /// Manages worker process lifecycle and communicates via named pipes.
    /// 
    /// Benefits:
    /// - Main app survives hardware monitoring crashes (NVML, etc.)
    /// - Auto-restarts worker if it crashes
    /// - Uses cached values during worker restart
    /// - Graceful degradation if worker unavailable
    /// </summary>
    public class HardwareWorkerClient : IDisposable
    {
        private const string PipeName = "OmenCore_HardwareWorker";
        private const string WorkerExeName = "OmenCore.HardwareWorker.exe";
        private const int ConnectionTimeoutMs = 5000;  // Increased for slow boot
        private const int RequestTimeoutMs = 2000;
        private const int MaxRestartAttempts = 5;
        private const int RestartCooldownMs = 5000;
        private const int WorkerStartupDelayMs = 2000;  // Increased for hardware scan time
        private const int MaxConnectionRetries = 5;     // More retries for slow boots
        
        private Process? _workerProcess;
        private NamedPipeClientStream? _pipeClient;
        private readonly Action<string>? _logger;
        
        private HardwareSample _cachedSample = new();
        private DateTime _lastSuccessfulRead = DateTime.MinValue;
        private int _restartAttempts = 0;
        private DateTime _lastRestartAttempt = DateTime.MinValue;
        private bool _disposed;
        private bool _enabled = true;
        private DateTime _lastPermanentDisable = DateTime.MinValue;
        private const int PermanentDisableCooldownMinutes = 30; // Allow re-enable after 30 minutes

        // Command queue for lifecycle protection
        private readonly Queue<string> _pendingCommands = new();
        private readonly object _commandQueueLock = new();
        private bool _replayingCommands = false;
        
        /// <summary>
        /// Whether the worker is currently connected and responding
        /// </summary>
        public bool IsConnected => _pipeClient?.IsConnected == true;
        
        /// <summary>
        /// Whether the out-of-process worker is enabled
        /// </summary>
        public bool IsEnabled => _enabled;
        
        /// <summary>
        /// Age of the last successful sample
        /// </summary>
        public TimeSpan SampleAge => DateTime.Now - _lastSuccessfulRead;
        
        public HardwareWorkerClient(Action<string>? logger = null)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Queue a command to be sent when worker is available
        /// </summary>
        public void QueueCommand(string command)
        {
            lock (_commandQueueLock)
            {
                if (_pendingCommands.Count < 10) // Limit queue size
                {
                    _pendingCommands.Enqueue(command);
                    _logger?.Invoke($"[Worker] Queued command: {command} (queue size: {_pendingCommands.Count})");
                }
                else
                {
                    _logger?.Invoke($"[Worker] Command queue full, dropping: {command}");
                }
            }
        }
        
        /// <summary>
        /// Replay queued commands after worker reconnection
        /// </summary>
        private async Task ReplayQueuedCommandsAsync()
        {
            if (_replayingCommands) return;
            
            _replayingCommands = true;
            try
            {
                List<string> commandsToReplay;
                lock (_commandQueueLock)
                {
                    commandsToReplay = _pendingCommands.ToList();
                    _pendingCommands.Clear();
                }
                
                if (commandsToReplay.Any())
                {
                    _logger?.Invoke($"[Worker] Replaying {commandsToReplay.Count} queued commands");
                    
                    foreach (var command in commandsToReplay)
                    {
                        try
                        {
                            var response = await SendRequestAsync(command);
                            _logger?.Invoke($"[Worker] Replayed command '{command}' -> '{response}'");
                        }
                        catch (Exception ex)
                        {
                            _logger?.Invoke($"[Worker] Failed to replay command '{command}': {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                _replayingCommands = false;
            }
        }
        
        /// <summary>
        /// Start the hardware worker process and connect
        /// </summary>
        public async Task<bool> StartAsync()
        {
            if (!_enabled) return false;
            
            try
            {
                // FIRST: Try to connect to an already-running worker (survives parent restarts)
                if (await TryConnectToExistingWorkerAsync())
                {
                    _logger?.Invoke("[Worker] Connected to existing worker — no new process needed");
                    return true;
                }
                
                // Find worker executable
                var workerPath = FindWorkerExecutable();
                if (workerPath == null)
                {
                    _logger?.Invoke("[Worker] Hardware worker executable not found, falling back to in-process");
                    _enabled = false;
                    return false;
                }
                
                // Start worker process
                _logger?.Invoke($"[Worker] Starting hardware worker: {workerPath}");
                
                // Pass our PID so worker can exit when we die
                var currentPid = Environment.ProcessId;
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = workerPath,
                    Arguments = currentPid.ToString(),  // Pass parent PID
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                
                _workerProcess = Process.Start(startInfo);
                if (_workerProcess == null)
                {
                    _logger?.Invoke("[Worker] Failed to start worker process");
                    return false;
                }
                
                _logger?.Invoke($"[Worker] Worker started with PID {_workerProcess.Id}");
                
                // Wait for worker to initialize pipe server (longer delay for boot scenarios)
                await Task.Delay(WorkerStartupDelayMs);
                
                // Connect to worker with retries
                for (int retry = 0; retry < MaxConnectionRetries; retry++)
                {
                    if (await ConnectAsync())
                    {
                        // Register as new parent
                        await RegisterAsParentAsync();
                        return true;
                    }
                    
                    if (retry < MaxConnectionRetries - 1)
                    {
                        _logger?.Invoke($"[Worker] Connection attempt {retry + 1} failed, retrying...");
                        await Task.Delay(1000);
                    }
                }
                
                _logger?.Invoke("[Worker] All connection attempts failed");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Error starting worker: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Try to connect to an already-running worker process (from a previous app session).
        /// This enables seamless temp readings across app restarts.
        /// </summary>
        private async Task<bool> TryConnectToExistingWorkerAsync()
        {
            try
            {
                _pipeClient?.Dispose();
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                
                // Short timeout — either the worker is there or it isn't
                using var cts = new CancellationTokenSource(1500);
                await _pipeClient.ConnectAsync(cts.Token);
                
                // Verify it's alive
                var response = await SendRequestAsync("PING");
                if (response != "PONG")
                {
                    _logger?.Invoke($"[Worker] Existing worker ping failed: {response}");
                    _pipeClient?.Dispose();
                    _pipeClient = null;
                    return false;
                }
                
                // Register ourselves as the new parent
                await RegisterAsParentAsync();
                
                _restartAttempts = 0;
                return true;
            }
            catch (OperationCanceledException)
            {
                // No existing worker — that's fine
                _pipeClient?.Dispose();
                _pipeClient = null;
                return false;
            }
            catch (Exception)
            {
                // Connection failed — no existing worker
                _pipeClient?.Dispose();
                _pipeClient = null;
                return false;
            }
        }
        
        /// <summary>
        /// Tell the worker to monitor our process as the new parent.
        /// This re-attaches the orphan watchdog to our PID.
        /// </summary>
        private async Task RegisterAsParentAsync()
        {
            try
            {
                var pid = Environment.ProcessId;
                var response = await SendRequestAsync($"SET_PARENT {pid}");
                if (response == "OK")
                {
                    _logger?.Invoke($"[Worker] Registered as new parent (PID {pid})");
                }
                else
                {
                    _logger?.Invoke($"[Worker] SET_PARENT response: {response}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Failed to register as parent: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Tell the worker to disable battery monitoring (dead/removed battery).
        /// Prevents Win32_Battery WMI queries that cause EC timeout errors on systems with dead batteries.
        /// </summary>
        public async Task SendDisableBatteryAsync()
        {
            try
            {
                var response = await SendRequestAsync("DISABLE_BATTERY");
                if (response == "OK")
                {
                    _logger?.Invoke("[Worker] Battery monitoring disabled in worker");
                }
                else
                {
                    _logger?.Invoke($"[Worker] DISABLE_BATTERY response: {response}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Failed to disable battery monitoring: {ex.Message}");
            }
        }
        
        private string? FindWorkerExecutable()
        {
            // Check same directory as main exe
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var workerPath = Path.Combine(appDir, WorkerExeName);
            
            if (File.Exists(workerPath))
                return workerPath;
            
            // Check Program Files installation
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            workerPath = Path.Combine(programFiles, "OmenCore", WorkerExeName);
            
            if (File.Exists(workerPath))
                return workerPath;
            
            // Check debug/dev paths
            var devPath = Path.Combine(appDir, "..", "..", "..", "OmenCore.HardwareWorker", "bin", "Release", "net8.0-windows", WorkerExeName);
            if (File.Exists(devPath))
                return Path.GetFullPath(devPath);
            
            devPath = Path.Combine(appDir, "..", "..", "..", "OmenCore.HardwareWorker", "bin", "Debug", "net8.0-windows", WorkerExeName);
            if (File.Exists(devPath))
                return Path.GetFullPath(devPath);
            
            return null;
        }
        
        private async Task<bool> ConnectAsync()
        {
            try
            {
                _pipeClient?.Dispose();
                // CurrentUserOnly restricts the pipe to the current user session for security
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                
                using var cts = new CancellationTokenSource(ConnectionTimeoutMs);
                await _pipeClient.ConnectAsync(cts.Token);
                
                // Test connection with ping
                var response = await SendRequestAsync("PING");
                if (response == "PONG")
                {
                    _logger?.Invoke("[Worker] Connected to hardware worker");
                    _restartAttempts = 0;
                    
                    // Replay any queued commands after successful reconnection
                    _ = ReplayQueuedCommandsAsync();
                    
                    return true;
                }
                
                _logger?.Invoke($"[Worker] Unexpected ping response: {response}");
                return false;
            }
            catch (OperationCanceledException)
            {
                _logger?.Invoke("[Worker] Connection timeout");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Connection error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get current hardware sample from worker
        /// </summary>
        public async Task<HardwareSample?> GetSampleAsync()
        {
            if (!_enabled) return null;
            
            // Check if we need to restart worker
            if (!IsConnected || (_workerProcess?.HasExited == true))
            {
                // Try connecting to an existing worker first (may have survived a parent restart)
                if (await TryConnectToExistingWorkerAsync())
                {
                    _logger?.Invoke("[Worker] Reconnected to existing worker on demand");
                }
                else if (!await TryRestartWorkerAsync())
                {
                    // Return cached sample if available
                    return SampleAge < TimeSpan.FromSeconds(10) ? _cachedSample : null;
                }
            }
            
            try
            {
                var json = await SendRequestAsync("GET");
                if (string.IsNullOrEmpty(json) || json == "UNKNOWN")
                {
                    _logger?.Invoke($"[Worker] Empty/unknown response from worker");
                    return _cachedSample;
                }
                
                _logger?.Invoke($"[Worker] Received JSON ({json.Length} bytes): GPU={json.Contains("GpuTemperature", StringComparison.OrdinalIgnoreCase)}");
                
                var sample = JsonSerializer.Deserialize<HardwareSample>(json);
                if (sample != null)
                {
                    _logger?.Invoke($"[Worker] Deserialized sample: CPU={sample.CpuTemperature}°C, GPU={sample.GpuTemperature}°C, CPULoad={sample.CpuLoad}%, GPULoad={sample.GpuLoad}%, RAM={sample.RamUsage}GB");
                    _cachedSample = sample;
                    _lastSuccessfulRead = DateTime.Now;
                }
                else
                {
                    _logger?.Invoke($"[Worker] Failed to deserialize sample from JSON");
                }
                
                return sample ?? _cachedSample;
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Error getting sample: {ex.Message}");
                return _cachedSample;
            }
        }
        
        private async Task<string> SendRequestAsync(string request)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected)
                return "";
            
            try
            {
                var requestBytes = Encoding.UTF8.GetBytes(request);
                await _pipeClient.WriteAsync(requestBytes, 0, requestBytes.Length);
                await _pipeClient.FlushAsync();
                
                var buffer = new byte[65536];
                using var cts = new CancellationTokenSource(RequestTimeoutMs);
                var bytesRead = await _pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                
                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Request error: {ex.Message}");
                return "";
            }
        }
        
        private async Task<bool> TryRestartWorkerAsync()
        {
            // Check if we're in permanent disable cooldown
            if (!_enabled && DateTime.Now - _lastPermanentDisable < TimeSpan.FromMinutes(PermanentDisableCooldownMinutes))
            {
                var remaining = TimeSpan.FromMinutes(PermanentDisableCooldownMinutes) - (DateTime.Now - _lastPermanentDisable);
                _logger?.Invoke($"[Worker] In permanent disable cooldown. {remaining.TotalMinutes:F1} minutes remaining.");
                return false;
            }
            
            // Re-enable if cooldown expired
            if (!_enabled && DateTime.Now - _lastPermanentDisable >= TimeSpan.FromMinutes(PermanentDisableCooldownMinutes))
            {
                _logger?.Invoke($"[Worker] Permanent disable cooldown expired, re-enabling worker");
                _enabled = true;
                _restartAttempts = 0; // Reset restart counter
            }
            
            // Check cooldown
            if (DateTime.Now - _lastRestartAttempt < TimeSpan.FromMilliseconds(RestartCooldownMs))
                return false;
            
            // Check max attempts - use exponential backoff after max attempts
            if (_restartAttempts >= MaxRestartAttempts)
            {
                if (_restartAttempts == MaxRestartAttempts)
                {
                    _logger?.Invoke($"[Worker] Max restart attempts ({MaxRestartAttempts}) reached, entering cooldown period");
                    _lastPermanentDisable = DateTime.Now;
                    _enabled = false;
                    _restartAttempts++;
                }
                return false;
            }
            
            _restartAttempts++;
            _lastRestartAttempt = DateTime.Now;
            _logger?.Invoke($"[Worker] Attempting restart ({_restartAttempts}/{MaxRestartAttempts})...");
            
            // Kill existing process if still running
            try
            {
                if (_workerProcess != null && !_workerProcess.HasExited)
                {
                    _workerProcess.Kill();
                    _workerProcess.WaitForExit(1000);
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Failed to kill existing process: {ex.Message}");
            }
            
            return await StartAsync();
        }
        
        /// <summary>
        /// Stop the worker process gracefully
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                if (_pipeClient?.IsConnected == true)
                {
                    await SendRequestAsync("SHUTDOWN");
                    await Task.Delay(500);
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Error sending shutdown command: {ex.Message}");
            }
            
            try
            {
                if (_workerProcess != null && !_workerProcess.HasExited)
                {
                    _workerProcess.Kill();
                    _workerProcess.WaitForExit(1000);
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Error killing worker process: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            try
            {
                // Fire and forget shutdown with exception handling
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await StopAsync();
                    }
                    finally
                    {
                        _pipeClient?.Dispose();
                        _workerProcess?.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Worker] Dispose error: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Hardware sample data - matches worker's HardwareSample class
    /// </summary>
    public class HardwareSample
    {
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Indicates if the sample contains fresh data from hardware sensors.
        /// False if sensors could not be read (stale data from last successful read).
        /// </summary>
        public bool IsFresh { get; set; } = true;
        
        /// <summary>
        /// Number of consecutive stale readings (for staleness detection)
        /// </summary>
        public int StaleCount { get; set; } = 0;
        
        // CPU
        public double CpuTemperature { get; set; }
        public double CpuLoad { get; set; }
        public double CpuPower { get; set; }
        public List<double> CpuCoreClocks { get; set; } = new();
        
        // GPU
        public string GpuName { get; set; } = "";
        public double GpuTemperature { get; set; }
        public double GpuHotspot { get; set; }
        public double GpuLoad { get; set; }
        public double GpuPower { get; set; }
        public double GpuClock { get; set; }
        public double GpuMemoryClock { get; set; }
        public double GpuVoltage { get; set; }
        public double GpuCurrent { get; set; }
        public double VramUsage { get; set; }
        public double VramTotal { get; set; }
        
        // Memory
        public double RamUsage { get; set; }
        public double RamTotal { get; set; }
        
        // Storage
        public double SsdTemperature { get; set; }
        
        // Battery
        public double BatteryCharge { get; set; }
        public double BatteryDischargeRate { get; set; }
        public bool IsOnAc { get; set; } = true;
        
        // Fans (name -> RPM)
        public Dictionary<string, double> FanSpeeds { get; set; } = new();
    }
}
