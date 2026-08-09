using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Hardware;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Per-key RGB for OMEN MAX keyboards, over the two interfaces the keyboard actually exposes.
    ///
    /// This backend exists because <see cref="HidPerKeyBackend"/>'s protocol is not this keyboard's.
    /// That one speaks the 0x0F / 0x42 / 0x52 command set from the 0x03F0 OMEN family, inferred
    /// from OpenRGB; measured on board 8D87, the keyboard is Darfon 0D62:54BF and speaks something
    /// else entirely. Both are kept: they serve different hardware, and neither is a fallback for
    /// the other.
    ///
    /// TWO INTERFACES, and routing colour to the right one is the main way to get lost here:
    ///
    ///   mi_03, <see cref="DojoKeyboardMcu"/>  the MCU's own protocol. STATIC COLOUR MAP (commands
    ///                                         0x05/0x06/0x07, 176-entry key map) and the twelve
    ///                                         device ANIMATIONS (command 0x03). The MCU holds the
    ///                                         map, draws it, and restores it after the Fn overlay.
    ///   mi_04, <see cref="HidLampArray"/>     HID LampArray, per-key addressed by lamp id. Used
    ///                                         for the per-key editor's individual key painting.
    ///                                         Requires host ownership, which means the MCU stops
    ///                                         drawing — and cannot restore after Fn.
    ///
    /// Uniform and zone colour go through mi_03, so the MCU owns the picture and the Fn key works.
    /// Per-key painting (the editor) goes through mi_04 for individual addressing, with an mi_03
    /// base layer as a fallback the MCU can restore to after Fn.
    ///
    /// Effects can be read back off the MCU and are checked here. Colours cannot - the LampArray
    /// spec has no colour readback - so "did that key turn red" has no software answer, and the
    /// colour methods report only that the device ACCEPTED the report.
    ///
    /// Board scope: measured on 8D87 (OMEN MAX 16-ak0xxx, BIOS F.07, EC 40.38). Protocol map and
    /// evidence: omen-max-16/reference/keyboard-mcu.md.
    /// </summary>
    public sealed class DojoPerKeyBackend : IKeyboardBackend
    {
        private readonly LoggingService _logging;

        private DojoKeyboardMcu? _mcu;
        private HidLampArray? _lamps;
        private IReadOnlyList<HidLampArray.LampInfo> _lampMap = Array.Empty<HidLampArray.LampInfo>();
        private ushort[] _lampZone = Array.Empty<ushort>();

        private Color[] _lastZoneColors = Enumerable.Repeat(Color.Black, ZoneCountConst).ToArray();
        private byte _brightness = 255;
        private bool _hostOwnsLamps;
        private bool _disposed;

        private const int ZoneCountConst = 4;

        public DojoPerKeyBackend(LoggingService logging) => _logging = logging;

        // ── IKeyboardBackend ───────────────────────────────────────────────────────

        public string Name => "OMEN MAX per-key (LampArray + MCU)";
        public KeyboardMethod Method => KeyboardMethod.HidPerKey;
        public bool IsAvailable => _lamps != null || _mcu != null;

        /// <summary>False, and it means COLOUR readback specifically. Effects are verified in
        /// <see cref="SetEffectAsync"/>; colours have no readback on either interface.</summary>
        public bool SupportsReadback => false;

        public int ZoneCount => ZoneCountConst;
        public bool IsPerKey => true;

        /// <summary>Number of individually addressable keys, or 0 if the lamp interface is absent.</summary>
        public int KeyCount => _lampMap.Count;

        /// <summary>Whether the device's animation engine is reachable.</summary>
        public bool SupportsDeviceEffects => _mcu != null;

        /// <summary>
        /// Whether this exact keyboard's protocol support was confirmed on hardware.
        ///
        /// False means the device is in HP's table for this code path but nobody here has driven it -
        /// so the code will be sent to it and may well work, but "may well" is the claim. A caller
        /// showing this to a user should say so; silently treating inference as measurement is how
        /// the guessed PID 0x054F ended up in the model database in the first place.
        /// </summary>
        public bool IsVerifiedDevice => _mcu?.IsVerifiedDevice ?? false;

        /// <summary>USB identity of the keyboard MCU, for display and field reports.</summary>
        public string DeviceIdentity => _mcu == null
            ? "no MCU"
            : $"{_mcu.VendorId:X4}:{_mcu.ProductId:X4}";

        public Task<bool> InitializeAsync()
        {
            try
            {
                _mcu = DojoKeyboardMcu.Open();
                if (_mcu != null)
                {
                    _logging.Info($"[DojoPerKey] MCU on mi_03: {_mcu.VendorId:X4}:{_mcu.ProductId:X4} " +
                                  $"'{_mcu.ProductName}' - device effects available");
                }
                else
                {
                    // The interesting failure is a matching keyboard whose mi_03 is missing or
                    // held open, which a bare "not found" cannot express.
                    var candidates = DojoKeyboardMcu.DescribeCandidates();
                    if (candidates.Count == 0)
                        _logging.Info("[DojoPerKey] No Darfon keyboard MCU present; device effects unavailable");
                    else
                        foreach (string line in candidates)
                            _logging.Info($"[DojoPerKey]   candidate: {line}");
                }

                OpenLampArray();

                if (!IsAvailable)
                {
                    _logging.Info("[DojoPerKey] Neither interface available; this is not an OMEN MAX per-key keyboard");
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"[DojoPerKey] Initialization failed: {ex.Message}", ex);
                Dispose();
                return Task.FromResult(false);
            }
        }

        public Task<RgbApplyResult> SetZoneColorsAsync(Color[] zoneColors)
        {
            var result = NewResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var normalized = Normalize(zoneColors);
                bool uniform = normalized.All(c => c.ToArgb() == normalized[0].ToArgb());

                if (uniform && _mcu != null)
                {
                    // MI_03: the MCU holds the colour map and redraws after the Fn overlay.
                    // No host control needed — the device is autonomous and draws its own picture.
                    ReleaseHostControlIfHeld();
                    var c = normalized[0];
                    result.BackendReportedSuccess = _mcu.SetStaticColor(c.R, c.G, c.B);
                    _logging.Info($"[DojoPerKey] Uniform fill via mi_03: " +
                                  $"{(result.BackendReportedSuccess ? "accepted" : "REFUSED")}");
                }
                else if (uniform && _lamps != null && _lampMap.Count > 0)
                {
                    TakeHostControl();
                    var c = normalized[0];
                    ushort first = _lampMap.Min(l => l.LampId);
                    ushort last = _lampMap.Max(l => l.LampId);
                    result.BackendReportedSuccess = _lamps.SetRange(first, last, c.R, c.G, c.B, _brightness);
                    _logging.Info($"[DojoPerKey] Uniform fill over lamps {first}-{last} (mi_04 fallback): " +
                                  $"{(result.BackendReportedSuccess ? "accepted" : "REFUSED")}");
                }
                else if (_lamps != null && _lampMap.Count > 0)
                {
                    // Mixed zones: set mi_03 base with zone 0's colour as Fn fallback, then
                    // mi_04 for the actual per-zone picture.
                    _mcu?.SetStaticColor(normalized[0].R, normalized[0].G, normalized[0].B);
                    TakeHostControl();

                    var lamps = new List<HidLampArray.LampColor>(_lampMap.Count);
                    for (int i = 0; i < _lampMap.Count; i++)
                    {
                        var c = normalized[_lampZone[i]];
                        lamps.Add(new HidLampArray.LampColor(_lampMap[i].LampId, c.R, c.G, c.B, _brightness));
                    }

                    result.BackendReportedSuccess = _lamps.SetLamps(lamps);
                    _logging.Info($"[DojoPerKey] {lamps.Count} lamps in " +
                                  $"{(lamps.Count + HidLampArray.MaxLampsPerUpdate - 1) / HidLampArray.MaxLampsPerUpdate} " +
                                  $"batches: {(result.BackendReportedSuccess ? "accepted" : "REFUSED")}");
                }
                else
                {
                    result.FailureReason = "No interface available for static colour";
                    return Task.FromResult(result);
                }

                if (result.BackendReportedSuccess)
                    _lastZoneColors = normalized;
                else
                    result.FailureReason = "The device refused one or more colour writes";
            }
            catch (Exception ex)
            {
                result.FailureReason = ex.Message;
                _logging.Error($"[DojoPerKey] SetZoneColorsAsync failed: {ex.Message}", ex);
            }
            finally
            {
                sw.Stop();
                result.DurationMs = (int)sw.ElapsedMilliseconds;
            }

            return Task.FromResult(result);
        }

        public Task<RgbApplyResult> SetZoneColorAsync(int zone, Color color)
        {
            if (zone < 0 || zone >= ZoneCountConst)
            {
                var bad = NewResult();
                bad.FailureReason = $"Invalid zone {zone}, must be 0-{ZoneCountConst - 1}";
                return Task.FromResult(bad);
            }

            // Neither interface reads colour back, so the other zones come from what was last
            // requested. Reconstructing them from the device is not an option here.
            var colors = _lastZoneColors.ToArray();
            colors[zone] = color;
            return SetZoneColorsAsync(colors);
        }

        /// <summary>Null - see <see cref="SupportsReadback"/>. Neither interface reads colour back.</summary>
        public Task<Color[]?> ReadZoneColorsAsync() => Task.FromResult<Color[]?>(null);

        /// <summary>
        /// Set brightness, which on this keyboard means the LampArray intensity channel and only that.
        ///
        /// MCU command 0x0C IS STILL SENT, and is expected to fail: measured on 8D87 it is refused at
        /// every payload value while every other command in this class is acknowledged through the
        /// same path. It costs one frame and would be the right lever on a board that implements it.
        ///
        /// The gap that leaves is real. Intensity scales a HOST-PAINTED picture, and a device-rendered
        /// effect is drawn by the MCU with no host involvement - so nothing here dims a running
        /// effect, and a UI implying otherwise would be lying.
        /// </summary>
        public async Task<bool> SetBrightnessAsync(int brightness)
        {
            int clamped = Math.Clamp(brightness, 0, 100);
            _brightness = (byte)(clamped * 255 / 100);

            bool mcuTook = _mcu?.SetBrightness((byte)clamped) ?? false;
            bool lampsTook = false;

            // Repaint ONLY when the host is already displaying a picture. Repainting
            // unconditionally would take the lamps away from a running device effect and freeze it
            // into a static frame - so asking to dim an animation would silently stop it, which is
            // not what anyone means by a brightness change.
            if (_hostOwnsLamps && _lamps != null && _lampMap.Count > 0)
            {
                var repaint = await SetZoneColorsAsync(_lastZoneColors);
                lampsTook = repaint.BackendReportedSuccess;
            }

            if (mcuTook)
                _logging.Info($"[DojoPerKey] MCU accepted brightness {clamped} via 0x0C - unexpected on 8D87, worth reporting");
            else if (!lampsTook)
                _logging.Info("[DojoPerKey] Brightness had nowhere to land: the MCU refused 0x0C and no " +
                              "host-painted picture was displayed to re-scale. A running device effect " +
                              "cannot be dimmed on this hardware.");

            return mcuTook || lampsTook;
        }

        public Task<bool> SetBacklightEnabledAsync(bool enabled)
        {
            // The MCU's own blank is the right lever: it leaves the installed effect in place, so
            // turning the backlight back on restores what was there rather than a black keyboard.
            if (_mcu != null) return Task.FromResult(_mcu.SetLightingEnabled(enabled));

            if (_lamps == null) return Task.FromResult(false);

            if (!enabled)
            {
                TakeHostControl();
                return Task.FromResult(_lamps.SetAll(0, 0, 0));
            }

            return SetZoneColorsAsync(_lastZoneColors)
                .ContinueWith(t => t.Result.BackendReportedSuccess);
        }

        /// <summary>
        /// Apply an effect, choosing the interface the effect belongs to.
        ///
        /// Static goes to the lamps as a picture. Everything else is a device-side animation, which
        /// means handing the lamps back first - an effect and a host-painted picture cannot both
        /// own the keyboard.
        /// </summary>
        public Task<RgbApplyResult> SetEffectAsync(
            KeyboardEffect effect, Color primaryColor, Color secondaryColor, int speed)
        {
            if (effect == KeyboardEffect.Off)
                return SetBacklightEnabledAsync(false)
                    .ContinueWith(t =>
                    {
                        var r = NewResult();
                        r.BackendReportedSuccess = t.Result;
                        if (!t.Result) r.FailureReason = "The device refused the blank command";
                        return r;
                    });

            if (effect == KeyboardEffect.Static)
            {
                var colors = Enumerable.Repeat(primaryColor, ZoneCountConst).ToArray();
                return SetZoneColorsAsync(colors);
            }

            var result = NewResult();

            if (_mcu == null)
            {
                result.FailureReason = $"Effect '{effect}' needs the MCU on mi_03, which is not open";
                return Task.FromResult(result);
            }

            if (!TryMapEffect(effect, out DojoKeyboardMcu.Effect wire))
            {
                // Reactive has no counterpart among the twelve: they all render without host
                // input, and a keypress-driven effect is not one of them.
                result.FailureReason = $"Effect '{effect}' has no equivalent in this MCU's effect set";
                return Task.FromResult(result);
            }

            var record = BuildRecord(wire, primaryColor, secondaryColor, speed);
            return Task.FromResult(ApplyRecord(record, result));
        }

        // ── Beyond the interface ───────────────────────────────────────────────────

        /// <summary>
        /// Drive one of the device's twelve effects directly, with the fields that effect actually
        /// consumes. The <see cref="IKeyboardBackend"/> vocabulary reaches four of them; this is
        /// how a caller reaches Ghosting, Ripple, Raindrop, OMEN X, Confetti, Sun, Swipe and
        /// Starlight, and how it picks a preset palette rather than a colour.
        /// </summary>
        public RgbApplyResult SetDeviceEffect(DojoKeyboardMcu.EffectRecord record)
        {
            var result = NewResult();

            if (_mcu == null)
            {
                result.FailureReason = "The MCU on mi_03 is not open";
                return result;
            }

            return ApplyRecord(record, result);
        }

        /// <summary>The effect the MCU is currently holding, or null if it will not answer.</summary>
        public DojoKeyboardMcu.EffectRecord? ReadDeviceEffect() => _mcu?.TryReadEffect();

        /// <summary>
        /// Send MCU brightness (command 0x0C) and NOTHING ELSE.
        ///
        /// Separate from <see cref="SetBrightnessAsync"/> because that one also repaints the lamps
        /// with a new intensity, and when a static picture is displayed the repaint alone accounts
        /// for any visible change - so it cannot tell you whether 0x0C did anything. Isolating the
        /// command is the only way to attribute the result to it, and this exists to make that
        /// test possible rather than for callers to prefer.
        /// </summary>
        public bool SetMcuBrightnessOnly(byte level) => _mcu?.SetBrightness(level) ?? false;

        /// <summary>
        /// Set the intensity attached to each lamp in subsequent colour writes. Does not repaint.
        /// </summary>
        public void SetLampIntensity(int brightness) =>
            _brightness = (byte)(Math.Clamp(brightness, 0, 100) * 255 / 100);

        /// <summary>
        /// Colour individual keys, addressed by lamp id. Ids come from <see cref="GetKeyMap"/>;
        /// each carries the HID usage of the key it sits under.
        ///
        /// Uses mi_04 for individual addressing, but first writes an mi_03 base layer so the MCU
        /// has something to restore after the Fn overlay. The mi_03 base is a uniform fill with
        /// the most common colour — not a perfect match, but not a black keyboard.
        ///
        /// Accepted, not verified - there is no colour readback to check it against.
        /// </summary>
        public bool SetKeyColors(IReadOnlyDictionary<ushort, Color> keyColors)
        {
            if (_lamps == null || keyColors.Count == 0) return false;

            if (_mcu != null && keyColors.Count > 0)
            {
                var dominant = MostCommonColor(keyColors.Values);
                _mcu.SetStaticColor(dominant.R, dominant.G, dominant.B);
            }

            TakeHostControl();

            var lamps = keyColors
                .Select(kv => new HidLampArray.LampColor(kv.Key, kv.Value.R, kv.Value.G, kv.Value.B, _brightness))
                .ToList();

            bool ok = _lamps.SetLamps(lamps);
            _logging.Info($"[DojoPerKey] {lamps.Count} lamps at intensity {_brightness}: " +
                          $"{(ok ? "accepted" : "REFUSED")}");
            return ok;
        }

        private static Color MostCommonColor(IEnumerable<Color> colors)
        {
            return colors
                .GroupBy(c => c.ToArgb())
                .OrderByDescending(g => g.Count())
                .First().First();
        }

        /// <summary>
        /// Every addressable lamp: its id, where it sits, and which key usage it lights.
        ///
        /// The ids come from what the DEVICE reported, not from what was asked for. On 8D87 the
        /// lamp-attributes response ignores the requested id and free-runs - ask for 0, 1, 2 and
        /// get back 41, 42, 43 - so a caller that assumed "ask for N, get N" would light the wrong
        /// keys and have no way to notice.
        /// </summary>
        public IReadOnlyList<HidLampArray.LampInfo> GetKeyMap() => _lampMap;

        /// <summary>
        /// Persist the installed effect so it survives a power cycle.
        ///
        /// A REAL FLASH WRITE. Call it when a user saves a profile; never on every apply, and never
        /// in a loop.
        /// </summary>
        public bool StoreToFlash() => _mcu?.StoreToFlash() ?? false;

        /// <summary>Put the lighting back to HP's firmware defaults. The recovery lever.</summary>
        public bool RestoreFirmwareDefaults() => _mcu?.RestoreFirmwareDefaults() ?? false;

        /// <summary>
        /// Give the lamps back to the device's own effect engine, which resumes whatever effect is
        /// installed and repaints over any host-painted picture.
        ///
        /// Explicit because it is destructive to a static picture and there is no undo - the
        /// picture is not stored anywhere the device can restore it from.
        /// </summary>
        public bool ReleaseToDeviceEffects()
        {
            if (_lamps == null) return false;

            bool ok = _lamps.SetAutonomousMode(true);
            _hostOwnsLamps = false;
            return ok;
        }

        // ── Internals ──────────────────────────────────────────────────────────────

        private RgbApplyResult ApplyRecord(DojoKeyboardMcu.EffectRecord record, RgbApplyResult result)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                // The device is about to animate, so it needs its lamps back. Skipped when the
                // host never took them - handing back something never taken is a no-op the device
                // would still have to parse.
                if (_hostOwnsLamps && _lamps != null)
                {
                    _lamps.SetAutonomousMode(true);
                    _hostOwnsLamps = false;
                }

                result.BackendReportedSuccess = _mcu!.SetEffect(record);
                if (!result.BackendReportedSuccess)
                {
                    result.FailureReason = $"The MCU refused effect {record.Effect}";
                    return result;
                }

                // The one place on this keyboard where a write can be checked. It confirms the
                // MCU installed the effect, not that anything is visible - two effects render
                // black by design, so a person still has the final say.
                var readback = _mcu.TryReadEffect();
                result.SupportsVerification = readback != null;
                result.VerificationPassed = readback?.Effect == record.Effect;

                if (readback == null)
                    _logging.Info($"[DojoPerKey] {record.Effect} accepted; the MCU did not answer the readback");
                else if (!result.VerificationPassed)
                    _logging.Warn($"[DojoPerKey] {record.Effect} accepted but the MCU reports {readback.Value.Effect}");
                else
                    _logging.Info($"[DojoPerKey] {record.Effect} installed and confirmed by readback");

                WarnIfEffectWillRenderBlack(record);
            }
            catch (Exception ex)
            {
                result.BackendReportedSuccess = false;
                result.FailureReason = ex.Message;
                _logging.Error($"[DojoPerKey] Effect write failed: {ex.Message}", ex);
            }
            finally
            {
                sw.Stop();
                result.DurationMs = (int)sw.ElapsedMilliseconds;
            }

            return result;
        }

        /// <summary>
        /// Two effects render black for reasons that are in the field map rather than in a fault,
        /// and both look exactly like a broken backend from the user's chair. Say so in the log
        /// before someone spends an evening on it.
        /// </summary>
        private void WarnIfEffectWillRenderBlack(DojoKeyboardMcu.EffectRecord record)
        {
            if (record.Effect == DojoKeyboardMcu.Effect.Swipe &&
                record.ColorNumber == DojoKeyboardMcu.ColorNumberPreset)
            {
                _logging.Warn("[DojoPerKey] Swipe has no preset palette and will render BLACK with a preset " +
                              "selected. Give it custom colours.");
            }

            if (record.Effect == DojoKeyboardMcu.Effect.AudioPulse &&
                record.InnerBrightness == 0 && record.OuterBrightness == 0)
            {
                _logging.Warn("[DojoPerKey] Audio Pulse IS its two level bytes; at 0 it renders BLACK. " +
                              "Feed InnerBrightness/OuterBrightness from an audio thread at ~5 Hz.");
            }
        }

        private static DojoKeyboardMcu.EffectRecord BuildRecord(
            DojoKeyboardMcu.Effect wire, Color primary, Color secondary, int speed)
        {
            // A secondary that is black or identical to the primary is how the shared profile model
            // spells "one colour", so it is read that way rather than sent as a second slot.
            bool twoColors = secondary.ToArgb() != primary.ToArgb() &&
                             !(secondary.R == 0 && secondary.G == 0 && secondary.B == 0);

            var colors = twoColors
                ? new[] { (primary.R, primary.G, primary.B), (secondary.R, secondary.G, secondary.B) }
                : new[] { (primary.R, primary.G, primary.B) };

            return new DojoKeyboardMcu.EffectRecord
            {
                Effect = wire,
                ShowMode = twoColors
                    ? DojoKeyboardMcu.ShowMode.MultipleCustomColors
                    : DojoKeyboardMcu.ShowMode.SingleCustomColor,

                // Zero-based count, not a count. Two colours send 1.
                ColorNumber = (byte)(colors.Length - 1),

                Speed = MapSpeed(speed),

                // OGH never sets this in an effect frame and uses command 0x0C instead; sending a
                // value here is an unexercised path, so it stays at what OGH sends.
                Brightness = 0,

                Direction = DojoKeyboardMcu.EffectDirection.LeftToRight,
                RippleSize = 1,
                RaindropFrequency = (byte)MapSpeed(speed),
                Colors = colors
            };
        }

        private static DojoKeyboardMcu.EffectSpeed MapSpeed(int speed) => speed switch
        {
            < 34 => DojoKeyboardMcu.EffectSpeed.Slow,
            < 67 => DojoKeyboardMcu.EffectSpeed.Medium,
            _ => DojoKeyboardMcu.EffectSpeed.Fast
        };

        private static bool TryMapEffect(KeyboardEffect effect, out DojoKeyboardMcu.Effect wire)
        {
            switch (effect)
            {
                case KeyboardEffect.Breathing: wire = DojoKeyboardMcu.Effect.Breathing; return true;
                case KeyboardEffect.ColorCycle: wire = DojoKeyboardMcu.Effect.ColorCycle; return true;
                case KeyboardEffect.Wave: wire = DojoKeyboardMcu.Effect.Wave; return true;
                default: wire = default; return false;
            }
        }

        private void OpenLampArray()
        {
            var arrays = HidLampArray.OpenAll();

            // Kind 1 is Keyboard. The lamp-count floor rejects the virtual four-zone array that
            // appears on the same machine, which is the light bar wearing a LampArray costume.
            var keyboard = arrays.FirstOrDefault(a => a.Kind == 1 && a.LampCount > 8);

            foreach (var other in arrays.Where(a => !ReferenceEquals(a, keyboard)))
                other.Dispose();

            if (keyboard == null)
            {
                _logging.Info("[DojoPerKey] No keyboard-kind LampArray; static per-key colour unavailable");
                return;
            }

            _lamps = keyboard;
            BuildLampMap(keyboard);

            _logging.Info($"[DojoPerKey] LampArray on mi_04: {keyboard.LampCount} lamps, " +
                          $"{_lampMap.Count} mapped, min update {keyboard.MinUpdateInterval.TotalMilliseconds:F0} ms");
        }

        /// <summary>
        /// Walk the lamps and key the map on the id the DEVICE reports.
        ///
        /// The extra pass is not defensive padding: on 8D87 the attributes response ignores the
        /// requested id and free-runs, so a single pass of LampCount requests returns a rotated
        /// set and misses some ids entirely. Walking twice and keying on what came back converges.
        /// </summary>
        private void BuildLampMap(HidLampArray keyboard)
        {
            var byId = new SortedDictionary<ushort, HidLampArray.LampInfo>();

            for (int i = 0; i < keyboard.LampCount * 2 && byId.Count < keyboard.LampCount; i++)
            {
                var info = keyboard.GetLampInfo((ushort)(i % keyboard.LampCount));
                if (info != null) byId[info.Value.LampId] = info.Value;
            }

            if (byId.Count < keyboard.LampCount)
            {
                _logging.Warn($"[DojoPerKey] Lamp map incomplete: {byId.Count} of {keyboard.LampCount} lamps " +
                              "answered. Unmapped lamps will not be coloured.");
            }

            _lampMap = byId.Values.ToList();
            _lampZone = AssignZones(_lampMap);
        }

        /// <summary>
        /// Zone each lamp by where it physically sits, left to right.
        ///
        /// Splitting the lamp INDEX into quarters instead would be simpler and wrong: lamp order is
        /// the device's wiring order, not a left-to-right sweep, so index quarters produce four
        /// scattered patches rather than four bands. The lamps carry their own coordinates for
        /// exactly this reason.
        /// </summary>
        private static ushort[] AssignZones(IReadOnlyList<HidLampArray.LampInfo> lamps)
        {
            var zones = new ushort[lamps.Count];
            if (lamps.Count == 0) return zones;

            uint minX = lamps.Min(l => l.XMicrometres);
            uint maxX = lamps.Max(l => l.XMicrometres);
            double span = Math.Max(1, (double)maxX - minX);

            for (int i = 0; i < lamps.Count; i++)
            {
                double fraction = (lamps[i].XMicrometres - minX) / span;
                zones[i] = (ushort)Math.Clamp((int)(fraction * ZoneCountConst), 0, ZoneCountConst - 1);
            }

            return zones;
        }

        /// <summary>
        /// Ask the device to stop running its own effects before painting lamps.
        ///
        /// This does not stop Windows Dynamic Lighting, which is a separate owner of LampArray
        /// devices and repaints continuously when enabled. A colour that vanishes within a frame
        /// with this already called is Dynamic Lighting, not the device.
        /// </summary>
        private void TakeHostControl()
        {
            if (_hostOwnsLamps || _lamps == null) return;

            _lamps.SetAutonomousMode(false);
            _hostOwnsLamps = true;
        }

        private void ReleaseHostControlIfHeld()
        {
            if (!_hostOwnsLamps || _lamps == null) return;

            _lamps.SetAutonomousMode(true);
            _hostOwnsLamps = false;
        }

        private RgbApplyResult NewResult() => new() { Method = Method, SupportsVerification = false };

        private static Color[] Normalize(Color[] zoneColors)
        {
            var normalized = Enumerable.Repeat(Color.Black, ZoneCountConst).ToArray();
            for (int i = 0; i < Math.Min(zoneColors.Length, ZoneCountConst); i++)
                normalized[i] = zoneColors[i];
            return normalized;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Hand the lamps back so the MCU can draw its own lighting. Without this, the keyboard
            // stays in host-control mode permanently — it outlives the process, survives a reboot
            // (the internal USB bus stays powered), and the MCU cannot restore its picture after the
            // Fn overlay or any other device-side redraw. That is the root cause of the reported
            // Fn-key bug: the overlay goes up, and nothing comes back.
            //
            // With static colour routed through mi_03, the MCU holds the picture and draws it
            // autonomously, so releasing here does not erase anything — it lets the MCU resume
            // drawing what it already has.
            ReleaseHostControlIfHeld();

            _lamps?.Dispose();
            _mcu?.Dispose();
            _lamps = null;
            _mcu = null;
        }
    }
}
