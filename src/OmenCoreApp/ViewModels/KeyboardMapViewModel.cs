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
    /// Per-LED editor backed by the keyboard's own layout - every light the colour map can address,
    /// at the position HP's layout table puts it, rather than a grid of assumed shape.
    ///
    /// IT IS BUILT FROM THE LAYOUT, NOT FROM LAMPARRAY, and the difference is the whole point. The
    /// device's LampArray reports 120 lamps on 8D87; the MCU colour map has 176 LEDs. They are not
    /// the same enumeration and they do not nest: an F key is one lamp but two LEDs, Num0 is two
    /// lamps but three LEDs, and the Omen, Calculator, Settings, Power, Fn and Copilot keys are LEDs
    /// with no lamp at all. Drawing from lamps therefore hid one LED of every dual-legend key, drew
    /// Num0 as two cells that fought over the same three LEDs, and omitted eight keys entirely.
    /// Where no layout is known the lamp map is still used, because a coarse editor beats none.
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

        /// <summary>
        /// Where the picture is remembered between runs. Optional: the editor is fully functional
        /// without it and simply forgets, which is what the tests exercise.
        /// </summary>
        private readonly ConfigurationService? _configService;

        // Layout scale. The view scales the canvas to fit, so PixelsPerMm only sets the resolution
        // the layout is computed at. The device reports lamp CENTRES and no extents, so a key has to
        // be given a size: 15 mm sits under the ~18 mm pitch, leaving a gap between neighbours
        // instead of a solid sheet. MinKeyWidth keeps the ~9 mm filler lamps clickable.
        private const double PixelsPerMm = 2.6;
        private const double KeySizeMm = 15.0;
        private const double MinKeyWidth = 14.0;

        // Layout mode. The table's units are HP's own UI pixels, which differ per keyboard, so the
        // board is normalised to its bounds and scaled to a fixed width - matching what the lamp
        // path produces, so switching between them does not resize the window. KeyInset leaves a
        // hairline between neighbouring caps; the rectangles butt up against each other exactly.
        private const double TargetCanvasWidth = 900.0;
        private const double KeyInset = 1.0;

        private string _brushColorHex = "#FF0000";
        private string _status = string.Empty;
        private int _brightness = 100;

        public KeyboardMapViewModel(KeyboardLightingService? keyboard, LoggingService logging,
                                    ConfigurationService? configService = null)
        {
            _keyboard = keyboard;
            _logging = logging;
            _configService = configService;

            PickColorCommand = new RelayCommand(_ => PickColor());
            SelectAllCommand = new RelayCommand(_ => SetSelection(Keys, true));
            ClearSelectionCommand = new RelayCommand(_ => SetSelection(Keys, false));
            PaintSelectionCommand = new RelayCommand(_ => PaintSelection());
            ClearAllColorsCommand = new RelayCommand(_ => ClearAllColors());
            ApplyCommand = new AsyncRelayCommand(async _ => await ApplyAsync(), _ => IsAvailable);
            OpenDynamicLightingSettingsCommand = new RelayCommand(_ => OpenDynamicLightingSettings());
            SaveToKeyboardCommand = new AsyncRelayCommand(async _ => await SaveToKeyboardAsync(), _ => IsAvailable);

            Load();

            // After Load, because it paints onto cells that Load creates. A saved picture for a
            // different layout simply finds no matching cells and leaves the editor at its default.
            RestoreSavedPicture();
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

        /// <summary>
        /// Warning text when Windows Dynamic Lighting is going to repaint this keyboard the moment
        /// OmenCore lets go of it, or empty when it will not.
        ///
        /// THIS IS THE MOST CONFUSING FAILURE THIS EDITOR HAS, because nothing goes wrong. Every
        /// write is accepted, the picture appears exactly as painted, and it holds for as long as
        /// the app is open. Then the app closes, Windows takes the device back, and the keyboard is
        /// its accent colour again — which reads as "OmenCore does not save my colours" and is
        /// really two features owning one device with no arbitration between them.
        ///
        /// Told rather than fixed, deliberately. Dynamic Lighting is Windows' feature and the toggle
        /// is the user's; silently turning off another feature's setting to make ours look better is
        /// not a trade this application gets to make on its own. See
        /// <see cref="OpenDynamicLightingSettingsCommand"/>, which takes them to the switch.
        /// </summary>
        public string DynamicLightingWarning
        {
            get
            {
                if (!IsAvailable) return string.Empty;

                var state = _keyboard?.GetDynamicLightingState();
                if (state == null || !state.WillRepaintWhenReleased) return string.Empty;

                return "Windows Dynamic Lighting is switched on for this keyboard. Your colours will " +
                       "look right while OmenCore is open, and Windows will repaint the keyboard as " +
                       "soon as it closes. Turn Dynamic Lighting off for this device to keep them.";
            }
        }

        public bool HasDynamicLightingWarning => DynamicLightingWarning.Length > 0;

        /// <summary>
        /// Re-read the Dynamic Lighting settings and update the banner.
        ///
        /// Worth calling whenever the editor comes back into view: the likeliest reason a user left
        /// it was to go and change the setting the banner just told them about, and a banner still
        /// saying so on their return would be its own small bug.
        /// </summary>
        public void RefreshDynamicLighting()
        {
            OnPropertyChanged(nameof(DynamicLightingWarning));
            OnPropertyChanged(nameof(HasDynamicLightingWarning));
        }

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
        /// Master brightness, 0-100, applied to the whole picture on the next apply. The per-cell
        /// half is <see cref="SelectionBrightness"/>, and the two multiply.
        ///
        /// The ONLY brightness lever this keyboard has, and it reaches host-painted colour only.
        /// Measured on 8D87: the MCU's own command 0x0C is refused at every payload value, and the
        /// effect record's brightness field is ignored at 0, 60 and 160 alike - sent through frames
        /// the MCU demonstrably consumed, and read back unchanged every time. So a running device
        /// effect cannot be dimmed, which the UI says rather than implying otherwise.
        ///
        /// This one is scaled by the BACKEND, not here. Doing it here would dim the picture the
        /// editor is holding, so the swatches would drift darker every time Apply was pressed.
        /// <see cref="KeyLampViewModel.Level"/> is scaled here instead, and safely, because it is a
        /// separate field rather than a rewrite of the colour the user picked.
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
                OnPropertyChanged(nameof(BrightnessHint));
            }
        }

        public int SelectedCount => Keys.Count(k => k.IsSelected);

        /// <summary>Whether the per-selection brightness slider has anything to act on.</summary>
        public bool HasSelection => SelectedCount > 0;

        /// <summary>
        /// What the two sliders are doing right now, in words, because their product is not obvious
        /// from two numbers sitting side by side. Says the arithmetic when a selection is dimmed and
        /// stays out of the way when it is not.
        /// </summary>
        public string BrightnessHint
        {
            get
            {
                const string always = " A running keyboard effect cannot be dimmed — the MCU draws those itself.";

                if (!HasSelection)
                    return "Applies on the next Apply. Select keys to dim them independently." + always;

                int selection = SelectionBrightness;
                if (selection >= 100)
                    return $"{SelectedCount} selected, at full. Applies on the next Apply." + always;

                return $"{SelectedCount} selected at {selection}% — they land at " +
                       $"{Brightness * selection / 100}% once the master's {Brightness}% is applied." + always;
            }
        }

        /// <summary>
        /// The selection's own brightness, 0-100, multiplied with <see cref="Brightness"/> on the way
        /// to the keyboard. This is the per-LED half of brightness; <see cref="Brightness"/> is the
        /// whole-picture half.
        ///
        /// Reads back the level the selection AGREES on, and 100 when it does not. A mixed selection
        /// has no single level to show, and showing one key's value would make the slider lie about
        /// the other eleven - but the control still has to sit somewhere, and the neutral end is the
        /// only position that is not a claim about the selection.
        ///
        /// Writing applies to every selected cell, light bar zones included. The bar's firmware
        /// brightness is one byte for all four zones, so per-zone dimming there is the same host-side
        /// arithmetic as the keys - see <see cref="ApplyAsync"/>.
        /// </summary>
        public int SelectionBrightness
        {
            get
            {
                var levels = Keys.Where(k => k.IsSelected).Select(k => k.Level).Distinct().Take(2).ToList();
                return levels.Count == 1 ? levels[0] : 100;
            }
            set
            {
                int clamped = Math.Clamp(value, 0, 100);
                var targets = Keys.Where(k => k.IsSelected).ToList();
                if (targets.Count == 0) return;

                foreach (var key in targets) key.Level = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BrightnessHint));
            }
        }

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

        /// <summary>Opens the Windows Dynamic Lighting settings page. Shown with the warning banner.</summary>
        public ICommand OpenDynamicLightingSettingsCommand { get; }

        /// <summary>
        /// Writes the current picture into the keyboard's own flash so it survives a power cycle.
        ///
        /// SEPARATE FROM APPLY ON PURPOSE. Apply is volatile and costs nothing to repeat; this is an
        /// MCU flash write. HP's own client flashes on every per-key apply, which is what makes a
        /// picture set in OGH survive a reboot — but in an editor where a user drags a brightness
        /// slider, "every apply" is a flash write per drag, and flash has a finite write count.
        /// One button, one write, one intent.
        /// </summary>
        public ICommand SaveToKeyboardCommand { get; }

        // ── Selection ──────────────────────────────────────────────────────────────

        /// <summary>Toggle one key: clicking a selected key deselects it.</summary>
        public void ToggleKey(KeyLampViewModel key)
        {
            key.IsSelected = !key.IsSelected;
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionBrightness));
            OnPropertyChanged(nameof(BrightnessHint));
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
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionBrightness));
            OnPropertyChanged(nameof(BrightnessHint));
        }

        private void SetSelection(IEnumerable<KeyLampViewModel> keys, bool selected)
        {
            foreach (var key in keys) key.IsSelected = selected;
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionBrightness));
            OnPropertyChanged(nameof(BrightnessHint));
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
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionBrightness));
            OnPropertyChanged(nameof(BrightnessHint));
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
            var leds = new Dictionary<int, System.Drawing.Color>();
            var barZones = new (byte R, byte G, byte B)[LightBarZoneCount];
            bool anyBar = false;

            foreach (var key in Keys)
            {
                // The cell's own level, applied here. The master is NOT applied here - the backend
                // does that on its way to the wire, so this stays a picture rather than a dimmed
                // copy of one. Effective brightness is the product of the two.
                var parsed = ScaleForLevel(ParseColor(key.ColorHex), key.Level);

                if (key.IsLightBar)
                {
                    // HP's light bar frame carries ONE brightness byte for all four zones, so a
                    // per-zone level cannot go to the firmware and has to be arithmetic on the
                    // colour, exactly as it is for the keys. The master still rides the byte.
                    barZones[key.ZoneIndex] = (parsed.R, parsed.G, parsed.B);
                    anyBar = true;
                    continue;
                }

                var color = ToDrawingColor(parsed);

                // Layout mode addresses the colour map directly, one position per cell. That is what
                // lets the two halves of a dual-legend key differ - flattening to lamps cannot, since
                // both halves are one lamp.
                if (key.IsLed)
                {
                    foreach (int position in key.LedPositions) leds[position] = color;
                    continue;
                }

                // Fallback: flattened back to lamps, because that is what the keyboard addresses.
                // A wide key contributes each of its lamps with the same colour.
                foreach (ushort lampId in key.LampIds) colors[lampId] = color;
            }

            // Brightness first: the backend applies it to the colour writes below, so setting it
            // after would need a second full repaint to take effect.
            bool brightnessOk = await _keyboard.SetPerKeyBrightnessAsync(Brightness);

            // Black, not the brush: every cell in the editor is in the dictionary, so the background
            // only reaches positions the layout does not claim - padding, and anything the table
            // omits. Painting those with a colour would light bytes nobody chose.
            bool ledsOk = leds.Count == 0 ||
                          await _keyboard.SetLedColorsAsync(leds, System.Drawing.Color.Black);
            bool keysOk = colors.Count == 0 || await _keyboard.SetKeyColorsAsync(colors);
            bool barOk = !anyBar || _keyboard.SetLightBarColors(barZones, (byte)Brightness);

            // "Accepted" is the honest word for the keyboard: there is no colour readback anywhere
            // in the LampArray spec, so the software cannot confirm a key changed - only that the
            // device took the report. The bar's colours ARE readable, but not through this call.
            var parts = new List<string>();
            if (leds.Count > 0) parts.Add($"{leds.Count} LEDs {(ledsOk ? "accepted" : "REFUSED")}");
            if (colors.Count > 0) parts.Add($"{colors.Count} keys {(keysOk ? "accepted" : "REFUSED")}");
            if (anyBar) parts.Add($"4 bar zones {(barOk ? "accepted" : "REFUSED")}");

            Status = string.Join(", ", parts) + ". Only looking confirms it.";

            // A backend that cannot take brightness sends the colours at full strength, and the
            // slider sitting at 20 while the keyboard blazes is exactly the silent no-op this fix
            // was for. Say it happened rather than leaving the user to infer it from the light.
            if (!brightnessOk && Brightness < 100)
                Status += $" Brightness {Brightness} was NOT applied - this keyboard's backend has no brightness lever.";

            _logging.Info($"[KeyboardMap] Apply: {leds.Count} LEDs ledsOk={ledsOk}, " +
                          $"{colors.Count} keys keysOk={keysOk}, bar={anyBar} barOk={barOk}, " +
                          $"brightness={Brightness} brightnessOk={brightnessOk}");

            // The setting can change while the editor is open, and this is the moment the user is
            // most likely to be looking for an explanation of what they just saw.
            RefreshDynamicLighting();

            // Saved even when the keyboard refused: the editor should come back as the user left
            // it, and a refusal is a hardware problem rather than a reason to forget their work.
            SaveSavedPicture();
        }

        /// <summary>
        /// Send the picture the editor was restored with to the keyboard.
        ///
        /// For startup only. The constructor restores the cells but deliberately sends nothing;
        /// this is the caller that decides the keyboard should be repainted, which is a user
        /// preference rather than a property of loading the editor.
        /// </summary>
        public Task ApplyRestoredPictureAsync() => ApplyAsync();

        /// <summary>
        /// Write the current picture to <c>config.json</c>.
        ///
        /// Called after a successful apply rather than on every brush stroke: what is worth
        /// remembering is what the keyboard was actually asked to show, and saving mid-edit would
        /// restore a half-painted picture on the next launch.
        /// </summary>
        private void SaveSavedPicture()
        {
            if (_configService?.Config == null) return;

            try
            {
                var settings = _configService.Config.KeyboardLighting ??= new Models.KeyboardLightingSettings();

                settings.PerKeyPicture = Keys.Select(k => new Models.SavedPerKeyCell
                {
                    Led = k.IsLed ? k.LedPositions[0] : -1,
                    Lamp = !k.IsLed && !k.IsLightBar && k.LampIds.Count > 0 ? k.LampIds[0] : -1,
                    Zone = k.IsLightBar ? k.ZoneIndex : -1,
                    Color = k.ColorHex,
                    Level = k.Level
                }).ToList();

                settings.PerKeyBrightness = Brightness;

                _configService.Save(_configService.Config);
                _logging.Info($"[KeyboardMap] Saved {settings.PerKeyPicture.Count} cells at master {Brightness}");
            }
            catch (Exception ex)
            {
                // Losing the saved copy is not worth failing an apply that already reached the
                // keyboard. The picture is on the hardware either way.
                _logging.Warn($"[KeyboardMap] Could not save the picture: {ex.Message}");
            }
        }

        /// <summary>
        /// Paint the saved picture back onto the editor's cells.
        ///
        /// Does NOT send anything to the keyboard. Restoring the editor and repainting the hardware
        /// are different decisions - the keyboard may well already be showing this picture, from its
        /// own flash or from the previous run - and sending on construction would repaint over a
        /// device effect the user chose since.
        /// </summary>
        private void RestoreSavedPicture()
        {
            var saved = _configService?.Config?.KeyboardLighting?.PerKeyPicture;
            if (saved == null || saved.Count == 0 || Keys.Count == 0) return;

            try
            {
                var byKey = new Dictionary<string, Models.SavedPerKeyCell>();
                foreach (var cell in saved)
                {
                    if (cell.Key.Length > 0) byKey[cell.Key] = cell;
                }

                int restored = 0;
                foreach (var key in Keys)
                {
                    if (!byKey.TryGetValue(CellKey(key), out var cell)) continue;

                    key.ColorHex = cell.Color;
                    key.Level = Math.Clamp(cell.Level, 0, 100);
                    restored++;
                }

                Brightness = Math.Clamp(
                    _configService?.Config?.KeyboardLighting?.PerKeyBrightness ?? 100, 0, 100);

                if (restored > 0)
                    Status = $"Restored the last picture ({restored} cells). " +
                             "Apply to send it to the keyboard.";

                _logging.Info($"[KeyboardMap] Restored {restored} of {saved.Count} saved cells");
            }
            catch (Exception ex)
            {
                _logging.Warn($"[KeyboardMap] Could not restore the saved picture: {ex.Message}");
            }
        }

        /// <summary>
        /// The stored identity of one editor cell. Must agree with what <see cref="SaveSavedPicture"/>
        /// writes, which is why both go through <see cref="Models.SavedPerKeyCell.Key"/>'s format.
        /// </summary>
        internal static string CellKey(KeyLampViewModel key) =>
            key.IsLightBar ? $"zone:{key.ZoneIndex}" :
            key.IsLed ? $"led:{key.LedPositions[0]}" :
            key.LampIds.Count > 0 ? $"lamp:{key.LampIds[0]}" :
            string.Empty;

        /// <summary>
        /// Opens Settings straight at Dynamic Lighting.
        ///
        /// <c>UseShellExecute</c> is required: <c>ms-settings:</c> is a protocol handler, not an
        /// executable, and the default for <c>ProcessStartInfo</c> on .NET Core is false — which
        /// fails with "The system cannot find the file specified" and looks like a missing
        /// Settings app rather than a missing flag.
        /// </summary>
        private void OpenDynamicLightingSettings()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:personalization-lighting",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // Not fatal: the banner already says what to do, this only saves them the clicks.
                _logging.Warn($"[KeyboardMap] Could not open the Dynamic Lighting settings page: {ex.Message}");
                Status = "Could not open Settings. It is under " +
                         "Settings > Personalisation > Dynamic Lighting.";
            }
        }

        /// <summary>
        /// Send the picture, then persist it in the keyboard's own flash.
        ///
        /// Applies first rather than flashing whatever happens to be installed, so that what is
        /// persisted is what the editor is showing. Flashing without applying would save the
        /// PREVIOUS picture and report success, which is the worst of both.
        /// </summary>
        private async Task SaveToKeyboardAsync()
        {
            if (_keyboard == null || Keys.Count == 0) return;

            await ApplyAsync();

            bool flashed = await _keyboard.StoreDeviceLightingToFlashAsync();

            Status = flashed
                ? "Saved to the keyboard. This picture now survives a power cycle, and shows with " +
                  "OmenCore closed."
                : "Applied, but the keyboard refused the save-to-flash — the picture is showing now " +
                  "and will not survive a power cycle.";

            // Not a claim that the colours are right: the flash write is acknowledged over the same
            // interface that cannot read a colour back. It says the MCU took the store command.
            _logging.Info($"[KeyboardMap] Save to keyboard flash: {(flashed ? "accepted" : "REFUSED")}");
        }

        // ── Loading ────────────────────────────────────────────────────────────────

        private void Load()
        {
            if (LoadFromLayout()) return;

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
        /// Draw the board from the layout table: one cell per LED, grouped inside its key's cap.
        /// False when this board has no layout, which sends the caller to the lamp map instead.
        /// </summary>
        private bool LoadFromLayout()
        {
            var layout = _keyboard?.GetKeyboardLayout();
            if (layout == null || layout.Keys.Count == 0 || layout.Bounds.Length != 4) return false;

            double left = layout.Bounds[0], top = layout.Bounds[1];
            double spanX = Math.Max(1, layout.Bounds[2] - left);
            double spanY = Math.Max(1, layout.Bounds[3] - top);

            // One scale for both axes, so the keyboard keeps its proportions instead of being
            // stretched to whatever aspect the canvas happens to have.
            double scale = TargetCanvasWidth / spanX;
            CanvasWidth = spanX * scale;
            CanvasHeight = spanY * scale;

            int ledCells = 0;

            foreach (var key in layout.Keys)
            {
                if (key.Rect.Length != 4 || key.Leds.Length == 0) continue;

                double x = (key.Rect[0] - left) * scale;
                double y = (key.Rect[1] - top) * scale;
                double w = Math.Max(1, (key.Rect[2] * scale) - KeyInset);
                double h = Math.Max(1, (key.Rect[3] * scale) - KeyInset);

                bool horizontal = SplitsHorizontally(key.Leds.Length, key.Rect[2], key.Rect[3]);
                int n = key.Leds.Length;

                for (int i = 0; i < n; i++)
                {
                    // Sub-cells are laid out in COLOUR-MAP ORDER along the cap, and that order is
                    // not a physical position - the layout table carries no sub-key geometry at all.
                    // So cell 1 is the key's first LED, not its left or top one. On Esc the first is
                    // the one under the legend and the second the one below it; nothing guarantees
                    // another row runs the same way. Light one and look.
                    double cx = horizontal ? x + (w * i / n) : x;
                    double cy = horizontal ? y : y + (h * i / n);
                    double cw = horizontal ? w / n : w;
                    double ch = horizontal ? h : h / n;

                    Keys.Add(new KeyLampViewModel
                    {
                        LedPositions = new[] { key.Leds[i] },
                        HpKeyName = key.Name,
                        LedOrdinal = i + 1,
                        LedCount = n,

                        // Only the first sub-cell is captioned. A cap split five ways is a few
                        // pixels wide and five copies of "Space" is unreadable; the tooltip names
                        // the key and the exact map position for every cell.
                        Label = i == 0 ? (key.Label ?? ShortName(key.Name)) : string.Empty,

                        X = cx,
                        Y = cy,
                        Width = cw,
                        Height = ch,
                        ColorHex = BrushColorHex
                    });

                    ledCells++;
                }
            }

            if (ledCells == 0) return false;

            AddLightBarZones(KeySizeMm * PixelsPerMm);

            string verified = layout.Verified
                ? string.Empty
                : " This layout has not been confirmed on hardware.";

            Status = $"{ledCells} LEDs across {layout.Keys.Count} keys ({layout.Id})" +
                     (HasLightBar ? " and 4 light bar zones." : ".") +
                     " Click to select, or drag a box. Hold Ctrl to add." + verified;

            _logging.Info($"[KeyboardMap] Loaded {ledCells} LED cells from layout {layout.Id} " +
                          $"({layout.Keys.Count} keys, verified={layout.Verified})" +
                          (HasLightBar ? ", plus 4 light bar zones" : string.Empty));

            return true;
        }

        /// <summary>
        /// Which way a key's LEDs are drawn across its cap.
        ///
        /// TWO LEDs ALWAYS STACK. A two-LED key is a dual-legend key - a digit over its shifted
        /// glyph, an F number over its media icon - and the two legends are printed one above the
        /// other whatever the cap's aspect ratio says. Esc is 26 x 19, wider than tall, and its two
        /// LEDs are one under the legend and one below it, so splitting on aspect would draw that
        /// pair side by side and it would be wrong on the entire top and number rows.
        ///
        /// Three or more is a long cap, and those run along it: Space five across, the numpad's
        /// Plus and Enter four down their tall rectangles.
        /// </summary>
        internal static bool SplitsHorizontally(int leds, double width, double height) =>
            leds > 2 && width >= height;

        /// <summary>
        /// HP's key name trimmed to something that fits a cap: "KeyBracketsL" is the table's name,
        /// "[" is what is printed on the keyboard. Built-in layouts ship no labels of their own -
        /// only the external keyboard does - so this fills that gap rather than showing raw names.
        /// </summary>
        internal static string ShortName(string name)
        {
            string bare = name.StartsWith("Key", StringComparison.Ordinal) ? name[3..] : name;

            return bare switch
            {
                "Back" => "Bsp",
                "BracketsL" => "[",
                "BracketsR" => "]",
                "Backslash" => "\\",
                "Colon" => ";",
                "Quote" => "'",
                "Tilde" => "`",
                "Comma" => ",",
                "Dot" => ".",
                "Slash" => "/",
                "Hyphen" => "-",
                "Equal" => "=",
                "Caps" => "Cap",
                "Enter" => "Ent",
                "ShiftL" => "Shift",
                "ShiftR" => "RShift",
                "CtrlL" => "Ctrl",
                "CtrlR" => "RCtrl",
                "AltL" => "Alt",
                "AltR" => "RAlt",
                "ArrUP" => "↑",
                "ArrDown" => "↓",
                "ArrLeft" => "←",
                "ArrRight" => "→",
                "NumPad" => "Num",
                "NumSlash" => "Num /",
                "NumStar" => "Num *",
                "NumDash" => "Num -",
                "NumPlus" => "Num +",
                "NumEnter" => "Num ⏎",
                "NumDel" => "Num .",
                "FNL" => "Fn",
                "Copliot" => "Copilot",   // HP's spelling in the table; the key is Copilot
                "Setting" => "Set",
                "Calc" => "Calc",
                _ => bare
            };
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

        /// <summary>
        /// Scale a colour by a per-cell level, 0-100.
        ///
        /// Short-circuits at 100 so an untouched picture is passed through byte-for-byte rather than
        /// through arithmetic that happens to be the identity. Same linear multiply and
        /// round-to-nearest as <c>DojoPerKeyBackend.Scale</c>, so a cell dimmed to 50 here and the
        /// master left at 100 lands on exactly the bytes the backend would have produced.
        /// </summary>
        private static System.Windows.Media.Color ScaleForLevel(System.Windows.Media.Color c, int level)
        {
            if (level >= 100) return c;
            if (level <= 0) return System.Windows.Media.Colors.Black;

            byte Scale(byte channel) => (byte)(((channel * level * 255 / 100) + 127) / 255);
            return System.Windows.Media.Color.FromRgb(Scale(c.R), Scale(c.G), Scale(c.B));
        }

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
        private int _level = 100;

        /// <summary>
        /// Every lamp under this key. Usually one, but a wide key carries several - Space has five
        /// on 8D87 - and colouring the key means colouring all of them.
        ///
        /// Empty in layout mode, where <see cref="LedPositions"/> is the address instead.
        /// </summary>
        public IReadOnlyList<ushort> LampIds { get; init; } = Array.Empty<ushort>();

        /// <summary>
        /// Positions in the MCU's colour map that this cell paints. One entry in layout mode - the
        /// cell IS one LED, which is the point of it - and empty on the lamp-map fallback.
        /// </summary>
        public IReadOnlyList<int> LedPositions { get; init; } = Array.Empty<int>();

        /// <summary>True when this cell addresses the colour map directly rather than a lamp.</summary>
        public bool IsLed => LedPositions.Count > 0;

        /// <summary>HP's name for the key this LED sits under, e.g. "KeySpace". Layout mode only.</summary>
        public string HpKeyName { get; init; } = string.Empty;

        /// <summary>Which of the key's LEDs this is, 1-based, and how many the key has.</summary>
        public int LedOrdinal { get; init; }
        public int LedCount { get; init; }

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

                if (IsLed)
                {
                    // The map position is the useful half of this: it is what the probe's
                    // --positions takes, so a cell in the editor and a cell on the command line
                    // are the same thing and can be checked against each other.
                    string which = LedCount > 1 ? $", LED {LedOrdinal} of {LedCount}" : string.Empty;
                    return $"{HpKeyName}{which} — colour map position {LedPositions[0]}";
                }

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

        /// <summary>
        /// This cell's own brightness, 0-100, multiplied with the editor's master before the colour
        /// goes out. 100 is "as painted", which is why it is the default - a cell nobody has dimmed
        /// must behave exactly as it did before per-cell levels existed.
        ///
        /// Kept SEPARATE from <see cref="ColorHex"/> rather than baked into it. Scaling the hex in
        /// place would be lossy and one-way: drag a key to 10% and back to 100% and the colour the
        /// user picked is gone, quantised to whatever survived the round trip. The picked colour is
        /// the user's, and this is a view on it.
        /// </summary>
        public int Level
        {
            get => _level;
            set
            {
                int clamped = Math.Clamp(value, 0, 100);
                if (_level == clamped) return;
                _level = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(KeyBrush));
                OnPropertyChanged(nameof(LabelBrush));
            }
        }

        /// <summary>
        /// The cap's colour: what was painted, scaled by this cell's own level.
        ///
        /// Deliberately does NOT include the editor's master brightness. Folding that in would be
        /// more literally WYSIWYG and much worse to use - at master 3 the whole editor goes near
        /// black and you cannot see what you are painting. Per-cell differences are the thing this
        /// view has to show; the master is one number, shown next to its own slider.
        /// </summary>
        public System.Windows.Media.SolidColorBrush KeyBrush
        {
            get
            {
                try
                {
                    var c = (System.Windows.Media.Color)
                        System.Windows.Media.ColorConverter.ConvertFromString(ColorHex);

                    return new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(ScaleChannel(c.R), ScaleChannel(c.G), ScaleChannel(c.B)));
                }
                catch
                {
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);
                }
            }
        }

        /// <summary>
        /// Mirrors <c>DojoPerKeyBackend.Scale</c> - same linear multiply, same round-to-nearest - so
        /// the cap and the key agree. A different rounding here would make the editor a slightly
        /// dishonest preview of the thing it exists to preview.
        /// </summary>
        private byte ScaleChannel(byte channel) => (byte)(((channel * Level * 255 / 100) + 127) / 255);

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
