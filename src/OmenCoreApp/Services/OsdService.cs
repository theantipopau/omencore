using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Views;

namespace OmenCore.Services
{
    /// <summary>
    /// Service to manage the in-game OSD overlay.
    /// 
    /// Key design principles:
    /// - Master toggle: When disabled, NO background process runs
    /// - Hotkey toggle: F12 by default to show/hide
    /// - Respects user choice to disable entirely
    /// </summary>
    public class OsdService : IDisposable
    {
        private readonly ConfigurationService _config;
        private readonly ThermalSensorProvider? _thermalProvider;
        private readonly FanService? _fanService;
        private readonly LoggingService _logging;
        
        private OsdOverlayWindow? _overlayWindow;
        private RtssIntegrationService? _rtssService;
        private HwndSource? _hotkeySource;
        private bool _isVisible;
        private bool _disposed;
        private System.Threading.Timer? _retryTimer;
        private int _retryAttempts = 0;
        private const int MaxRetryAttempts = 5;
        private const int RetryIntervalMs = 2000;
        
        // Win32 hotkey registration
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9001;
        
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        public bool IsEnabled => _config.Config.Osd.Enabled;
        public bool IsVisible => _isVisible;
        
        private Func<OmenCore.Models.MonitoringSample?>? _getMonitoringSample;
        
        public OsdService(
            ConfigurationService config, 
            LoggingService logging,
            ThermalSensorProvider? thermalProvider = null, 
            FanService? fanService = null)
        {
            _config = config;
            _logging = logging;
            _thermalProvider = thermalProvider;
            _fanService = fanService;
        }
        
        /// <summary>
        /// Set the monitoring sample source for accurate CPU/GPU load data
        /// </summary>
        public void SetMonitoringSampleSource(Func<OmenCore.Models.MonitoringSample?> getMonitoringSample)
        {
            _getMonitoringSample = getMonitoringSample;
            _overlayWindow?.SetMonitoringSampleSource(getMonitoringSample);
        }
        
        /// <summary>
        /// Initialize OSD if enabled in settings.
        /// Call this from App.xaml.cs after main window is ready.
        /// </summary>
        public void Initialize()
        {
            if (!_config.Config.Osd.Enabled)
            {
                _logging.Info("OSD: Disabled in settings (no background process)");
                return;
            }
            
            try
            {
                // Create RTSS service for real FPS data (if RTSS is running)
                if (_config.Config.Osd.UseRtssForFps)
                {
                    _rtssService = new RtssIntegrationService(_logging);
                }
                
                // Create overlay window
                _overlayWindow = new OsdOverlayWindow(
                    _config.Config.Osd,
                    _thermalProvider,
                    _fanService,
                    _rtssService);
                
                // Pass monitoring sample source if available
                if (_getMonitoringSample != null)
                    _overlayWindow.SetMonitoringSampleSource(_getMonitoringSample);
                
                // Register global hotkey
                RegisterToggleHotkey();
                
                _logging.Info($"OSD: Initialized (hotkey: {_config.Config.Osd.ToggleHotkey}, position: {_config.Config.Osd.Position})");
            }
            catch (Exception ex)
            {
                _logging.Error($"OSD: Failed to initialize: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Show the OSD overlay.
        /// </summary>
        public void Show()
        {
            if (_overlayWindow == null || _isVisible) return;
            
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _overlayWindow.Show();
                _overlayWindow.StartUpdates();
                _isVisible = true;
            });
            
            _logging.Info("OSD: Shown");
        }
        
        /// <summary>
        /// Hide the OSD overlay.
        /// </summary>
        public void Hide()
        {
            if (_overlayWindow == null || !_isVisible) return;
            
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _overlayWindow.StopUpdates();
                _overlayWindow.Hide();
                _isVisible = false;
            });
            
            _logging.Info("OSD: Hidden");
        }
        
        /// <summary>
        /// Toggle OSD visibility.
        /// </summary>
        public void Toggle()
        {
            if (_isVisible)
                Hide();
            else
                Show();
        }
        
