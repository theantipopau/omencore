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
    ///   mi_04, <see cref="HidLampArray"/>     HID LampArray, per-key addressed by lamp id, with a
    ///                                         per-lamp intensity channel. Requires host ownership,
    ///                                         which means the MCU stops drawing — and cannot
    ///                                         restore after Fn.
    ///
    /// ALL colour goes through mi_03 whenever the board's layout is known, uniform and per-key
    /// alike, so the MCU owns the picture and the Fn key works. mi_04 is the fallback for a board
    /// whose lamp ids cannot be turned into colour-map positions, and that path cannot survive Fn.
    ///
    /// BRIGHTNESS IS ARITHMETIC HERE, not a device setting. mi_04 has an intensity channel and
    /// mi_03 has none, so the colour map is scaled on the host on its way to the wire — see
    /// <see cref="Scale"/>. The MCU's own brightness command is refused by this firmware.
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

        /// <summary>Which keyboard this is, and so which LED bytes make up each key. Null when the
        /// board is not in the catalogue, which costs per-key Fn survival and nothing else.</summary>
        private KeyboardLayout? _layout;

        /// <summary>lamp id -> the mi_03 colour map positions that light the same key, resolved
        /// through the lamp's HID usage. Empty when <see cref="_layout"/> is null.</summary>
        private Dictionary<ushort, int[]> _lampToLeds = new();

        private Color[] _lastZoneColors = Enumerable.Repeat(Color.Black, ZoneCountConst).ToArray();
        private byte _brightness = 255;
        private bool _hostOwnsLamps;
        private bool _disposed;

        /// <summary>
        /// The last colour map handed to the MCU, UNSCALED — as the caller painted it, not as it
        /// went out on the wire. Brightness re-sends this at a new setting, which it can only do
        /// if what is remembered is the picture rather than the last dimmed copy of it; keeping
        /// the scaled bytes would make each brightness change compound on the previous one.
        /// </summary>
        private byte[]? _mapR, _mapG, _mapB;

        /// <summary>
        /// Whether <see cref="_mapR"/> is what the keyboard is drawing right now. False once an
        /// effect, host lamp ownership or a blank has taken the display, and it is what stops a
        /// brightness change from yanking a running animation back to a stale static frame.
        /// </summary>
        private bool _mcuShowsHostMap;

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
                    var c = normalized[0];
                    result.BackendReportedSuccess = WriteUniformColorMap(c);
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
                else if (_mcu != null && _lampToLeds.Count > 0)
                {
                    // Mixed zones through the mi_03 map, so the bands survive the Fn overlay.
                    // Painting mi_04 instead needs host control, and host control is exactly what
                    // stops the MCU redrawing - the zones would vanish on the first Fn press.
                    var perLamp = new Dictionary<ushort, Color>(_lampMap.Count);
                    for (int i = 0; i < _lampMap.Count; i++)
                        perLamp[_lampMap[i].LampId] = normalized[_lampZone[i]];

                    result.BackendReportedSuccess = SetKeyColors(perLamp);
                    _logging.Info($"[DojoPerKey] {ZoneCountConst} zones over the mi_03 colour map: " +
                                  $"{(result.BackendReportedSuccess ? "accepted" : "REFUSED")}");
                }
                else if (_lamps != null && _lampMap.Count > 0)
                {
                    // No layout for this board, so lamp ids cannot become colour-map positions.
                    // Uniform mi_03 base as the Fn fallback, per-zone picture on mi_04 above it.
                    if (_mcu != null) WriteUniformColorMap(normalized[0], release: false);
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
                                  $"batches (no layout; will NOT survive Fn): " +
                                  $"{(result.BackendReportedSuccess ? "accepted" : "REFUSED")}");
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
        /// Set brightness, which on this keyboard means re-scaling the host-painted picture and
        /// only that.
        ///
        /// MCU command 0x0C IS STILL SENT, and is expected to fail: measured on 8D87 it is refused at
        /// every payload value while every other command in this class is acknowledged through the
        /// same path. It costs one frame and would be the right lever on a board that implements it.
        ///
        /// So the picture is re-sent instead, dimmed. Which picture depends on who is drawing:
        /// the mi_03 colour map when the MCU holds one, the mi_04 lamps when the host took them.
        ///
        /// The gap that leaves is real. Both are HOST-PAINTED pictures, and a device-rendered effect
        /// is drawn by the MCU from its own state with no host involvement - so nothing here dims a
        /// running effect, and a UI implying otherwise would be lying.
        /// </summary>
        public async Task<bool> SetBrightnessAsync(int brightness)
        {
            int clamped = Math.Clamp(brightness, 0, 100);
            _brightness = (byte)(clamped * 255 / 100);

            bool mcuTook = _mcu?.SetBrightness((byte)clamped) ?? false;
            bool repainted = false;

            // Repaint ONLY when the host is already displaying a picture. Repainting
            // unconditionally would take the display away from a running device effect and freeze
            // it into a static frame - so asking to dim an animation would silently stop it, which
            // is not what anyone means by a brightness change.
            if (_mcuShowsHostMap && _mapR != null && _mapG != null && _mapB != null)
            {
                repainted = WriteColorMap(_mapR, _mapG, _mapB);
            }
            else if (_hostOwnsLamps && _lamps != null && _lampMap.Count > 0)
            {
                var repaint = await SetZoneColorsAsync(_lastZoneColors);
                repainted = repaint.BackendReportedSuccess;
            }

            if (mcuTook)
                _logging.Info($"[DojoPerKey] MCU accepted brightness {clamped} via 0x0C - unexpected on 8D87, worth reporting");
            else if (repainted)
                _logging.Info($"[DojoPerKey] Brightness {clamped} applied by re-scaling the displayed picture");
            else
                _logging.Info("[DojoPerKey] Brightness had nowhere to land: the MCU refused 0x0C and no " +
                              "host-painted picture was displayed to re-scale. A running device effect " +
                              "cannot be dimmed on this hardware.");

            return mcuTook || repainted;
        }

        public Task<bool> SetBacklightEnabledAsync(bool enabled)
        {
            // The MCU's own blank is the right lever: it leaves the installed effect in place, so
            // turning the backlight back on restores what was there rather than a black keyboard.
            if (_mcu != null)
            {
                bool toggled = _mcu.SetLightingEnabled(enabled);

                // A blanked keyboard is not displaying the map even though the MCU still holds it.
                // Without this a brightness change would re-send the map - and the colour write
                // carries command 0x09 payload 0x01, so it would turn the backlight back on.
                if (toggled) _mcuShowsHostMap = enabled && _mapR != null;

                return Task.FromResult(toggled);
            }

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
        /// Set the brightness applied to subsequent colour writes on BOTH interfaces - the mi_04
        /// intensity channel, and the host-side scaling of the mi_03 colour map. Does not repaint,
        /// which is the whole point: the per-key editor calls this and then writes a full picture,
        /// and a repaint here would send the map twice for one Apply.
        /// </summary>
        public void SetLampIntensity(int brightness) =>
            _brightness = (byte)(Math.Clamp(brightness, 0, 100) * 255 / 100);

        /// <summary>
        /// Colour individual keys, addressed by lamp id. Ids come from <see cref="GetKeyMap"/>;
        /// each carries the HID usage of the key it sits under.
        ///
        /// Goes to mi_03 as a full per-key colour map whenever the layout is known, because that
        /// map is the MCU's own state and is what it redraws from when the Fn overlay clears. The
        /// picture then survives Fn with no host involvement at all.
        ///
        /// The earlier version painted mi_04 and took host control, which suppressed the mi_03 map
        /// it had just written; pressing Fn left the keyboard on the overlay frame until something
        /// repainted it. Host control and a durable picture are mutually exclusive on this device —
        /// taking it is what stops the MCU drawing. So this path must not take it.
        ///
        /// mi_04 is the fallback for an unknown board only, where there is no way to turn a lamp id
        /// into a colour-map position. That path cannot survive Fn, and says so.
        ///
        /// Accepted, not verified - there is no colour readback to check it against.
        /// </summary>
        public bool SetKeyColors(IReadOnlyDictionary<ushort, Color> keyColors)
        {
            if (keyColors.Count == 0) return false;

            if (_mcu != null && _lampToLeds.Count > 0 && _layout != null)
            {
                // Every LED of a key takes the key's colour. Callers wanting the legends of a
                // dual-legend key coloured separately address the positions with SetLedColors.
                var leds = new Dictionary<int, Color>(keyColors.Count * 2);
                int placed = 0;
                foreach (var (lampId, color) in keyColors)
                {
                    if (!_lampToLeds.TryGetValue(lampId, out var positions)) continue;
                    foreach (int position in positions) leds[position] = color;
                    placed++;
                }

                bool mapped = SetLedColors(leds, MostCommonColor(keyColors.Values));
                _logging.Info($"[DojoPerKey] {placed} of {keyColors.Count} keys into the mi_03 colour map " +
                              $"({_layout.Id}): {(mapped ? "accepted" : "REFUSED")}");
                if (mapped) return true;

                _logging.Warn("[DojoPerKey] mi_03 colour map refused; falling back to mi_04, which " +
                              "will not survive the Fn overlay.");
            }

            if (_lamps == null) return false;

            TakeHostControl();

            var lamps = keyColors
                .Select(kv => new HidLampArray.LampColor(kv.Key, kv.Value.R, kv.Value.G, kv.Value.B, _brightness))
                .ToList();

            bool ok = _lamps.SetLamps(lamps);
            _logging.Info($"[DojoPerKey] {lamps.Count} lamps at intensity {_brightness} over mi_04 " +
                          $"(no layout for this board; will NOT survive Fn): {(ok ? "accepted" : "REFUSED")}");
            return ok;
        }

        /// <summary>
        /// The detected keyboard, or null when the board is unknown. A UI enumerating keys to
        /// colour reads it from here — each <see cref="KeyboardKey"/> carries the colour-map
        /// positions that light it, which is what <see cref="SetLedColors"/> takes.
        /// </summary>
        public KeyboardLayout? Layout => _layout;

        /// <summary>
        /// Colour INDIVIDUAL LEDs, addressed by colour-map position rather than by key.
        ///
        /// A key is not one LED. Dual-legend keys have two, Space has five, backspace six, and the
        /// map is per-LED natively — so the legends of one key can take different colours, which
        /// is what the keyboard already does for the Fn overlay. <see cref="KeyboardKey.Leds"/>
        /// lists the positions of a key, in the order HP's table declares them.
        ///
        /// WHICH LED IS WHICH ON THE KEY IS NOT KNOWN FROM DATA. The layout tables give every LED
        /// of a key the key's own rectangle, so the order is a byte order and not a geometry, and
        /// it is not consistent between rows: on Esc position 0 is the LED under the legend and 1
        /// is below it, while on the number row the digit takes the lower position and the shifted
        /// glyph the higher. A UI that labels them should say "LED 1 of 2", not "top" and "bottom",
        /// unless someone has looked at that key on that keyboard.
        ///
        /// <paramref name="background"/> fills every position the caller did not name, so a partial
        /// update does not black out the rest of the keyboard.
        ///
        /// Goes to mi_03 without taking host control, so the picture survives the Fn overlay.
        /// Accepted, not verified — there is no colour readback on this device.
        /// </summary>
        public bool SetLedColors(IReadOnlyDictionary<int, Color> ledColors, Color background)
        {
            if (_mcu == null) return false;

            var r = new byte[DojoKeyboardMcu.ColorMapLength];
            var g = new byte[DojoKeyboardMcu.ColorMapLength];
            var b = new byte[DojoKeyboardMcu.ColorMapLength];
            Array.Fill(r, background.R);
            Array.Fill(g, background.G);
            Array.Fill(b, background.B);

            foreach (var (position, color) in ledColors)
            {
                if (position < 0 || position >= DojoKeyboardMcu.ColorMapLength) continue;
                r[position] = color.R;
                g[position] = color.G;
                b[position] = color.B;
            }

            return WriteColorMap(r, g, b);
        }

        /// <summary>
        /// Scale one colour channel by the brightness setting, the way mi_04's intensity channel
        /// scales a lamp: linear, and rounded to nearest rather than truncated so a dim picture
        /// does not lose a step to integer division at every level.
        /// </summary>
        internal static byte Scale(byte channel, byte intensity) =>
            (byte)((channel * intensity + 127) / 255);

        /// <summary>
        /// Hand an unscaled colour map to the MCU, applying brightness on the way out.
        ///
        /// The scaling belongs HERE and not in the maps callers build, because mi_03 has no
        /// intensity channel — the 176-entry map is raw RGB and the MCU draws exactly what it is
        /// given. Every per-key picture on a board with a known layout goes through this path, so
        /// leaving the scaling to the caller is what made the brightness slider a no-op: the value
        /// reached <see cref="_brightness"/> and then only mi_04 ever read it, and mi_04 is the
        /// fallback for boards this one is not.
        ///
        /// <paramref name="release"/> is false only for the mi_04 base layer, which is written to
        /// be RESTORED after Fn rather than displayed now. Releasing there would hand the display
        /// back to the MCU in the middle of an apply that is about to take it again, and the
        /// keyboard would flick through the old effect on the way.
        /// </summary>
        private bool WriteColorMap(byte[] r, byte[] g, byte[] b, bool release = true)
        {
            if (_mcu == null) return false;

            var sr = new byte[r.Length];
            var sg = new byte[g.Length];
            var sb = new byte[b.Length];
            for (int i = 0; i < r.Length; i++)
            {
                sr[i] = Scale(r[i], _brightness);
                sg[i] = Scale(g[i], _brightness);
                sb[i] = Scale(b[i], _brightness);
            }

            // The MCU only draws its own map while it owns the display, so release first. Taking
            // host control here is what broke the Fn overlay: it suppresses the map being written.
            if (release) ReleaseHostControlIfHeld();

            bool ok = _mcu.SetStaticColorMap(sr, sg, sb);
            if (ok)
            {
                _mapR = r;
                _mapG = g;
                _mapB = b;

                // Only the MCU drawing its own map counts as displaying it. When the host holds
                // the lamps this map is a base layer for the Fn overlay to restore to, not what
                // is on the keyboard now, and brightness must repaint the lamps instead.
                _mcuShowsHostMap = release && !_hostOwnsLamps;
            }
            return ok;
        }

        /// <summary>Fill the whole colour map with one colour, through the scaling path above.</summary>
        private bool WriteUniformColorMap(Color c, bool release = true)
        {
            var r = new byte[DojoKeyboardMcu.ColorMapLength];
            var g = new byte[DojoKeyboardMcu.ColorMapLength];
            var b = new byte[DojoKeyboardMcu.ColorMapLength];
            Array.Fill(r, c.R);
            Array.Fill(g, c.G);
            Array.Fill(b, c.B);

            return WriteColorMap(r, g, b, release);
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
            _mcuShowsHostMap = false;
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

                // The animation engine is drawing now, not the static map. Brightness must not
                // repaint it: re-sending the map would replace a running effect with a still frame.
                _mcuShowsHostMap = false;

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
            BuildKeyBridge();
        }

        /// <summary>
        /// Work out which keyboard this is, then map each lamp to the colour-map positions that
        /// light the same key.
        ///
        /// Detection is by board id, which also fixes the firmware cycle — the same model ships on
        /// both sides of a cycle boundary with different LED maps, so the model name alone is not
        /// enough. An unknown board leaves the bridge empty rather than guessing: the wrong layout
        /// lights the wrong keys and looks like a working feature.
        /// </summary>
        private void BuildKeyBridge()
        {
            var catalog = KeyboardLayoutCatalog.Instance;
            string board = OmenBoard.Product;

            _layout = SelectedLayoutId != null ? catalog.ById(SelectedLayoutId) : catalog.ForBoard(board);
            _lampToLeds = new Dictionary<ushort, int[]>();

            if (_layout == null)
            {
                _logging.Warn($"[DojoPerKey] Board '{board}' is not in the keyboard layout catalogue. " +
                              "Per-key colour will not survive the Fn overlay until a layout is " +
                              "chosen manually.");
                return;
            }

            int unmapped = 0;
            foreach (var lamp in _lampMap)
            {
                string? keyName = HidUsageKeyNames.Name(lamp.KeyUsage);
                var key = keyName == null ? null : _layout.ByName(keyName);
                if (key == null) { unmapped++; continue; }

                _lampToLeds[lamp.LampId] = key.Leds;
            }

            _logging.Info($"[DojoPerKey] Layout {_layout.Id} ({_layout.Leds} LEDs, {_layout.Keys.Count} keys" +
                          $"{(_layout.Verified ? "" : ", UNVERIFIED on this hardware")}) for board '{board}'; " +
                          $"{_lampToLeds.Count} of {_lampMap.Count} lamps bridged to colour-map positions" +
                          $"{(unmapped > 0 ? $", {unmapped} without a usable HID usage (vendor keys)" : "")}.");
        }

        /// <summary>
        /// Force a layout instead of detecting one, for a board the catalogue does not know or gets
        /// wrong. Null returns to detection. Takes effect on the next connect.
        ///
        /// Only Dojo/Global has been confirmed on hardware; everything else in the catalogue is
        /// derived and unverified, which is the reason this override exists at all.
        /// </summary>
        public static string? SelectedLayoutId { get; set; }

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

            // The MCU stops drawing the moment the host takes the lamps, so whatever map it holds
            // is no longer on the display and re-sending it would not be a repaint of what is there.
            _mcuShowsHostMap = false;
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
