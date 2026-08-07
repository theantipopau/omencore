using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// Per-key editor backed by the keyboard's MEASURED lamp map - every key at the position the
    /// device reported, rather than a grid of assumed shape.
    ///
    /// This exists alongside the 6 x 14 grid editor rather than replacing it: that one is a fixed
    /// abstraction that works on any per-key model, while this needs a backend that has actually
    /// interrogated the hardware. Where no map is available <see cref="IsAvailable"/> is false and
    /// the view hides itself, leaving the existing editor as it was.
    ///
    /// The selection maths lives here rather than in the code-behind so it can be tested without a
    /// window - a bounds check that is off by one selects a neighbouring key, which looks plausible
    /// until the wrong key lights up.
    ///
    /// WITH THANKS TO arfelious/omen-rgb-linux (https://github.com/arfelious/omen-rgb-linux), whose
    /// hand-built key table for this chassis family was used to check this layout. That project
    /// addresses a 182-entry LED space over the vendor protocol rather than this 120-lamp HID
    /// LampArray, so none of its offsets are reused here - but its per-key WIDTHS independently
    /// establish that one key spans several LEDs (backspace 8, left shift 7, caps lock 5), which is
    /// the model this layout is built on and the bug that produced a crushed modifier column.
    /// </summary>
    public class KeyboardMapViewModel : ViewModelBase
    {
        private readonly KeyboardLightingService? _keyboard;
        private readonly LoggingService _logging;

        // Layout scale. The view scales the canvas to fit, so PixelsPerMm only sets the resolution
        // the layout is computed at. The device reports lamp CENTRES and no extents, so a key has to
        // be given a size: 15 mm sits under the ~18 mm pitch, leaving a gap between neighbours
        // instead of a solid sheet. MinKeyWidth keeps the ~9 mm filler lamps clickable.
        private const double PixelsPerMm = 2.6;
        private const double KeySizeMm = 15.0;
        private const double MinKeyWidth = 14.0;

        private string _brushColorHex = "#FF0000";
        private string _status = string.Empty;
        private int _brightness = 100;

        public KeyboardMapViewModel(KeyboardLightingService? keyboard, LoggingService logging)
        {
            _keyboard = keyboard;
            _logging = logging;

            PickColorCommand = new RelayCommand(_ => PickColor());
            SelectAllCommand = new RelayCommand(_ => SetSelection(Keys, true));
            ClearSelectionCommand = new RelayCommand(_ => SetSelection(Keys, false));
            PaintSelectionCommand = new RelayCommand(_ => PaintSelection());
            ClearAllColorsCommand = new RelayCommand(_ => ClearAllColors());
            ApplyCommand = new AsyncRelayCommand(async _ => await ApplyAsync(), _ => IsAvailable);

            Load();
        }

        public ObservableCollection<KeyLampViewModel> Keys { get; } = new();

        /// <summary>Whether a measured map was found. False hides the whole editor.</summary>
        public bool IsAvailable => Keys.Count > 0;

        private const int LightBarZoneCount = 4;

        /// <summary>Whether the light bar is reachable, and so worth drawing under the keyboard.</summary>
        public bool HasLightBar => _keyboard?.IsLightBarAvailable ?? false;

        /// <summary>
        /// Warning text for a keyboard that matched HP's device table but has never been driven here,
        /// or empty when this exact device was confirmed on hardware. The editor is offered either
        /// way - the map comes from the device itself - but the user is told which footing they are
        /// on. Treating HP's table as measurement is what put a non-existent PID in the database.
        /// </summary>
        public string VerificationWarning =>
            !IsAvailable || (_keyboard?.IsVerifiedPerKeyDevice ?? false)
                ? string.Empty
                : $"This keyboard ({_keyboard?.PerKeyDeviceIdentity}) matches HP's device list for this " +
                  "protocol but has not been confirmed on hardware here. Per-key colour is expected to " +
                  "work and the layout is read from the keyboard itself, so it should be correct — but " +
                  "if anything misbehaves, that is worth reporting with this device id.";

        public bool HasVerificationWarning => VerificationWarning.Length > 0;

        /// <summary>Logical canvas size, in device-independent pixels.</summary>
        public double CanvasWidth { get; private set; }
        public double CanvasHeight { get; private set; }

        public string BrushColorHex
        {
            get => _brushColorHex;
            set
            {
                if (_brushColorHex == value) return;
                _brushColorHex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BrushColorBrush));
            }
        }

        public System.Windows.Media.SolidColorBrush BrushColorBrush => new(ParseColor(BrushColorHex));

        /// <summary>
        /// 0-100, applied as the LampArray per-lamp intensity channel on the next apply.
        ///
        /// The ONLY brightness lever this keyboard has: the MCU's own command is refused by this
        /// firmware at every value, and no device effect consumes the effect record's brightness
        /// field. So this scales a host-painted picture and nothing else - a running effect cannot be
        /// dimmed, which the UI says rather than implying otherwise.
        /// </summary>
        public int Brightness
        {
            get => _brightness;
            set
            {
                int clamped = Math.Clamp(value, 0, 100);
                if (_brightness == clamped) return;
                _brightness = clamped;
                OnPropertyChanged();
            }
        }

        public int SelectedCount => Keys.Count(k => k.IsSelected);

        public string Status
        {
            get => _status;
            private set { _status = value; OnPropertyChanged(); }
        }

        public ICommand PickColorCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand PaintSelectionCommand { get; }
        public ICommand ClearAllColorsCommand { get; }
        public ICommand ApplyCommand { get; }

        // ── Selection ──────────────────────────────────────────────────────────────

        /// <summary>Toggle one key: clicking a selected key deselects it.</summary>
        public void ToggleKey(KeyLampViewModel key)
        {
            key.IsSelected = !key.IsSelected;
            OnPropertyChanged(nameof(SelectedCount));
        }

        /// <summary>
        /// Select every key whose drawn rectangle INTERSECTS the band, not every key whose centre
        /// falls inside it - catching only enclosed centres is what users read as "it missed some".
        /// </summary>
        /// <param name="additive">True to add to the current selection, false to replace it.</param>
        public void SelectWithin(double x1, double y1, double x2, double y2, bool additive)
        {
            double left = Math.Min(x1, x2), right = Math.Max(x1, x2);
            double top = Math.Min(y1, y2), bottom = Math.Max(y1, y2);

            foreach (var key in Keys)
            {
                bool hit = key.X < right && key.X + key.Width > left &&
                           key.Y < bottom && key.Y + key.Height > top;

                if (hit) key.IsSelected = true;
                else if (!additive) key.IsSelected = false;
            }

            OnPropertyChanged(nameof(SelectedCount));
        }

        private void SetSelection(IEnumerable<KeyLampViewModel> keys, bool selected)
        {
            foreach (var key in keys) key.IsSelected = selected;
            OnPropertyChanged(nameof(SelectedCount));
        }

        // ── Colour ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Open the app's existing colour picker rather than a new one - a second picker would be a
        /// second set of swatches and hex parsing to keep consistent with this page.
        /// </summary>
        private void PickColor()
        {
            var dialog = new Views.ColorPickerDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            dialog.SetInitialColor(BrushColorHex);

            if (dialog.ShowDialog() == true && dialog.DialogResultOk)
            {
                BrushColorHex = dialog.SelectedHexColor;

                // Painting immediately is the point of picking a colour while keys are selected -
                // otherwise the picker sets a brush and appears to have done nothing.
                if (SelectedCount > 0) PaintSelection();
            }
        }

        /// <summary>
        /// Colour the selected keys, then CLEAR the selection. The white selection ring sits on top
        /// of the key's own colour, so leaving it up hides the colour just applied - which reads as
        /// "the paint button did nothing".
        /// </summary>
        private void PaintSelection()
        {
            var targets = Keys.Where(k => k.IsSelected).ToList();

            if (targets.Count == 0)
            {
                Status = "Nothing selected. Click keys, or drag a box across them.";
                return;
            }

            foreach (var key in targets)
            {
                key.ColorHex = BrushColorHex;
                key.IsSelected = false;
            }

            OnPropertyChanged(nameof(SelectedCount));
            Status = $"Painted {targets.Count} key(s) {BrushColorHex}. " +
                     "Keep going, then Apply to send the whole map to the keyboard.";
        }

        private void ClearAllColors()
        {
            foreach (var key in Keys) key.ColorHex = "#000000";
            Status = "All keys set to black. Apply to send it.";
        }

        private async Task ApplyAsync()
        {
            if (_keyboard == null || Keys.Count == 0) return;

            // One picture, two devices. The keys and the bar are edited together and sent apart,
            // because they are different hardware on different transports - HID lamps for the
            // keyboard, HP's WMI command or the bar's own LampArray for the bar.
            var colors = new Dictionary<ushort, System.Drawing.Color>();
            var barZones = new (byte R, byte G, byte B)[LightBarZoneCount];
            bool anyBar = false;

            foreach (var key in Keys)
            {
                var parsed = ParseColor(key.ColorHex);

                if (key.IsLightBar)
                {
                    barZones[key.ZoneIndex] = (parsed.R, parsed.G, parsed.B);
                    anyBar = true;
                    continue;
                }

                // Flattened back to lamps, because that is what the keyboard addresses. A wide key
                // contributes each of its lamps with the same colour.
                var color = ToDrawingColor(parsed);
                foreach (ushort lampId in key.LampIds) colors[lampId] = color;
            }

            // Brightness first: it is the intensity byte attached to each lamp in the write below,
            // so setting it after would need a second full repaint to take effect.
            await _keyboard.SetPerKeyBrightnessAsync(Brightness);

            bool keysOk = colors.Count == 0 || await _keyboard.SetKeyColorsAsync(colors);
            bool barOk = !anyBar || _keyboard.SetLightBarColors(barZones, (byte)Brightness);

            // "Accepted" is the honest word for the keyboard: there is no colour readback anywhere
            // in the LampArray spec, so the software cannot confirm a key changed - only that the
            // device took the report. The bar's colours ARE readable, but not through this call.
            var parts = new List<string>();
            if (colors.Count > 0) parts.Add($"{colors.Count} keys {(keysOk ? "accepted" : "REFUSED")}");
            if (anyBar) parts.Add($"4 bar zones {(barOk ? "accepted" : "REFUSED")}");

            Status = string.Join(", ", parts) + ". Only looking confirms it.";
            _logging.Info($"[KeyboardMap] Apply: {colors.Count} keys keysOk={keysOk}, " +
                          $"bar={anyBar} barOk={barOk}");
        }

        // ── Loading ────────────────────────────────────────────────────────────────

        private void Load()
        {
            var map = _keyboard?.GetMeasuredKeyMap() ?? Array.Empty<HidLampArray.LampInfo>();
            if (map.Count == 0)
            {
                Status = "No measured key map on this machine.";
                return;
            }

            // Lay out from the lamps' own coordinates, normalised so the leftmost/topmost key sits
            // at the canvas edge. Absolute device coordinates would leave whatever margin the
            // firmware happens to report, which differs per model.
            uint minX = map.Min(l => l.XMicrometres), maxX = map.Max(l => l.XMicrometres);
            uint minY = map.Min(l => l.YMicrometres), maxY = map.Max(l => l.YMicrometres);

            double keySize = KeySizeMm * PixelsPerMm;
            CanvasWidth = ((maxX - minX) / 1000.0 * PixelsPerMm) + keySize;
            CanvasHeight = ((maxY - minY) / 1000.0 * PixelsPerMm) + keySize;

            // Snap every key in a row to one Y. Lamp centres within a row differ by a fraction of a
            // millimetre - real manufacturing placement - and drawn faithfully that reads as a
            // wobbly keyboard rather than as precision.
            var rowTops = SnapRows(map);

            var keyGroups = GroupLampsIntoKeys(map);

            for (int i = 0; i < keyGroups.Count; i++)
            {
                var group = keyGroups[i];
                double first = (group[0].XMicrometres - minX) / 1000.0 * PixelsPerMm;
                double last = (group[^1].XMicrometres - minX) / 1000.0 * PixelsPerMm;

                // Width runs to the NEXT key in the same row, not a fixed key size. This is what
                // fixes the crushed left-hand columns: the keyboard reports filler lamps between
                // real keys - usage 0x03 at 9 mm spacing - which at a full 15 mm cap overlapped
                // their neighbours three deep. Measuring to the next key gives each lamp the room it
                // actually occupies, so a filler renders as a sliver and Space still spans five.
                double available = keySize;
                if (i + 1 < keyGroups.Count)
                {
                    var next = keyGroups[i + 1];
                    bool sameRow = rowTops[next[0].YMicrometres] == rowTops[group[0].YMicrometres];

                    // Two keys at the SAME x are the half-height arrow stack: Up and Down share one
                    // reported position. They are split vertically below, so neither constrains the
                    // other's width.
                    bool stacked = sameRow && next[0].XMicrometres == group[^1].XMicrometres;

                    if (sameRow && !stacked)
                    {
                        double nextX = (next[0].XMicrometres - minX) / 1000.0 * PixelsPerMm;
                        available = Math.Max(MinKeyWidth, Math.Min(keySize, nextX - last));
                    }
                }

                // Half-height keys, for the inverted-T arrow cluster: Up and Down occupy one cell,
                // stacked. Drawing them side by side at full height is what produced an overlap.
                bool sharesCellWithPrevious = i > 0 &&
                    keyGroups[i - 1][^1].XMicrometres == group[0].XMicrometres &&
                    rowTops[keyGroups[i - 1][0].YMicrometres] == rowTops[group[0].YMicrometres];
                bool sharesCellWithNext = i + 1 < keyGroups.Count &&
                    keyGroups[i + 1][0].XMicrometres == group[^1].XMicrometres &&
                    rowTops[keyGroups[i + 1][0].YMicrometres] == rowTops[group[0].YMicrometres];

                bool halfHeight = sharesCellWithPrevious || sharesCellWithNext;
                double height = halfHeight ? keySize / 2 : keySize;
                double yNudge = sharesCellWithPrevious ? keySize / 2 : 0;

                Keys.Add(new KeyLampViewModel
                {
                    LampIds = group.Select(l => l.LampId).ToArray(),
                    KeyUsage = group[0].KeyUsage,
                    Label = KeyName(group[0].KeyUsage),

                    // A wide key spans from the first lamp's box to the last one's, so Shift draws
                    // as one long cap rather than three overlapping squares.
                    X = first,
                    Y = ((rowTops[group[0].YMicrometres] - minY) / 1000.0 * PixelsPerMm) + yNudge,
                    Width = last - first + available,
                    Height = height,

                    // Keys start at the BRUSH colour, not black. Apply sends every key, so a black
                    // default means painting WASD red and applying blanks the rest of the keyboard -
                    // which reads as "it does not work". There is no colour readback here, so the
                    // true state cannot be loaded; the brush is the honest default because it is the
                    // one colour the user has already chosen.
                    ColorHex = BrushColorHex
                });
            }

            int keyCount = Keys.Count;
            AddLightBarZones(keySize);

            Status = HasLightBar
                ? $"{keyCount} keys and 4 light bar zones. Click to select, or drag a box. Hold Ctrl to add."
                : $"{keyCount} keys. Click to select, or drag a box. Hold Ctrl to add.";

            _logging.Info($"[KeyboardMap] Loaded {keyCount} keys from the measured lamp map" +
                          (HasLightBar ? ", plus 4 light bar zones" : string.Empty));
        }

        /// <summary>
        /// Collect the lamps into physical KEYS - the difference between a keyboard and a scatter
        /// plot. The device reports lamps, not keys, and a wide key carries several: Space has five
        /// on 8D87, both Shifts three, Caps and Tab two. Their centres are 7 to 11 mm apart, closer
        /// than one key is wide, so a cap per lamp drew three overlapping squares where the keyboard
        /// has one long one.
        ///
        /// Lamps join a key when they agree on all three of usage, row, and adjacency. All three are
        /// needed - usage alone merges the two Enter keys either side of the numpad, adjacency alone
        /// merges neighbouring letters. Usage 0 lamps are NEVER merged: "no key reported" is not
        /// evidence two lamps belong together, and on this board each is independently addressable.
        /// </summary>
        internal static List<List<HidLampArray.LampInfo>> GroupLampsIntoKeys(
            IReadOnlyList<HidLampArray.LampInfo> map)
        {
            // A row is a band of Y, not an exact value: lamps under one key row can differ by a
            // fraction of a millimetre. A key is ~15 mm tall, so half that separates rows safely.
            const uint RowToleranceUm = 7_000;

            // Two lamps under one key cap are at most a key-width apart. Beyond that they are
            // separate keys that happen to share a usage - the two Enters, the two Shifts.
            const uint AdjacencyLimitUm = 20_000;

            var ordered = map.OrderBy(l => l.YMicrometres).ThenBy(l => l.XMicrometres).ToList();
            var keys = new List<List<HidLampArray.LampInfo>>();

            foreach (var lamp in ordered)
            {
                var current = keys.Count > 0 ? keys[^1] : null;
                var previous = current?[^1];

                bool joins = current != null && previous.HasValue
                    && lamp.KeyUsage != 0
                    && lamp.KeyUsage == previous.Value.KeyUsage
                    && AbsDiff(lamp.YMicrometres, previous.Value.YMicrometres) <= RowToleranceUm
                    && AbsDiff(lamp.XMicrometres, previous.Value.XMicrometres) <= AdjacencyLimitUm;

                if (joins) current!.Add(lamp);
                else keys.Add(new List<HidLampArray.LampInfo> { lamp });
            }

            return keys;
        }

        private static uint AbsDiff(uint a, uint b) => a > b ? a - b : b - a;

        /// <summary>
        /// Map each lamp's reported Y to a single Y shared by its whole row.
        ///
        /// Rows are found by gap rather than by a fixed count: the row COUNT differs by model, and
        /// hard-coding six would silently mis-group anything else. Clustering on the gap between
        /// sorted Y values works wherever rows are further apart than the wobble within one.
        /// </summary>
        private static Dictionary<uint, uint> SnapRows(IReadOnlyList<HidLampArray.LampInfo> map)
        {
            const uint RowGapUm = 7_000;

            var distinct = map.Select(l => l.YMicrometres).Distinct().OrderBy(y => y).ToList();
            var snapped = new Dictionary<uint, uint>();

            uint rowTop = distinct[0];
            uint previous = distinct[0];

            foreach (uint y in distinct)
            {
                if (y - previous > RowGapUm) rowTop = y;
                snapped[y] = rowTop;
                previous = y;
            }

            return snapped;
        }

        /// <summary>
        /// Lay the light bar's four zones across the full canvas width, below the keyboard.
        ///
        /// Drawn to scale with the keys rather than as a separate widget, because that is what the
        /// hardware looks like - a 323 mm strip under a 342 mm keyboard - and because it puts the
        /// zones inside the same selection model, so one drag and one picker cover both devices.
        /// </summary>
        private void AddLightBarZones(double keySize)
        {
            if (!HasLightBar) return;

            double gap = keySize * 0.35;
            double top = CanvasHeight + gap;
            double zoneWidth = (CanvasWidth - (gap * (LightBarZoneCount - 1))) / LightBarZoneCount;
            double zoneHeight = keySize * 0.75;

            var current = _keyboard?.GetLightBarColors() ?? Array.Empty<(byte R, byte G, byte B)?>();

            for (int zone = 0; zone < LightBarZoneCount; zone++)
            {
                // The bar's colours ARE readable, unlike the keyboard's, so the zones start at what
                // the hardware is showing rather than at the brush colour. That makes the editor
                // open as a picture of the current state for the half of it that can be read.
                string colour = zone < current.Length && current[zone] != null
                    ? $"#{current[zone]!.Value.R:X2}{current[zone]!.Value.G:X2}{current[zone]!.Value.B:X2}"
                    : BrushColorHex;

                Keys.Add(new KeyLampViewModel
                {
                    IsLightBar = true,
                    ZoneIndex = zone,
                    Label = $"Bar {zone + 1}",
                    X = zone * (zoneWidth + gap),
                    Y = top,
                    Width = zoneWidth,
                    Height = zoneHeight,
                    ColorHex = colour
                });
            }

            CanvasHeight = top + zoneHeight;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static System.Drawing.Color ToDrawingColor(System.Windows.Media.Color c) =>
            System.Drawing.Color.FromArgb(c.R, c.G, c.B);

        private static System.Windows.Media.Color ParseColor(string hex)
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    hex.StartsWith('#') ? hex : "#" + hex);
                return c;
            }
            catch
            {
                return System.Windows.Media.Colors.Black;
            }
        }

        /// <summary>
        /// A readable name for a HID keyboard usage, abbreviated to fit a key cap. Three characters
        /// or so on purpose: the cap is drawn at the key's real size, so an overflowing label gets
        /// trimmed anyway and choosing the abbreviation beats letting the renderer pick it. An
        /// unnamed usage renders blank rather than as truncated hex; the full value is in the
        /// tooltip, which is enough to match a key against the probe's --map output.
        /// </summary>
        internal static string KeyName(byte usage) => usage switch
        {
            0x00 => "",
            >= 0x04 and <= 0x1D => ((char)('A' + (usage - 0x04))).ToString(),
            >= 0x1E and <= 0x26 => ((char)('1' + (usage - 0x1E))).ToString(),
            0x27 => "0",
            0x28 => "Ent",
            0x29 => "Esc",
            0x2A => "Bsp",
            0x2B => "Tab",
            0x2C => "Space",
            0x2D => "-",
            0x2E => "=",
            0x2F => "[",
            0x30 => "]",
            0x31 => "\\",
            0x33 => ";",
            0x34 => "'",
            0x35 => "`",
            0x36 => ",",
            0x37 => ".",
            0x38 => "/",
            0x39 => "Cap",
            >= 0x3A and <= 0x45 => "F" + (usage - 0x39),
            0x46 => "PrtSc",
            0x47 => "ScrLk",
            0x48 => "Pause",
            0x49 => "Ins",
            0x4A => "Home",
            0x4B => "PgUp",
            0x4C => "Del",
            0x4D => "End",
            0x4E => "PgDn",
            0x4F => "→",
            0x50 => "←",
            0x51 => "↓",
            0x52 => "↑",
            0x53 => "Num",
            0x54 => "Num /",
            0x55 => "Num *",
            0x56 => "Num -",
            0x57 => "Num +",
            0x58 => "Num ⏎",
            >= 0x59 and <= 0x61 => "Num " + (usage - 0x58),
            0x62 => "Num 0",
            0x63 => "Num .",
            0x65 => "Menu",
            0xE0 => "Ctrl",
            0xE1 => "Shift",
            0xE2 => "Alt",
            0xE3 => "Win",
            0xE4 => "RCtrl",
            0xE5 => "RShift",
            0xE6 => "RAlt",
            _ => "·"
        };
    }

    /// <summary>One addressable key: where it is, what it lights, and what colour it holds.</summary>
    public class KeyLampViewModel : ViewModelBase
    {
        private bool _isSelected;
        private string _colorHex = "#000000";

        /// <summary>
        /// Every lamp under this key. Usually one, but a wide key carries several - Space has five
        /// on 8D87 - and colouring the key means colouring all of them.
        /// </summary>
        public IReadOnlyList<ushort> LampIds { get; init; } = Array.Empty<ushort>();

        public byte KeyUsage { get; init; }
        public string Label { get; init; } = string.Empty;

        /// <summary>
        /// True for a light bar zone rather than a key.
        ///
        /// The zones live in the same collection as the keys on purpose: selection, painting and the
        /// colour picker then work across both with no second implementation, and a drag can span
        /// the keyboard and the bar in one gesture. Only <see cref="ApplyAsync"/> cares about the
        /// difference, because the two go to different devices over different transports.
        /// </summary>
        public bool IsLightBar { get; init; }

        /// <summary>Light bar zone index, 0 leftmost. Meaningless unless <see cref="IsLightBar"/>.</summary>
        public int ZoneIndex { get; init; }

        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }

        /// <summary>
        /// Carries the full identity, since the cap only has room for an abbreviation. This is what
        /// makes an unnamed key usable: the dot on the cap says nothing, the tooltip says exactly
        /// which lamp and usage it is.
        /// </summary>
        public string Tooltip
        {
            get
            {
                if (IsLightBar)
                    return $"Light bar zone {ZoneIndex + 1} of 4, left to right";

                string lamps = LampIds.Count == 1
                    ? $"lamp {LampIds[0]}"
                    : $"lamps {string.Join(", ", LampIds)}";

                return KeyUsage == 0
                    ? $"{lamps} — no key bound"
                    : $"{lamps} — usage 0x{KeyUsage:X2}{(Label == "·" ? " (unnamed)" : $" ({Label})")}";
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
        }

        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (_colorHex == value) return;
                _colorHex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(KeyBrush));
                OnPropertyChanged(nameof(LabelBrush));
            }
        }

        public System.Windows.Media.SolidColorBrush KeyBrush
        {
            get
            {
                try
                {
                    return new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ColorHex));
                }
                catch
                {
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);
                }
            }
        }

        /// <summary>
        /// Black text on a light key, white on a dark one. A single fixed label colour makes the
        /// caps unreadable at one end of the range or the other, and this editor spends most of its
        /// time at the dark end.
        /// </summary>
        public System.Windows.Media.SolidColorBrush LabelBrush
        {
            get
            {
                var c = KeyBrush.Color;
                double luminance = ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;
                return new System.Windows.Media.SolidColorBrush(
                    luminance > 0.55 ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.White);
            }
        }
    }
}
