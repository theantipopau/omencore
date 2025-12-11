using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for managing global keyboard hotkeys
    /// </summary>
    public class HotkeyService : IDisposable
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;

        // Modifier keys
        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        #endregion

        private readonly LoggingService _logging;
        private readonly Dictionary<int, HotkeyBinding> _registeredHotkeys = new();
        private IntPtr _windowHandle;
        private HwndSource? _source;
        private int _nextHotkeyId = 1;
        private bool _isEnabled = true;

        // Events for hotkey actions
        public event EventHandler? ToggleFanModeRequested;
        public event EventHandler? TogglePerformanceModeRequested;
        public event EventHandler? ToggleBoostModeRequested;
        public event EventHandler? ToggleQuietModeRequested;
        public event EventHandler? ShowWindowRequested;
        public event EventHandler? HideWindowRequested;
        public event EventHandler? ToggleWindowRequested;
        public event EventHandler? OpenFanControlRequested;
        public event EventHandler? OpenDashboardRequested;
        public event EventHandler? TakeScreenshotRequested;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                _logging.Info($"Hotkeys {(_isEnabled ? "enabled" : "disabled")}");
            }
        }

        public IReadOnlyDictionary<int, HotkeyBinding> RegisteredHotkeys => _registeredHotkeys;

        public HotkeyService(LoggingService logging)
        {
            _logging = logging;
        }

        /// <summary>
        /// Initialize hotkey handling with the main window
        /// </summary>
        public void Initialize(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(windowHandle);
            _source?.AddHook(WndProc);
            
            _logging.Info("HotkeyService initialized");
        }

        /// <summary>
        /// Register default hotkeys
        /// </summary>
        public void RegisterDefaultHotkeys()
        {
            // Ctrl+Shift+F = Cycle fan modes
            RegisterHotkey(HotkeyAction.ToggleFanMode, ModifierKeys.Control | ModifierKeys.Shift, Key.F);
            
            // Ctrl+Shift+P = Cycle performance modes
            RegisterHotkey(HotkeyAction.TogglePerformanceMode, ModifierKeys.Control | ModifierKeys.Shift, Key.P);
            
            // Ctrl+Shift+B = Toggle boost mode
            RegisterHotkey(HotkeyAction.ToggleBoostMode, ModifierKeys.Control | ModifierKeys.Shift, Key.B);
            
            // Ctrl+Shift+Q = Toggle quiet mode
            RegisterHotkey(HotkeyAction.ToggleQuietMode, ModifierKeys.Control | ModifierKeys.Shift, Key.Q);
            
            // Ctrl+Shift+O = Show/hide OmenCore window
            RegisterHotkey(HotkeyAction.ToggleWindow, ModifierKeys.Control | ModifierKeys.Shift, Key.O);
            
            _logging.Info($"Registered {_registeredHotkeys.Count} default hotkeys");
        }

        /// <summary>
        /// Register a hotkey binding
        /// </summary>
        public bool RegisterHotkey(HotkeyAction action, ModifierKeys modifiers, Key key)
        {
            if (_windowHandle == IntPtr.Zero)
            {
                _logging.Info("Cannot register hotkey - window handle not initialized");
                return false;
            }

            // Convert WPF keys to Win32
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            uint mod = ConvertModifiers(modifiers);

            int id = _nextHotkeyId++;
            
            if (RegisterHotKey(_windowHandle, id, mod | MOD_NOREPEAT, vk))
            {
                var binding = new HotkeyBinding
                {
                    Id = id,
                    Action = action,
                    Modifiers = modifiers,
                    Key = key
                };
                _registeredHotkeys[id] = binding;
                _logging.Info($"Registered hotkey: {binding}");
                return true;
            }
            else
            {
                _logging.Info($"Failed to register hotkey: {modifiers}+{key} for {action}");
                return false;
            }
        }

        /// <summary>
        /// Unregister a specific hotkey
        /// </summary>
        public bool UnregisterHotkey(int id)
        {
            if (_registeredHotkeys.TryGetValue(id, out var binding))
            {
                UnregisterHotKey(_windowHandle, id);
                _registeredHotkeys.Remove(id);
                _logging.Info($"Unregistered hotkey: {binding}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Unregister all hotkeys
        /// </summary>
        public void UnregisterAllHotkeys()
        {
            foreach (var id in _registeredHotkeys.Keys)
            {
                UnregisterHotKey(_windowHandle, id);
            }
            _registeredHotkeys.Clear();
            _logging.Info("Unregistered all hotkeys");
        }

        /// <summary>
        /// Update a hotkey binding
        /// </summary>
        public bool UpdateHotkey(HotkeyAction action, ModifierKeys modifiers, Key key)
        {
            // Find and remove existing binding for this action
            int? existingId = null;
            foreach (var kvp in _registeredHotkeys)
            {
                if (kvp.Value.Action == action)
                {
                    existingId = kvp.Key;
                    break;
                }
            }

            if (existingId.HasValue)
            {
                UnregisterHotkey(existingId.Value);
            }

            // Register new binding
            return RegisterHotkey(action, modifiers, key);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && _isEnabled)
            {
                int id = wParam.ToInt32();
                if (_registeredHotkeys.TryGetValue(id, out var binding))
                {
                    handled = true;
                    HandleHotkeyPressed(binding);
                }
            }
            return IntPtr.Zero;
        }

        private void HandleHotkeyPressed(HotkeyBinding binding)
        {
            _logging.Info($"Hotkey pressed: {binding}");

            switch (binding.Action)
            {
                case HotkeyAction.ToggleFanMode:
                    ToggleFanModeRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.TogglePerformanceMode:
                    TogglePerformanceModeRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.ToggleBoostMode:
                    ToggleBoostModeRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.ToggleQuietMode:
                    ToggleQuietModeRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.ShowWindow:
                    ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.HideWindow:
                    HideWindowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.ToggleWindow:
                    ToggleWindowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.OpenFanControl:
                    OpenFanControlRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.OpenDashboard:
                    OpenDashboardRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.TakeScreenshot:
                    TakeScreenshotRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private uint ConvertModifiers(ModifierKeys modifiers)
        {
            uint result = MOD_NONE;
            if (modifiers.HasFlag(ModifierKeys.Alt)) result |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control)) result |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift)) result |= MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Windows)) result |= MOD_WIN;
            return result;
        }

        public void Dispose()
        {
            UnregisterAllHotkeys();
            _source?.RemoveHook(WndProc);
            _source?.Dispose();
        }
    }

    /// <summary>
    /// Available hotkey actions
    /// </summary>
    public enum HotkeyAction
    {
        ToggleFanMode,
        TogglePerformanceMode,
        ToggleBoostMode,
        ToggleQuietMode,
        ShowWindow,
        HideWindow,
        ToggleWindow,
        OpenFanControl,
        OpenDashboard,
        TakeScreenshot
    }

    /// <summary>
    /// Represents a hotkey binding
    /// </summary>
    public class HotkeyBinding
    {
        public int Id { get; set; }
        public HotkeyAction Action { get; set; }
        public ModifierKeys Modifiers { get; set; }
        public Key Key { get; set; }
        public bool IsEnabled { get; set; } = true;

        public override string ToString()
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(Key.ToString());
            return $"{string.Join("+", parts)} → {Action}";
        }

        public string DisplayString
        {
            get
            {
                var parts = new List<string>();
                if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                parts.Add(Key.ToString());
                return string.Join(" + ", parts);
            }
        }
    }
}
