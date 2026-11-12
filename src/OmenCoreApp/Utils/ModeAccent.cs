using System;
using System.Windows;
using System.Windows.Media;

namespace OmenCore.Utils
{
    /// <summary>
    /// The window's mode colour (roadmap 4.B "mode-aware chrome", an idea from Ohman): the selected
    /// navigation tab and the sidebar's performance marker take the colour of the active profile, so the
    /// machine's state reads at a glance without a label. The colours are the ones the profile cards on
    /// the General page already use, so the chrome and the card agree.
    ///
    /// This deliberately drives its own brush, <c>ModeAccentBrush</c>, and never <c>AccentBrush</c>:
    /// AccentBrush also means "error" and "warning" in status text across the app, and a green error
    /// message under Quiet would be wrong. Like the high-contrast brushes it is unfrozen and recoloured in
    /// place, because every consumer holds a StaticResource reference to the same instance.
    /// </summary>
    public static class ModeAccent
    {
        public const string BrushKey = "ModeAccentBrush";

        public static readonly Color Performance = Color.FromRgb(0xFF, 0x6B, 0x35);
        public static readonly Color Balanced = Color.FromRgb(0x00, 0xD4, 0xFF);
        public static readonly Color Quiet = Color.FromRgb(0x00, 0xFF, 0x88);
        public static readonly Color Custom = Color.FromRgb(0xA8, 0x55, 0xF7);
        public static readonly Color Brand = Color.FromRgb(0xFF, 0x00, 0x5C);

        /// <summary>The colour for a profile name as GeneralViewModel.SelectedProfile reports it; anything else gets the brand red.</summary>
        public static Color For(string? profile) => profile switch
        {
            "Performance" => Performance,
            "Balanced" => Balanced,
            "Quiet" => Quiet,
            "Custom" => Custom,
            _ => Brand
        };

        /// <summary>
        /// Recolours the brush. In high contrast the user's own highlight colour wins over every mode colour,
        /// because the point of high contrast is that the scheme, not the app, picks the colours.
        /// </summary>
        public static void Apply(ResourceDictionary resources, string? profile, bool highContrast)
        {
            if (resources[BrushKey] is not SolidColorBrush brush || brush.IsFrozen)
                return;

            brush.Color = highContrast ? SystemColors.HighlightColor : For(profile);
        }
    }
}
