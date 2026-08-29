using System;

namespace OmenCore.Models
{
    /// <summary>
    /// One painted cell of a per-key picture, as it is stored in <c>config.json</c>.
    ///
    /// STORES WHAT THE USER PAINTED, NOT WHAT WENT ON THE WIRE. The colour here is the one they
    /// picked and the level is separate, exactly as the editor holds them, so a restore reproduces
    /// the editable picture rather than a flattened photograph of it. Baking the level into the
    /// colour would be lossy and one-way — a cell taken to 10% and back to 100% would come back a
    /// different colour than it started — which is the same reason the editor keeps them apart.
    ///
    /// The addressing mirrors the editor's three kinds of cell, and exactly one applies:
    /// <list type="bullet">
    /// <item><see cref="Led"/> — a position in the MCU's 176-entry colour map. The normal case on a
    /// board with a known layout, and the finest: one cell is one LED, so the two halves of a
    /// dual-legend key can differ.</item>
    /// <item><see cref="Lamp"/> — a HID LampArray lamp id, for a board whose layout is unknown.</item>
    /// <item><see cref="Zone"/> — a light bar zone, 0 leftmost.</item>
    /// </list>
    /// </summary>
    public class SavedPerKeyCell
    {
        /// <summary>Colour-map position, or -1 when this cell is not addressed that way.</summary>
        public int Led { get; set; } = -1;

        /// <summary>LampArray lamp id, or -1 when this cell is not addressed that way.</summary>
        public int Lamp { get; set; } = -1;

        /// <summary>Light bar zone index, or -1 when this cell is not a light bar zone.</summary>
        public int Zone { get; set; } = -1;

        /// <summary>The painted colour, "#RRGGBB". Unscaled.</summary>
        public string Color { get; set; } = "#000000";

        /// <summary>This cell's own brightness, 0-100. Multiplies with the master.</summary>
        public int Level { get; set; } = 100;

        /// <summary>
        /// A stable key for matching a stored cell back to a cell in the rebuilt editor.
        ///
        /// Deliberately includes the KIND as well as the index: lamp 12 and colour-map position 12
        /// are different lights, and a board that changes which addressing it uses — because a
        /// layout was added to the catalogue between two runs — must not silently restore one
        /// picture onto the other's addresses.
        /// </summary>
        public string Key =>
            Zone >= 0 ? $"zone:{Zone}" :
            Led >= 0 ? $"led:{Led}" :
            Lamp >= 0 ? $"lamp:{Lamp}" :
            string.Empty;
    }
}
