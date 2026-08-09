using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Which LED byte belongs to which key, for every per-key OMEN keyboard, plus which of those
    /// keyboards a given board has.
    ///
    /// The MCU's static colour map is one byte per LED, NOT one per key: a key with two printed
    /// legends has two LEDs, Space has five, the backspace key six. So "make W blue" is meaningless
    /// without a grouping, and the grouping is board-specific. This is that grouping.
    ///
    /// The data is derived from OGH's own embedded layout tables — key name, rectangle and LED
    /// indices, nothing else — by omen-max-16/tools/static/emit-omencore-layouts.py. Regenerate it
    /// there rather than editing <c>KeyboardLayouts.json</c> by hand.
    ///
    /// ONLY ONE LAYOUT HAS BEEN SEEN ON HARDWARE: Dojo/Global, board 8D87, cycle 25C1. Every other
    /// entry is derived from the same tables by the same rule and nobody has watched it light up.
    /// <see cref="KeyboardLayout.Verified"/> says which is which, and callers that surface a layout
    /// to a user should surface that flag too — a keyboard that lights the wrong key is worse than
    /// one that admits it does not know.
    /// </summary>
    public sealed class KeyboardLayoutCatalog
    {
        // Matched by suffix rather than spelled out in full: the resource is prefixed with the
        // project's ROOT NAMESPACE (OmenCoreApp), not the namespace this class is in (OmenCore),
        // and the two differ. A hardcoded full name compiles, ships, and then throws on the first
        // per-key apply.
        private const string ResourceSuffix = ".KeyboardLayouts.json";

        private readonly Dictionary<string, KeyboardLayout> _layouts;
        private readonly Dictionary<string, BoardEntry> _boards;

        private static readonly Lazy<KeyboardLayoutCatalog> Shared = new(Load);

        /// <summary>The catalogue, parsed once.</summary>
        public static KeyboardLayoutCatalog Instance => Shared.Value;

        private KeyboardLayoutCatalog(Dictionary<string, KeyboardLayout> layouts,
                                      Dictionary<string, BoardEntry> boards)
        {
            _layouts = layouts;
            _boards = boards;
        }

        /// <summary>Every layout, for a picker. Verified ones first, then alphabetical.</summary>
        public IReadOnlyList<KeyboardLayout> All =>
            _layouts.Values.OrderByDescending(l => l.Verified).ThenBy(l => l.Id).ToList();

        /// <summary>One layout by id, e.g. "Dojo/Global". Null when the id is not in the table.</summary>
        public KeyboardLayout? ById(string id) =>
            id != null && _layouts.TryGetValue(id, out var layout) ? layout : null;

        /// <summary>
        /// The layout for a board id (Win32_BaseBoard.Product, e.g. "8D87") and keyboard language.
        ///
        /// The board id alone picks the keyboard AND the firmware cycle, which matters: a device
        /// ships across a cycle boundary — Dojo and Vibrance each have both 25C1 and 26C1 SSIDs —
        /// and the two firmwares blank different LED positions. So this cannot be keyed by model
        /// name, and a near-miss is not a safe guess.
        ///
        /// <paramref name="language"/> comes from the MCU's own GetDeviceInfo response. A layout
        /// with no table for that language falls back to Global, which is what OGH does.
        ///
        /// Returns null when the board is unknown — the caller should then ask the user rather
        /// than assume, because guessing the wrong keyboard lights the wrong keys silently.
        /// </summary>
        public KeyboardLayout? ForBoard(string? boardId, string? language = null)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return null;
            if (!_boards.TryGetValue(boardId.Trim().ToUpperInvariant(), out var board)) return null;

            if (!string.IsNullOrWhiteSpace(language) &&
                _layouts.TryGetValue($"{board.Layout}/{language}", out var localised))
                return localised;

            return ById($"{board.Layout}/Global");
        }

        /// <summary>What the catalogue knows about a board id, for logging and for a UI that wants
        /// to say which keyboard it thinks this is. Null when unknown.</summary>
        public BoardEntry? BoardInfo(string? boardId) =>
            !string.IsNullOrWhiteSpace(boardId) &&
            _boards.TryGetValue(boardId.Trim().ToUpperInvariant(), out var board) ? board : null;

        private static KeyboardLayoutCatalog Load()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string name = assembly.GetManifestResourceNames()
                              .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                          ?? throw new InvalidOperationException(
                              $"No embedded resource ending in {ResourceSuffix}. It is generated by " +
                              "omen-max-16/tools/static/emit-omencore-layouts.py and must be listed " +
                              "as an EmbeddedResource in the csproj.");

            using var stream = assembly.GetManifestResourceStream(name)!;

            var file = JsonSerializer.Deserialize<CatalogFile>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidOperationException($"{name} did not parse.");

            return new KeyboardLayoutCatalog(
                file.Layouts ?? new Dictionary<string, KeyboardLayout>(),
                file.Boards ?? new Dictionary<string, BoardEntry>());
        }

        private sealed class CatalogFile
        {
            [JsonPropertyName("layouts")] public Dictionary<string, KeyboardLayout>? Layouts { get; set; }
            [JsonPropertyName("boards")] public Dictionary<string, BoardEntry>? Boards { get; set; }
        }
    }

    /// <summary>One keyboard: how many LED bytes its colour map has, and what the keys are.</summary>
    public sealed class KeyboardLayout
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";

        /// <summary>Length of the MCU colour map, padding excluded.</summary>
        [JsonPropertyName("leds")] public int Leds { get; set; }

        /// <summary>Left, top, right, bottom over every key, in the source table's own units.
        /// Divide by this to get relative coordinates; the absolute values mean nothing.</summary>
        [JsonPropertyName("bounds")] public double[] Bounds { get; set; } = Array.Empty<double>();

        /// <summary>False for every layout except the one board this was measured on. Show it.</summary>
        [JsonPropertyName("verified")] public bool Verified { get; set; }

        [JsonPropertyName("keys")] public List<KeyboardKey> Keys { get; set; } = new();

        private Dictionary<string, KeyboardKey>? _byName;

        /// <summary>One key by HP's name, e.g. "KeySpace".</summary>
        public KeyboardKey? ByName(string name)
        {
            _byName ??= Keys.ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase);
            return name != null && _byName.TryGetValue(name, out var key) ? key : null;
        }
    }

    /// <summary>
    /// One physical key: its name, where it sits, and which LED bytes light it.
    ///
    /// <see cref="Rect"/> is the KEY's rectangle, and every LED of the key repeats it — the source
    /// tables carry no sub-key geometry at all. So this says which key an LED belongs to and never
    /// where on the key it sits. Intra-key order is only knowable by lighting one and looking, and
    /// it is not consistent between rows. Colouring a whole key needs none of that; colouring one
    /// printed legend of a dual-legend key does, and this data cannot supply it.
    /// </summary>
    public sealed class KeyboardKey
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";

        /// <summary>X, Y, width, height in the layout's own units — see <see cref="KeyboardLayout.Bounds"/>.</summary>
        [JsonPropertyName("rect")] public double[] Rect { get; set; } = Array.Empty<double>();

        /// <summary>Indices into the MCU colour map. Usually contiguous, but do not assume it.</summary>
        [JsonPropertyName("leds")] public int[] Leds { get; set; } = Array.Empty<int>();

        /// <summary>The legend OGH prints, where the source table carries one. Built-in keyboards
        /// ship no labels; the external keyboard does.</summary>
        [JsonPropertyName("label")] public string? Label { get; set; }
    }

    /// <summary>What board id maps to which keyboard.</summary>
    public sealed class BoardEntry
    {
        [JsonPropertyName("device")] public string Device { get; set; } = "";
        [JsonPropertyName("display")] public string Display { get; set; } = "";

        /// <summary>HP's firmware cycle for this SSID, e.g. "25C1". Decides which of a device's
        /// layout variants applies, and is already folded into <see cref="Layout"/>.</summary>
        [JsonPropertyName("cycle")] public string Cycle { get; set; } = "";

        /// <summary>Layout id without the language suffix.</summary>
        [JsonPropertyName("layout")] public string Layout { get; set; } = "";
    }
}
