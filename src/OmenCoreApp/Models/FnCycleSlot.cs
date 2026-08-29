namespace OmenCore.Models
{
    /// <summary>
    /// One effect profile staged for the keyboard's <c>Fn+1</c> / <c>Fn+2</c> cycle.
    ///
    /// Stored as strings rather than the wire enums on purpose: this round-trips through
    /// config.json, and a renumbered enum must not silently repoint someone's saved profile at a
    /// different effect. Unknown names are dropped when the plan is built.
    /// </summary>
    public class FnCycleSlot
    {
        /// <summary>Name of a <c>DojoKeyboardMcu.Effect</c> member, e.g. "Wave".</summary>
        public string Effect { get; set; } = "Wave";

        /// <summary>Display name, so the list reads the way the effects card does.</summary>
        public string DisplayName { get; set; } = "Wave";

        public bool UseCustomColors { get; set; }
        public string PrimaryColorHex { get; set; } = "#FF0000";
        public string SecondaryColorHex { get; set; } = "#0000FF";

        /// <summary>Name of a <c>DojoKeyboardMcu.ShowMode</c> preset — used when custom colours are off.</summary>
        public string Theme { get; set; } = "Volcano";

        public string Speed { get; set; } = "Medium";
        public string Direction { get; set; } = "Left to right";

        /// <summary>
        /// The profile the keyboard should be left displaying after the cycle is written.
        ///
        /// Not a wire field. The keyboard shows whatever was written last, so "active" is expressed
        /// by ordering the writes rather than by an extra command.
        /// </summary>
        public bool IsActive { get; set; }
    }
}
