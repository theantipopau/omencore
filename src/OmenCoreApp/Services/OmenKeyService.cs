using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for intercepting the physical OMEN key on HP OMEN laptops.
    /// 
    /// Uses two methods to detect the OMEN key:
    /// 1. Low-level keyboard hook (catches most key codes)
    /// 2. WMI BIOS event monitoring (catches HP BIOS-level events)
    /// Uses low-level keyboard hook to detect the OMEN key press and allow
    /// custom actions instead of launching HP OMEN Gaming Hub.
    /// 
    /// The OMEN key typically sends one of:
    /// - VK_LAUNCH_APP2 (0xB7) - Media key for app launch
    /// - VK 157 (0x9D) - Used on some OMEN models
    /// - F24 (0x87) - Used on some OMEN models
    /// - Custom OEM key code via HP WMI
    /// </summary>
    public class OmenKeyService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly ConfigurationService? _configService;
        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc? _hookProc;
        private bool _isEnabled = true;
        private bool _disposed;
        private OmenKeyAction _currentAction = OmenKeyAction.ToggleOmenCore;
        private string _externalAppPath = string.Empty;
        private long _lastKeyPressTicks = 0; // Use ticks for thread-safe Interlocked operations
        private const int DebounceMs = 300;
        
        // WMI event watcher for HP BIOS events
        private ManagementEventWatcher? _wmiEventWatcher;

        // Common key codes for OMEN key (varies by laptop model)
        private const int VK_LAUNCH_APP2 = 0xB7;  // Media key often used by OEM
        private const int VK_LAUNCH_APP1 = 0xB6;  // Some models use this
        private const int VK_OMEN_157 = 0x9D;     // 157 decimal - some OMEN models
        private const int VK_F24 = 0x87;          // F24 - some OMEN models
        private const int VK_OEM_OMEN = 0xFF;     // Some models use this
        
        // Brightness keys that should NEVER be treated as OMEN key
        private const int VK_BRIGHTNESS_DOWN = 0x70;  // F1 - brightness down
        private const int VK_BRIGHTNESS_UP = 0x71;    // F2 - brightness up
        private const int VK_F2 = 0x71;               // F2
        private const int VK_F3 = 0x72;               // F3
        
        // Additional keys that must NEVER be treated as OMEN key (GitHub #46)
        private const int VK_SCROLL_LOCK = 0x91;      // Scroll Lock - scan code 0x46 conflicts with OMEN
        private const int VK_PAUSE = 0x13;            // Pause/Break key
        private const int VK_NUM_LOCK = 0x90;         // Num Lock

        // HP OMEN-specific scan codes (varies by model)
        private static readonly int[] OmenScanCodes = { 0xE045, 0xE046, 0x0046, 0x009D };
        
        // Excluded scan codes (Calculator, standard media keys that conflict)
        private static readonly int[] ExcludedScanCodes = { 0x0021 }; // Calculator key scan code

        #region Win32 API

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        #endregion

        /// <summary>
        /// Fired when the OMEN key is pressed.
        /// </summary>
        public event EventHandler? OmenKeyPressed;

        /// <summary>
        /// Fired when the OMEN key is released.
        /// </summary>
        public event EventHandler? OmenKeyReleased;
        
        /// <summary>
        /// Fired to toggle OmenCore window visibility
        /// </summary>
        public event EventHandler? ToggleOmenCoreRequested;
        
        /// <summary>
        /// Fired to cycle performance modes
        /// </summary>
        public event EventHandler? CyclePerformanceRequested;
        
        /// <summary>
        /// Fired to cycle fan modes
        /// </summary>
        public event EventHandler? CycleFanModeRequested;
        
        /// <summary>
        /// Fired to toggle max cooling
        /// </summary>
        public event EventHandler? ToggleMaxCoolingRequested;

        /// <summary>
        /// Get or set whether OMEN key interception is enabled.
        /// When disabled, the key passes through to system normally.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                _logging.Info($"OMEN key interception {(_isEnabled ? "enabled" : "disabled")}");
                SaveSettings();
            }
        }
        
        /// <summary>
        /// Get or set the action to perform when OMEN key is pressed
        /// </summary>
        public OmenKeyAction CurrentAction
        {
            get => _currentAction;
            set
            {
                _currentAction = value;
                _logging.Info($"OMEN key action set to: {value}");
                SaveSettings();
            }
        }
        
        /// <summary>
        /// Path to external application to launch (when action is LaunchExternalApp)
        /// </summary>
        public string ExternalAppPath
        {
            get => _externalAppPath;
            set
            {
                _externalAppPath = value;
                SaveSettings();
            }
        }

        /// <summary>
        /// Get whether the keyboard hook is active.
        /// </summary>
        public bool IsHookActive => _hookHandle != IntPtr.Zero;

        public OmenKeyService(LoggingService logging, ConfigurationService? configService = null)
        {
            _logging = logging;
            _configService = configService;
            LoadSettings();
        }

        /// <summary>
        /// Start intercepting the OMEN key.
        /// </summary>
        public bool StartInterception()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                _logging.Warn("OMEN key hook already active");
                return true;
            }

            try
            {
                _hookProc = HookCallback;
                
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                if (curModule == null)
                {
                    _logging.Error("Failed to get main module for keyboard hook");
                    return false;
                }

                _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, 
                    GetModuleHandle(curModule.ModuleName), 0);

                if (_hookHandle == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    _logging.Error($"Failed to set keyboard hook. Error code: {error}");
                    return false;
                }

                // Enumerate available HP WMI event classes for diagnostics
                EnumerateHpWmiClasses();
                
                // Also start WMI event monitoring for HP BIOS hotkey events
                StartWmiEventWatcher();

                _logging.Info("✓ OMEN key interception started");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to start OMEN key interception: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Enumerate HP-related WMI classes to help identify OMEN key event source
        /// </summary>
        private void EnumerateHpWmiClasses()
        {
            try
            {
                // Check root\wmi for HP/OMEN related event classes
                using var searcher = new ManagementObjectSearcher(@"root\wmi", 
                    "SELECT * FROM meta_class WHERE __CLASS LIKE '%BIOS%' OR __CLASS LIKE '%HP%' OR __CLASS LIKE '%OMEN%' OR __CLASS LIKE '%aborpc%'");
                
                var classes = new System.Collections.Generic.List<string>();
                foreach (ManagementObject obj in searcher.Get())
                {
                    var className = obj["__CLASS"]?.ToString();
                    if (!string.IsNullOrEmpty(className) && className.Contains("Event", StringComparison.OrdinalIgnoreCase))
                    {
                        classes.Add(className);
                    }
                }
                
                if (classes.Count > 0)
                {
                    _logging.Debug($"Available HP WMI event classes in root\\wmi: {string.Join(", ", classes)}");
                }
            }
            catch (Exception ex)
            {
                _logging.Debug($"Could not enumerate WMI classes: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Start WMI event watcher for HP BIOS events (OMEN key may fire via this path)
        /// OmenMon uses: SELECT * FROM hpqBEvnt WHERE eventData = 8613 AND eventId = 29
        /// </summary>
        private void StartWmiEventWatcher()
        {
            _logging.Debug("Attempting to start WMI BIOS event watchers...");
            
            // HP OMEN key fires via hpqBEvnt WMI event class
            // From OmenMon: eventData = 8613 AND eventId = 29
            // 
            // IMPORTANT: Only use queries that specifically target the OMEN key event.
            // DO NOT use broad queries like "SELECT * FROM hpqBEvnt" as this catches
            // ALL BIOS events (fan changes, thermal events, power state changes) and
            // causes focus-stealing behavior where OmenCore repeatedly comes to front.
            var wmiSources = new[]
            {
                // OmenMon's exact query for OMEN key - THIS IS THE ONLY SAFE OPTION
                (@"root\wmi", "SELECT * FROM hpqBEvnt WHERE eventData = 8613 AND eventId = 29"),
            };
            
            foreach (var (ns, queryStr) in wmiSources)
            {
                try
                {
                    var watcher = new ManagementEventWatcher(ns, queryStr);
                    watcher.EventArrived += OnWmiEventArrived;
                    watcher.Start();
                    
                    _wmiEventWatcher = watcher;
                    var shortQuery = queryStr.Length > 50 ? queryStr.Substring(0, 50) + "..." : queryStr;
                    _logging.Info($"✓ WMI event watcher started: {ns} - {shortQuery}");
                    return; // Success, stop trying
                }
                catch (Exception ex)
                {
                    _logging.Debug($"WMI source not available ({ns}): {ex.Message}");
                }
            }
            
            // If specific OMEN key query fails, rely only on keyboard hook - much safer
            // than catching all BIOS events which causes focus-stealing
            _logging.Info("OMEN key WMI event not available - using keyboard hook only (safer)");
            _logging.Debug("TIP: If OMEN key doesn't work via keyboard hook, try OmenMon's Task Scheduler trigger");
        }

        private void OnWmiEventArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                // Even though we query for eventId=29, eventData=8613, HP BIOS on some models
                // may use the same event codes for brightness and other Fn keys.
                // We need to verify the actual event data to filter out false positives.
                var wmiEvent = e.NewEvent;
                var className = wmiEvent.ClassPath?.ClassName ?? "Unknown";
                
                // Try to extract eventId and eventData from the WMI event
                int? eventId = null;
                int? eventData = null;
                
                try
                {
                    eventId = Convert.ToInt32(wmiEvent["eventId"]);
                    eventData = Convert.ToInt32(wmiEvent["eventData"]);
                }
                catch 
                { 
                    // Properties may not exist on all models
                }
                
                _logging.Debug($"WMI event received: class={className}, eventId={eventId}, eventData={eventData}");
                
                // Known brightness/hotkey event IDs to exclude (varies by model)
                // eventId 17 = Brightness change events on some HP models
                // eventId 4 = Power/battery events
                // eventId 5 = Thermal events
                // Only eventId=29 with eventData=8613 should be OMEN key
                if (eventId.HasValue && eventId.Value != 29)
                {
                    _logging.Debug($"WMI event filtered: eventId={eventId} is not 29 (OMEN key)");
                    return;
                }
                
                // Some models report different eventData for brightness (e.g., 8610, 8611, 8612)
                // Only 8613 is the OMEN key
                if (eventData.HasValue && eventData.Value != 8613)
                {
                    _logging.Debug($"WMI event filtered: eventData={eventData} is not 8613 (OMEN key)");
                    return;
                }
                
                _logging.Info($"🔑 OMEN key detected via WMI ({className}, eventId={eventId}, eventData={eventData})");
                
                // Debounce to prevent double-triggers (thread-safe)
                var lastTicks = Interlocked.Read(ref _lastKeyPressTicks);
                var timeSinceLastPress = (DateTime.UtcNow.Ticks - lastTicks) / TimeSpan.TicksPerMillisecond;
                if (timeSinceLastPress < DebounceMs)
                {
                    _logging.Debug("OMEN key debounced (too soon after last press)");
                    return;
                }
                Interlocked.Exchange(ref _lastKeyPressTicks, DateTime.UtcNow.Ticks);
                
                // Fire events on background thread to avoid blocking WMI
                Task.Run(() =>
                {
                    OmenKeyPressed?.Invoke(this, EventArgs.Empty);
                    ExecuteAction();
                });
            }
            catch (Exception ex)
            {
                _logging.Debug($"WMI event processing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop intercepting the OMEN key.
        /// </summary>
        public void StopInterception()
        {
            // Stop WMI watcher - unsubscribe event handler to prevent memory leak
            if (_wmiEventWatcher != null)
            {
                try
                {
                    _wmiEventWatcher.Stop();
                    _wmiEventWatcher.EventArrived -= OnWmiEventArrived;
                }
                catch (Exception ex)
                {
                    _logging.Debug($"Error stopping WMI watcher: {ex.Message}");
                }
                finally
                {
                    _wmiEventWatcher.Dispose();
                    _wmiEventWatcher = null;
                }
            }
            
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                _hookProc = null;
                _logging.Info("OMEN key interception stopped");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isEnabled)
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                
                // Log ALL key presses to help identify the OMEN key
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    // Enable verbose logging for all keys (helps identify OMEN key)
                    // Log: all special keys, function keys, extended keys, AND high VK codes
                    bool isSpecialKey = hookStruct.vkCode >= 0x70 ||  // F1 and above, all special keys
                                       hookStruct.vkCode == 0x5B || hookStruct.vkCode == 0x5C || // Win keys
                                       (hookStruct.flags & 0x01) != 0 || // Extended key
                                       hookStruct.scanCode > 0x50; // Non-standard scan codes
                    
                    if (isSpecialKey)
                    {
                        // Use Info level temporarily to see all special key presses
                        _logging.Info($"[KeyHook] VK=0x{hookStruct.vkCode:X2} ({hookStruct.vkCode}), Scan=0x{hookStruct.scanCode:X4}, Flags=0x{hookStruct.flags:X}");
                    }
                }
                
                bool isOmenKey = IsOmenKey(hookStruct.vkCode, hookStruct.scanCode);

                if (isOmenKey)
                {
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        // Debounce check (thread-safe)
                        var lastTicks = Interlocked.Read(ref _lastKeyPressTicks);
                        var timeSinceLastPress = (DateTime.UtcNow.Ticks - lastTicks) / TimeSpan.TicksPerMillisecond;
                        if (timeSinceLastPress < DebounceMs)
                        {
                            return new IntPtr(1); // Block duplicate
                        }
                        Interlocked.Exchange(ref _lastKeyPressTicks, DateTime.UtcNow.Ticks);
                        
                        _logging.Info($"🔑 OMEN key detected: VK=0x{hookStruct.vkCode:X2}, Scan=0x{hookStruct.scanCode:X4}");
                        
                        // Fire event and execute action on a separate thread to avoid blocking the hook
                        Task.Run(() => 
                        {
                            OmenKeyPressed?.Invoke(this, EventArgs.Empty);
                            ExecuteAction();
                        });
                        
                        // Return non-zero to block the key from reaching other apps
                        // This prevents OMEN Gaming Hub from launching
                        return new IntPtr(1);
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        Task.Run(() => OmenKeyReleased?.Invoke(this, EventArgs.Empty));
                        return new IntPtr(1);
                    }
                }
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
        
        private void ExecuteAction()
        {
            _logging.Info($"OMEN key pressed - executing: {_currentAction}");
            
            switch (_currentAction)
            {
                case OmenKeyAction.ToggleOmenCore:
                    ToggleOmenCoreRequested?.Invoke(this, EventArgs.Empty);
                    break;
                    
                case OmenKeyAction.CyclePerformance:
                    CyclePerformanceRequested?.Invoke(this, EventArgs.Empty);
                    break;
                    
                case OmenKeyAction.CycleFanMode:
                    CycleFanModeRequested?.Invoke(this, EventArgs.Empty);
                    break;
                    
                case OmenKeyAction.ToggleMaxCooling:
                    ToggleMaxCoolingRequested?.Invoke(this, EventArgs.Empty);
                    break;
                    
                case OmenKeyAction.LaunchExternalApp:
                    LaunchExternalApplication();
                    break;
                    
                case OmenKeyAction.DoNothing:
                    // Key is blocked but no action taken
                    break;
            }
        }
        
        private void LaunchExternalApplication()
        {
            if (string.IsNullOrWhiteSpace(_externalAppPath))
            {
                _logging.Warn("OMEN key set to launch app but no path configured");
                return;
            }
            
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _externalAppPath,
                    UseShellExecute = true
                });
                _logging.Info($"Launched external app: {_externalAppPath}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to launch external app '{_externalAppPath}': {ex.Message}");
            }
        }
        
        private void LoadSettings()
        {
            if (_configService == null) return;
            
            try
            {
                // Check both old and new config locations for backwards compatibility
                _isEnabled = _configService.Config.Features?.OmenKeyInterceptionEnabled ?? _configService.Config.OmenKeyEnabled;
                _externalAppPath = _configService.Config.OmenKeyExternalApp ?? string.Empty;
                
                // Try Features.OmenKeyAction first, fall back to OmenKeyAction
                var actionStr = _configService.Config.Features?.OmenKeyAction ?? _configService.Config.OmenKeyAction;
                if (Enum.TryParse<OmenKeyAction>(actionStr, out var action))
                {
                    _currentAction = action;
                }
                
                _logging.Info($"OMEN key settings loaded: Enabled={_isEnabled}, Action={_currentAction}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to load OMEN key settings: {ex.Message}");
            }
        }
        
        private void SaveSettings()
        {
            if (_configService == null) return;
            
            try
            {
                _configService.Config.OmenKeyEnabled = _isEnabled;
                _configService.Config.OmenKeyAction = _currentAction.ToString();
                _configService.Config.OmenKeyExternalApp = _externalAppPath;
                _configService.Save(_configService.Config);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save OMEN key settings: {ex.Message}");
            }
        }

        private bool IsOmenKey(uint vkCode, uint scanCode)
        {
            // CRITICAL: Exclude Scroll Lock, Pause, and Num Lock keys (GitHub #46)
            // Scroll Lock has scan code 0x46 which conflicts with OMEN scan codes
            // These are standard keyboard keys that should NEVER trigger OmenCore
            if (vkCode == VK_SCROLL_LOCK || vkCode == VK_PAUSE || vkCode == VK_NUM_LOCK)
            {
                _logging.Debug($"Excluded lock key: VK=0x{vkCode:X2}, Scan=0x{scanCode:X4} - NOT OMEN key");
                return false;
            }
            
            // CRITICAL: Exclude brightness keys and other function keys that should never trigger OMEN actions
            // These keys are commonly used with Fn modifier and can conflict with OMEN key detection
            // Issue #42: Fn+F2/F3 brightness keys incorrectly triggering OmenCore
            // User report: Fn+F3/F4 on Victus triggering app open
            if (vkCode == VK_BRIGHTNESS_DOWN || vkCode == VK_BRIGHTNESS_UP ||
                vkCode == VK_F2 || vkCode == VK_F3 ||
                (vkCode >= 0x70 && vkCode <= 0x87)) // All F1-F24 keys (VK_F1=0x70 through VK_F24=0x87)
            {
                _logging.Debug($"Excluded F-key: VK=0x{vkCode:X2}, Scan=0x{scanCode:X4} - NOT OMEN key");
                return false;
            }
            
            // First, exclude known non-OMEN keys that share VK codes
            foreach (var excludedScan in ExcludedScanCodes)
            {
                if (scanCode == excludedScan)
                {
                    _logging.Debug($"Excluded scan code 0x{scanCode:X4} (Calculator/media key) - NOT OMEN key");
                    return false;
                }
            }
            
            // Check virtual key codes commonly used for OMEN key
            if (vkCode == VK_LAUNCH_APP2)
            {
                // VK_LAUNCH_APP2 is shared with Calculator key on some keyboards
                // Only treat as OMEN key if scan code matches known OMEN scan codes
                foreach (var omenScan in OmenScanCodes)
                {
                    if (scanCode == omenScan)
                    {
                        _logging.Debug($"VK_LAUNCH_APP2 with OMEN scan code 0x{scanCode:X4} - OMEN key confirmed");
                        return true;
                    }
                }
                
                // Log unrecognized scan codes for debugging (but don't treat as OMEN key)
                _logging.Debug($"VK_LAUNCH_APP2 with unknown scan code: 0x{scanCode:X4} - NOT treated as OMEN key (may be Calculator)");
                return false;
            }

            // Some OMEN models use a dedicated virtual key
            if (vkCode == VK_OEM_OMEN)
            {
                _logging.Debug($"VK_OEM_OMEN (0xFF) detected - OMEN key");
                return true;
            }
            
            // Some newer OMEN models use VK_LAUNCH_APP1 (0xB6)
            // IMPORTANT: Require OMEN-specific scan code validation to avoid false positives
            // from Remote Desktop, media apps, and other software that uses VK_LAUNCH_APP1
            if (vkCode == VK_LAUNCH_APP1)
            {
                foreach (var omenScan in OmenScanCodes)
                {
                    if (scanCode == omenScan)
                    {
                        _logging.Debug($"VK_LAUNCH_APP1 (0xB6) with OMEN scan code 0x{scanCode:X4} - OMEN key confirmed");
                        return true;
                    }
                }
                _logging.Debug($"VK_LAUNCH_APP1 (0xB6) with non-OMEN scan code: 0x{scanCode:X4} - NOT treated as OMEN key (likely Remote Desktop or media app)");
                return false;
            }
            
            // VK 157 (0x9D) - reported on some OMEN models
            if (vkCode == VK_OMEN_157)
            {
                _logging.Debug($"VK 157 (0x9D) detected with scan code: 0x{scanCode:X4} - OMEN key");
                return true;
            }
            
            // F24 (0x87) - reported on some OMEN models
            if (vkCode == VK_F24)
            {
                _logging.Debug($"F24 (0x87) detected with scan code: 0x{scanCode:X4} - OMEN key");
                return true;
            }
            
            // Check scan code even if VK doesn't match (some models send odd VK codes)
            foreach (var omenScan in OmenScanCodes)
            {
                if (scanCode == omenScan)
                {
                    _logging.Debug($"OMEN scan code 0x{scanCode:X4} matched (VK=0x{vkCode:X2}) - OMEN key");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Enable logging of all key presses to identify the OMEN key code.
        /// Use this during development to find the correct key code for your laptop model.
        /// </summary>
        public void EnableKeyDiscoveryMode(int durationSeconds = 30)
        {
            _logging.Info($"Key discovery mode enabled for {durationSeconds} seconds. Press keys to see their codes...");
            
            var originalHook = _hookProc;
            _hookProc = (nCode, wParam, lParam) =>
            {
                if (nCode >= 0)
                {
                    var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int msg = wParam.ToInt32();
                    
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        _logging.Info($"[KEY DISCOVERY] VK=0x{hookStruct.vkCode:X2}, Scan=0x{hookStruct.scanCode:X4}, Flags=0x{hookStruct.flags:X}");
                    }
                }
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            };

            // Restore normal hook after duration
            Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ =>
            {
                _hookProc = originalHook;
                _logging.Info("Key discovery mode ended");
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopInterception();
                _disposed = true;
            }
        }
    }
    
    /// <summary>
    /// Actions that can be bound to the OMEN key
    /// </summary>
    public enum OmenKeyAction
    {
        /// <summary>Show/hide OmenCore window</summary>
        ToggleOmenCore,
        
        /// <summary>Cycle through performance modes</summary>
        CyclePerformance,
        
        /// <summary>Cycle through fan presets</summary>
        CycleFanMode,
        
        /// <summary>Toggle max cooling on/off</summary>
        ToggleMaxCooling,
        
        /// <summary>Launch a user-specified external application</summary>
        LaunchExternalApp,
        
        /// <summary>Suppress the key but do nothing</summary>
        DoNothing
    }
}
