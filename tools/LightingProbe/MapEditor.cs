// Drive KeyboardMapViewModel - the type the UI binds to - against real hardware.
//
// The per-key editor's failures were all in the VIEW-MODEL rather than the transport: keys
// initialised black so the first Apply blanked the keyboard, the selection ring hid the colour it
// had just painted, brightness never reached the lamps. None of that shows up in a transport test or
// in a unit test with no keyboard. So this runs the same objects the window runs, in the order a
// user clicks them. WRITES, so it is behind --commit like everything else.

using System.Linq;
using OmenCore.Services;
using OmenCore.ViewModels;

namespace OmenCore.Tools.LightingProbe;

internal static class MapEditor
{
    internal static int Run(string[] args)
    {
        bool commit = args.Contains("--commit");

        Console.WriteLine("=== Per-key editor view-model, against hardware ===\n");

        // Initialize starts the writer thread; without it every log line is queued and discarded,
        // which cost a round of "the code did not run" when it had run and said so into a void.
        var logging = new LoggingService();
        logging.Initialize();

        // Pass the WMI BIOS, as the app does. Without it the bar is unreachable and its zones read as
        // nulls - which looked like a broken readback, and was this harness constructing the service
        // differently from the window it stands in for.
        var bios = new OmenCore.Hardware.HpWmiBios(logging);

        // Backend probing happens in the constructor, so the service is live once this returns.
        var keyboard = new KeyboardLightingService(logging, wmiBios: bios);
        var vm = new KeyboardMapViewModel(keyboard, logging);

        var layout = keyboard.GetKeyboardLayout();

        Console.WriteLine($"  IsPerKey            : {keyboard.IsPerKey}");
        Console.WriteLine($"  SupportsDeviceEffects: {keyboard.SupportsDeviceEffects}");
        Console.WriteLine($"  GetMeasuredKeyMap   : {keyboard.GetMeasuredKeyMap().Count} lamps");
        Console.WriteLine($"  GetKeyboardLayout   : " + (layout == null
            ? "none - this board is not in the catalogue"
            : $"{layout.Id}, {layout.Keys.Count} keys, {layout.Leds} LEDs" +
              (layout.Verified ? " (confirmed on hardware)" : " (NOT confirmed on hardware)")));
        Console.WriteLine($"  available           : {vm.IsAvailable}");

        if (!vm.IsAvailable)
        {
            Console.WriteLine("\n  No measured lamp map, so the editor would be hidden in the UI.");
            Console.WriteLine("  The three lines above localise it: the backend logs 120 mapped lamps,");
            Console.WriteLine("  so a zero here is the service layer, not the hardware.");
            return 1;
        }

        int barZones = vm.Keys.Count(k => k.IsLightBar);
        var cells = vm.Keys.Where(k => !k.IsLightBar).ToList();

        // Which enumeration the editor drew from. They are not interchangeable: the layout has one
        // cell per LED, the lamp map one per lamp, and there are 176 of the first against 120 of the
        // second on this board. Every count below means a different thing depending on this line.
        bool byLed = cells.Count > 0 && cells[0].IsLed;

        Console.WriteLine($"  drawn from: {(byLed ? "the layout table, one cell per LED" : "the lamp map, one cell per lamp")}");
        Console.WriteLine($"  cells     : {cells.Count} " + (byLed
            ? $"(the layout declares {layout?.Leds} LEDs)"
            : $"(from {keyboard.GetMeasuredKeyMap().Count} lamps)"));
        Console.WriteLine($"  light bar : {(vm.HasLightBar ? "present" : "not available")}, " +
                          $"{barZones} zones in the map");

        foreach (var zone in vm.Keys.Where(k => k.IsLightBar))
        {
            Console.WriteLine($"    {zone.Label}  x {zone.X,5:F0} .. {zone.X + zone.Width,5:F0}  " +
                              $"y {zone.Y,5:F0}  {zone.ColorHex} (read from the bar)");
        }
        Console.WriteLine($"  canvas    : {vm.CanvasWidth:F0} x {vm.CanvasHeight:F0}");
        // Brightness is settable here so the lever is exercised rather than merely reported. It is
        // the LampArray intensity channel - the only brightness this keyboard has, the MCU command
        // being refused - and it only takes effect on the next colour write.
        int at = Array.IndexOf(args, "--brightness");
        if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int level))
            vm.Brightness = level;

        Console.WriteLine($"  brightness: {vm.Brightness}");
        Console.WriteLine($"  status    : {vm.Status}");

        // The grouping is the fix for the crushed modifier column, so show the widest keys: if
        // Space is five separate keys rather than one, it is visible right here.
        Console.WriteLine(byLed
            ? "\n  widest cells (a wide cap's LEDs, so these should be single-LED slices):"
            : "\n  widest keys (multi-lamp keys should be the wide ones):");
        foreach (var key in cells.OrderByDescending(k => k.Width).Take(8))
        {
            Console.WriteLine($"    {Describe(key),-28} {key.Width:F0} wide at ({key.X:F0}, {key.Y:F0})");
        }

        // The left three columns were crushed because filler lamps - usage 0x03, ~9 mm from their
        // neighbour, one per left-hand row - were drawn at a full key width and overlapped three
        // deep. Check no key now overlaps the next one in its row.
        Console.WriteLine("\n  left-edge cells, first three rows (filler lamps should be slivers):");
        foreach (var key in cells.Where(k => k.X < 90).OrderBy(k => k.Y).ThenBy(k => k.X).Take(10))
        {
            Console.WriteLine($"    {Describe(key),-28} x {key.X,5:F0} .. {key.X + key.Width,5:F0}  " +
                              $"({key.Width,4:F0} wide)");
        }

        int overlaps = 0;
        foreach (var row in vm.Keys.GroupBy(k => Math.Round(k.Y)))
        {
            var ordered = row.OrderBy(k => k.X).ToList();
            for (int i = 0; i + 1 < ordered.Count; i++)
            {
                // One pixel of tolerance: the drawn caps carry a 1 px margin each side.
                if (ordered[i].X + ordered[i].Width > ordered[i + 1].X + 1)
                {
                    overlaps++;
                    Console.WriteLine($"    overlap: {ordered[i].Label} ends {ordered[i].X + ordered[i].Width:F0}, " +
                                      $"{ordered[i + 1].Label} starts {ordered[i + 1].X:F0} " +
                                      $"(row y={Math.Round(ordered[i].Y)})");
                }
            }
        }

        Console.WriteLine($"\n  overlaps  : {overlaps}");
        Console.WriteLine(overlaps == 0
            ? "              none - no key is drawn over its neighbour"
            : "              KEYS OVERLAP - the layout will look crushed");

        // Rows must share a Y after snapping, or the keyboard looks wobbly. The bar sits on its own
        // Y below the keys and is not a keyboard row, so it is excluded from the count.
        var rows = cells.Select(k => Math.Round(k.Y)).Distinct().OrderBy(y => y).ToList();
        Console.WriteLine($"\n  rows      : {rows.Count} distinct Y values ({string.Join(", ", rows)})");

        if (byLed)
        {
            // A cap divides into its LEDs, and a vertical division puts a cell at a Y no key row
            // sits at. So distinct Ys outnumber rows here BY DESIGN, and the 5-to-8 check that
            // guards the lamp path would fail on a correct build. Coverage is the check that means
            // something in this mode - see below.
            Console.WriteLine("              more than one per row, as expected - a stacked cap puts "
                              + "its second LED on its own Y");
        }
        else
        {
            Console.WriteLine(rows.Count is >= 5 and <= 8
                ? "              plausible for a 6-row laptop keyboard with a numpad"
                : "              SUSPICIOUS - snapping may not be working");
        }

        // The check the layout path exists for: every byte of the colour map is reachable from the
        // editor, and no two cells claim the same one. A duplicate is the bug that made half of
        // Num0 inert - two cells resolving to one LED, the second silently overwriting the first.
        if (byLed && layout != null)
        {
            var positions = cells.SelectMany(k => k.LedPositions).ToList();
            var duplicated = positions.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var missing = Enumerable.Range(0, layout.Leds).Except(positions).ToList();

            Console.WriteLine($"\n  coverage  : {positions.Distinct().Count()} of {layout.Leds} "
                              + $"colour-map positions, {duplicated.Count} claimed twice");
            Console.WriteLine(duplicated.Count == 0 && missing.Count == 0
                ? "              every LED addressable exactly once"
                : $"              GAPS OR COLLISIONS - missing [{string.Join(", ", missing.Take(12))}], "
                  + $"duplicated [{string.Join(", ", duplicated.Take(12))}]");
        }

        // Every key starts at the brush colour, not black. A black default means the first Apply
        // blanks whatever the user did not paint. Bar zones are excluded: those read their colour
        // back off the hardware, so a dark one is the bar being dark, not a bad default.
        int black = cells.Count(k => k.ColorHex is "#000000" or "000000");
        Console.WriteLine($"\n  initial   : {cells.Count - black} keys at the brush colour, {black} black");
        Console.WriteLine(black == 0
            ? "              good - a first Apply will not blank the keyboard"
            : "              WARNING - those keys would go dark on the first Apply");

        // Now the gesture a user actually makes: drag a band, paint it, check the selection was
        // dropped so the colour is visible.
        // Set the background the keys will hold, before the band is painted over it.
        vm.BrushColorHex = "#200000";
        ((System.Windows.Input.ICommand)vm.SelectAllCommand).Execute(null);
        ((System.Windows.Input.ICommand)vm.PaintSelectionCommand).Execute(null);
        Console.WriteLine($"\n  background: all keys #200000 (dim red)");

        Console.WriteLine("  --- simulating a drag across the top-left region, then Paint ---");
        vm.SelectWithin(0, 0, vm.CanvasWidth / 4, vm.CanvasHeight / 3, additive: false);
        Console.WriteLine($"  selected  : {vm.SelectedCount} keys");

        // Paint the band a DIFFERENT colour from the background the keys started at, or the test
        // proves nothing: green keys on a green keyboard look identical whether the band landed or
        // not. Keys initialise at the brush colour, so the brush has to change between the two.
        vm.BrushColorHex = "#00FF00";
        ((System.Windows.Input.ICommand)vm.PaintSelectionCommand).Execute(null);

        int green = vm.Keys.Count(k => k.ColorHex.Equals("#00FF00", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"  painted   : {green} keys now #00FF00");
        Console.WriteLine($"  selection : {vm.SelectedCount} still selected " +
                          (vm.SelectedCount == 0
                              ? "(good - the ring is gone so the colour shows)"
                              : "(BAD - the white ring hides the colour just painted)"));
        Console.WriteLine($"  status    : {vm.Status}");

        if (!commit)
        {
            Console.WriteLine("\n  DRY RUN. The map was built and painted in memory; nothing was sent.");
            Console.WriteLine("  Add --commit to push it to the keyboard.");
            return 0;
        }

        Console.WriteLine("\n  --- Apply ---");
        ((System.Windows.Input.ICommand)vm.ApplyCommand).Execute(null);
        System.Threading.Thread.Sleep(400);
        Console.WriteLine($"  status    : {vm.Status}");

        Console.WriteLine("\n  LOOK AT THE KEYBOARD.");
        Console.WriteLine($"    expect: the top-left ~12 keys BRIGHT GREEN, every other key DIM RED");
        Console.WriteLine("    all dark        -> the write is not reaching the lamps");
        Console.WriteLine("    an animation    -> the device effect engine still owns the keys");
        Console.WriteLine("    partly coloured -> the lamp map is incomplete");

        return 0;
    }

    /// <summary>
    /// Name a cell the way its tooltip does, so probe output and the window agree.
    ///
    /// The two modes address different things and printing one shape for both is what made this
    /// harness report "0 lamp" against a correct layout-driven build: the cells were right, the
    /// field being printed was simply not the one they carry.
    /// </summary>
    private static string Describe(OmenCore.ViewModels.KeyLampViewModel key) =>
        key.IsLed
            ? $"{key.HpKeyName} LED {key.LedOrdinal}/{key.LedCount} @{key.LedPositions[0]}"
            : $"{key.Label} ({key.LampIds.Count} lamp{(key.LampIds.Count == 1 ? "" : "s")})";
}
