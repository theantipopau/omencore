using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.Services.KeyboardLighting;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// The two surfaces that are rendered BY THE HARDWARE rather than painted by us: the keyboard's
    /// built-in effect engine, and the light bar.
    ///
    /// Kept apart from the per-key editor because they are a different kind of thing. An effect is
    /// one frame the device then animates on its own - it survives this process exiting, needs no
    /// repaint loop, and cannot be combined with a host-painted picture, because installing one
    /// hands the keys back to the firmware.
    ///
    /// WHAT IS DELIBERATELY NOT OFFERED HERE:
    ///
    /// * Keyboard brightness. The MCU refuses the brightness command on 8D87, and no effect
    ///   consumes the brightness field, so a slider would move nothing. The per-key editor's
    ///   intensity is the only working lever and it only scales a host-painted picture.
    /// * Light bar brightness and animations when unelevated. Those exist solely behind HP's WMI
    ///   commands. <see cref="SupportsLightBarEffects"/> reports it so the view can hide them
    ///   rather than offer controls that silently do nothing.
    /// </summary>
    public class DeviceLightingViewModel : ViewModelBase
    {
        private readonly KeyboardLightingService? _keyboard;
        private readonly LoggingService _logging;
        private readonly ConfigurationService? _config;

        private EffectOption? _selectedEffect;
        private ThemeOption? _selectedTheme;
        private string _speed = "Medium";
        private string _direction = "Left to right";
        private string _primaryColorHex = "#FF0000";
        private string _secondaryColorHex = "#0000FF";
        private bool _useCustomColors;
        private string _status = string.Empty;

        private LightBarEffectOption? _selectedBarEffect;
        private string _barColorHex = "#0000FF";
        private int _barBrightness = 100;
        private string _barStatus = string.Empty;

        public DeviceLightingViewModel(
            KeyboardLightingService? keyboard, LoggingService logging, ConfigurationService? config = null)
        {
            _keyboard = keyboard;
            _logging = logging;
            _config = config;

            _selectedEffect = Effects.FirstOrDefault();
            _selectedTheme = Themes.FirstOrDefault();
            _selectedBarEffect = LightBarEffects.FirstOrDefault();

            ApplyEffectCommand = new AsyncRelayCommand(async _ => await ApplyEffectAsync(), _ => SupportsDeviceEffects);
            ReadEffectCommand = new RelayCommand(_ => ReadEffect(), _ => SupportsDeviceEffects);
            ApplyLightBarColorCommand = new RelayCommand(_ => ApplyLightBarColor(), _ => IsLightBarAvailable);
            ApplyLightBarEffectCommand = new RelayCommand(_ => ApplyLightBarEffect(), _ => SupportsLightBarEffects);
            LightBarOffCommand = new RelayCommand(_ => LightBarOff(), _ => IsLightBarAvailable);

            PickPrimaryColorCommand = new RelayCommand(_ => PickInto(PrimaryColorHex, h => PrimaryColorHex = h));
            PickSecondaryColorCommand = new RelayCommand(_ => PickInto(SecondaryColorHex, h => SecondaryColorHex = h));
            PickBarColorCommand = new RelayCommand(_ => PickInto(BarColorHex, h => BarColorHex = h));

            StageCurrentEffectCommand = new RelayCommand(_ => StageCurrentEffect(), _ => SupportsDeviceEffects);
            RemoveFnSlotCommand = new RelayCommand(p => RemoveFnSlot(p as FnCycleSlotViewModel));
            MakeFnSlotActiveCommand = new RelayCommand(p => MakeFnSlotActive(p as FnCycleSlotViewModel));
            WriteFnCycleCommand = new AsyncRelayCommand(
                async _ => await WriteFnCycleAsync(),
                _ => SupportsDeviceEffects && FnCycleSlots.Count > 0);

            LoadFnCycle();

            if (SupportsDeviceEffects) ReadEffect();
            RefreshLightBar();
        }

        // ── Keyboard effects ───────────────────────────────────────────────────────

        public bool SupportsDeviceEffects => _keyboard?.SupportsDeviceEffects ?? false;

        /// <summary>
        /// The device's twelve, in the order OMEN Gaming Hub lists them so a user moving across
        /// recognises the set. The wire values are NOT this order and are not exposed.
        /// </summary>
        public ObservableCollection<EffectOption> Effects { get; } = new(new[]
        {
            new EffectOption("Colour cycle", DojoKeyboardMcu.Effect.ColorCycle),
            new EffectOption("Starlight",    DojoKeyboardMcu.Effect.Starlight),
            new EffectOption("Breathing",    DojoKeyboardMcu.Effect.Breathing),
            new EffectOption("Ghosting",     DojoKeyboardMcu.Effect.Ghosting),
            new EffectOption("Ripple",       DojoKeyboardMcu.Effect.Ripple),
            new EffectOption("Wave",         DojoKeyboardMcu.Effect.Wave),
            new EffectOption("OMEN X",       DojoKeyboardMcu.Effect.OmenX),
            new EffectOption("Raindrop",     DojoKeyboardMcu.Effect.Raindrop),
            new EffectOption("Audio pulse",  DojoKeyboardMcu.Effect.AudioPulse),
            new EffectOption("Confetti",     DojoKeyboardMcu.Effect.Confetti),
            new EffectOption("Sun",          DojoKeyboardMcu.Effect.Sun),
            new EffectOption("Swipe",        DojoKeyboardMcu.Effect.Swipe),
        });

        public ObservableCollection<ThemeOption> Themes { get; } = new(new[]
        {
            new ThemeOption("Volcano", DojoKeyboardMcu.ShowMode.Volcano),
            new ThemeOption("Jungle",  DojoKeyboardMcu.ShowMode.Jungle),
            new ThemeOption("Ocean",   DojoKeyboardMcu.ShowMode.Ocean),
            new ThemeOption("Rainbow", DojoKeyboardMcu.ShowMode.Rainbow),
        });

        public ObservableCollection<string> Speeds { get; } = new(new[] { "Slow", "Medium", "Fast" });

        public ObservableCollection<string> Directions { get; } = new(new[]
        {
            "Left to right", "Right to left", "Up", "Down", "Inward", "Outward", "Clockwise", "Anticlockwise"
        });

        public EffectOption? SelectedEffect
        {
            get => _selectedEffect;
            set
            {
                _selectedEffect = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectAdvice));
            }
        }

        public ThemeOption? SelectedTheme
        {
            get => _selectedTheme;
            set { _selectedTheme = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectAdvice)); }
        }

        public string Speed { get => _speed; set { _speed = value; OnPropertyChanged(); } }
        public string Direction { get => _direction; set { _direction = value; OnPropertyChanged(); } }

        public bool UseCustomColors
        {
            get => _useCustomColors;
            set { _useCustomColors = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectAdvice)); }
        }

        public string PrimaryColorHex
        {
            get => _primaryColorHex;
            set { _primaryColorHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrimaryColorBrush)); }
        }

        public string SecondaryColorHex
        {
            get => _secondaryColorHex;
            set { _secondaryColorHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(SecondaryColorBrush)); }
        }

        /// <summary>
        /// Swatches for the two custom effect colours. A hex box alone makes the user hold the
        /// mapping from six characters to a colour in their head, which is exactly the job a swatch
        /// does for free - and these two feed effects where the colours matter to each other, so
        /// seeing them side by side is most of the point.
        /// </summary>
        public System.Windows.Media.SolidColorBrush PrimaryColorBrush => BrushFrom(PrimaryColorHex);
        public System.Windows.Media.SolidColorBrush SecondaryColorBrush => BrushFrom(SecondaryColorHex);

        public ICommand PickPrimaryColorCommand { get; private set; } = null!;
        public ICommand PickSecondaryColorCommand { get; private set; } = null!;
        public ICommand PickBarColorCommand { get; private set; } = null!;

        /// <summary>
        /// Black for anything unparseable rather than throwing. The hex box accepts keystrokes as
        /// they are typed, so "#F" is a state this passes through on the way to "#FF8800" and is
        /// not an error worth surfacing.
        /// </summary>
        internal static System.Windows.Media.SolidColorBrush BrushFrom(string hex)
        {
            try
            {
                return new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                        hex.StartsWith('#') ? hex : "#" + hex));
            }
            catch
            {
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);
            }
        }

        /// <summary>
        /// Open the shared picker on a hex property. Same dialog the per-key editor uses - a second
        /// colour-picking implementation in one app is how two of them end up disagreeing about
        /// what "#FF0000" means.
        /// </summary>
        private void PickInto(string current, Action<string> assign)
        {
            var dialog = new Views.ColorPickerDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            dialog.SetInitialColor(current);

            if (dialog.ShowDialog() == true && dialog.DialogResultOk)
                assign(dialog.SelectedHexColor);
        }

        public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

        /// <summary>
        /// Warn about the two combinations that produce a black keyboard by design. Both look
        /// exactly like a broken feature from the user's chair, and both are the firmware doing what
        /// the field map says - so they are worth saying BEFORE the button is pressed rather than
        /// explaining afterwards.
        /// </summary>
        public string EffectAdvice
        {
            get
            {
                if (SelectedEffect?.Wire == DojoKeyboardMcu.Effect.Swipe && !UseCustomColors)
                    return "Swipe has no preset palette — with a theme selected it renders black. Tick custom colours.";

                if (SelectedEffect?.Wire == DojoKeyboardMcu.Effect.AudioPulse)
                    return "Audio pulse is driven by live audio levels the host feeds it. Applied on its own it shows a steady colour rather than pulsing.";

                if (SelectedEffect?.Wire is DojoKeyboardMcu.Effect.Confetti or DojoKeyboardMcu.Effect.Sun && UseCustomColors)
                    return "Confetti and Sun take no custom colours — the theme is used regardless.";

                return string.Empty;
            }
        }

        public ICommand ApplyEffectCommand { get; }
        public ICommand ReadEffectCommand { get; }

        private async Task ApplyEffectAsync()
        {
            if (_keyboard == null || SelectedEffect == null) return;

            bool ok = await _keyboard.SetDeviceEffectAsync(FnCyclePlan.ToRecord(CurrentSlot()));
            Status = ok
                ? $"{SelectedEffect.Name} installed. It keeps running with OmenCore closed."
                : $"The keyboard refused {SelectedEffect.Name}.";
        }

        /// <summary>
        /// The card's current settings as a storable profile.
        ///
        /// Apply and "add to Fn cycle" both go through this, so a profile in the cycle is the same
        /// frame the user just watched Apply produce. Building the record twice is how the two would
        /// quietly drift apart.
        /// </summary>
        private Models.FnCycleSlot CurrentSlot() => new()
        {
            Effect = (SelectedEffect?.Wire ?? DojoKeyboardMcu.Effect.Wave).ToString(),
            DisplayName = SelectedEffect?.Name ?? "Wave",
            UseCustomColors = UseCustomColors,
            PrimaryColorHex = PrimaryColorHex,
            SecondaryColorHex = SecondaryColorHex,
            Theme = (SelectedTheme?.Wire ?? DojoKeyboardMcu.ShowMode.Volcano).ToString(),
            Speed = Speed,
            Direction = Direction
        };

        private void ReadEffect()
        {
            var current = _keyboard?.ReadDeviceEffect();
            if (current == null)
            {
                Status = "The keyboard did not answer a state read.";
                return;
            }

            var r = current.Value;
            string palette = r.ColorNumber == DojoKeyboardMcu.ColorNumberPreset
                ? r.ShowMode.ToString()
                : $"{r.ColorNumber + 1} custom colour(s)";

            Status = $"On the keyboard now: {r.Effect}, {palette}, {r.Speed.ToString().ToLowerInvariant()}.";
        }

        // ── The Fn+1 / Fn+2 cycle ──────────────────────────────────────────────────
        //
        // The keyboard steps its own list of saved effects when you press Fn+1 (next) or Fn+2
        // (previous), with no software running at all. That list is not HP's - it holds what a host
        // wrote to it, so OmenCore can curate it, and the result keeps working after this process
        // exits, after a reinstall, and on a machine where OmenCore was never installed.
        //
        // What the firmware will NOT let us do, and what the UI therefore must not imply:
        //
        //   * There is no readback for the list. The MCU answers "what is showing now" and nothing
        //     else, so the collection below is what WE staged - never a reading of the keyboard.
        //   * There is no command to remove or reorder an entry. Writing adds to and updates what
        //     the keyboard already holds; taking a profile out of this list does not take it off
        //     the keyboard. Said plainly in FnCycleLimitation rather than buried here.

        /// <summary>Delay between the effect frames of a cycle write.
        ///
        /// Precautionary, not measured to be necessary - back-to-back 0x03 frames have not been
        /// shown to drop. The failure it guards against is a slot quietly missing from the cycle,
        /// which the user would only find by pressing Fn+1 a week later, and twelve slots costs
        /// under two seconds. Cheap insurance against an expensive-to-notice bug.</summary>
        private const int CycleWriteSettleMs = 120;

        /// <summary>Profiles staged for the cycle. Ours, not the keyboard's - see above.</summary>
        public ObservableCollection<FnCycleSlotViewModel> FnCycleSlots { get; } = new();

        public bool HasFnCycleSlots => FnCycleSlots.Count > 0;

        /// <summary>Shown in place of the list when it is empty, so the card explains itself rather
        /// than presenting a blank area and two buttons.</summary>
        public string FnCycleEmptyHint => FnCycleSlots.Count > 0
            ? string.Empty
            : "Nothing staged yet. Set up an effect above, then choose \"Add to cycle\".";

        private string _fnCycleStatus = string.Empty;
        public string FnCycleStatus
        {
            get => _fnCycleStatus;
            private set { _fnCycleStatus = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// The one thing about this feature a user cannot discover by trying it: removal does not
        /// reach the keyboard. Someone who stages three profiles, writes, removes one, writes again
        /// and then presses Fn+1 will find four - and with no readback there is nothing in the UI
        /// that could have shown them the fourth.
        /// </summary>
        public string FnCycleLimitation =>
            "Writing adds to or updates what the keyboard already holds. There is no command to " +
            "remove an entry, so a profile taken off this list stays on the keyboard until " +
            "something overwrites that effect type.";

        /// <summary>Live warning about the staged set, or empty. Same two black-by-design traps the
        /// effects card warns about, checked across the whole set before any frame goes out.</summary>
        public string FnCycleAdvice =>
            FnCycleSlots.Count == 0
                ? string.Empty
                : FnCyclePlan.Validate(FnCyclePlan.Order(FnCycleSlots.Select(s => s.Model).ToList()));

        public ICommand StageCurrentEffectCommand { get; private set; } = null!;
        public ICommand RemoveFnSlotCommand { get; private set; } = null!;
        public ICommand MakeFnSlotActiveCommand { get; private set; } = null!;
        public ICommand WriteFnCycleCommand { get; private set; } = null!;

        private void StageCurrentEffect()
        {
            var slot = CurrentSlot();

            // One slot per effect type in the firmware, so two entries of the same effect could
            // never become two cycle positions. Replacing in place rather than appending keeps the
            // list honest about what the keyboard will end up holding.
            var existing = FnCycleSlots.FirstOrDefault(s =>
                string.Equals(s.Model.Effect, slot.Effect, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                slot.IsActive = existing.Model.IsActive;
                FnCycleSlots[FnCycleSlots.IndexOf(existing)] = new FnCycleSlotViewModel(slot);
                FnCycleStatus = $"{slot.DisplayName} updated — the keyboard keeps one profile per effect.";
            }
            else
            {
                // First one staged is the one to leave showing, until the user says otherwise.
                slot.IsActive = FnCycleSlots.Count == 0;
                FnCycleSlots.Add(new FnCycleSlotViewModel(slot));
                FnCycleStatus = $"{slot.DisplayName} added. Nothing is on the keyboard until you write the cycle.";
            }

            FnCycleChanged();
        }

        private void RemoveFnSlot(FnCycleSlotViewModel? slot)
        {
            if (slot == null || !FnCycleSlots.Remove(slot)) return;

            // The list must always name an active profile while it has any, or a write would leave
            // the keyboard showing whichever happened to be last.
            if (slot.Model.IsActive && FnCycleSlots.Count > 0)
                MakeFnSlotActive(FnCycleSlots[0]);

            FnCycleStatus = $"{slot.Model.DisplayName} removed from the list. It is still on the keyboard.";
            FnCycleChanged();
        }

        private void MakeFnSlotActive(FnCycleSlotViewModel? slot)
        {
            if (slot == null) return;

            foreach (var s in FnCycleSlots) s.IsActive = ReferenceEquals(s, slot);
            FnCycleChanged();
        }

        private async Task WriteFnCycleAsync()
        {
            if (_keyboard == null) return;

            var ordered = FnCyclePlan.Order(FnCycleSlots.Select(s => s.Model).ToList());
            if (ordered.Count == 0)
            {
                FnCycleStatus = "Nothing staged to write.";
                return;
            }

            FnCycleStatus = $"Writing {ordered.Count} profile(s)…";

            var refused = new List<string>();
            foreach (var slot in ordered)
            {
                bool ok = await _keyboard.SetDeviceEffectAsync(FnCyclePlan.ToRecord(slot));
                if (!ok) refused.Add(slot.DisplayName);

                await Task.Delay(CycleWriteSettleMs);
            }

            // The last frame is the one showing, and flashing is what makes THAT survive a power
            // cycle. Whether the rest of the list survives one is not something this project has
            // measured, so the status text below does not claim it.
            bool flashed = await _keyboard.StoreDeviceLightingToFlashAsync();

            int written = ordered.Count - refused.Count;
            var active = ordered.LastOrDefault();

            if (refused.Count == ordered.Count)
                FnCycleStatus = "The keyboard refused every profile. Nothing changed.";
            else if (refused.Count > 0)
                FnCycleStatus =
                    $"{written} of {ordered.Count} written; the keyboard refused {string.Join(", ", refused)}. " +
                    $"Press Fn+1 to step the cycle.";
            else
                FnCycleStatus =
                    $"{written} profile(s) written{(flashed ? "" : " (the keyboard refused the save-to-flash)")}. " +
                    $"Showing {active?.DisplayName}. Press Fn+1 for the next, Fn+2 for the previous — " +
                    $"this works with OmenCore closed.";

            _logging.Info($"[FnCycle] Wrote {written}/{ordered.Count} profiles, flash={flashed}");
        }

        private void FnCycleChanged()
        {
            OnPropertyChanged(nameof(HasFnCycleSlots));
            OnPropertyChanged(nameof(FnCycleEmptyHint));
            OnPropertyChanged(nameof(FnCycleAdvice));
            SaveFnCycle();
        }

        private void LoadFnCycle()
        {
            var saved = _config?.Config?.KeyboardLighting?.FnCycleSlots;
            if (saved == null) return;

            foreach (var slot in saved)
                FnCycleSlots.Add(new FnCycleSlotViewModel(slot));

            OnPropertyChanged(nameof(HasFnCycleSlots));
            OnPropertyChanged(nameof(FnCycleEmptyHint));
            OnPropertyChanged(nameof(FnCycleAdvice));
        }

        private void SaveFnCycle()
        {
            var config = _config?.Config;
            if (config?.KeyboardLighting == null) return;

            config.KeyboardLighting.FnCycleSlots = FnCycleSlots.Select(s => s.Model).ToList();

            try { _config!.Save(config); }
            catch (Exception ex) { _logging.Warn($"[FnCycle] Could not save the staged cycle: {ex.Message}"); }
        }

        /// <summary>One staged profile, for the list. Wraps the stored model rather than copying its
        /// fields, so what is bound and what is serialised cannot disagree.</summary>
        public class FnCycleSlotViewModel : ViewModelBase
        {
            public FnCycleSlotViewModel(Models.FnCycleSlot model) => Model = model;

            public Models.FnCycleSlot Model { get; }

            public string DisplayName => Model.DisplayName;

            public bool IsActive
            {
                get => Model.IsActive;
                set { Model.IsActive = value; OnPropertyChanged(); }
            }

            /// <summary>What this profile will actually look like, in the terms the effects card
            /// uses - so the list can be checked against the card without opening anything.</summary>
            public string Summary
            {
                get
                {
                    string palette = Model.UseCustomColors
                        ? $"{Model.PrimaryColorHex} → {Model.SecondaryColorHex}"
                        : Model.Theme;

                    return $"{palette} · {Model.Speed.ToLowerInvariant()} · {Model.Direction.ToLowerInvariant()}";
                }
            }

            public System.Windows.Media.SolidColorBrush SwatchBrush =>
                Model.UseCustomColors ? BrushFrom(Model.PrimaryColorHex) : ThemeSwatch(Model.Theme);

            /// <summary>A preset palette has no single colour, so the swatch shows a recognisable
            /// member of it rather than nothing. Indicative, not a rendering of the palette.</summary>
            private static System.Windows.Media.SolidColorBrush ThemeSwatch(string theme) => theme switch
            {
                "Jungle" => BrushFrom("#0FFA36"),
                "Ocean" => BrushFrom("#0F36FA"),
                "Rainbow" => BrushFrom("#7D00B2"),
                _ => BrushFrom("#EA002A")
            };
        }

        // ── Light bar ──────────────────────────────────────────────────────────────

        public bool IsLightBarAvailable => _keyboard?.IsLightBarAvailable ?? false;
        public bool SupportsLightBarEffects => _keyboard?.SupportsLightBarEffects ?? false;

        public ObservableCollection<LightBarEffectOption> LightBarEffects { get; } = new(new[]
        {
            new LightBarEffectOption("Colour cycle", HpWmiBios.LightBarEffect.ColorCycle),
            new LightBarEffectOption("Starlight",    HpWmiBios.LightBarEffect.Starlight),
            new LightBarEffectOption("Breathing",    HpWmiBios.LightBarEffect.Breathing),
            new LightBarEffectOption("Wave",         HpWmiBios.LightBarEffect.Wave),
            new LightBarEffectOption("Raindrop",     HpWmiBios.LightBarEffect.Raindrop),
            new LightBarEffectOption("Audio pulse",  HpWmiBios.LightBarEffect.AudioPulse),
            new LightBarEffectOption("Confetti",     HpWmiBios.LightBarEffect.Confetti),
            new LightBarEffectOption("Sun",          HpWmiBios.LightBarEffect.Sun),
            new LightBarEffectOption("Swipe",        HpWmiBios.LightBarEffect.Swipe),
        });

        public LightBarEffectOption? SelectedBarEffect
        {
            get => _selectedBarEffect;
            set { _selectedBarEffect = value; OnPropertyChanged(); }
        }

        public string BarColorHex
        {
            get => _barColorHex;
            set { _barColorHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(BarColorBrush)); }
        }

        public System.Windows.Media.SolidColorBrush BarColorBrush => BrushFrom(BarColorHex);

        public int BarBrightness
        {
            get => _barBrightness;
            set { _barBrightness = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        public string BarStatus { get => _barStatus; private set { _barStatus = value; OnPropertyChanged(); } }

        /// <summary>Shown when unelevated, so the missing controls are explained rather than absent.</summary>
        public string LightBarLimitation => SupportsLightBarEffects
            ? string.Empty
            : "Run OmenCore as administrator for light bar brightness and animations — those go through " +
              "HP's BIOS interface, which Windows restricts. Colour works either way.";

        public ICommand ApplyLightBarColorCommand { get; }
        public ICommand ApplyLightBarEffectCommand { get; }
        public ICommand LightBarOffCommand { get; }

        private void ApplyLightBarColor()
        {
            if (_keyboard == null) return;

            bool ok = _keyboard.SetLightBarColors(new[] { ToRgb(BarColorHex) }, (byte)BarBrightness);
            BarStatus = ok ? "Light bar updated." : "The light bar refused the update.";
            RefreshLightBar();
        }

        private void ApplyLightBarEffect()
        {
            if (_keyboard == null || SelectedBarEffect == null) return;

            // Swipe is custom-colour only on this device too, so it is given the picked colour
            // rather than a theme - otherwise selecting it from a menu produces a dark bar.
            bool custom = SelectedBarEffect.Wire == HpWmiBios.LightBarEffect.Swipe;

            bool ok = _keyboard.SetLightBarAnimation(
                SelectedBarEffect.Wire,
                custom ? HpWmiBios.LightBarTheme.Custom : HpWmiBios.LightBarTheme.Galaxy,
                HpWmiBios.LightBarSpeed.Medium,
                (byte)BarBrightness,
                custom ? new[] { ToRgb(BarColorHex) } : null);

            BarStatus = ok
                ? $"{SelectedBarEffect.Name} sent. There is no readback for animations — look at the bar."
                : $"The light bar refused {SelectedBarEffect.Name}.";
        }

        private void LightBarOff()
        {
            if (_keyboard == null) return;
            BarStatus = _keyboard.SetLightBarOff() ? "Light bar off." : "The light bar refused the command.";
        }

        private void RefreshLightBar()
        {
            var zones = _keyboard?.GetLightBarColors() ?? Array.Empty<(byte R, byte G, byte B)?>();
            if (zones.Length == 0) return;

            BarStatus = "Now showing: " + string.Join("  ",
                zones.Select(z => z == null ? "??" : $"#{z.Value.R:X2}{z.Value.G:X2}{z.Value.B:X2}"));
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static (byte R, byte G, byte B) ToRgb(string hex)
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    hex.StartsWith('#') ? hex : "#" + hex);
                return (c.R, c.G, c.B);
            }
            catch
            {
                return (0, 0, 0);
            }
        }

        public record EffectOption(string Name, DojoKeyboardMcu.Effect Wire)
        {
            public override string ToString() => Name;
        }

        public record ThemeOption(string Name, DojoKeyboardMcu.ShowMode Wire)
        {
            public override string ToString() => Name;
        }

        public record LightBarEffectOption(string Name, HpWmiBios.LightBarEffect Wire)
        {
            public override string ToString() => Name;
        }
    }
}
