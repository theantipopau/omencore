using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using HidSharp;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Per-key RGB backend for HP OMEN MAX laptop keyboards using USB HID protocol.
    ///
    /// Protocol based on:
    ///   - OpenRGB HPOmenKeyboard controller implementation (gitlab.com/CalcProgrammer1/OpenRGB)
    ///   - OmenHubLighter decompilation of HP.Omen.Core.Common.dll
    ///   - SignalRGB HP OMEN plugin protocol analysis
    ///
    /// Supported keyboards (VID 0x03F0):
    ///   0x0538 – OMEN 16 (2023) Intel/AMD internal keyboard
    ///   0x0547 – OMEN 16 (2024) internal keyboard
    ///   0x0549 – OMEN 17 (2024) internal keyboard
    ///
    /// The internal keyboard is not always under HP's own vendor ID - on OMEN MAX 16 ak0xxx
    /// it is Darfon 0x0D62:0x54BF - so the scan covers the ODM vendor IDs too and logs every
    /// candidate with its output report length. A device is only opened if it is listed in
    /// KnownPerKeyPids AND has a 65-byte output report; scanning is not using.
    ///
    /// Current capability: zone-mapped static colors (maps 4 WMI zone colors across
    /// the full key matrix). Full per-key editor addressing is a follow-up once PIDs
    /// are field-confirmed for each OMEN MAX generation.
    /// </summary>
    public class HidPerKeyBackend : IKeyboardBackend, IDisposable
    {
        private readonly LoggingService _logging;
        private HidDevice? _device;
        private HidStream? _stream;
        private Color[] _lastRequestedZoneColors = Enumerable.Repeat(Color.Black, 4).ToArray();
        private bool _initialized;
        private bool _disposed;

        // HP Inc. USB vendor ID
        private const int HP_VID = 0x03F0;

        /// <summary>
        /// Vendor IDs worth scanning. Restricting this to HP_VID made whole models invisible:
        /// on OMEN MAX 16 ak0xxx (board 8D87) the internal keyboard enumerates under Darfon
        /// (0x0D62), HP's keyboard ODM, and nothing at all under 0x03F0 except a HyperX
        /// wireless receiver and, if docked, a Thunderbolt dock. Scanning is not using: a
        /// device still has to appear in <see cref="KnownPerKeyPids"/> before anything is
        /// written to it. This list only decides what gets *looked at and logged*.
        /// </summary>
        private static readonly Dictionary<int, string> ScannedVendorIds = new()
        {
            { HP_VID, "HP" },
            { 0x0D62, "Darfon (HP keyboard ODM)" },
        };

        // Known OMEN per-key laptop keyboard controller PIDs, keyed by (VID, PID).
        // Sources: OpenRGB device database, OmenHubLighter decompilation, community reports.
        // A device is only opened and written to if it appears here AND its output report
        // length matches PACKET_SIZE - see the report-length check in InitializeAsync.
        private static readonly Dictionary<(int Vid, int Pid), string> KnownPerKeyPids = new()
        {
            { (HP_VID, 0x0538), "OMEN 16 (2023) Intel/AMD keyboard" },
            { (HP_VID, 0x053A), "OMEN Sequoia / external gaming keyboard" },
            { (HP_VID, 0x0547), "OMEN 16 (2024) keyboard" },
            { (HP_VID, 0x0549), "OMEN 17 (2024) keyboard" },

            // NOT listed, deliberately, and the reason is worth keeping:
            //
            //   0x03F0:0x054E / 0x054F were guesses at the OMEN MAX 16 keyboards, inferred from
            //   adjacent generations. Measured on an ak0xxx (8D87): neither exists. The internal
            //   keyboard is 0x0D62:0x54BF "HP Gaming Keyboard II", on the board's own USB
            //   controller with a null serial and RemovalPolicy = ExpectNoRemoval, exposing two
            //   vendor collections at exactly 65-byte output reports (MI_00 and MI_03) - the
            //   right shape for PACKET_SIZE. What is NOT established is whether it speaks the
            //   command set below (CMD_BYTE 0x0F / SUB_* from the 0x03F0 family), and adding it
            //   here would send those bytes to it on every start to find out.
            //
            // Add it once someone has confirmed the protocol on hardware they are willing to
            // power-cycle - not before. A wrong PID that does nothing is a much cheaper mistake
            // than a right PID with a wrong protocol.
        };

        // HID packet layout.
        // Every packet is 65 bytes: 1 report-ID byte (0x00) + 64 data bytes.
        // All HP OMEN per-key keyboard commands share this fixed report size.
        private const int PACKET_SIZE = 65;
        private const byte REPORT_ID = 0x00;

        // Per-key command bytes (OpenRGB HPOmenKeyboard, OmenHubLighter decompilation).
        private const byte CMD_BYTE         = 0x0F;  // All per-key commands use this as data[0]
        private const byte SUB_ENTER_EFFECT = 0x42;  // Enter static/direct per-key mode
        private const byte SUB_SET_COLORS   = 0x52;  // Write per-key color segment
        private const byte SUB_COMMIT       = 0x50;  // Commit pending writes to hardware

        // Static mode identifier placed in data[2] alongside SUB_ENTER_EFFECT.
        private const byte STATIC_MODE_ID = 0x03;

        // Key layout constants.
        // Color data is sent in segments; each segment carries up to KEYS_PER_SEGMENT
        // key colors (3 bytes each: R, G, B).
        private const int KEYS_PER_SEGMENT = 20;
        private const int TOTAL_KEY_COUNT  = 104; // Standard OMEN layout

        // ── IKeyboardBackend interface ─────────────────────────────────────────────

        public string Name        => "HID Per-Key (OMEN USB)";
        public KeyboardMethod Method => KeyboardMethod.HidPerKey;
        public bool IsAvailable  => _initialized && _stream != null;
        public bool SupportsReadback => false; // HP per-key protocol is write-only
        public int  ZoneCount    => 0;         // Per-key: zone count not applicable
        public bool IsPerKey     => true;

        public HidPerKeyBackend(LoggingService logging)
        {
            _logging = logging;
        }

        // ── Initialization ─────────────────────────────────────────────────────────

        public Task<bool> InitializeAsync()
        {
            try
            {
                var vendorList = string.Join(", ", ScannedVendorIds.Select(v => $"0x{v.Key:X4} {v.Value}"));
                _logging.Info($"[HidPerKey] Scanning for OMEN per-key keyboard ({vendorList})...");

                var scannedDevices = DeviceList.Local
                    .GetHidDevices()
                    .Where(d => ScannedVendorIds.ContainsKey(d.VendorID))
                    .ToList();

                if (scannedDevices.Count == 0)
                {
                    _logging.Info($"[HidPerKey] No candidate HID devices found ({vendorList})");
                    _initialized = false;
                    return Task.FromResult(false);
                }

                _logging.Info($"[HidPerKey] Found {scannedDevices.Count} candidate HID device(s):");

                HidDevice? candidate = null;
                foreach (var d in scannedDevices)
                {
                    var vid  = d.VendorID;
                    var pid  = d.ProductID;
                    var name = SafeGetProductName(d);

                    // A composite keyboard exposes several collections under one PID - a keyboard
                    // collection, a consumer-control one, a mouse one, and the vendor collection
                    // that carries lighting. Only the last has an output report the size of a
                    // per-key packet, so this is what distinguishes it. Selecting on PID alone
                    // took whichever collection Windows happened to enumerate first.
                    var outputLength = SafeGetMaxOutputReportLength(d);
                    var usable = outputLength == PACKET_SIZE;

                    if (KnownPerKeyPids.TryGetValue((vid, pid), out var knownName) && usable)
                    {
                        _logging.Info($"[HidPerKey]   OK {vid:X4}:{pid:X4} out={outputLength} - {knownName} ('{name}')");
                        candidate ??= d;
                    }
                    else
                    {
                        // Log every candidate with its report length. The lengths are the part
                        // that makes a field report actionable: a device with out=65 is a
                        // plausible lighting endpoint, one with out=0 never is.
                        var why = usable
                            ? "not in the per-key device list"
                            : $"output report {outputLength} != {PACKET_SIZE}, not a lighting endpoint";
                        _logging.Info($"[HidPerKey]   ? {vid:X4}:{pid:X4} out={outputLength} - '{name}' ({why})");
                    }
                }

                if (candidate == null)
                {
                    _logging.Info("[HidPerKey] No confirmed OMEN per-key keyboard found; zone lighting " +
                        "will fall back to WMI, which on some models reaches only the light bar. " +
                        "Please report the VID:PID and out= values logged above.");
                    _initialized = false;
                    return Task.FromResult(false);
                }

                // Try to open the device for writing.
                try
                {
                    _stream              = candidate.Open();
                    _stream.WriteTimeout = 1000;
                    _device              = candidate;
                    _logging.Info($"[HidPerKey] Opened keyboard device PID 0x{candidate.ProductID:X4}");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"[HidPerKey] Could not open keyboard device PID 0x{candidate.ProductID:X4}: {ex.Message}");
                    _logging.Info("[HidPerKey] The keyboard may be held open by OMEN Light Studio or OGH. " +
                        "Close conflicting apps and retry.");
                    _initialized = false;
                    return Task.FromResult(false);
                }

                // Enter per-key static mode. If the device rejects it the protocol bytes
                // are wrong for this PID and we fall back rather than corrupt the keyboard state.
                if (!SendEnterPerKeyMode())
                {
                    _logging.Warn("[HidPerKey] Device opened but SUB_ENTER_EFFECT packet was not acknowledged. " +
                        "The per-key protocol may differ for this model. Falling back.");
                    _stream?.Dispose();
                    _stream      = null;
                    _initialized = false;
                    return Task.FromResult(false);
                }

                _initialized = true;
                _logging.Info("[HidPerKey] OK Per-key keyboard backend initialized successfully");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"[HidPerKey] Initialization failed: {ex.Message}", ex);
                _initialized = false;
                return Task.FromResult(false);
            }
        }

        // ── IKeyboardBackend apply methods ─────────────────────────────────────────

        public async Task<RgbApplyResult> SetZoneColorsAsync(Color[] zoneColors)
        {
            var sw     = Stopwatch.StartNew();
            var result = new RgbApplyResult { Method = Method, SupportsVerification = false };

            if (!IsAvailable)
            {
                result.FailureReason = "HID per-key backend not available";
                return result;
            }

            try
            {
                // Map 4 zone colors to the full key matrix and send.
                var normalizedZoneColors = NormalizeZoneColors(zoneColors);
                var keyColors            = MapZoneColorsToKeys(normalizedZoneColors);
                result.BackendReportedSuccess = WritePerKeyColors(keyColors);

                if (result.BackendReportedSuccess)
                {
                    _lastRequestedZoneColors = normalizedZoneColors.ToArray();
                    _logging.Info("[HidPerKey] OK Zone-mapped per-key colors written");
                }
                else
                {
                    result.FailureReason = "One or more HID per-key write packets failed";
                }
            }
            catch (Exception ex)
            {
                result.FailureReason = ex.Message;
                _logging.Error($"[HidPerKey] SetZoneColorsAsync failed: {ex.Message}", ex);
            }
            finally
            {
                sw.Stop();
                result.DurationMs = (int)sw.ElapsedMilliseconds;
            }

            await Task.CompletedTask;
            return result;
        }

        public Task<RgbApplyResult> SetZoneColorAsync(int zone, Color color)
        {
            if (zone < 0 || zone > 3)
            {
                return Task.FromResult(new RgbApplyResult
                {
                    Method = Method,
                    FailureReason = $"Invalid zone {zone}, must be 0-3"
                });
            }

            // The HID protocol is write-only, so preserve the last colors OmenCore
            // requested instead of blacking out the other zones on a single-zone write.
            var colors = _lastRequestedZoneColors.ToArray();
            colors[zone] = color;
            return SetZoneColorsAsync(colors);
        }

        public Task<Color[]?> ReadZoneColorsAsync()
            => Task.FromResult<Color[]?>(null); // write-only protocol

        public Task<bool> SetBrightnessAsync(int brightness)
        {
            if (!IsAvailable) return Task.FromResult(false);

            // Inject brightness into the mode packet and resend.
            var packet = BuildPacket(SUB_ENTER_EFFECT);
            packet[3] = STATIC_MODE_ID;
            packet[4] = (byte)Math.Clamp(brightness, 0, 100);
            return Task.FromResult(WritePacket(packet));
        }

        public Task<bool> SetBacklightEnabledAsync(bool enabled)
        {
            if (!IsAvailable) return Task.FromResult(false);

            if (!enabled)
            {
                // All-black write turns the backlight off without changing mode.
                var off = new Color[4];
                return SetZoneColorsAsync(off)
                    .ContinueWith(t => t.Result.BackendReportedSuccess);
            }

            // Re-enter per-key mode; firmware re-illuminates the keys.
            return Task.FromResult(SendEnterPerKeyMode());
        }

        public Task<RgbApplyResult> SetEffectAsync(
            KeyboardEffect effect,
            Color primaryColor,
            Color secondaryColor,
            int speed)
        {
            if (effect == KeyboardEffect.Off)
                return SetBacklightEnabledAsync(false)
                    .ContinueWith(t => new RgbApplyResult
                    {
                        Method = Method,
                        BackendReportedSuccess = t.Result,
                        SupportsVerification   = false
                    });

            if (effect == KeyboardEffect.Static)
            {
                var colors = new Color[4];
                for (int i = 0; i < 4; i++) colors[i] = primaryColor;
                return SetZoneColorsAsync(colors);
            }

            // Breathing and other animated effects are not yet mapped over the per-key
            // HID protocol. Return a clear non-failure so the caller does not retry
            // with a broken state.
            return Task.FromResult(new RgbApplyResult
            {
                Method                = Method,
                BackendReportedSuccess = false,
                FailureReason         = $"Effect '{effect}' is not yet supported by the HID per-key backend"
            });
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        /// <summary>Send the mode-select packet to enter static per-key mode.</summary>
        private bool SendEnterPerKeyMode()
        {
            var packet = BuildPacket(SUB_ENTER_EFFECT);
            packet[3]  = STATIC_MODE_ID;
            return WritePacket(packet);
        }

        /// <summary>Send the commit packet so the keyboard applies all buffered color writes.</summary>
        private bool SendCommit()
            => WritePacket(BuildPacket(SUB_COMMIT));

        /// <summary>
        /// Map 4 zone colors linearly across TOTAL_KEY_COUNT keys.
        /// Zone 0 = left quarter, Zone 1 = mid-left, Zone 2 = mid-right, Zone 3 = right quarter.
        /// </summary>
        private static Color[] MapZoneColorsToKeys(Color[] zoneColors)
        {
            var result    = new Color[TOTAL_KEY_COUNT];
            int keysPerZone = TOTAL_KEY_COUNT / 4;

            for (int i = 0; i < TOTAL_KEY_COUNT; i++)
            {
                int zoneIdx = Math.Min(i / keysPerZone, 3);
                result[i]   = zoneIdx < zoneColors.Length ? zoneColors[zoneIdx] : Color.Black;
            }

            return result;
        }

        private static Color[] NormalizeZoneColors(Color[] zoneColors)
        {
            var normalized = Enumerable.Repeat(Color.Black, 4).ToArray();
            for (int i = 0; i < Math.Min(zoneColors.Length, normalized.Length); i++)
            {
                normalized[i] = zoneColors[i];
            }

            return normalized;
        }

        /// <summary>
        /// Send all key colors to the hardware in fixed-size segments, then commit.
        /// Each segment packet carries up to KEYS_PER_SEGMENT * 3 color bytes.
        /// </summary>
        private bool WritePerKeyColors(Color[] keyColors)
        {
            int segmentCount = (int)Math.Ceiling((double)keyColors.Length / KEYS_PER_SEGMENT);

            for (int seg = 0; seg < segmentCount; seg++)
            {
                var packet = BuildPacket(SUB_SET_COLORS);
                packet[3]  = (byte)seg; // segment index

                int startKey = seg * KEYS_PER_SEGMENT;
                int endKey   = Math.Min(startKey + KEYS_PER_SEGMENT, keyColors.Length);

                // Color data begins at byte 4 of the 65-byte packet.
                for (int k = startKey; k < endKey; k++)
                {
                    int offset = 4 + (k - startKey) * 3;
                    if (offset + 2 >= PACKET_SIZE) break;

                    packet[offset]     = keyColors[k].R;
                    packet[offset + 1] = keyColors[k].G;
                    packet[offset + 2] = keyColors[k].B;
                }

                if (!WritePacket(packet))
                {
                    _logging.Warn($"[HidPerKey] Failed to write color segment {seg}/{segmentCount - 1}");
                    return false;
                }
            }

            return SendCommit();
        }

        /// <summary>
        /// Allocate a zeroed 65-byte packet with the report ID and the shared command
        /// byte pre-populated at the expected offsets.
        /// </summary>
        private static byte[] BuildPacket(byte subCommand)
        {
            var p = new byte[PACKET_SIZE];
            p[0] = REPORT_ID;
            p[1] = CMD_BYTE;
            p[2] = subCommand;
            return p;
        }

        private bool WritePacket(byte[] packet)
        {
            if (_stream == null) return false;

            try
            {
                _stream.Write(packet, 0, packet.Length);
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"[HidPerKey] HID write failed: {ex.Message}");
                TryReopenDevice();
                return false;
            }
        }

        private void TryReopenDevice()
        {
            if (_device == null) return;

            try
            {
                _stream?.Dispose();
                _stream              = _device.Open();
                _stream.WriteTimeout = 1000;
                _logging.Info("[HidPerKey] Reopened HID keyboard device after write failure");
            }
            catch (Exception ex)
            {
                _logging.Warn($"[HidPerKey] Reopen failed: {ex.Message}");
                _stream = null;
            }
        }

        /// <summary>
        /// Output report length, or -1 if the device will not report it. Some collections
        /// throw here rather than answering, and an unreadable length must not read as 0 -
        /// 0 is a meaningful value meaning "no output reports at all".
        /// </summary>
        private int SafeGetMaxOutputReportLength(HidDevice d)
        {
            try
            {
                return d.GetMaxOutputReportLength();
            }
            catch (Exception ex)
            {
                _logging.Debug($"[HidPerKey] Could not read output report length for " +
                               $"{d.VendorID:X4}:{d.ProductID:X4}: {ex.Message}");
                return -1;
            }
        }

        private string SafeGetProductName(HidDevice d)
        {
            try
            {
                return d.GetProductName() ?? "unknown";
            }
            catch (Exception ex)
            {
                _logging.Debug($"[HidPerKey] Could not read product name for PID 0x{d.ProductID:X4}: {ex.Message}");
                return "unknown";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _stream?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by another failure path; nothing else to release.
            }
            catch (Exception ex)
            {
                _logging.Debug($"[HidPerKey] Dispose ignored stream cleanup failure: {ex.Message}");
            }
            _stream = null;
        }
    }
}
