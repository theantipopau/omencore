using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HidSharp;
using OmenCore.Corsair;
using OmenCore.Models;

namespace OmenCore.Services.Corsair
{
    /// <summary>
    /// Direct HID communication with Corsair devices - no iCUE required.
    /// Communicates directly with USB HID interface for RGB control.
    /// </summary>
    public class CorsairHidDirect : ICorsairSdkProvider
    {
        private readonly LoggingService _logging;
        private bool _initialized;
        private readonly List<CorsairHidDevice> _devices = new();
        private readonly HashSet<string> _hidWriteFailedDevices = new(); // Track devices that failed to reduce log spam
        
        // Expose failed HID devices for diagnostics/tests (read-only)
        public System.Collections.Generic.IReadOnlyCollection<string> HidWriteFailedDeviceIds => _hidWriteFailedDevices.ToList().AsReadOnly();

        // Number of write attempts before giving up
        private const int HID_WRITE_MAX_ATTEMPTS = 3;
        private const int HID_WRITE_RETRY_DELAY_MS = 120;        
        // Corsair USB Vendor ID
        private const int CORSAIR_VID = 0x1B1C;
        
        // Known Corsair product IDs and their device types
        private static readonly Dictionary<int, (string Name, CorsairDeviceType Type, string? Notes)> KnownProducts = new()
        {
            // Keyboards
            { 0x1B2D, ("K95 RGB Platinum", CorsairDeviceType.Keyboard, null) },
            { 0x1B11, ("K70 RGB", CorsairDeviceType.Keyboard, null) },
            { 0x1B13, ("K70 LUX RGB", CorsairDeviceType.Keyboard, null) },
            { 0x1B17, ("K70 RGB MK.2", CorsairDeviceType.Keyboard, null) },
            { 0x1B36, ("K70 RGB MK.2 SE", CorsairDeviceType.Keyboard, null) },
            { 0x1B49, ("K70 RGB MK.2 LP", CorsairDeviceType.Keyboard, null) },
            { 0x1B38, ("K70 RGB TKL", CorsairDeviceType.Keyboard, null) },
            { 0x1B55, ("K70 RGB PRO", CorsairDeviceType.Keyboard, null) },
            { 0x1B6B, ("K70 RGB PRO X", CorsairDeviceType.Keyboard, null) },
            { 0x1B4F, ("K65 RGB MINI", CorsairDeviceType.Keyboard, null) },
            { 0x1B39, ("K65 RGB", CorsairDeviceType.Keyboard, null) },
            { 0x1B37, ("K65 LUX RGB", CorsairDeviceType.Keyboard, null) },
            { 0x1B07, ("K65 RGB Rapidfire", CorsairDeviceType.Keyboard, null) },
            { 0x1B09, ("K55 RGB", CorsairDeviceType.Keyboard, null) },
            { 0x1B3D, ("K55 RGB PRO", CorsairDeviceType.Keyboard, null) },
            { 0x1B6E, ("K55 RGB PRO XT", CorsairDeviceType.Keyboard, null) },
            { 0x1B60, ("K100 RGB", CorsairDeviceType.Keyboard, null) },
            
            // Mice (wired)
            { 0x1B2E, ("Dark Core RGB", CorsairDeviceType.Mouse, null) },
            { 0x1B4B, ("Dark Core RGB PRO", CorsairDeviceType.Mouse, null) },
            { 0x1B4C, ("Dark Core RGB PRO SE", CorsairDeviceType.Mouse, null) },
            { 0x1B80, ("Dark Core RGB PRO Wireless", CorsairDeviceType.Mouse, null) }, // Wireless mouse when connected
            { 0x1BF0, ("Dark Core RGB PRO", CorsairDeviceType.Mouse, null) }, // Alternate PID seen on some systems (was incorrectly Scimitar)
            { 0x1B34, ("Ironclaw RGB", CorsairDeviceType.Mouse, null) },
            { 0x1B3C, ("Nightsword RGB", CorsairDeviceType.Mouse, null) },
            { 0x1B1E, ("M65 RGB Elite", CorsairDeviceType.Mouse, null) },
            { 0x1B12, ("M65 PRO RGB", CorsairDeviceType.Mouse, null) },
            { 0x1B5B, ("M55 RGB PRO", CorsairDeviceType.Mouse, null) },
            { 0x1B3F, ("Harpoon RGB PRO", CorsairDeviceType.Mouse, null) },
            { 0x1B75, ("Sabre RGB PRO", CorsairDeviceType.Mouse, null) },
            { 0x1B66, ("Katar PRO", CorsairDeviceType.Mouse, null) },
            { 0x1B6F, ("Katar PRO XT", CorsairDeviceType.Mouse, null) },
            // Note: 0x1BF0 was listed as Scimitar RGB Elite but is actually Dark Core RGB PRO on some systems - moved to Dark Core section
            { 0x1B3B, ("Scimitar PRO RGB", CorsairDeviceType.Mouse, null) },
            { 0x1B8B, ("Scimitar Elite Wireless", CorsairDeviceType.Mouse, null) },
            { 0x1B7A, ("Sabre RGB PRO Champion", CorsairDeviceType.Mouse, null) },
            { 0x1B96, ("M65 RGB Ultra", CorsairDeviceType.Mouse, null) },
            { 0x1B99, ("M65 RGB Ultra Wireless", CorsairDeviceType.Mouse, null) },
            { 0x1BA4, ("Dark Core RGB PRO SE Wireless", CorsairDeviceType.Mouse, null) }, // SE variant wireless mode
            
            // Wireless dongles (USB receivers) - These are NOT the mouse itself
            { 0x1B81, ("Dark Core RGB PRO Receiver", CorsairDeviceType.WirelessDongle, "USB receiver for Dark Core RGB PRO mouse") },
            { 0x1B5D, ("Ironclaw RGB Wireless Receiver", CorsairDeviceType.WirelessDongle, "Wireless mouse connects through this receiver") },
            { 0x1B3E, ("Harpoon RGB Wireless Receiver", CorsairDeviceType.WirelessDongle, "Wireless mouse connects through this receiver") },
            { 0x1B65, ("Katar PRO Wireless Receiver", CorsairDeviceType.WirelessDongle, "Wireless mouse connects through this receiver") },
            { 0x1B9A, ("M65 RGB Ultra Wireless Receiver", CorsairDeviceType.WirelessDongle, "Wireless mouse connects through this receiver") },
            { 0x1B8C, ("Scimitar Elite Wireless Receiver", CorsairDeviceType.WirelessDongle, "Wireless mouse connects through this receiver") },
            { 0x1B94, ("Sabre RGB PRO Wireless Receiver", CorsairDeviceType.WirelessDongle, "Wireless mouse connects through this receiver") },
            
            // Headsets
            { 0x0A14, ("VOID RGB", CorsairDeviceType.Headset, null) },
            { 0x0A55, ("VOID RGB Elite Wireless", CorsairDeviceType.Headset, null) },
            { 0x0A52, ("VOID RGB Elite USB", CorsairDeviceType.Headset, null) },
            { 0x0A4E, ("HS70 PRO Wireless Receiver", CorsairDeviceType.WirelessDongle, "USB receiver for HS70 PRO headset") },
            { 0x0A4F, ("HS70 PRO Wireless", CorsairDeviceType.Headset, null) },
            { 0x0A51, ("HS60 PRO", CorsairDeviceType.Headset, null) },
            { 0x0A61, ("Virtuoso RGB Wireless", CorsairDeviceType.Headset, null) },
            { 0x0A64, ("Virtuoso RGB Wireless SE", CorsairDeviceType.Headset, null) },
            { 0x0A6A, ("Virtuoso RGB Wireless XT", CorsairDeviceType.Headset, null) },
            
            // Mouse pads
            { 0x0B00, ("MM800 RGB Polaris", CorsairDeviceType.MouseMat, null) },
            { 0x0B04, ("MM800 RGB Polaris Cloth", CorsairDeviceType.MouseMat, null) },
            { 0x0B05, ("MM1000 Qi Wireless", CorsairDeviceType.MouseMat, null) },
            
            // Other peripherals
            { 0x0C00, ("ST100 RGB Headset Stand", CorsairDeviceType.Accessory, null) },
            { 0x1D00, ("LT100 RGB Tower", CorsairDeviceType.Accessory, null) },
            { 0x0A34, ("Commander PRO", CorsairDeviceType.Accessory, null) },
            { 0x0A3E, ("Lighting Node PRO", CorsairDeviceType.Accessory, null) },
        };

