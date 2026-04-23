using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Razer
{
    /// <summary>
    /// Service for interacting with Razer Chroma SDK via the REST API.
    /// Requires Razer Synapse 3 to be installed with Chroma Connect module.
    /// 
    /// SDK Documentation: https://developer.razer.com/works-with-chroma/rest/
    /// </summary>
    public class RazerService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly HttpClient _httpClient;
        private bool _isInitialized;
        private bool _disposed;
        private readonly List<RazerDevice> _devices = new();
        
        private string? _sessionUri;
        private int _heartbeatInterval = 1000; // ms
        private System.Timers.Timer? _heartbeatTimer;

        // Reconnect back-off: 1 s → 2 s → 5 s → 30 s → 5 min
        private static readonly int[] _reconnectDelays = [1_000, 2_000, 5_000, 30_000, 300_000];
        private int _reconnectAttempts;
        private volatile bool _isReconnecting;
        private System.Threading.Timer? _reconnectTimer;
        
        // Chroma SDK endpoints
        private const string CHROMA_SDK_URL = "http://localhost:54235/razer/chromasdk";
        private static readonly JsonSerializerOptions JsonOptions = new() 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false 
        };
        private static readonly TimeSpan SessionInitTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan EffectApplyTimeout = TimeSpan.FromSeconds(3);

        public bool IsAvailable { get; private set; }
        public bool IsSessionActive => !string.IsNullOrEmpty(_sessionUri);
        public IReadOnlyList<RazerDevice> Devices => _devices.AsReadOnly();
        
        public event EventHandler? DevicesChanged;
        public event EventHandler<RazerEffectEventArgs>? EffectApplied;

        public RazerService(LoggingService logging)
        {
            _logging = logging;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _reconnectTimer = new System.Threading.Timer(ReconnectTimerCallback, null,
                System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _logging.Info("RazerService created");
        }

        /// <summary>
        /// Initialize the Razer Chroma SDK and create a session.
        /// </summary>
        public bool Initialize()
        {
            if (_isInitialized)
                return IsAvailable;

            _logging.Info("Initializing Razer Chroma SDK...");

            try
            {
                // Check if Razer Synapse is running
                var razerProcesses = System.Diagnostics.Process.GetProcessesByName("Razer Synapse 3");
                var razerProcesses2 = System.Diagnostics.Process.GetProcessesByName("RazerCentralService");
                
                bool synapseRunning = razerProcesses.Length > 0 || razerProcesses2.Length > 0;
                
                // Clean up process handles
                foreach (var p in razerProcesses) p.Dispose();
                foreach (var p in razerProcesses2) p.Dispose();

                if (!synapseRunning)
                {
                    _logging.Info("Razer Synapse not detected - Razer features unavailable");
                    IsAvailable = false;
                    _isInitialized = true;
                    return false;
                }

                _logging.Info("Razer Synapse detected running");
                
                // Try to create a Chroma SDK session
                if (TryRunBoolWithTimeout(() => InitializeSessionAsync(), SessionInitTimeout, "InitializeSessionAsync"))
                {
                    IsAvailable = true;
                    StartHeartbeat();
                    _logging.Info("Razer Chroma SDK session established");
                }
                else
                {
                    _logging.Info("Razer Synapse running but Chroma SDK session failed - using basic mode");
                    IsAvailable = true; // Still mark as available for basic effects
                }

                _isInitialized = true;
                return IsAvailable;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to initialize Razer SDK: {ex.Message}");
                IsAvailable = false;
                _isInitialized = true;
                return false;
            }
        }
        
        /// <summary>
        /// Initialize a Chroma SDK REST API session.
        /// </summary>
        private async Task<bool> InitializeSessionAsync()
        {
            try
            {
                var appInfo = new
                {
                    title = "OmenCore",
                    description = "HP OMEN hardware control with RGB sync",
                    author = new { name = "OmenCore", contact = "https://github.com/Jeyloh/omencore" },
                    device_supported = new[] { "keyboard", "mouse", "headset", "mousepad", "keypad", "chromalink" },
                    category = "application"
                };
                
                var json = JsonSerializer.Serialize(appInfo, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(CHROMA_SDK_URL, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseBody);
                    
                    if (doc.RootElement.TryGetProperty("uri", out var uriElement))
                    {
                        _sessionUri = uriElement.GetString();
                        _logging.Info($"Chroma SDK session URI: {_sessionUri}");
                    }
                    
                    if (doc.RootElement.TryGetProperty("heartbeat", out var heartbeatElement))
                    {
                        _heartbeatInterval = heartbeatElement.GetInt32();
                    }
                    
                    return !string.IsNullOrEmpty(_sessionUri);
                }
                
                _logging.Warn($"Chroma SDK init failed: {response.StatusCode}");
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logging.Warn($"Chroma SDK not reachable: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logging.Error($"Chroma SDK init error: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Start heartbeat timer to keep session alive.
        /// </summary>
        private void StartHeartbeat()
        {
            _heartbeatTimer?.Stop();
            _heartbeatTimer?.Dispose();
            
            _heartbeatTimer = new System.Timers.Timer(_heartbeatInterval);
            _heartbeatTimer.Elapsed += async (s, e) => await SendHeartbeatAsync();
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Start();
        }
        
        private async Task SendHeartbeatAsync()
        {
            if (string.IsNullOrEmpty(_sessionUri))
                return;
                
            try
            {
                var response = await _httpClient.PutAsync($"{_sessionUri}/heartbeat", null);
                if (!response.IsSuccessStatusCode)
                {
                    _logging.Warn("Chroma SDK heartbeat failed — session may have expired");
                    OnSessionLost();
                }
                else
                {
                    _reconnectAttempts = 0;
                }
            }
            catch
            {
                OnSessionLost();
            }
        }

        private void OnSessionLost()
        {
            _sessionUri = null;
            if (!_isReconnecting && IsAvailable)
                ScheduleReconnect();
        }

        private void ScheduleReconnect()
        {
            if (_isReconnecting) return;
            _isReconnecting = true;
            int delay = _reconnectDelays[Math.Min(_reconnectAttempts, _reconnectDelays.Length - 1)];
            _logging.Info($"Scheduling Chroma SDK reconnect in {delay / 1000}s (attempt {_reconnectAttempts + 1})");
            _reconnectTimer?.Change(delay, System.Threading.Timeout.Infinite);
        }

        private void ReconnectTimerCallback(object? state)
            => _ = TryReconnectAsync();

        private async Task TryReconnectAsync()
        {
            try
            {
                _logging.Info($"Attempting Chroma SDK reconnect (attempt {_reconnectAttempts + 1})...");
                var success = await InitializeSessionAsync();
                if (success)
                {
                    _reconnectAttempts = 0;
                    _isReconnecting = false;
                    StartHeartbeat();
                    _logging.Info("Chroma SDK session re-established via back-off reconnect");
                }
                else
                {
                    _reconnectAttempts++;
                    _isReconnecting = false;
                    if (IsAvailable) ScheduleReconnect();
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Chroma SDK reconnect attempt failed: {ex.Message}");
                _reconnectAttempts++;
                _isReconnecting = false;
                if (IsAvailable) ScheduleReconnect();
            }
        }

        private bool TryRunBoolWithTimeout(Func<Task<bool>> operation, TimeSpan timeout, string operationName)
        {
            try
            {
                // Run on thread-pool so async continuations don't depend on a UI synchronization context.
                var task = Task.Run(operation);
                return task.WaitAsync(timeout).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                _logging.Warn($"Razer operation timed out after {timeout.TotalMilliseconds:F0}ms: {operationName}");
                return false;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Razer operation failed: {operationName} - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Discover connected Razer devices.
        /// </summary>
        public void DiscoverDevices()
        {
            if (!_isInitialized)
                Initialize();

            _logging.Info("Discovering Razer devices...");
            _devices.Clear();

            if (!IsAvailable)
            {
                _logging.Info("Razer SDK not available, skipping device discovery");
                DevicesChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            try
            {
                // Razer Chroma SDK doesn't expose individual device enumeration via REST
                // We add device categories that are generally available
                if (IsSessionActive)
                {
                    // Add supported device categories
                    _devices.Add(new RazerDevice 
                    { 
                        DeviceId = "razer-keyboard", 
                        Name = "Razer Keyboard", 
                        DeviceType = RazerDeviceType.Keyboard,
                        Status = new RazerDeviceStatus { IsConnected = true }
                    });
                    
                    _devices.Add(new RazerDevice 
                    { 
                        DeviceId = "razer-mouse", 
                        Name = "Razer Mouse", 
                        DeviceType = RazerDeviceType.Mouse,
                        Status = new RazerDeviceStatus { IsConnected = true }
                    });
                    
                    _devices.Add(new RazerDevice 
                    { 
                        DeviceId = "razer-mousepad", 
                        Name = "Razer Mousepad", 
                        DeviceType = RazerDeviceType.MouseMat,
                        Status = new RazerDeviceStatus { IsConnected = true }
                    });
                    
                    _devices.Add(new RazerDevice 
                    { 
                        DeviceId = "razer-headset", 
                        Name = "Razer Headset", 
                        DeviceType = RazerDeviceType.Headset,
                        Status = new RazerDeviceStatus { IsConnected = true }
                    });
                    
                    _devices.Add(new RazerDevice 
                    { 
                        DeviceId = "razer-chromalink", 
                        Name = "Chroma Link Devices", 
                        DeviceType = RazerDeviceType.ChromaLink,
                        Status = new RazerDeviceStatus { IsConnected = true }
                    });
                }
                
                _logging.Info($"Razer device discovery complete. Found {_devices.Count} device category(s)");
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logging.Error($"Error discovering Razer devices: {ex.Message}", ex);
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Set a static color on all Razer devices.
        /// </summary>
        public bool SetStaticColor(byte r, byte g, byte b)
        {
            if (!IsAvailable)
            {
                _logging.Warn("Cannot set Razer color - SDK not available");
                return false;
            }

            _logging.Info($"Setting Razer static color: R={r}, G={g}, B={b}");
            
            try
            {
                if (IsSessionActive)
                {
                    if (TryRunBoolWithTimeout(() => ApplyStaticEffectAsync(r, g, b), EffectApplyTimeout, "ApplyStaticEffectAsync"))
                    {
                        EffectApplied?.Invoke(this, new RazerEffectEventArgs("static", r, g, b));
                        return true;
                    }
                }
                
                _logging.Info("Razer static color applied (session mode)");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set Razer color: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Apply static color effect via Chroma REST API.
        /// </summary>
        private async Task<bool> ApplyStaticEffectAsync(byte r, byte g, byte b)
        {
            if (string.IsNullOrEmpty(_sessionUri))
                return false;
                
            // Razer uses BGR format
            int color = b << 16 | g << 8 | r;
            
            var devices = new[] { "keyboard", "mouse", "mousepad", "headset", "chromalink" };
            bool anySuccess = false;
            
            foreach (var device in devices)
            {
                try
                {
                    var effect = new { effect = "CHROMA_STATIC", param = new { color } };
                    var json = JsonSerializer.Serialize(effect, JsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await _httpClient.PutAsync($"{_sessionUri}/{device}", content);
                    if (response.IsSuccessStatusCode)
                    {
                        anySuccess = true;
                    }
                }
                catch
                {
                    // Continue with other devices
                }
            }
            
            return anySuccess;
        }

        /// <summary>
        /// Apply a breathing effect on all Razer devices.
        /// </summary>
        public bool SetBreathingEffect(byte r, byte g, byte b)
        {
            if (!IsAvailable)
                return false;

            _logging.Info($"Setting Razer breathing effect: R={r}, G={g}, B={b}");
            
            try
            {
                if (IsSessionActive)
                {
                    if (TryRunBoolWithTimeout(() => ApplyBreathingEffectAsync(r, g, b), EffectApplyTimeout, "ApplyBreathingEffectAsync"))
                    {
                        EffectApplied?.Invoke(this, new RazerEffectEventArgs("breathing", r, g, b));
                        return true;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set Razer breathing effect: {ex.Message}", ex);
                return false;
            }
        }
        
        private async Task<bool> ApplyBreathingEffectAsync(byte r, byte g, byte b)
        {
            if (string.IsNullOrEmpty(_sessionUri))
                return false;
                
            int color = b << 16 | g << 8 | r;
            
            var devices = new[] { "keyboard", "mouse", "mousepad", "headset", "chromalink" };
            bool anySuccess = false;
            
            foreach (var device in devices)
            {
                try
                {
                    var effect = new { effect = "CHROMA_BREATHING", param = new { color1 = color, color2 = 0, type = 1 } };
                    var json = JsonSerializer.Serialize(effect, JsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await _httpClient.PutAsync($"{_sessionUri}/{device}", content);
                    if (response.IsSuccessStatusCode)
                    {
                        anySuccess = true;
                    }
                }
                catch
                {
                    // Continue with other devices
                }
            }
            
            return anySuccess;
        }

        /// <summary>
        /// Apply a spectrum cycling effect on all Razer devices.
        /// </summary>
        public bool SetSpectrumEffect()
        {
            if (!IsAvailable)
                return false;

            _logging.Info("Setting Razer spectrum cycling effect");
            
            try
            {
                if (IsSessionActive)
                {
                    if (TryRunBoolWithTimeout(() => ApplySpectrumEffectAsync(), EffectApplyTimeout, "ApplySpectrumEffectAsync"))
                    {
                        EffectApplied?.Invoke(this, new RazerEffectEventArgs("spectrum"));
                        return true;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set Razer spectrum effect: {ex.Message}", ex);
                return false;
            }
        }
        
        private async Task<bool> ApplySpectrumEffectAsync()
        {
            if (string.IsNullOrEmpty(_sessionUri))
                return false;
                
            var devices = new[] { "keyboard", "mouse", "mousepad", "headset", "chromalink" };
            bool anySuccess = false;
            
            foreach (var device in devices)
            {
                try
                {
                    var effect = new { effect = "CHROMA_SPECTRUMCYCLING" };
                    var json = JsonSerializer.Serialize(effect, JsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await _httpClient.PutAsync($"{_sessionUri}/{device}", content);
                    if (response.IsSuccessStatusCode)
                    {
                        anySuccess = true;
                    }
                }
                catch
                {
                    // Continue with other devices
                }
            }
            
            return anySuccess;
        }
        
        /// <summary>
        /// Apply a wave effect on keyboard.
        /// </summary>
        public bool SetWaveEffect(bool rightToLeft = true)
        {
            if (!IsAvailable)
                return false;

            _logging.Info($"Setting Razer wave effect (direction: {(rightToLeft ? "right-to-left" : "left-to-right")})");
            
            try
            {
                if (IsSessionActive)
                {
                    if (TryRunBoolWithTimeout(() => ApplyWaveEffectAsync(rightToLeft), EffectApplyTimeout, "ApplyWaveEffectAsync"))
                    {
                        EffectApplied?.Invoke(this, new RazerEffectEventArgs("wave"));
                        return true;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set Razer wave effect: {ex.Message}", ex);
                return false;
            }
        }
        
        private async Task<bool> ApplyWaveEffectAsync(bool rightToLeft)
        {
            if (string.IsNullOrEmpty(_sessionUri))
                return false;
                
            try
            {
                var effect = new { effect = "CHROMA_WAVE", param = new { direction = rightToLeft ? 2 : 1 } };
                var json = JsonSerializer.Serialize(effect, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync($"{_sessionUri}/keyboard", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Apply a reactive effect (responds to key presses).
        /// </summary>
        public bool SetReactiveEffect(byte r, byte g, byte b, int duration = 2)
        {
            if (!IsAvailable)
                return false;

            _logging.Info($"Setting Razer reactive effect: R={r}, G={g}, B={b}, duration={duration}");
            
            try
            {
                if (IsSessionActive)
                {
                    if (TryRunBoolWithTimeout(() => ApplyReactiveEffectAsync(r, g, b, duration), EffectApplyTimeout, "ApplyReactiveEffectAsync"))
                    {
                        EffectApplied?.Invoke(this, new RazerEffectEventArgs("reactive", r, g, b));
                        return true;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set Razer reactive effect: {ex.Message}", ex);
                return false;
            }
        }
        
        private async Task<bool> ApplyReactiveEffectAsync(byte r, byte g, byte b, int duration)
        {
            if (string.IsNullOrEmpty(_sessionUri))
                return false;
                
            int color = b << 16 | g << 8 | r;
            
            try
            {
                var effect = new { effect = "CHROMA_REACTIVE", param = new { color, duration = Math.Clamp(duration, 1, 3) } };
                var json = JsonSerializer.Serialize(effect, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync($"{_sessionUri}/keyboard", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Apply custom per-key colors on keyboard (22x6 grid).
        /// </summary>
        public bool SetCustomKeyboardEffect(int[,] colors)
        {
            if (!IsAvailable || colors.GetLength(0) != 6 || colors.GetLength(1) != 22)
                return false;

            _logging.Info("Setting Razer custom keyboard effect");
            
            try
            {
                if (IsSessionActive)
                {
                    return TryRunBoolWithTimeout(() => ApplyCustomKeyboardEffectAsync(colors), EffectApplyTimeout, "ApplyCustomKeyboardEffectAsync");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set Razer custom keyboard effect: {ex.Message}", ex);
                return false;
            }
        }
        
        private async Task<bool> ApplyCustomKeyboardEffectAsync(int[,] colors)
        {
            if (string.IsNullOrEmpty(_sessionUri))
                return false;
                
            try
            {
                // Convert 2D array to array of arrays
                var colorArray = new int[6][];
                for (int i = 0; i < 6; i++)
                {
                    colorArray[i] = new int[22];
                    for (int j = 0; j < 22; j++)
                    {
                        colorArray[i][j] = colors[i, j];
                    }
                }
                
                var effect = new { effect = "CHROMA_CUSTOM", param = colorArray };
                var json = JsonSerializer.Serialize(effect, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync($"{_sessionUri}/keyboard", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Close the Chroma SDK session.
        /// </summary>
        public async Task CloseSessionAsync()
        {
            if (string.IsNullOrEmpty(_sessionUri))
                return;
                
            try
            {
                await _httpClient.DeleteAsync(_sessionUri);
                _logging.Info("Chroma SDK session closed");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Error closing Chroma session: {ex.Message}");
            }
            finally
            {
                _sessionUri = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _logging.Info("Disposing RazerService");
            
            _heartbeatTimer?.Stop();
            _heartbeatTimer?.Dispose();

            _reconnectTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _reconnectTimer?.Dispose();
            _isReconnecting = false;
            
            // Close SDK session
            if (IsSessionActive)
            {
                try
                {
                    Task.Run(() => CloseSessionAsync()).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logging.Warn($"[RazerService] Dispose: CloseSessionAsync failed: {ex.Message}");
                }
            }
            
            _httpClient.Dispose();
            _disposed = true;
        }
    }
    
    /// <summary>
    /// Event args for Razer effect application.
    /// </summary>
    public class RazerEffectEventArgs : EventArgs
    {
        public string EffectType { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        
        public RazerEffectEventArgs(string effectType, byte r = 0, byte g = 0, byte b = 0)
        {
            EffectType = effectType;
            R = r;
            G = g;
            B = b;
        }
    }
}
