using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace OmenCore.Utils
{
    /// <summary>
    /// Puts the app's chrome inside the colours Windows reports while high contrast is on, and
    /// restores the normal palette when it is switched off (WP-45, definition-of-done item 8).
    ///
    /// WPF cannot swap the palette by merging a second resource dictionary: every style and view
    /// reaches these brushes through <c>StaticResource</c>, which resolves once at load time to the
    /// brush object itself. Adding an override dictionary later leaves all of those references
    /// pointing at the old objects. So this mutates the brushes in place instead - every consumer
    /// already shares the same instance, and a <see cref="SolidColorBrush.Color"/> change repaints
    /// all of them with no XAML re-parse and no per-window work.
    ///
    /// That only works on brushes that are not frozen, which is why the ones listed here carry
    /// <c>PresentationOptions:Freeze="False"</c> in <c>Styles/ModernStyles.xaml"</c>. A frozen
    /// brush is reported and skipped rather than throwing during startup; the tests in
    /// <c>HighContrastThemeTests</c> fail if a key here is missing from the style sheet or loses
    /// that attribute, so the two cannot drift apart silently.
    ///
    /// Scope is deliberate: this covers the chrome (window and surface backgrounds, text, borders,
    /// accent and focus ring). Literal hex fills inside individual control templates and status
    /// badges keep their own colours - recolouring those is WP-33's density/chrome pass, and a
    /// half-converted template would look worse than one that stays put.
    /// </summary>
    public sealed class HighContrastTheme
    {
        /// <summary>Keys in <c>Styles/ModernStyles.xaml</c> this theme is allowed to repaint.</summary>
        public static class Keys
        {
            public const string BackgroundDark = "BackgroundDarkBrush";
            public const string SurfaceDark = "SurfaceDarkBrush";
            public const string SurfaceMedium = "SurfaceMediumBrush";
            public const string SurfaceLight = "SurfaceLightBrush";
            public const string SurfaceHighlight = "SurfaceHighlightBrush";
            public const string TextPrimary = "TextPrimaryBrush";
            public const string TextSecondary = "TextSecondaryBrush";
            public const string TextTertiary = "TextTertiaryBrush";
            public const string TextMuted = "TextMutedBrush";
            public const string Border = "BorderBrush";
            public const string Accent = "AccentBrush";
            public const string FocusRing = "FocusRingBrush";
        }

        /// <summary>
        /// Where the skip diagnostics go. The app points this at its own log at startup; it stays a
        /// delegate so this class needs no reference to App (and its tests need no host).
        /// </summary>
        public static Action<string>? LogSink { get; set; }

        private static void Log(string message)
        {
            Debug.WriteLine(message);
            LogSink?.Invoke(message);
        }

        private readonly ResourceDictionary _resources;
        private readonly Func<bool> _isActive;
        private readonly Dictionary<string, Color> _originals = new(StringComparer.Ordinal);
        private bool _applied;

        /// <param name="resources">The application's merged resources (the brushes live in ModernStyles).</param>
        /// <param name="isActive">
        /// Overridable so the on/off logic can be tested without a high-contrast desktop. Defaults to
        /// the real system setting.
        /// </param>
        public HighContrastTheme(ResourceDictionary resources, Func<bool>? isActive = null)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _isActive = isActive ?? ReadSystemHighContrast;
        }

        public bool IsApplied => _applied;

        /// <summary>
        /// Windows' own answer. SystemParameters can throw in hosts with no display (a service, an
        /// RDP session torn down mid-call), and a theme must never be the reason startup fails.
        /// </summary>
        public static bool ReadSystemHighContrast()
        {
            try
            {
                return SystemParameters.HighContrast;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// What each chrome brush becomes in high contrast. SystemColors is the only palette that
        /// matches whatever scheme the user picked (High Contrast Black, White, or a custom one),
        /// and it is what native controls in the same window already use.
        /// </summary>
        public static IReadOnlyDictionary<string, Color> HighContrastColors() =>
            new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                [Keys.BackgroundDark] = SystemColors.WindowColor,
                [Keys.SurfaceDark] = SystemColors.WindowColor,
                [Keys.SurfaceMedium] = SystemColors.ControlColor,
                [Keys.SurfaceLight] = SystemColors.ControlColor,
                [Keys.SurfaceHighlight] = SystemColors.ControlLightColor,
                // Secondary and muted text collapse onto WindowText on purpose: GrayText is chosen
                // by the scheme for *disabled* content, and this app uses it for explanatory text
                // that must stay readable.
                [Keys.TextPrimary] = SystemColors.WindowTextColor,
                [Keys.TextSecondary] = SystemColors.WindowTextColor,
                [Keys.TextTertiary] = SystemColors.WindowTextColor,
                [Keys.TextMuted] = SystemColors.WindowTextColor,
                [Keys.Border] = SystemColors.WindowTextColor,
                [Keys.Accent] = SystemColors.HighlightColor,
                [Keys.FocusRing] = SystemColors.HighlightColor
            };

        /// <summary>Applies the palette the OS currently asks for; idempotent.</summary>
        public void ApplyIfNeeded() => Apply(_isActive());

        /// <summary>Applies or reverts the high-contrast palette; idempotent in both directions.</summary>
        public void Apply(bool highContrast)
        {
            if (highContrast == _applied)
            {
                return;
            }

            if (highContrast)
            {
                EnterHighContrast();
            }
            else
            {
                ExitHighContrast();
            }

            _applied = highContrast;
        }

        private void EnterHighContrast()
        {
            foreach (var pair in HighContrastColors())
            {
                string key = pair.Key;

                if (!_resources.Contains(key))
                {
                    Log($"High contrast: '{key}' is not in the resource dictionary; skipped");
                    continue;
                }

                if (_resources[key] is not SolidColorBrush brush)
                {
                    Log($"High contrast: '{key}' is not a SolidColorBrush; skipped");
                    continue;
                }

                if (brush.IsFrozen)
                {
                    // Not thrown from here: a frozen brush means the style sheet lost its
                    // PresentationOptions:Freeze="False", which the drift test catches at build
                    // time. Startup should still come up in the normal palette.
                    Log($"High contrast: '{key}' is frozen and cannot be recoloured; " +
                        "add PresentationOptions:Freeze=\"False\" to it in ModernStyles.xaml");
                    continue;
                }

                _originals[key] = brush.Color;
                brush.Color = pair.Value;
            }
        }

        private void ExitHighContrast()
        {
            foreach (var pair in _originals)
            {
                if (_resources.Contains(pair.Key) && _resources[pair.Key] is SolidColorBrush brush && !brush.IsFrozen)
                {
                    brush.Color = pair.Value;
                }
            }

            _originals.Clear();
        }
    }
}
