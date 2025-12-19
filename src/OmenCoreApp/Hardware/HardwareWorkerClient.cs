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
        private const int WorkerStartupDelayMs = 1500;  // Give worker more time to start pipe server
        private const int MaxConnectionRetries = 3;
        
        private Process? _workerProcess;
        private NamedPipeClientStream? _pipeClient;
        private readonly object _lock = new();
        private readonly Action<string>? _logger;
        
        private HardwareSample _cachedSample = new();
        private DateTime _lastSuccessfulRead = DateTime.MinValue;
        private int _restartAttempts = 0;
        private DateTime _lastRestartAttempt = DateTime.MinValue;
        private bool _disposed;
        private bool _enabled = true;
        
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
        /// Start the hardware worker process and connect
        /// </summary>
        public async Task<bool> StartAsync()
        {
            if (!_enabled) return false;
            
            try
            {
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
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = workerPath,
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
                        return true;
                    
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
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                
                using var cts = new CancellationTokenSource(ConnectionTimeoutMs);
                await _pipeClient.ConnectAsync(cts.Token);
                
                // Test connection with ping
                var response = await SendRequestAsync("PING");
                if (response == "PONG")
                {
                    _logger?.Invoke("[Worker] Connected to hardware worker");
                    _restartAttempts = 0;
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
                if (!await TryRestartWorkerAsync())
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
                    return _cachedSample;
                }
                
                var sample = JsonSerializer.Deserialize<HardwareSample>(json);
                if (sample != null)
                {
                    _cachedSample = sample;
                    _lastSuccessfulRead = DateTime.Now;
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
            // Check cooldown
            if (DateTime.Now - _lastRestartAttempt < TimeSpan.FromMilliseconds(RestartCooldownMs))
                return false;
            
            // Check max attempts
            if (_restartAttempts >= MaxRestartAttempts)
            {
                if (_restartAttempts == MaxRestartAttempts)
                {
                    _logger?.Invoke($"[Worker] Max restart attempts ({MaxRestartAttempts}) reached, disabling worker");
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
            catch { }
            
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
            catch { }
            
            try
            {
                if (_workerProcess != null && !_workerProcess.HasExited)
                {
                    _workerProcess.Kill();
                    _workerProcess.WaitForExit(1000);
                }
            }
            catch { }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            try
            {
                // Fire and forget shutdown
                _ = Task.Run(async () =>
                {
                    await StopAsync();
                    _pipeClient?.Dispose();
                    _workerProcess?.Dispose();
                });
            }
            catch { }
        }
    }
    
    /// <summary>
    /// Hardware sample data - matches worker's HardwareSample class
    /// </summary>
    public class HardwareSample
    {
        public DateTime Timestamp { get; set; }
        
        // CPU
        public double CpuTemperature { get; set; }
        public double CpuLoad { get; set; }
        public double CpuPower { get; set; }
        
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
