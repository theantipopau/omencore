using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HidSharp;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Service for controlling RGB lighting on OMEN desktop PCs.
    /// Supports case fans, LED strips, logo lighting, and front panel accents.
    /// Compatible with OMEN 45L, 40L, 35L, 30L, 25L, and Obelisk series.
    /// </summary>
    public class OmenDesktopRgbService : IDisposable
    {
        private readonly LoggingService _logging;
        private bool _initialized;
        private bool _disposed;
        private List<DesktopRgbZone> _zones = new();
        private string _desktopModel = string.Empty;

        #region WMI Constants

        private const string HP_WMI_BIOS_NAMESPACE = @"root\HP\InstrumentedBIOS";
        private const string HP_WMI_HARDWARE_NAMESPACE = @"root\WMI";
        private const string OMEN_RGB_CLASS = "HPBIOS_BIOSInteger";
        private const string OMEN_RGB_METHOD_CLASS = "HPWMI_RGBControl";

        // Known BIOS setting names for desktop RGB (varies by model)
        private const string SETTING_RGB_ENABLED = "RGB Lighting";
        private const string SETTING_RGB_MODE = "RGB Mode";
        private const string SETTING_RGB_COLOR = "RGB Color";
        private const string SETTING_RGB_SPEED = "RGB Animation Speed";

        #endregion

        #region USB HID Constants (for direct control)

        // OMEN Desktop RGB Controller USB identifiers
        private const int OMEN_RGB_VID = 0x103C; // HP Vendor ID
        private static readonly int[] OMEN_RGB_PIDs = { 0x84FD, 0x84FE, 0x8602, 0x8603 }; // Known RGB controller PIDs
        
        // HID interface requirements from SignalRGB plugin
        private const int OMEN_RGB_INTERFACE = 0x00;
        private const int OMEN_RGB_USAGE = 0x00;
        private const int OMEN_RGB_USAGE_PAGE = 0x0001;
        
        // HID packet structure (58 bytes)
        private const int HID_PACKET_SIZE = 58;
        private const byte HID_HEADER_1 = 0x3E;
        private const byte HID_HEADER_2 = 0x12;
        
        // Effect modes for HID protocol (from OpenRGB HPOmen30LController)
        private const byte HID_MODE_STATIC = 0x01;
        private const byte HID_MODE_DIRECT = 0x04;   // Direct mode for SignalRGB integration
        private const byte HID_MODE_OFF = 0x05;      // Turn off LEDs
        private const byte HID_MODE_BREATHING = 0x06;
        private const byte HID_MODE_CYCLE = 0x07;
        private const byte HID_MODE_BLINKING = 0x08;
        private const byte HID_MODE_WAVE = 0x09;
        private const byte HID_MODE_RADIAL = 0x0A;
        
        // Brightness levels
        private const byte HID_BRIGHTNESS_25 = 0x19;
        private const byte HID_BRIGHTNESS_50 = 0x32;
        private const byte HID_BRIGHTNESS_75 = 0x4B;
        private const byte HID_BRIGHTNESS_100 = 0x64;
        
        // LED Module IDs
        private const byte HID_LED_ALL = 0x00;
        private const byte HID_LED_FRONT = 0x01;
        private const byte HID_LED_LIGHT_BAR = 0x02;
        private const byte HID_LED_CPU_COOLER = 0x04;
        
        // Power states
        private const byte HID_POWER_ON = 0x01;
        private const byte HID_POWER_SUSPEND = 0x02;
        
        // Animation speeds
        private const byte HID_SPEED_SLOW = 0x01;
        private const byte HID_SPEED_MEDIUM = 0x02;
        private const byte HID_SPEED_FAST = 0x03;
        
        // Themes (predefined color patterns)
        private const byte HID_THEME_CUSTOM = 0x00;
        private const byte HID_THEME_GALAXY = 0x01;
        private const byte HID_THEME_VOLCANO = 0x02;
        private const byte HID_THEME_JUNGLE = 0x03;
        private const byte HID_THEME_OCEAN = 0x04;
        private const byte HID_THEME_UNICORN = 0x05;
        
        // Type bytes (determines packet structure)
        private const byte HID_TYPE_STATIC = 0x02;
        private const byte HID_TYPE_DIRECT = 0x04;   // Per-zone RGBA with brightness
        private const byte HID_TYPE_ANIMATED = 0x0A; // For breathing/cycle with multiple colors
        
        // Zone IDs (matches OpenRGB mapping)
        private const byte ZONE_LOGO = 0x01;
        private const byte ZONE_BAR = 0x02;
        private const byte ZONE_FAN = 0x03;
        private const byte ZONE_CPU = 0x04;
        private const byte ZONE_BOT_FAN = 0x05;
        private const byte ZONE_MID_FAN = 0x06;
        private const byte ZONE_TOP_FAN = 0x07;
        
        // HID device for direct control
        private HidDevice? _hidDevice;
        private HidStream? _hidStream;
        
        // Current power state mode
        private RgbPowerState _currentPowerState = RgbPowerState.On;

        #endregion

        /// <summary>
        /// Gets whether the desktop RGB service is initialized and available.
        /// </summary>
        public bool IsAvailable => _initialized && _zones.Count > 0;

        /// <summary>
        /// Gets the detected desktop model name.
        /// </summary>
        public string DesktopModel => _desktopModel;

        /// <summary>
        /// Gets the list of detected RGB zones.
        /// </summary>
        public IReadOnlyList<DesktopRgbZone> Zones => _zones.AsReadOnly();

        /// <summary>
        /// Gets the total number of RGB zones.
        /// </summary>
        public int ZoneCount => _zones.Count;

        public OmenDesktopRgbService(LoggingService logging)
        {
            _logging = logging;
        }

        /// <summary>
        /// Initialize the desktop RGB service and detect available zones.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_initialized) return IsAvailable;

            try
            {
                _logging.Info("OmenDesktopRGB: Initializing...");

                // Check if this is an OMEN desktop
                if (!await DetectOmenDesktopAsync())
                {
                    _logging.Info("OmenDesktopRGB: Not an OMEN desktop or RGB not detected");
                    return false;
                }

                // Try WMI-based detection first
                if (await DetectZonesViaWmiAsync())
                {
                    _logging.Info($"OmenDesktopRGB: Detected {_zones.Count} zones via WMI");
                }
                // Fall back to USB HID detection
                else if (await DetectZonesViaUsbAsync())
                {
                    _logging.Info($"OmenDesktopRGB: Detected {_zones.Count} zones via USB HID");
                }
                else
                {
                    _logging.Info("OmenDesktopRGB: No RGB zones detected");
                    return false;
                }

                _initialized = true;
                _logging.Info($"OmenDesktopRGB: Initialized with {_zones.Count} zones on {_desktopModel}");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"OmenDesktopRGB: Initialization failed: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Detect if this is an OMEN desktop system.
        /// </summary>
        private async Task<bool> DetectOmenDesktopAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                        var model = obj["Model"]?.ToString() ?? "";

                        if (manufacturer.Contains("HP", StringComparison.OrdinalIgnoreCase) &&
                            model.Contains("OMEN", StringComparison.OrdinalIgnoreCase))
                        {
                            _desktopModel = model;
                            
                            // Check if it's a desktop (not laptop)
                            var systemType = obj["SystemType"]?.ToString() ?? "";
                            var pcType = obj["PCSystemType"]?.ToString() ?? "1";
                            
                            // PCSystemType: 1 = Desktop, 2 = Mobile
                            if (pcType == "1" || model.Contains("45L") || model.Contains("40L") || 
                                model.Contains("35L") || model.Contains("30L") || model.Contains("25L") ||
                                model.Contains("Obelisk"))
                            {
                                _logging.Info($"OmenDesktopRGB: Detected OMEN desktop: {model}");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: WMI detection failed: {ex.Message}");
                }

                return false;
            });
        }

        /// <summary>
        /// Detect RGB zones via HP WMI interface.
        /// </summary>
        private async Task<bool> DetectZonesViaWmiAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(HP_WMI_BIOS_NAMESPACE, "SELECT * FROM HPBIOS_BIOSSetting");
                    
                    bool hasRgb = false;
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var name = obj["Name"]?.ToString() ?? "";
                        
                        // Look for RGB-related BIOS settings
                        if (name.Contains("RGB", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("LED", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Light", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRgb = true;
                            _logging.Debug($"OmenDesktopRGB: Found BIOS setting: {name}");
                        }
                    }

                    if (hasRgb)
                    {
                        // Create default zone configuration for OMEN desktops
                        CreateDefaultZones();
                        return _zones.Count > 0;
                    }
                }
                catch (ManagementException ex)
                {
                    _logging.Debug($"OmenDesktopRGB: WMI namespace not available: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: WMI zone detection failed: {ex.Message}");
                }

                return false;
            });
        }

        /// <summary>
        /// Detect RGB zones via USB HID interface using HidSharp for direct device access.
        /// Based on SignalRGB HP OMEN desktop plugin protocol.
        /// </summary>
        private async Task<bool> DetectZonesViaUsbAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var deviceList = DeviceList.Local;
                    var hidDevices = deviceList.GetHidDevices();
                    
                    foreach (var device in hidDevices)
                    {
                        if (device.VendorID == OMEN_RGB_VID && OMEN_RGB_PIDs.Contains(device.ProductID))
                        {
                            // Validate HID interface per SignalRGB plugin requirements
                            try
                            {
                                var reportDescriptor = device.GetReportDescriptor();
                                foreach (var item in reportDescriptor.DeviceItems)
                                {
                                    foreach (var usage in item.Usages.GetAllValues())
                                    {
                                        // Check for correct usage page and usage
                                        var usagePage = (int)((usage >> 16) & 0xFFFF);
                                        var usageId = (int)(usage & 0xFFFF);
                                        
                                        if (usagePage == OMEN_RGB_USAGE_PAGE)
                                        {
                                            _hidDevice = device;
                                            var productName = GetProductName(device);
                                            _logging.Info($"OmenDesktopRGB: Found HID RGB controller: {productName} (VID:0x{device.VendorID:X4} PID:0x{device.ProductID:X4})");
                                            
                                            // Create SignalRGB-compatible zone configuration
                                            CreateSignalRgbZones();
                                            
                                            // Try to open the device for communication
                                            if (TryOpenHidDevice())
                                            {
                                                _logging.Info($"OmenDesktopRGB: Successfully opened HID device for direct RGB control");
                                            }
                                            else
                                            {
                                                _logging.Warn($"OmenDesktopRGB: Could not open HID device - will use WMI fallback");
                                            }
                                            
                                            return _zones.Count > 0;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logging.Debug($"OmenDesktopRGB: Could not validate HID interface for device 0x{device.ProductID:X4}: {ex.Message}");
                            }
                            
                            // Fallback: Accept device without strict interface validation
                            _hidDevice = device;
                            _logging.Info($"OmenDesktopRGB: Found RGB controller (PID:0x{device.ProductID:X4}) - using default zone config");
                            CreateSignalRgbZones();
                            TryOpenHidDevice();
                            return _zones.Count > 0;
                        }
                    }
                    
                    // Secondary fallback: WMI-based PnP entity detection
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_103C%'");
                    
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var deviceId = obj["DeviceID"]?.ToString() ?? "";
                        var name = obj["Name"]?.ToString() ?? "";

                        foreach (var pid in OMEN_RGB_PIDs)
                        {
                            if (deviceId.Contains($"PID_{pid:X4}", StringComparison.OrdinalIgnoreCase))
                            {
                                _logging.Info($"OmenDesktopRGB: Found RGB controller via WMI: {name} ({deviceId})");
                                CreateSignalRgbZones();
                                return _zones.Count > 0;
                            }
                        }

                        // Generic RGB controller detection
                        if (name.Contains("RGB", StringComparison.OrdinalIgnoreCase) && 
                            name.Contains("OMEN", StringComparison.OrdinalIgnoreCase))
                        {
                            _logging.Info($"OmenDesktopRGB: Found generic OMEN RGB: {name}");
                            CreateDefaultZones();
                            return _zones.Count > 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: USB detection failed: {ex.Message}");
                }

                return false;
            });
        }
        
        /// <summary>
        /// Get the product name from a HID device.
        /// </summary>
        private string GetProductName(HidDevice device)
        {
            try
            {
                var name = device.GetProductName();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            catch { }
            
            return device.ProductID switch
            {
                0x84FD => "OMEN Desktop RGB Controller",
                0x84FE => "OMEN Desktop RGB Controller (Alt)",
                0x8602 => "OMEN 45L RGB Controller",
                0x8603 => "OMEN 30L RGB Controller",
                _ => $"OMEN RGB Controller (0x{device.ProductID:X4})"
            };
        }
        
        /// <summary>
        /// Try to open the HID device for direct RGB control.
        /// </summary>
        private bool TryOpenHidDevice()
        {
            if (_hidDevice == null)
                return false;
                
            try
            {
                _hidStream?.Dispose();
                _hidStream = _hidDevice.Open();
                _hidStream.WriteTimeout = 1000;
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"OmenDesktopRGB: Could not open HID device: {ex.Message}");
                _hidStream = null;
                return false;
            }
        }
        
        /// <summary>
        /// Create SignalRGB/OpenRGB-compatible zone configuration for HP OMEN desktops.
        /// Based on OpenRGB HPOmen30LController zone mapping:
        /// - Zone 0 (0x01): Logo/Diamond
        /// - Zone 1 (0x02): Light Bar
        /// - Zone 2 (0x03): Front Fan
        /// - Zone 3 (0x04): CPU Cooler
        /// - Zone 4 (0x05): Bottom Fan
        /// - Zone 5 (0x06): Middle Fan
        /// - Zone 6 (0x07): Top Fan
        /// </summary>
        private void CreateSignalRgbZones()
        {
            _zones.Clear();
            
            // OpenRGB-compatible zone layout (matches 58-byte HID packet structure)
            _zones.Add(new DesktopRgbZone(0, "OMEN Logo", RgbZoneType.Logo, true));
            _zones.Add(new DesktopRgbZone(1, "Light Bar", RgbZoneType.LedStrip, true));
            _zones.Add(new DesktopRgbZone(2, "Front Fan", RgbZoneType.Fan, true));
            _zones.Add(new DesktopRgbZone(3, "CPU Cooler", RgbZoneType.Fan, true));
            _zones.Add(new DesktopRgbZone(4, "Bottom Fan", RgbZoneType.Fan, true));
            _zones.Add(new DesktopRgbZone(5, "Middle Fan", RgbZoneType.Fan, true));
            _zones.Add(new DesktopRgbZone(6, "Top Fan", RgbZoneType.Fan, true));
            
            _logging.Info($"OmenDesktopRGB: Created {_zones.Count} OpenRGB-compatible zones");
        }

        /// <summary>
        /// Create default zone configuration based on detected desktop model.
        /// </summary>
        private void CreateDefaultZones()
        {
            _zones.Clear();

            // OMEN 45L has the most zones
            if (_desktopModel.Contains("45L", StringComparison.OrdinalIgnoreCase))
            {
                _zones.Add(new DesktopRgbZone(0, "Top Fan 1", RgbZoneType.Fan, true));
                _zones.Add(new DesktopRgbZone(1, "Top Fan 2", RgbZoneType.Fan, true));
                _zones.Add(new DesktopRgbZone(2, "Top Fan 3", RgbZoneType.Fan, true));
                _zones.Add(new DesktopRgbZone(3, "Front Fan 1", RgbZoneType.Fan, true));
                _zones.Add(new DesktopRgbZone(4, "Front Fan 2", RgbZoneType.Fan, true));
                _zones.Add(new DesktopRgbZone(5, "Front Fan 3", RgbZoneType.Fan, true));
                _zones.Add(new DesktopRgbZone(6, "Interior Strip", RgbZoneType.LedStrip, true));
                _zones.Add(new DesktopRgbZone(7, "OMEN Logo", RgbZoneType.Logo, true));
                _zones.Add(new DesktopRgbZone(8, "Front Panel", RgbZoneType.Accent, true));
            }
            // OMEN 25L/30L - fewer zones
            else if (_desktopModel.Contains("25L", StringComparison.OrdinalIgnoreCase) ||
                     _desktopModel.Contains("30L", StringComparison.OrdinalIgnoreCase))
            {
                _zones.Add(new DesktopRgbZone(0, "Front Fan", RgbZoneType.Fan, true));
                _zones.Add(new DesktopRgbZone(1, "OMEN Logo", RgbZoneType.Logo, true));
                _zones.Add(new DesktopRgbZone(2, "Front Accent", RgbZoneType.Accent, true));
            }
            // Default configuration for unknown models
            else
            {
                _zones.Add(new DesktopRgbZone(0, "RGB Zone 1", RgbZoneType.Generic, true));
                _zones.Add(new DesktopRgbZone(1, "RGB Zone 2", RgbZoneType.Generic, false));
                _zones.Add(new DesktopRgbZone(2, "OMEN Logo", RgbZoneType.Logo, true));
            }

            _logging.Info($"OmenDesktopRGB: Created {_zones.Count} default zones for {_desktopModel}");
        }

        /// <summary>
        /// Set the color for a specific zone.
        /// </summary>
        /// <param name="zoneId">Zone ID</param>
        /// <param name="r">Red (0-255)</param>
        /// <param name="g">Green (0-255)</param>
        /// <param name="b">Blue (0-255)</param>
        public async Task<bool> SetZoneColorAsync(int zoneId, byte r, byte g, byte b)
        {
            if (!IsAvailable) return false;

            var zone = _zones.FirstOrDefault(z => z.Id == zoneId);
            if (zone == null)
            {
                _logging.Warn($"OmenDesktopRGB: Zone {zoneId} not found");
                return false;
            }

            try
            {
                _logging.Debug($"OmenDesktopRGB: Setting zone {zoneId} ({zone.Name}) to RGB({r},{g},{b})");

                // Try direct HID control first (preferred method)
                if (_hidStream != null && await SetZoneColorViaHidAsync(zoneId, r, g, b))
                {
                    zone.CurrentColor = (r, g, b);
                    return true;
                }
                
                // Fallback to WMI
                if (await SetZoneColorViaWmiAsync(zoneId, r, g, b))
                {
                    zone.CurrentColor = (r, g, b);
                    return true;
                }

                _logging.Warn($"OmenDesktopRGB: Failed to set color for zone {zoneId} - no working control method");
                return false;
            }
            catch (Exception ex)
            {
                _logging.Error($"OmenDesktopRGB: Error setting zone color: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Set all zones to the same color using direct HID for better performance.
        /// This sends a single HID packet with all zone colors.
        /// </summary>
        public async Task<bool> SetAllZonesColorViaHidAsync(byte r, byte g, byte b, 
            RgbEffectMode mode = RgbEffectMode.Static, int brightness = 100)
        {
            if (_hidStream == null)
                return false;
                
            return await Task.Run(() =>
            {
                try
                {
                    var packet = BuildHidPacket(mode, brightness);
                    
                    // Set all controllable zones to the same color
                    foreach (var zone in _zones.Where(z => z.IsControllable))
                    {
                        SetZoneColorInPacket(packet, zone.Id, r, g, b);
                        zone.CurrentColor = (r, g, b);
                    }
                    
                    return WriteHidPacket(packet);
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: HID set all zones failed: {ex.Message}");
                    return false;
                }
            });
        }
        
        /// <summary>
        /// Set a zone color via direct HID communication.
        /// Based on SignalRGB HP OMEN desktop plugin 58-byte packet structure.
        /// </summary>
        private async Task<bool> SetZoneColorViaHidAsync(int zoneId, byte r, byte g, byte b)
        {
            if (_hidStream == null)
                return false;
                
            return await Task.Run(() =>
            {
                try
                {
                    // Build packet with current zone colors, updating the specified zone
                    var packet = BuildHidPacket(RgbEffectMode.Static, 100);
                    
                    // Set all current colors first
                    foreach (var zone in _zones)
                    {
                        SetZoneColorInPacket(packet, zone.Id, zone.CurrentColor.R, zone.CurrentColor.G, zone.CurrentColor.B);
                    }
                    
                    // Update the target zone
                    SetZoneColorInPacket(packet, zoneId, r, g, b);
                    
                    return WriteHidPacket(packet);
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: HID set zone color failed: {ex.Message}");
                    return false;
                }
            });
        }
        
        /// <summary>
        /// Build a 58-byte HID packet for HP OMEN desktop RGB control.
        /// Structure based on SignalRGB plugin analysis.
        /// </summary>
        private byte[] BuildHidPacket(RgbEffectMode mode, int brightnessPercent)
        {
            var packet = new byte[HID_PACKET_SIZE];
            
            // Header
            packet[0] = 0x00;  // Report ID
            packet[1] = HID_HEADER_1;  // 0x3E
            packet[2] = HID_HEADER_2;  // 0x12
            
            // Mode
            packet[3] = mode switch
            {
                RgbEffectMode.Static => HID_MODE_STATIC,
                RgbEffectMode.Breathing => HID_MODE_BREATHING,
                RgbEffectMode.ColorCycle => HID_MODE_CYCLE,
                _ => HID_MODE_STATIC
            };
            
            // Custom color count (set for static mode)
            packet[4] = 0x01;
            packet[5] = 0x01;
            packet[6] = 0x00;
            packet[7] = 0x00;
            
            // Zone colors are at packet[8] through packet[28] (7 zones × 3 bytes each)
            // Initialized to 0 (black) - caller fills in with SetZoneColorInPacket
            
            // Brightness at packet[48]
            packet[48] = brightnessPercent switch
            {
                <= 25 => HID_BRIGHTNESS_25,
                <= 50 => HID_BRIGHTNESS_50,
                <= 75 => HID_BRIGHTNESS_75,
                _ => HID_BRIGHTNESS_100
            };
            
            // Static color mode indicator
            packet[49] = 0x02;
            
            // LED module selection - all modules
            packet[54] = HID_LED_ALL;
            
            // Power state - on
            packet[55] = HID_POWER_ON;
            
            // Theme - custom (use explicit colors)
            packet[56] = HID_THEME_CUSTOM;
            
            // Speed - not applicable for static
            packet[57] = 0x00;
            
            return packet;
        }
        
        /// <summary>
        /// Set the color for a specific zone in an HID packet.
        /// Zone RGB data starts at byte 8, with 3 bytes per zone (R, G, B).
        /// </summary>
        private void SetZoneColorInPacket(byte[] packet, int zoneId, byte r, byte g, byte b)
        {
            if (zoneId < 0 || zoneId > 6)
                return;
                
            int offset = 8 + (zoneId * 3);
            packet[offset] = r;
            packet[offset + 1] = g;
            packet[offset + 2] = b;
        }
        
        /// <summary>
        /// Write an HID packet to the RGB controller.
        /// </summary>
        private bool WriteHidPacket(byte[] packet)
        {
            if (_hidStream == null)
                return false;
                
            try
            {
                _hidStream.Write(packet, 0, packet.Length);
                _logging.Debug($"OmenDesktopRGB: Wrote {packet.Length}-byte HID packet");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"OmenDesktopRGB: HID write failed: {ex.Message}");
                
                // Try to reopen the device
                if (TryOpenHidDevice())
                {
                    try
                    {
                        _hidStream!.Write(packet, 0, packet.Length);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
                
                return false;
            }
        }
        
        #region Advanced Desktop RGB Features (OpenRGB/SignalRGB Protocol)
        
        /// <summary>
        /// Set a zone to Direct mode for real-time RGB control.
        /// Direct mode uses RGBA per zone (4 bytes: brightness + RGB).
        /// This is the preferred mode for SignalRGB integration.
        /// </summary>
        public async Task<bool> SetZoneDirectModeAsync(int zoneId, byte r, byte g, byte b, byte brightness = 100)
        {
            if (_hidStream == null)
                return false;
                
            return await Task.Run(() =>
            {
                try
                {
                    var packet = new byte[HID_PACKET_SIZE];
                    var zoneValue = GetZoneValue(zoneId);
                    if (zoneValue == 0) return false;
                    
                    // Build direct mode packet (OpenRGB protocol)
                    packet[0x02] = HID_HEADER_2; // Version ID
                    packet[0x03] = HID_MODE_DIRECT;
                    packet[0x04] = 0x01; // Color count
                    packet[0x05] = 0x01; // Current color
                    
                    // Direct mode uses RGBA (4 bytes per zone)
                    int index = (zoneValue - 1);
                    packet[0x08 + index * 4] = brightness;
                    packet[0x09 + index * 4] = r;
                    packet[0x0A + index * 4] = g;
                    packet[0x0B + index * 4] = b;
                    
                    packet[0x30] = brightness; // Global brightness
                    packet[0x31] = HID_TYPE_DIRECT;
                    packet[0x36] = zoneValue; // Zone to update
                    packet[0x37] = (byte)_currentPowerState;
                    
                    var success = WriteHidPacket(packet);
                    if (success)
                    {
                        var zone = _zones.FirstOrDefault(z => z.Id == zoneId);
                        if (zone != null)
                        {
                            zone.CurrentColor = (r, g, b);
                            zone.Brightness = brightness;
                        }
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: Direct mode failed for zone {zoneId}: {ex.Message}");
                    return false;
                }
            });
        }
        
        /// <summary>
        /// Set RGB for suspend/sleep state (displayed when PC is suspended).
        /// </summary>
        public async Task<bool> SetSuspendColorsAsync(byte r, byte g, byte b, RgbEffectMode mode = RgbEffectMode.Static)
        {
            if (_hidStream == null)
                return false;
                
            var previousState = _currentPowerState;
            try
            {
                _currentPowerState = RgbPowerState.Suspend;
                
                foreach (var zone in _zones.Where(z => z.IsControllable))
                {
                    await SetZoneColorAsync(zone.Id, r, g, b);
                }
                
                _logging.Info($"OmenDesktopRGB: Set suspend colors to RGB({r},{g},{b})");
                return true;
            }
            finally
            {
                _currentPowerState = previousState;
            }
        }
        
        /// <summary>
        /// Apply a predefined theme (Galaxy, Volcano, Jungle, Ocean, Unicorn).
        /// </summary>
        public async Task<bool> ApplyThemeAsync(DesktopRgbTheme theme, int speed = 2)
        {
            if (_hidStream == null)
                return false;
                
            return await Task.Run(() =>
            {
                try
                {
                    var themeValue = theme switch
                    {
                        DesktopRgbTheme.Galaxy => HID_THEME_GALAXY,
                        DesktopRgbTheme.Volcano => HID_THEME_VOLCANO,
                        DesktopRgbTheme.Jungle => HID_THEME_JUNGLE,
                        DesktopRgbTheme.Ocean => HID_THEME_OCEAN,
                        DesktopRgbTheme.Unicorn => HID_THEME_UNICORN,
                        _ => HID_THEME_CUSTOM
                    };
                    
                    var success = true;
                    foreach (var zone in _zones.Where(z => z.IsControllable))
                    {
                        var packet = new byte[HID_PACKET_SIZE];
                        var zoneValue = GetZoneValue(zone.Id);
                        
                        packet[0x02] = HID_HEADER_2;
                        packet[0x03] = HID_MODE_CYCLE; // Animated mode for themes
                        packet[0x30] = HID_BRIGHTNESS_100;
                        packet[0x31] = HID_TYPE_ANIMATED;
                        packet[0x36] = zoneValue;
                        packet[0x37] = (byte)_currentPowerState;
                        packet[0x38] = themeValue;
                        packet[0x39] = (byte)Math.Clamp(speed, 1, 3);
                        
                        if (!WriteHidPacket(packet))
                            success = false;
                    }
                    
                    _logging.Info($"OmenDesktopRGB: Applied theme {theme}");
                    return success;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: Theme application failed: {ex.Message}");
                    return false;
                }
            });
        }
        
        /// <summary>
        /// Set per-zone brightness (0-100).
        /// </summary>
        public async Task<bool> SetZoneBrightnessAsync(int zoneId, int brightness)
        {
            var zone = _zones.FirstOrDefault(z => z.Id == zoneId);
            if (zone == null || !zone.IsControllable)
                return false;
                
            zone.Brightness = (byte)Math.Clamp(brightness, 0, 100);
            
            // Reapply color with new brightness
            return await SetZoneDirectModeAsync(zoneId, zone.CurrentColor.R, zone.CurrentColor.G, zone.CurrentColor.B, zone.Brightness);
        }
        
        /// <summary>
        /// Set breathing effect with custom colors.
        /// </summary>
        public async Task<bool> SetBreathingEffectAsync(int zoneId, byte r, byte g, byte b, int speed = 2)
        {
            if (_hidStream == null)
                return false;
                
            return await Task.Run(() =>
            {
                try
                {
                    var packet = new byte[HID_PACKET_SIZE];
                    var zoneValue = GetZoneValue(zoneId);
                    if (zoneValue == 0) return false;
                    
                    packet[0x02] = HID_HEADER_2;
                    packet[0x03] = HID_MODE_BREATHING;
                    packet[0x04] = 0x01; // Color count
                    packet[0x05] = 0x01;
                    
                    int index = zoneValue - 1;
                    packet[0x08 + index * 3] = r;
                    packet[0x09 + index * 3] = g;
                    packet[0x0A + index * 3] = b;
                    
                    packet[0x30] = HID_BRIGHTNESS_100;
                    packet[0x31] = HID_TYPE_ANIMATED;
                    packet[0x36] = zoneValue;
                    packet[0x37] = (byte)_currentPowerState;
                    packet[0x38] = HID_THEME_CUSTOM;
                    packet[0x39] = (byte)Math.Clamp(speed, 1, 3);
                    
                    var zone = _zones.FirstOrDefault(z => z.Id == zoneId);
                    if (zone != null)
                    {
                        zone.CurrentMode = RgbEffectMode.Breathing;
                        zone.CurrentColor = (r, g, b);
                    }
                    
                    return WriteHidPacket(packet);
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: Breathing effect failed: {ex.Message}");
                    return false;
                }
            });
        }
        
        /// <summary>
        /// Set color cycle (rainbow) effect.
        /// </summary>
        public async Task<bool> SetColorCycleEffectAsync(int zoneId, int speed = 2)
        {
            if (_hidStream == null)
                return false;
                
            return await Task.Run(() =>
            {
                try
                {
                    var packet = new byte[HID_PACKET_SIZE];
                    var zoneValue = GetZoneValue(zoneId);
                    if (zoneValue == 0) return false;
                    
                    packet[0x02] = HID_HEADER_2;
                    packet[0x03] = HID_MODE_CYCLE;
                    packet[0x30] = HID_BRIGHTNESS_100;
                    packet[0x31] = HID_TYPE_ANIMATED;
                    packet[0x36] = zoneValue;
                    packet[0x37] = (byte)_currentPowerState;
                    packet[0x38] = HID_THEME_CUSTOM;
                    packet[0x39] = (byte)Math.Clamp(speed, 1, 3);
                    
                    var zone = _zones.FirstOrDefault(z => z.Id == zoneId);
                    if (zone != null)
                        zone.CurrentMode = RgbEffectMode.ColorCycle;
                    
                    return WriteHidPacket(packet);
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: Color cycle failed: {ex.Message}");
                    return false;
                }
            });
        }
        
        /// <summary>
        /// Set wave effect (requires multiple zones).
        /// </summary>
        public async Task<bool> SetWaveEffectAsync(int speed = 2)
        {
            if (_hidStream == null)
                return false;
                
            return await Task.Run(() =>
            {
                try
                {
                    var success = true;
                    foreach (var zone in _zones.Where(z => z.IsControllable))
                    {
                        var packet = new byte[HID_PACKET_SIZE];
                        var zoneValue = GetZoneValue(zone.Id);
                        
                        packet[0x02] = HID_HEADER_2;
                        packet[0x03] = HID_MODE_WAVE;
                        packet[0x30] = HID_BRIGHTNESS_100;
                        packet[0x31] = HID_TYPE_ANIMATED;
                        packet[0x36] = zoneValue;
                        packet[0x37] = (byte)_currentPowerState;
                        packet[0x38] = HID_THEME_CUSTOM;
                        packet[0x39] = (byte)Math.Clamp(speed, 1, 3);
                        
                        zone.CurrentMode = RgbEffectMode.Wave;
                        
                        if (!WriteHidPacket(packet))
                            success = false;
                    }
                    
                    _logging.Info($"OmenDesktopRGB: Applied wave effect");
                    return success;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: Wave effect failed: {ex.Message}");
                    return false;
                }
            });
        }
        
        /// <summary>
        /// Turn off specific zone LEDs.
        /// </summary>
        public async Task<bool> TurnOffZoneAsync(int zoneId)
        {
            if (_hidStream == null)
                return false;
                
            return await Task.Run(() =>
            {
                try
                {
                    var packet = new byte[HID_PACKET_SIZE];
                    var zoneValue = GetZoneValue(zoneId);
                    if (zoneValue == 0) return false;
                    
                    packet[0x02] = HID_HEADER_2;
                    packet[0x03] = HID_MODE_OFF;
                    packet[0x36] = zoneValue;
                    packet[0x37] = (byte)_currentPowerState;
                    
                    var zone = _zones.FirstOrDefault(z => z.Id == zoneId);
                    if (zone != null)
                        zone.CurrentMode = RgbEffectMode.Off;
                    
                    return WriteHidPacket(packet);
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OmenDesktopRGB: Turn off zone failed: {ex.Message}");
                    return false;
                }
            });
        }
        
        /// <summary>
        /// Turn off all zones.
        /// </summary>
        public async Task<bool> TurnOffAllAsync()
        {
            var success = true;
            foreach (var zone in _zones.Where(z => z.IsControllable))
            {
                if (!await TurnOffZoneAsync(zone.Id))
                    success = false;
            }
            return success;
        }
        
        /// <summary>
        /// Get the HID zone value from zone ID.
        /// </summary>
        private byte GetZoneValue(int zoneId)
        {
            // Zone IDs map to 1-based zone values
            return zoneId switch
            {
                0 => ZONE_LOGO,
                1 => ZONE_BAR,
                2 => ZONE_FAN,
                3 => ZONE_CPU,
                4 => ZONE_BOT_FAN,
                5 => ZONE_MID_FAN,
                6 => ZONE_TOP_FAN,
                _ => 0
            };
        }
        
        #endregion

        /// <summary>
        /// Set color for all zones.
        /// </summary>
        public async Task<bool> SetAllZonesColorAsync(byte r, byte g, byte b)
        {
            if (!IsAvailable) return false;

            var success = true;
            foreach (var zone in _zones.Where(z => z.IsControllable))
            {
                if (!await SetZoneColorAsync(zone.Id, r, g, b))
                {
                    success = false;
                }
            }

            return success;
        }

        /// <summary>
        /// Set the effect mode for a zone.
        /// </summary>
        /// <param name="zoneId">Zone ID</param>
        /// <param name="mode">Effect mode</param>
        /// <param name="speed">Animation speed (1-10)</param>
        public async Task<bool> SetZoneModeAsync(int zoneId, RgbEffectMode mode, int speed = 5)
        {
            if (!IsAvailable) return false;

            var zone = _zones.FirstOrDefault(z => z.Id == zoneId);
            if (zone == null) return false;

            try
            {
                _logging.Debug($"OmenDesktopRGB: Setting zone {zoneId} mode to {mode}, speed {speed}");

                // Try WMI method
                if (await SetZoneModeViaWmiAsync(zoneId, mode, speed))
                {
                    zone.CurrentMode = mode;
                    zone.AnimationSpeed = speed;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logging.Error($"OmenDesktopRGB: Error setting zone mode: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Set the effect mode for all zones.
        /// </summary>
        public async Task<bool> SetAllZonesModeAsync(RgbEffectMode mode, int speed = 5)
        {
            if (!IsAvailable) return false;

            var success = true;
            foreach (var zone in _zones.Where(z => z.IsControllable))
            {
                if (!await SetZoneModeAsync(zone.Id, mode, speed))
                {
                    success = false;
                }
            }

            return success;
        }

        /// <summary>
        /// Set zone color via WMI.
        /// </summary>
        private async Task<bool> SetZoneColorViaWmiAsync(int zoneId, byte r, byte g, byte b)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Try HP WMI BIOS method
                    using var classInstance = new ManagementClass(HP_WMI_BIOS_NAMESPACE, "HPBIOS_BIOSSettingInterface", null);
                    
                    // Pack RGB into 32-bit value (0x00RRGGBB)
                    uint rgbValue = (uint)((r << 16) | (g << 8) | b);
                    
                    var inParams = classInstance.GetMethodParameters("SetBIOSSetting");
                    inParams["Name"] = $"RGB Zone {zoneId} Color";
                    inParams["Value"] = rgbValue.ToString();
                    inParams["Password"] = "";

                    var outParams = classInstance.InvokeMethod("SetBIOSSetting", inParams, null);
                    var returnValue = outParams?["Return"]?.ToString() ?? "1";

                    if (returnValue == "0")
                    {
                        _logging.Debug($"OmenDesktopRGB: WMI SetBIOSSetting succeeded for zone {zoneId}");
                        return true;
                    }
                }
                catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidNamespace)
                {
                    _logging.Debug("OmenDesktopRGB: HP WMI namespace not available");
                }
                catch (Exception ex)
                {
                    _logging.Debug($"OmenDesktopRGB: WMI color set failed: {ex.Message}");
                }

                return false;
            });
        }

        /// <summary>
        /// Set zone effect mode via WMI.
        /// </summary>
        private async Task<bool> SetZoneModeViaWmiAsync(int zoneId, RgbEffectMode mode, int speed)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var classInstance = new ManagementClass(HP_WMI_BIOS_NAMESPACE, "HPBIOS_BIOSSettingInterface", null);
                    
                    var inParams = classInstance.GetMethodParameters("SetBIOSSetting");
                    inParams["Name"] = $"RGB Zone {zoneId} Mode";
                    inParams["Value"] = ((int)mode).ToString();
                    inParams["Password"] = "";

                    var outParams = classInstance.InvokeMethod("SetBIOSSetting", inParams, null);
                    var returnValue = outParams?["Return"]?.ToString() ?? "1";

                    if (returnValue == "0")
                    {
                        // Also set speed if supported
                        inParams["Name"] = $"RGB Zone {zoneId} Speed";
                        inParams["Value"] = speed.ToString();
                        classInstance.InvokeMethod("SetBIOSSetting", inParams, null);
                        
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Debug($"OmenDesktopRGB: WMI mode set failed: {ex.Message}");
                }

                return false;
            });
        }

        /// <summary>
        /// Sync all desktop RGB zones with a unified effect.
        /// </summary>
        public async Task SyncAllZonesAsync(byte r, byte g, byte b, RgbEffectMode mode = RgbEffectMode.Static, int speed = 5)
        {
            await SetAllZonesModeAsync(mode, speed);
            await SetAllZonesColorAsync(r, g, b);
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Close HID stream
            try
            {
                _hidStream?.Dispose();
                _hidStream = null;
                _hidDevice = null;
            }
            catch { }
            
            _zones.Clear();
            _initialized = false;
            _disposed = true;

            _logging.Info("OmenDesktopRGB: Disposed");
        }
    }

    /// <summary>
    /// Represents an RGB zone on an OMEN desktop.
    /// </summary>
    public class DesktopRgbZone
    {
        public int Id { get; }
        public string Name { get; }
        public RgbZoneType Type { get; }
        public bool IsControllable { get; }
        public (byte R, byte G, byte B) CurrentColor { get; set; } = (255, 0, 0);
        public RgbEffectMode CurrentMode { get; set; } = RgbEffectMode.Static;
        public int AnimationSpeed { get; set; } = 5;
        public byte Brightness { get; set; } = 100;

        public DesktopRgbZone(int id, string name, RgbZoneType type, bool controllable)
        {
            Id = id;
            Name = name;
            Type = type;
            IsControllable = controllable;
        }
    }

    /// <summary>
    /// Types of RGB zones on OMEN desktops.
    /// </summary>
    public enum RgbZoneType
    {
        Generic,
        Fan,
        LedStrip,
        Logo,
        Accent
    }

    /// <summary>
    /// RGB effect modes.
    /// </summary>
    public enum RgbEffectMode
    {
        Off = 0,
        Static = 1,
        Breathing = 2,
        ColorCycle = 3,
        Rainbow = 4,
        Wave = 5,
        Reactive = 6,
        Blinking = 7,
        Radial = 8
    }
    
    /// <summary>
    /// Power state for RGB lighting (On vs Suspended).
    /// Allows different lighting when PC is on vs when suspended/sleeping.
    /// </summary>
    public enum RgbPowerState : byte
    {
        On = 0x01,
        Suspend = 0x02
    }
    
    /// <summary>
    /// Predefined RGB themes from HP OMEN Command Center.
    /// </summary>
    public enum DesktopRgbTheme
    {
        Custom = 0,
        Galaxy = 1,
        Volcano = 2,
        Jungle = 3,
        Ocean = 4,
        Unicorn = 5
    }
}
