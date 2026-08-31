using System;
using System.Collections.Generic;

namespace OmenCore.Models
{
    /// <summary>
    /// Represents a saved RGB scene that can be applied across all devices.
    /// </summary>
    public class RgbScene
    {
        /// <summary>
        /// Unique identifier for the scene.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Display name of the scene.
        /// </summary>
        public string Name { get; set; } = "Untitled Scene";

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Icon or emoji for visual identification.
        /// </summary>
        public string Icon { get; set; } = "🎨";

        /// <summary>
        /// The effect type to apply.
        /// </summary>
        public RgbSceneEffect Effect { get; set; } = RgbSceneEffect.Static;

        /// <summary>
        /// Primary color as hex string (e.g., "#FF0000").
        /// </summary>
        public string PrimaryColor { get; set; } = "#E6002E";

        /// <summary>
        /// Secondary color for gradient/breathing effects.
        /// </summary>
        public string? SecondaryColor { get; set; }

        /// <summary>
        /// Animation speed (0-100).
        /// </summary>
        public int Speed { get; set; } = 50;

        /// <summary>
        /// Brightness level (0-100).
        /// </summary>
        public int Brightness { get; set; } = 100;

        /// <summary>
        /// Per-zone colors for multi-zone devices (4-zone keyboard).
        /// Key is zone index (0-3), value is hex color.
        /// </summary>
        public Dictionary<int, string> ZoneColors { get; set; } = new();

        /// <summary>
        /// Whether this scene is the default scene.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Whether to apply to HP OMEN keyboard.
        /// </summary>
        public bool ApplyToOmenKeyboard { get; set; } = true;

        /// <summary>
        /// Whether to apply to Logitech devices.
        /// </summary>
        public bool ApplyToLogitech { get; set; } = true;

        /// <summary>
        /// Whether to apply to Corsair devices.
        /// </summary>
        public bool ApplyToCorsair { get; set; } = true;

        /// <summary>
        /// Whether to apply to Razer devices.
        /// </summary>
        public bool ApplyToRazer { get; set; } = true;

        /// <summary>
        /// Performance mode that triggers this scene automatically.
        /// Null means no auto-trigger.
        /// </summary>
        public string? TriggerOnPerformanceMode { get; set; }

        /// <summary>
        /// Time of day to activate this scene (HH:mm format).
        /// Null means no time-based activation.
        /// </summary>
        public string? ScheduledTime { get; set; }

        /// <summary>
        /// Days of week when scheduled time applies (0=Sunday).
        /// Empty means all days.
        /// </summary>
        public List<int> ScheduledDays { get; set; } = new();

        /// <summary>
        /// Create a clone of this scene with a new ID.
        /// </summary>
        public RgbScene Clone()
        {
            return new RgbScene
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"{Name} (Copy)",
                Description = Description,
                Icon = Icon,
                Effect = Effect,
                PrimaryColor = PrimaryColor,
                SecondaryColor = SecondaryColor,
                Speed = Speed,
                Brightness = Brightness,
                ZoneColors = new Dictionary<int, string>(ZoneColors),
                IsDefault = false,
                ApplyToOmenKeyboard = ApplyToOmenKeyboard,
                ApplyToLogitech = ApplyToLogitech,
                ApplyToCorsair = ApplyToCorsair,
                ApplyToRazer = ApplyToRazer,
                TriggerOnPerformanceMode = TriggerOnPerformanceMode,
                ScheduledTime = ScheduledTime,
                ScheduledDays = new List<int>(ScheduledDays)
            };
        }
    }

    /// <summary>
    /// RGB effect types supported by scenes.
    /// </summary>
    public enum RgbSceneEffect
    {
        /// <summary>Static color.</summary>
        Static,

        /// <summary>Breathing/pulsing effect.</summary>
        Breathing,

        /// <summary>Rainbow/spectrum cycling.</summary>
        Spectrum,

        /// <summary>Wave effect.</summary>
        Wave,

        /// <summary>Gradient between two colors.</summary>
        Gradient,

        /// <summary>Reactive - responds to key presses.</summary>
        Reactive,

        /// <summary>Ambient - samples screen colors.</summary>
        Ambient,

        /// <summary>Audio reactive - pulses with system output audio.</summary>
        AudioReactive,

        /// <summary>Turn off all lighting.</summary>
        Off
    }

    /// <summary>
    /// Result of applying a scene.
    /// </summary>
    public class RgbSceneApplyResult
    {
        public bool Success { get; set; }
        public string SceneId { get; set; } = string.Empty;
        public string SceneName { get; set; } = string.Empty;
        public int ProvidersApplied { get; set; }
        public int ProvidersFailed { get; set; }
        public List<string> Errors { get; set; } = new();
        public TimeSpan ApplyDuration { get; set; }
    }

    /// <summary>
    /// Event args for scene changes.
    /// </summary>
    public class RgbSceneChangedEventArgs : EventArgs
    {
        public RgbScene? PreviousScene { get; set; }
        public RgbScene CurrentScene { get; set; } = null!;
        public string Trigger { get; set; } = "manual"; // manual, performance, schedule, startup
    }
}
