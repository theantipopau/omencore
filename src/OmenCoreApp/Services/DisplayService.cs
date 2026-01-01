using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for controlling display settings including refresh rate and power state.
    /// Implements features from OmenMon like quick refresh rate switching.
    /// </summary>
    public class DisplayService
    {
        private readonly LoggingService _logging;
        
        // Default preset values (can be configured)
        public int HighRefreshRate { get; set; } = 165;
        public int LowRefreshRate { get; set; } = 60;

        public DisplayService(LoggingService logging)
        {
            _logging = logging;
        }

        #region P/Invoke Declarations
        
        [DllImport("user32.dll")]
        private static extern int EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        [DllImport("user32.dll")]
        private static extern int EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);
        
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int CDS_TEST = 0x02;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DISP_CHANGE_RESTART = 1;
        private const int SC_MONITORPOWER = 0xF170;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int MONITOR_OFF = 2;
        private const int MONITOR_ON = -1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        #endregion

        /// <summary>
        /// Get the current refresh rate of the primary display.
        /// </summary>
        public int GetCurrentRefreshRate()
        {
            try
            {
                var dm = new DEVMODE
                {
                    dmSize = (short)Marshal.SizeOf(typeof(DEVMODE))
                };

                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) != 0)
                {
                    return dm.dmDisplayFrequency;
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to get refresh rate: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Get all available refresh rates for the current resolution.
        /// </summary>
        public List<int> GetAvailableRefreshRates()
        {
            var refreshRates = new HashSet<int>();
            
            try
            {
                var dm = new DEVMODE
                {
                    dmSize = (short)Marshal.SizeOf(typeof(DEVMODE))
                };

                // Get current resolution
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) == 0)
                    return refreshRates.ToList();
                    
                int currentWidth = dm.dmPelsWidth;
                int currentHeight = dm.dmPelsHeight;
                int currentBpp = dm.dmBitsPerPel;

                // Enumerate all modes and find those matching current resolution
                int modeNum = 0;
                while (EnumDisplaySettings(null, modeNum++, ref dm) != 0)
                {
                    if (dm.dmPelsWidth == currentWidth && 
                        dm.dmPelsHeight == currentHeight && 
                        dm.dmBitsPerPel == currentBpp)
                    {
                        refreshRates.Add(dm.dmDisplayFrequency);
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to enumerate refresh rates: {ex.Message}");
            }
            
            return refreshRates.OrderBy(r => r).ToList();
        }

        /// <summary>
        /// Set the display refresh rate.
        /// </summary>
        /// <param name="refreshRate">Target refresh rate in Hz</param>
        /// <returns>True if successful</returns>
        public bool SetRefreshRate(int refreshRate)
        {
            try
            {
                var dm = new DEVMODE
                {
                    dmSize = (short)Marshal.SizeOf(typeof(DEVMODE))
                };

                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) == 0)
                {
                    _logging.Warn("Failed to get current display settings");
                    return false;
                }

                // Find a mode with the requested refresh rate
                int targetWidth = dm.dmPelsWidth;
                int targetHeight = dm.dmPelsHeight;
                int targetBpp = dm.dmBitsPerPel;
                
                int modeNum = 0;
                bool found = false;
                
                while (EnumDisplaySettings(null, modeNum++, ref dm) != 0)
                {
                    if (dm.dmPelsWidth == targetWidth && 
                        dm.dmPelsHeight == targetHeight && 
                        dm.dmBitsPerPel == targetBpp &&
                        dm.dmDisplayFrequency == refreshRate)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    _logging.Warn($"Refresh rate {refreshRate}Hz not available at current resolution");
                    return false;
                }

                // Test the change first
                int testResult = ChangeDisplaySettings(ref dm, CDS_TEST);
                if (testResult != DISP_CHANGE_SUCCESSFUL)
                {
                    _logging.Warn($"Display settings test failed: {testResult}");
                    return false;
                }

                // Apply the change
                int result = ChangeDisplaySettings(ref dm, CDS_UPDATEREGISTRY);
                if (result == DISP_CHANGE_SUCCESSFUL || result == DISP_CHANGE_RESTART)
                {
                    _logging.Info($"✓ Refresh rate changed to {refreshRate}Hz");
                    return true;
                }
                else
                {
                    _logging.Warn($"Failed to change refresh rate: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set refresh rate: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Toggle between high and low refresh rates.
        /// </summary>
        /// <returns>The new refresh rate, or 0 if failed</returns>
        public int ToggleRefreshRate()
        {
            int current = GetCurrentRefreshRate();
            var available = GetAvailableRefreshRates();
            
            // Determine target rate
            int target;
            if (current >= HighRefreshRate || !available.Contains(LowRefreshRate))
            {
                // Currently high (or low not available), switch to low if possible
                target = available.Contains(LowRefreshRate) ? LowRefreshRate : available.Min();
            }
            else
            {
                // Currently low or mid, switch to high if possible
                target = available.Contains(HighRefreshRate) ? HighRefreshRate : available.Max();
            }
            
            if (SetRefreshRate(target))
            {
                return target;
            }
            return 0;
        }

        /// <summary>
        /// Switch to high refresh rate.
        /// </summary>
        public bool SetHighRefreshRate()
        {
            var available = GetAvailableRefreshRates();
            int target = available.Contains(HighRefreshRate) ? HighRefreshRate : available.Max();
            return SetRefreshRate(target);
        }

        /// <summary>
        /// Switch to low/power-saving refresh rate.
        /// </summary>
        public bool SetLowRefreshRate()
        {
            var available = GetAvailableRefreshRates();
            int target = available.Contains(LowRefreshRate) ? LowRefreshRate : available.Min();
            return SetRefreshRate(target);
        }

        /// <summary>
        /// Turn off the display while keeping the system running.
        /// Useful for background tasks while saving power.
        /// </summary>
        public bool TurnOffDisplay()
        {
            try
            {
                // Get the foreground window to send the message to
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    // Use desktop window as fallback
                    hwnd = GetDesktopWindow();
                }
                
                SendMessage(hwnd, WM_SYSCOMMAND, SC_MONITORPOWER, MONITOR_OFF);
                _logging.Info("✓ Display turned off");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to turn off display: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Turn on the display.
        /// </summary>
        public bool TurnOnDisplay()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = GetDesktopWindow();
                }
                
                SendMessage(hwnd, WM_SYSCOMMAND, SC_MONITORPOWER, MONITOR_ON);
                _logging.Info("✓ Display turned on");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to turn on display: {ex.Message}", ex);
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        /// <summary>
        /// Get display information.
        /// </summary>
        public DisplayInfo GetDisplayInfo()
        {
            try
            {
                var dm = new DEVMODE
                {
                    dmSize = (short)Marshal.SizeOf(typeof(DEVMODE))
                };

                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) != 0)
                {
                    return new DisplayInfo
                    {
                        Width = dm.dmPelsWidth,
                        Height = dm.dmPelsHeight,
                        RefreshRate = dm.dmDisplayFrequency,
                        BitsPerPixel = dm.dmBitsPerPel
                    };
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to get display info: {ex.Message}");
            }
            
            return new DisplayInfo();
        }
    }

    public class DisplayInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
        public int BitsPerPixel { get; set; }

        public override string ToString() => $"{Width}x{Height} @ {RefreshRate}Hz";
    }
}