        /// <summary>
        /// Update the current mode displayed on OSD
        /// </summary>
        public void SetCurrentMode(string mode)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _overlayWindow?.SetCurrentMode(mode);
            });
        }
        
        /// <summary>
        /// Update the performance mode displayed on OSD
        /// </summary>
        public void SetPerformanceMode(string mode)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _overlayWindow?.SetPerformanceMode(mode);
            });
        }
        
        /// <summary>
        /// Update the fan mode displayed on OSD
        /// </summary>
        public void SetFanMode(string mode)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _overlayWindow?.SetFanMode(mode);
            });
        }
        
        /// <summary>
        /// Update OSD settings at runtime
        /// </summary>
        public void UpdateSettings()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _overlayWindow?.UpdateSettings(_config.Config.Osd);
            });
        }
        
        /// <summary>
        /// Enable or disable OSD entirely.
        /// When disabled, all OSD resources are released.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _config.Config.Osd.Enabled = enabled;
            _config.Save(_config.Config);
            
            if (enabled)
            {
                Initialize();
            }
            else
            {
                Shutdown();
            }
        }
        
        /// <summary>
        /// Shutdown OSD completely.
        /// </summary>
        public void Shutdown()
        {
            UnregisterToggleHotkey();
            
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _overlayWindow?.StopUpdates();
                _overlayWindow?.Close();
                _overlayWindow = null;
            });
            
            _isVisible = false;
            _logging.Info("OSD: Shutdown");
        }
        
        private void RegisterToggleHotkey()
        {
            try
            {
                // Parse hotkey from settings (e.g., "F12", "Ctrl+Shift+O")
                var hotkeyStr = _config.Config.Osd.ToggleHotkey;
                if (string.IsNullOrEmpty(hotkeyStr)) return;
                
                uint modifiers = 0;
                uint vk = 0;
                
                var parts = hotkeyStr.Split('+');
                foreach (var part in parts)
                {
                    var trimmed = part.Trim().ToUpperInvariant();
                    switch (trimmed)
                    {
                        case "CTRL":
                        case "CONTROL":
                            modifiers |= 0x0002; // MOD_CONTROL
                            break;
                        case "ALT":
                            modifiers |= 0x0001; // MOD_ALT
                            break;
                        case "SHIFT":
                            modifiers |= 0x0004; // MOD_SHIFT
                            break;
                        default:
                            // Try to parse as Key enum
                            if (Enum.TryParse<Key>(trimmed, true, out var key))
                            {
                                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                            }
                            else if (trimmed.StartsWith("F") && int.TryParse(trimmed[1..], out var fNum))
                            {
                                // F1-F24 keys
                                vk = (uint)(0x70 + fNum - 1); // VK_F1 = 0x70
                            }
                            break;
                    }
                }
                
                if (vk == 0)
                {
                    _logging.Warn($"OSD: Invalid hotkey '{hotkeyStr}'");
                    return;
                }
                
                // Try to get window handle - use dedicated hidden window if main window not ready
                IntPtr hwnd = IntPtr.Zero;
                try
                {
                    var mainWindow = Application.Current?.MainWindow;
                    if (mainWindow != null && mainWindow.IsLoaded)
                    {
                        var helper = new WindowInteropHelper(mainWindow);
                        hwnd = helper.Handle;
                    }
                }
                catch
                {
                    // Main window not available
                }
                
                if (hwnd == IntPtr.Zero)
                {
                    // Main window not ready - start retry timer
                    _logging.Info($"OSD: Main window not ready, will retry hotkey registration...");
                    StartHotkeyRetryTimer(modifiers, vk, hotkeyStr);
                    return;
                }
                
                RegisterHotkeyWithHandle(hwnd, modifiers, vk, hotkeyStr);
            }
            catch (Exception ex)
            {
                _logging.Error($"OSD: Hotkey registration failed: {ex.Message}", ex);
            }
        }
        
        private void StartHotkeyRetryTimer(uint modifiers, uint vk, string hotkeyStr)
        {
            _retryAttempts = 0;
            _retryTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            var mainWindow = Application.Current?.MainWindow;
                            if (mainWindow != null && mainWindow.IsLoaded)
                            {
                                var helper = new WindowInteropHelper(mainWindow);
                                var hwnd = helper.Handle;
                                if (hwnd != IntPtr.Zero)
                                {
                                    RegisterHotkeyWithHandle(hwnd, modifiers, vk, hotkeyStr);
                                    _retryTimer?.Dispose();
                                    _retryTimer = null;
                                    _logging.Info($"OSD: Hotkey registered after {_retryAttempts + 1} retry attempts");
                                    return;
                                }
                            }
                        }
                        catch { }
                        
                        _retryAttempts++;
                        if (_retryAttempts >= MaxRetryAttempts)
                        {
                            _logging.Warn($"OSD: Failed to register hotkey after {MaxRetryAttempts} attempts");
                            _retryTimer?.Dispose();
                            _retryTimer = null;
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logging.Error($"OSD: Hotkey retry error: {ex.Message}", ex);
                }
            }, null, RetryIntervalMs, RetryIntervalMs);
        }
        
        private void RegisterHotkeyWithHandle(IntPtr hwnd, uint modifiers, uint vk, string hotkeyStr)
        {
            _hotkeySource = HwndSource.FromHwnd(hwnd);
            _hotkeySource?.AddHook(HwndHook);
            
            if (RegisterHotKey(hwnd, HOTKEY_ID, modifiers, vk))
            {
                _logging.Info($"OSD: Hotkey registered ({hotkeyStr})");
            }
            else
            {
                _logging.Warn($"OSD: Failed to register hotkey ({hotkeyStr})");
            }
        }
        
        private void UnregisterToggleHotkey()
        {
            try
            {
                if (_hotkeySource != null)
                {
                    var helper = new WindowInteropHelper(Application.Current.MainWindow);
                    UnregisterHotKey(helper.Handle, HOTKEY_ID);
                    _hotkeySource.RemoveHook(HwndHook);
                    _hotkeySource = null;
                }
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
        
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _retryTimer?.Dispose();
                _retryTimer = null;
                _rtssService?.Dispose();
                _rtssService = null;
                Shutdown();
                _disposed = true;
            }
        }
    }
}