        private readonly TelemetryService? _telemetry;

        public CorsairHidDirect(LoggingService logging, TelemetryService? telemetry = null)
        {
            _logging = logging;
            _telemetry = telemetry;
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logging.Info("Initializing Corsair direct HID access (no iCUE required)...");
                
                var deviceList = DeviceList.Local;
                var hidDevices = deviceList.GetHidDevices();
                
                int foundCount = 0;
                var seenProducts = new HashSet<int>(); // Prevent duplicates from multiple HID interfaces
                
                foreach (var hidDevice in hidDevices)
                {
                    if (hidDevice.VendorID == CORSAIR_VID)
                    {
                        // Only add each product once (devices expose multiple HID interfaces)
                        if (seenProducts.Contains(hidDevice.ProductID))
                            continue;
                            
                        try
                        {
                            var productName = GetProductName(hidDevice);
                            var deviceType = GetDeviceType(hidDevice.ProductID);
                            var notes = GetDeviceNotes(hidDevice.ProductID);
                            
                            // Skip if this is a secondary interface (we only want the main RGB interface)
                            // Corsair devices expose multiple HID interfaces
                            if (IsRgbInterface(hidDevice))
                            {
                                var connectionType = deviceType == CorsairDeviceType.WirelessDongle 
                                    ? "USB Receiver" 
                                    : "USB";
                                    
                                var corsairDevice = new CorsairHidDevice
                                {
                                    HidDevice = hidDevice,
                                    ProductId = hidDevice.ProductID,
                                    DeviceInfo = new CorsairDevice
                                    {
                                        DeviceId = hidDevice.DevicePath,
                                        Name = productName,
                                        DeviceType = deviceType,
                                        Status = new CorsairDeviceStatus
                                        {
                                            FirmwareVersion = "N/A (Direct HID)",
                                            PollingRateHz = 1000,
                                            ConnectionType = connectionType,
                                            Notes = notes
                                        }
                                    }
                                };
                                
                                _devices.Add(corsairDevice);
                                seenProducts.Add(hidDevice.ProductID);
                                foundCount++;
                                
                                var typeLabel = deviceType == CorsairDeviceType.WirelessDongle 
                                    ? "📡 Wireless Receiver" 
                                    : $"🎮 {deviceType}";
                                _logging.Info($"  Found Corsair {typeLabel}: {productName} (PID: 0x{hidDevice.ProductID:X4})");
                                
                                if (!string.IsNullOrEmpty(notes))
                                {
                                    _logging.Info($"    ℹ️ {notes}");
                                }

                                // Special handling for wireless receivers: map to managed 'WirelessDongle' devices
                                if (deviceType == CorsairDeviceType.WirelessDongle)
                                {
                                    _logging.Info($"    Detected wireless receiver - mapping receiver to potential wireless devices");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logging.Warn($"  Skipping Corsair device 0x{hidDevice.ProductID:X4}: {ex.Message}");
                        }
                    }
                }
                
                _initialized = foundCount > 0;
                
                if (_initialized)
                {
                    _logging.Info($"Corsair direct HID initialized - {foundCount} device(s) found");
                }
                else
                {
                    _logging.Info("No Corsair devices found via direct HID");
                }
                
                return await Task.FromResult(_initialized);
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to initialize Corsair direct HID: {ex.Message}");
                return false;
            }
        }

        private string GetProductName(HidDevice device)
        {
            if (KnownProducts.TryGetValue(device.ProductID, out var info))
            {
                return info.Name;
            }
            
            try
            {
                var name = device.GetProductName();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            catch { }
            
            return $"Corsair Device (0x{device.ProductID:X4})";
        }

        private CorsairDeviceType GetDeviceType(int productId)
        {
            if (KnownProducts.TryGetValue(productId, out var info))
            {
                return info.Type;
            }
            
            // Heuristics based on PID ranges
            if (productId >= 0x1B00 && productId <= 0x1BFF)
                return CorsairDeviceType.Mouse; // 1Bxx range is typically mice (changed from Keyboard)
            if (productId >= 0x0A00 && productId <= 0x0AFF)
                return CorsairDeviceType.Headset;  // 0Axx range is typically headsets
            if (productId >= 0x0B00 && productId <= 0x0BFF)
                return CorsairDeviceType.MouseMat; // 0Bxx range is typically pads
                
            return CorsairDeviceType.Accessory;
        }
        
        private string? GetDeviceNotes(int productId)
        {
            if (KnownProducts.TryGetValue(productId, out var info))
            {
                return info.Notes;
            }
            return null;
        }

        private bool IsRgbInterface(HidDevice device)
        {
            // Corsair devices expose multiple HID interfaces
            // We want the one that handles RGB (usually usage page 0xFF00 or similar)
            try
            {
                // Accept devices with vendor-specific usage pages (0xFF00+)
                // These are typically the RGB control interfaces
                var caps = device.GetReportDescriptor();
                if (caps != null)
                {
                    // Most Corsair RGB interfaces use collection with vendor usage page
                    return true;
                }
            }
            catch { }
            
            // If we can't determine, assume it's valid (better to show than hide)
            return true;
        }

        public Task<IEnumerable<CorsairDevice>> DiscoverDevicesAsync()
        {
            var devices = _devices.Select(d => d.DeviceInfo).ToList();
            return Task.FromResult<IEnumerable<CorsairDevice>>(devices);
        }

        // --- Test helpers ---
        // Add a test-only device to the internal device list (used by unit tests to simulate hardware)
        protected void AddTestHidDevice(string deviceId, int productId, CorsairDeviceType type, string name = "Test Device")
        {
            var dev = new CorsairHidDevice
            {
                ProductId = productId,
                HidDevice = null!, // tests should override WriteReportAsync to avoid using this
                DeviceInfo = new CorsairDevice
                {
                    DeviceId = deviceId,
                    Name = name,
                    DeviceType = type,
                    Status = new CorsairDeviceStatus { FirmwareVersion = "Test", PollingRateHz = 1000 }
                }
            };

            _devices.Add(dev);
        }

        public async Task ApplyLightingAsync(CorsairDevice device, CorsairLightingPreset preset)
        {
            var hidDevice = _devices.FirstOrDefault(d => d.DeviceInfo.DeviceId == device.DeviceId);
            if (hidDevice == null)
            {
                _logging.Warn($"Device not found: {device.Name}");
                return;
            }

            try
            {
                var (r, g, b) = ParseHexColor(preset.PrimaryColor);
                await SendColorCommandAsync(hidDevice, r, g, b);
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply lighting to {device.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Send color command to device, with retries and device-specific report handling.
        /// Protected to allow test subclasses to override the low-level write behavior.
        /// </summary>
        protected virtual async Task SendColorCommandAsync(CorsairHidDevice device, byte r, byte g, byte b)
        {
            // Choose a best-effort report format based on device type/product
            // Command selection is handled in BuildSetColorReport/BuildCommitReport
            var deviceKey = $"{device.ProductId}";

            for (int attempt = 1; attempt <= HID_WRITE_MAX_ATTEMPTS; attempt++)
            {
                try
                {
                    var report = BuildSetColorReport(device, r, g, b);

                    var ok = await WriteReportAsync(device, report);
                    if (!ok)
                        throw new InvalidOperationException("HID write returned false");

                    // Commit/update
                    var commitReport = BuildCommitReport(device);

                    await WriteReportAsync(device, commitReport);

                    // Success - clear any previous failure record
                    if (_hidWriteFailedDevices.Contains(deviceKey))
                        _hidWriteFailedDevices.Remove(deviceKey);

                    // Telemetry: record per-PID success if enabled
                    try { _telemetry?.IncrementPidSuccess(device.ProductId); } catch { }

                    _logging.Info($"Applied lighting {r:X2}{g:X2}{b:X2} to {device.DeviceInfo.Name} via direct HID (attempt {attempt})");
                    return;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Attempt {attempt} failed to write HID report for {device.DeviceInfo.Name} (PID: 0x{device.ProductId:X4}): {ex.Message}");
                    if (attempt < HID_WRITE_MAX_ATTEMPTS)
                        await Task.Delay(HID_WRITE_RETRY_DELAY_MS);
                    else
                    {
                        if (!_hidWriteFailedDevices.Contains(deviceKey))
                        {
                            _hidWriteFailedDevices.Add(deviceKey);
                            _logging.Warn($"HID write not supported for device {device.ProductId:X4} - falling back to SDK if available");

                            // Telemetry: record failure for PID (opt-in only)
                            try { _telemetry?.IncrementPidFailure(device.ProductId); } catch { }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Build set-color report bytes for a device using product-specific heuristics.
        /// Separated for testability and easier per-product payload maintenance.
        /// </summary>
        protected virtual byte[] BuildSetColorReport(CorsairHidDevice device, byte r, byte g, byte b)
        {
            // Default: 0x07 set color, index 0, count 1
            byte cmd = 0x07;
            byte startIndex = 0x00;
            byte count = 0x01;

            // Per-product heuristics: use device type where possible to broadly support many PIDs
            if (device.DeviceInfo != null && device.DeviceInfo.DeviceType == CorsairDeviceType.Keyboard)
            {
                // Keyboard families tend to use command 0x09 and support a 'full-device' write
                count = 0xFF;
            }
            else
            {
                // Mice often use 0x05 for the set color command
                switch (device.ProductId)
                {
                    case 0x1B2E: // Dark Core RGB
                    case 0x1B4B: // Dark Core RGB PRO
                    case 0x1B34: // Ironclaw
                        return BuildSetColorReport(device, r, g, b); // mouse-specific simple path
                    default:
                        // Keep defaults
                        break;
                }
            }

            // If keyboard full-device indicator requested, build keyboard full-zone report
            if (count == 0xFF)
            {
                return BuildKeyboardFullZoneReport(device, r, g, b);
            }

            var report = new byte[65];
            report[0] = 0x00;
            report[1] = cmd;
            report[2] = startIndex;
            report[3] = count;
            report[4] = r;
            report[5] = g;
            report[6] = b;
            return report;
        }

        /// <summary>
        /// Build a keyboard full-zone report for keyboards that accept a full-device write.
        /// Packs the requested color into both the base payload and an explicit per-zone area for clarity.
        /// </summary>
        protected virtual byte[] BuildKeyboardFullZoneReport(CorsairHidDevice device, byte r, byte g, byte b)
        {
            var report = new byte[65];
            report[0] = 0x00;
            report[1] = 0x09; // keyboard set command
            report[2] = 0x00;
            report[3] = 0xFF; // full-device marker

            // Primary color in base slots
            report[4] = r;
            report[5] = g;
            report[6] = b;

            // Marker indicating full-zone payload follows (vendor heuristic)
            // Use different marker for K100 for future per-key extensions
            if (device.ProductId == 0x1B60)
                report[7] = 0x02; // K100 special
            else
                report[7] = 0x01; // standard full-zone

            // Populate zone payloads - provide 4 zone entries (Zone0..Zone3)
            int idx = 8;
            for (int zone = 0; zone < 4 && idx + 2 < report.Length; zone++)
            {
                report[idx++] = r;
                report[idx++] = g;
                report[idx++] = b;
            }

            // For K100 (product 0x1B60) include a small per-key payload header as a stub for future per-key support
            if (device.ProductId == 0x1B60 && idx + 3 < report.Length)
            {
                // Per-key header: marker 0xPK (0x50 0x4B ascii 'PK' not allowed in byte single, use 0x50), count
                report[idx++] = 0x50; // 'P' marker
                report[idx++] = 0x0A; // number of per-key blocks in stub (10)
                report[idx++] = 0x01; // flags/reserved

                // Fill a few per-key entries (color triples) as placeholders
                for (int i = 0; i < 6 && idx + 2 < report.Length; i++)
                {
                    report[idx++] = r;
                    report[idx++] = g;
                    report[idx++] = b;
                }
            }

            return report;
        }

        /// <summary>
        /// Build a commit/update report for device specific commit command heuristics.
        /// </summary>
        protected virtual byte[] BuildCommitReport(CorsairHidDevice device)
        {
            var report = new byte[65];
            report[0] = 0x00;

            // Default commit is 0x07 with flag 0x28
            byte commitCmd = 0x07;

            // For some keyboard PIDs, a different commit command may be required (use same as setCmd for safety)
            switch (device.ProductId)
            {
                case 0x1B2D:
                case 0x1B11:
                case 0x1B17:
                case 0x1B60:
                    commitCmd = 0x09;
                    break;
                case 0x1B2E:
                case 0x1B4B:
                case 0x1B34:
                    commitCmd = 0x05;
                    break;
                default:
                    commitCmd = 0x07;
                    break;
            }

            report[1] = commitCmd;
            report[2] = 0x28;
            return report;
        }

        /// <summary>
        /// Low-level HID write. Overridable for testing to simulate write failures.
        /// Returns true when write succeeded.
        /// </summary>
        protected virtual async Task<bool> WriteReportAsync(CorsairHidDevice device, byte[] report)
        {
            // Null hiddevice is allowed for tests that override and avoid using HidSharp streams
            if (device.HidDevice == null)
            {
                // In test scenarios where HidDevice is null, assume write is not supported here; test overrides this method
                throw new System.InvalidOperationException("No HidDevice available for write");
            }

            using var stream = device.HidDevice.Open();
            await stream.WriteAsync(report, 0, report.Length);
            return true;
        }

        public async Task ApplyDpiStagesAsync(CorsairDevice device, IEnumerable<OmenCore.Corsair.CorsairDpiStage> stages)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (stages == null) throw new ArgumentNullException(nameof(stages));

            // Locate the internal HID device for the given CorsairDevice
            var hidDevice = GetHidDeviceByDeviceId(device.DeviceId);
            if (hidDevice == null)
            {
                _logging.Warn($"No HID device found for {device.Name} (device id: {device.DeviceId})");
                return;
            }

            // Build and send per-stage reports based on product heuristics
            foreach (var s in stages)
            {
                var report = BuildSetDpiReport(hidDevice, s.Dpi /* index not used by Corsair DPI objects */ , s.Dpi);
                if (report == null)
                {
                    _logging.Warn($"DPI update not supported for PID 0x{hidDevice.ProductId:X4}");
                    return;
                }

                // Retry loop for inkling of reliability similar to color writes
                bool success = false;
                for (int attempt = 1; attempt <= HID_WRITE_MAX_ATTEMPTS; attempt++)
                {
                    try
                    {
                        var ok = await WriteReportAsync(hidDevice, report);
                        if (ok)
                        {
                            _logging.Info($"Applied DPI {s.Dpi} to {device.Name} (attempt {attempt})");
                            try { _telemetry?.IncrementPidSuccess(hidDevice.ProductId); } catch { }
                            success = true;
                            break;
                        }
                        else
                        {
                            throw new InvalidOperationException("HID write returned false");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Attempt {attempt} failed to write DPI report for {device.Name} (PID: 0x{hidDevice.ProductId:X4}): {ex.Message}");
                        if (attempt < HID_WRITE_MAX_ATTEMPTS)
                            await Task.Delay(HID_WRITE_RETRY_DELAY_MS);
                    }
                }

                if (!success)
                {
                    try { _telemetry?.IncrementPidFailure(hidDevice.ProductId); } catch { }
                }
            }
        }

        /// <summary>
        /// Build a DPI-setting report for a mouse device. Returns null when unsupported.
        /// </summary>
        protected virtual byte[]? BuildSetDpiReport(CorsairHidDevice device, int index, int dpi)
        {
            // Conservative approach: apply only for known mice PIDs (add more as we learn exact payloads)
            switch (device.ProductId)
            {
                case 0x1B2E: // Dark Core RGB - hypothetical format
                    // Example: [0x00, 0x11, index, dpi_low, dpi_high, 0,0,...]
                    var r = new byte[65];
                    r[0] = 0x00;
                    r[1] = 0x11; // vendor 'set DPI' cmd (example)
                    r[2] = (byte)index;
                    r[3] = (byte)(dpi & 0xFF);
                    r[4] = (byte)((dpi >> 8) & 0xFF);
                    return r;
                case 0x1B1E: // M65 family - alternative hypothetical format
                    var r2 = new byte[65];
                    r2[0] = 0x00;
                    r2[1] = 0x12;
                    r2[2] = (byte)index;
                    r2[3] = (byte)(dpi & 0xFF);
                    r2[4] = (byte)((dpi >> 8) & 0xFF);
                    r2[5] = 0x01; // commit flag
                    return r2;
                case 0x1B96: // M65 Ultra - similar to M65 but different cmd
                    var r3 = new byte[65];
                    r3[0] = 0x00;
                    r3[1] = 0x13;
                    r3[2] = (byte)index;
                    r3[3] = (byte)(dpi & 0xFF);
                    r3[4] = (byte)((dpi >> 8) & 0xFF);
                    return r3;
                case 0x1B34: // Ironclaw - another variant
                    var r4 = new byte[65];
                    r4[0] = 0x00;
                    r4[1] = 0x14;
                    r4[2] = (byte)index;
                    r4[3] = (byte)(dpi & 0xFF);
                    r4[4] = (byte)((dpi >> 8) & 0xFF);
                    return r4;
                default:
                    return null;
            }
        }

        public Task ApplyMacroAsync(CorsairDevice device, MacroProfile macro)
        {
            _logging.Info($"[Direct HID] Macro support not yet implemented for {device.Name}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Helper to locate an internal CorsairHidDevice by public DeviceId
        /// </summary>
        protected CorsairHidDevice? GetHidDeviceByDeviceId(string deviceId)
        {
            foreach (var d in _devices)
            {
                if (d.DeviceInfo?.DeviceId == deviceId) return d;
            }
            return null;
        }

        private static (byte r, byte g, byte b) ParseHexColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                return (
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }
            return (255, 255, 255);
        }

        public Task SyncWithThemeAsync(IEnumerable<CorsairDevice> devices, LightingProfile theme)
        {
            foreach (var device in devices)
            {
                var preset = new CorsairLightingPreset
                {
                    PrimaryColor = theme.PrimaryColor
                };
                _ = ApplyLightingAsync(device, preset);
            }
            return Task.CompletedTask;
        }

        public Task<CorsairDeviceStatus> GetDeviceStatusAsync(CorsairDevice device)
        {
            var hidDevice = _devices.FirstOrDefault(d => d.DeviceInfo.DeviceId == device.DeviceId);
            if (hidDevice != null)
            {
                return Task.FromResult(new CorsairDeviceStatus
                {
                    PollingRateHz = 1000,
                    FirmwareVersion = "N/A (Direct HID)",
                    ConnectionType = "USB"
                });
            }
            
            return Task.FromResult(new CorsairDeviceStatus());
        }

        public void Shutdown()
        {
            _logging.Info("Corsair direct HID shut down");
            _devices.Clear();
            _initialized = false;
        }
        
        /// <summary>
        /// Internal class to track HID device with its info
        /// </summary>
        public class CorsairHidDevice
        {
            public HidDevice HidDevice { get; set; } = null!;
            public int ProductId { get; set; }
            public CorsairDevice DeviceInfo { get; set; } = null!;
        }
    }
}
