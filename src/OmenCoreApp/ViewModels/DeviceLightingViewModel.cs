using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Hardware;
using OmenCore.Services;
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

        public DeviceLightingViewModel(KeyboardLightingService? keyboard, LoggingService logging)
        {
            _keyboard = keyboard;
            _logging = logging;

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

            var speed = Speed switch
            {
                "Slow" => DojoKeyboardMcu.EffectSpeed.Slow,
                "Fast" => DojoKeyboardMcu.EffectSpeed.Fast,
                _ => DojoKeyboardMcu.EffectSpeed.Medium
            };

            var colors = UseCustomColors
                ? new[] { ToRgb(PrimaryColorHex), ToRgb(SecondaryColorHex) }
                : Array.Empty<(byte, byte, byte)>();

            var record = new DojoKeyboardMcu.EffectRecord
            {
                Effect = SelectedEffect.Wire,
                ShowMode = UseCustomColors
                    ? DojoKeyboardMcu.ShowMode.MultipleCustomColors
                    : SelectedTheme?.Wire ?? DojoKeyboardMcu.ShowMode.Volcano,

                // Zero-based count when custom, and the preset sentinel otherwise. Sending a count
                // where a sentinel belongs is what makes Swipe render black.
                ColorNumber = UseCustomColors ? (byte)(colors.Length - 1) : DojoKeyboardMcu.ColorNumberPreset,

                Speed = speed,
                Brightness = 0,
                Direction = ToDirection(Direction),
                RippleSize = 1,
                RaindropFrequency = (byte)speed,
                Colors = colors
            };

            bool ok = await _keyboard.SetDeviceEffectAsync(record);
            Status = ok
                ? $"{SelectedEffect.Name} installed. It keeps running with OmenCore closed."
                : $"The keyboard refused {SelectedEffect.Name}.";
        }

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

        private static DojoKeyboardMcu.EffectDirection ToDirection(string name) => name switch
        {
            "Right to left" => DojoKeyboardMcu.EffectDirection.RightToLeft,
            "Up" => DojoKeyboardMcu.EffectDirection.Up,
            "Down" => DojoKeyboardMcu.EffectDirection.Down,
            "Inward" => DojoKeyboardMcu.EffectDirection.Inward,
            "Outward" => DojoKeyboardMcu.EffectDirection.Outward,
            "Clockwise" => DojoKeyboardMcu.EffectDirection.Clockwise,
            "Anticlockwise" => DojoKeyboardMcu.EffectDirection.CounterClockwise,
            _ => DojoKeyboardMcu.EffectDirection.LeftToRight
        };

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
