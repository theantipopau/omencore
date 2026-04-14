using System;
using System.Buffers;
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
        private readonly bool _orphanTimeoutEnabled;
        private readonly int _orphanTimeoutMinutes;
        private bool _ownsWorkerProcess;
        
        private HardwareSample _cachedSample = new();
        private DateTime _lastSuccessfulRead = DateTime.MinValue;
        private int _restartAttempts = 0;
        private DateTime _lastRestartAttempt = DateTime.MinValue;
        private bool _disposed;
        private bool _enabled = true;
        private DateTime _lastPermanentDisable = DateTime.MinValue;
        private const int PermanentDisableCooldownMinutes = 5; // Allow re-enable after 5 minutes (was 30 — too aggressive, causes frozen temps)

        // Command queue for lifecycle protection
        private readonly Queue<string> _pendingCommands = new();
        private readonly object _commandQueueLock = new();
        private bool _replayingCommands = false;
        private readonly string _workerSessionId = Guid.NewGuid().ToString("N")[..8];
        
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
        
        public HardwareWorkerClient(Action<string>? logger = null, bool orphanTimeoutEnabled = true, int orphanTimeoutMinutes = 5)
        {
            _logger = logger;
            _orphanTimeoutEnabled = orphanTimeoutEnabled;
            _orphanTimeoutMinutes = Math.Clamp(orphanTimeoutMinutes, 1, 60); // Clamp to reasonable range
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
            var startupCorrelationId = CreateOperationCorrelationId("start");
            LogWorker("Worker startup requested", startupCorrelationId);
            
            try
            {
                // FIRST: Try to connect to an already-running worker (survives parent restarts)
                if (await TryConnectToExistingWorkerAsync(startupCorrelationId))
                {
                    LogWorker("Connected to existing worker; skipped process launch", startupCorrelationId);
                    return true;
                }
                
                // Find worker executable
                var workerPath = FindWorkerExecutable();
                if (workerPath == null)
                {
                    LogWorker("Hardware worker executable not found; falling back to in-process monitoring", startupCorrelationId);
                    _enabled = false;
                    return false;
                }
                
                // Start worker process
                LogWorker($"Starting hardware worker: {workerPath}", startupCorrelationId);
                
                // Pass our PID so worker can exit when we die
                var currentPid = Environment.ProcessId;
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = workerPath,
                    Arguments = $"{currentPid} {_orphanTimeoutEnabled} {_orphanTimeoutMinutes}",  // Pass parent PID, orphan timeout enabled, timeout minutes
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                
                _workerProcess = Process.Start(startInfo);
                if (_workerProcess == null)
                {
                    LogWorker("Failed to start worker process", startupCorrelationId);
                    return false;
                }

                _ownsWorkerProcess = true;
                
                LogWorker($"Worker started with PID {_workerProcess.Id}", startupCorrelationId);
                
                // Wait for worker to initialize pipe server (longer delay for boot scenarios)
                await Task.Delay(WorkerStartupDelayMs);
                
                // Connect to worker with retries
                for (int retry = 0; retry < MaxConnectionRetries; retry++)
                {
                    if (await ConnectAsync(startupCorrelationId))
                    {
                        if (_ownsWorkerProcess && _workerProcess?.HasExited == true)
                        {
                            LogWorker("Launched worker handle exited before connection completed; attached to existing worker instance", startupCorrelationId);
                            ReleaseOwnedWorkerProcessHandle();
                        }

                        // Register as new parent
                        await RegisterAsParentAsync(startupCorrelationId);
                        return true;
                    }
                    
                    if (retry < MaxConnectionRetries - 1)
                    {
                        LogWorker($"Connection attempt {retry + 1} failed, retrying", startupCorrelationId);
                        await Task.Delay(1000);
                    }
                }
                
                LogWorker("All connection attempts failed", startupCorrelationId);
                return false;
            }
            catch (Exception ex)
            {
                LogWorker($"Error starting worker: {ex.Message}", startupCorrelationId);
                return false;
            }
        }
        
        /// <summary>
        /// Try to connect to an already-running worker process (from a previous app session).
        /// This enables seamless temp readings across app restarts.
        /// </summary>
        private async Task<bool> TryConnectToExistingWorkerAsync(string? correlationId = null)
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
                    LogWorker($"Existing worker ping failed: {response}", correlationId);
                    _pipeClient?.Dispose();
                    _pipeClient = null;
                    return false;
                }
                
                // Register ourselves as the new parent
                await RegisterAsParentAsync(correlationId);

                ReleaseOwnedWorkerProcessHandle();
                
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
        private async Task RegisterAsParentAsync(string? correlationId = null)
        {
            try
            {
                var pid = Environment.ProcessId;
                var response = await SendRequestAsync($"SET_PARENT {pid}");
                if (response == "OK")
                {
                    LogWorker($"Registered as new parent (PID {pid})", correlationId);
                }
                else
                {
                    LogWorker($"SET_PARENT response: {response}", correlationId);
                }
            }
            catch (Exception ex)
            {
                LogWorker($"Failed to register as parent: {ex.Message}", correlationId);
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
        
        private async Task<bool> ConnectAsync(string? correlationId = null)
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
                    LogWorker("Connected to hardware worker", correlationId);
                    _restartAttempts = 0;
                    
                    // Replay any queued commands after successful reconnection
                    _ = ReplayQueuedCommandsAsync();
                    
                    return true;
                }
                
                LogWorker($"Unexpected ping response: {response}", correlationId);
                return false;
            }
            catch (OperationCanceledException)
            {
                LogWorker("Connection timeout", correlationId);
                return false;
            }
            catch (Exception ex)
            {
                LogWorker($"Connection error: {ex.Message}", correlationId);
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
            if (ShouldRecoverConnection(IsConnected, _ownsWorkerProcess, _workerProcess?.HasExited == true))
            {
                var reconnectCorrelationId = CreateOperationCorrelationId("recover");
                // Try connecting to an existing worker first (may have survived a parent restart)
                if (await TryConnectToExistingWorkerAsync(reconnectCorrelationId))
                {
                    LogWorker("Reconnected to existing worker on demand", reconnectCorrelationId);
                    _restartAttempts = 0; // Reset on successful reconnect
                }
                else if (!await TryRestartWorkerAsync(reconnectCorrelationId))
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

        private static bool ShouldRecoverConnection(bool isConnected, bool ownsWorkerProcess, bool workerProcessExited)
        {
            if (!isConnected)
            {
                return true;
            }

            return ownsWorkerProcess && workerProcessExited;
        }

        private void ReleaseOwnedWorkerProcessHandle()
        {
            _ownsWorkerProcess = false;

            try
            {
                _workerProcess?.Dispose();
            }
            catch
            {
            }

            _workerProcess = null;
        }
        
        private async Task<string> SendRequestAsync(string request)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected)
                return "";
            
            var buffer = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                var requestBytes = Encoding.UTF8.GetBytes(request);
                await _pipeClient.WriteAsync(requestBytes, 0, requestBytes.Length);
                await _pipeClient.FlushAsync();
                
                using var cts = new CancellationTokenSource(RequestTimeoutMs);
                var bytesRead = await _pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                
                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Worker] Request error: {ex.Message}");
                return "";
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        
        private async Task<bool> TryRestartWorkerAsync(string? correlationId = null)
        {
            // Check if we're in permanent disable cooldown
            if (!_enabled && DateTime.Now - _lastPermanentDisable < TimeSpan.FromMinutes(PermanentDisableCooldownMinutes))
            {
                var remaining = TimeSpan.FromMinutes(PermanentDisableCooldownMinutes) - (DateTime.Now - _lastPermanentDisable);
                LogWorker($"In permanent disable cooldown. {remaining.TotalMinutes:F1} minutes remaining.", correlationId);
                return false;
            }
            
            // Re-enable if cooldown expired
            if (!_enabled && DateTime.Now - _lastPermanentDisable >= TimeSpan.FromMinutes(PermanentDisableCooldownMinutes))
            {
                LogWorker("Permanent disable cooldown expired; re-enabling worker", correlationId);
                _enabled = true;
                _restartAttempts = 0; // Reset restart counter
            }
            
            // Check cooldown — exponential backoff per attempt: 2s, 5s, 10s, 20s, 30s
            var cooldownMs = _restartAttempts switch
            {
                <= 1 => 2_000,
                2    => 5_000,
                3    => 10_000,
                4    => 20_000,
                _    => 30_000
            };
            if (DateTime.Now - _lastRestartAttempt < TimeSpan.FromMilliseconds(cooldownMs))
                return false;
            
            // Check max attempts
            if (_restartAttempts >= MaxRestartAttempts)
            {
                if (_restartAttempts == MaxRestartAttempts)
                {
                    LogWorker($"Max restart attempts ({MaxRestartAttempts}) reached; entering cooldown period", correlationId);
                    _lastPermanentDisable = DateTime.Now;
                    _enabled = false;
                    _restartAttempts++;
                }
                return false;
            }
            
            _restartAttempts++;
            _lastRestartAttempt = DateTime.Now;
            LogWorker($"Attempting restart ({_restartAttempts}/{MaxRestartAttempts})", correlationId);
            
            // Kill existing process if still running
            try
            {
                if (_workerProcess != null && !_workerProcess.HasExited)
                {
                    _workerProcess.Kill();
                    _workerProcess.WaitForExit(1000);
                }

                ReleaseOwnedWorkerProcessHandle();
            }
            catch (Exception ex)
            {
                LogWorker($"Failed to kill existing process: {ex.Message}", correlationId);
            }
            
            return await StartAsync();
        }

        private string CreateOperationCorrelationId(string operation)
        {
            return $"{operation}-{Guid.NewGuid().ToString("N")[..8]}";
        }

        private void LogWorker(string message, string? correlationId = null)
        {
            _logger?.Invoke(FormatWorkerLog(message, correlationId));
        }

        private string FormatWorkerLog(string message, string? correlationId = null)
        {
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                return $"[Worker][session={_workerSessionId}] {message}";
            }

            return $"[Worker][session={_workerSessionId}][correlation={correlationId}] {message}";
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

                ReleaseOwnedWorkerProcessHandle();
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
