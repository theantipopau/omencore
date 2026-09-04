using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmenCore.Utils
{
    /// <summary>
    /// Tracks the laptop lid open/closed state for the LidState automation trigger. Windows only
    /// exposes lid transitions as a push notification (WM_POWERBROADCAST /
    /// PBT_POWERSETTINGCHANGE for GUID_LIDSWITCH_STATE_CHANGE), delivered to a real window's
    /// message queue - there's no poll-on-demand API the way GetSystemPowerStatus covers AC/battery.
    /// WinForms' SystemEvents.PowerModeChanged doesn't cover lid transitions at all, only
    /// AC/battery and suspend/resume.
    ///
    /// Since AutomationService evaluates triggers by polling every 5 seconds rather than reacting
    /// to events, and Core has no window of its own (WPF-owned windows live in OmenCoreApp), this
    /// runs a tiny message-only window (HWND_MESSAGE parent - never visible, no taskbar entry) on
    /// its own dedicated background thread with its own GetMessage/DispatchMessage pump, entirely
    /// self-contained and independent of any WPF Dispatcher. It exists purely to receive
    /// WM_POWERBROADCAST and cache the latest known lid state for AutomationService's poll to read.
    /// </summary>
    public static class LidSwitchMonitor
    {
        // {BA3E0F4D-B817-4094-A2D1-D56379E6A0F3} - GUID_LIDSWITCH_STATE_CHANGE
        private static readonly Guid GuidLidSwitchStateChange = new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");

        private const string ClassName = "OmenCoreLidSwitchMonitorWindow";
        private const uint WM_POWERBROADCAST = 0x0218;
        private const int PBT_POWERSETTINGCHANGE = 0x8013;
        private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
        private const int HWND_MESSAGE = -3;

        private static readonly object StartLock = new();
        private static bool _startAttempted;
        // Nullable<bool> can't be marked volatile; this is a single 5-second-poll consumer
        // (AutomationService.EvaluateLidTrigger) reading a value only ever written from the
        // dedicated message-pump thread, so a torn/stale read isn't a real concern here - the
        // same tolerance every other cached hardware-signal field in this codebase relies on.
        private static bool? _isLidClosed;

        // Kept as a static field, not a local, so the delegate isn't garbage-collected while
        // native code still holds a function pointer to it.
        private static readonly WndProcDelegate WndProcHandler = WndProc;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        /// <summary>
        /// The most recently observed lid state, or null if no lid-change notification has been
        /// received yet (includes: monitoring hasn't started, the window/notification registration
        /// failed, or this machine has no lid at all - e.g. a desktop). Starts the background
        /// monitor thread on first access; safe to call repeatedly and from any thread.
        /// </summary>
        public static bool? IsLidClosed
        {
            get
            {
                EnsureStarted();
                return _isLidClosed;
            }
        }

        /// <summary>True once the message window and power-setting notification registration have
        /// both succeeded. False (rather than throwing) if either step failed - a desktop PC with
        /// no lid, or any environment where this native surface isn't available.</summary>
        public static bool IsAvailable { get; private set; }

        /// <summary>
        /// Pure, unit-testable interpretation of a WM_POWERBROADCAST/PBT_POWERSETTINGCHANGE
        /// payload for GUID_LIDSWITCH_STATE_CHANGE: per the notification's own documented
        /// contract, Data is a DWORD that is 0 when the lid just closed, 1 when it just opened.
        /// Returns null for any other GUID or a payload too short to carry a DWORD - callers
        /// should leave the cached state unchanged in that case, not clear it.
        /// </summary>
        internal static bool? InterpretLidBroadcast(Guid settingGuid, int dataLength, int dataValue)
        {
            if (settingGuid != GuidLidSwitchStateChange || dataLength < sizeof(int))
                return null;

            return dataValue == 0;
        }

        private static void EnsureStarted()
        {
            if (_startAttempted)
                return;

            lock (StartLock)
            {
                if (_startAttempted)
                    return;
                _startAttempted = true;

                try
                {
                    var pumpThread = new Thread(RunMessagePump)
                    {
                        IsBackground = true,
                        Name = "OmenCore-LidSwitchMonitor"
                    };
                    pumpThread.SetApartmentState(ApartmentState.STA);
                    pumpThread.Start();
                }
                catch
                {
                    IsAvailable = false;
                }
            }
        }

        private static void RunMessagePump()
        {
            try
            {
                var hInstance = GetModuleHandle(null);
                var wndClass = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = WndProcHandler,
                    hInstance = hInstance,
                    lpszClassName = ClassName,
                    lpszMenuName = null
                };

                if (RegisterClassEx(ref wndClass) == 0)
                {
                    IsAvailable = false;
                    return;
                }

                var hwnd = CreateWindowEx(
                    0, ClassName, ClassName, 0, 0, 0, 0, 0,
                    new IntPtr(HWND_MESSAGE), IntPtr.Zero, hInstance, IntPtr.Zero);

                if (hwnd == IntPtr.Zero)
                {
                    IsAvailable = false;
                    return;
                }

                var guid = GuidLidSwitchStateChange;
                var notifyHandle = RegisterPowerSettingNotification(hwnd, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);
                IsAvailable = notifyHandle != IntPtr.Zero;

                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                IsAvailable = false;
            }
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                try
                {
                    // POWERBROADCAST_SETTING layout: GUID PowerSetting (16 bytes),
                    // DWORD DataLength (4 bytes), UCHAR Data[] (DataLength bytes).
                    var settingGuid = Marshal.PtrToStructure<Guid>(lParam);
                    var dataLength = Marshal.ReadInt32(lParam, 16);
                    var dataValue = dataLength >= sizeof(int) ? Marshal.ReadInt32(lParam, 20) : 0;

                    var interpreted = InterpretLidBroadcast(settingGuid, dataLength, dataValue);
                    if (interpreted.HasValue)
                    {
                        _isLidClosed = interpreted.Value;
                    }
                }
                catch
                {
                    // Leave the last-known state as-is rather than clearing it on a malformed payload.
                }

                return IntPtr.Zero;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }
}
