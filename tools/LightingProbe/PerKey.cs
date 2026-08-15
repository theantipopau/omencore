// Drive OmenCore's per-key backend on real hardware.
//
// This runs DojoPerKeyBackend - the shipping type, through the shipping IKeyboardBackend surface -
// rather than a copy of the protocol. A mistake in what ships shows up here, which is the whole
// point of a probe living in this repo instead of a scratch file.
//
// The keyboard has TWO interfaces and they do different jobs, so the commands split the same way:
//
//   --zones RRGGBB,...       STATIC colour, painted per key over mi_04. No readback anywhere.
//   --effect <name>          a DEVICE-SIDE animation over mi_03. One frame, rendered by the MCU,
//                            and it holds after this process exits. Readable back.
//
// WRITES ARE GATED. --commit is required by everything that changes the keyboard; without it the
// frame is composed, decoded and printed, and nothing is sent. --read-effect is exempt, because a
// read is the safe thing and making it awkward makes people guess instead.
//
// WHAT "ACCEPTED" MEANS. The MCU answers EC AC to a frame it parsed. That is not the keyboard
// saying anything lit - during a stuck state on 8D87 every frame OGH sent was acknowledged and the
// keyboard stayed dark. Close a claim with the readback AND with looking at the keyboard.
//
// TWO EFFECTS RENDER BLACK ON PURPOSE and both look like a bug:
//   Swipe        has no preset palette. With a preset selected it shows nothing; give it colours.
//   AudioPulse   IS its two level bytes. At 0 it is black by definition. --pulse feeds them.

using System.Drawing;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.Services.KeyboardLighting;

namespace OmenCore.Tools.LightingProbe;

internal static class PerKey
{
    private static readonly (string Name, DojoKeyboardMcu.Effect Wire)[] Effects =
    {
        ("colorcycle", DojoKeyboardMcu.Effect.ColorCycle),
        ("starlight",  DojoKeyboardMcu.Effect.Starlight),
        ("breathing",  DojoKeyboardMcu.Effect.Breathing),
        ("ghosting",   DojoKeyboardMcu.Effect.Ghosting),
        ("ripple",     DojoKeyboardMcu.Effect.Ripple),
        ("wave",       DojoKeyboardMcu.Effect.Wave),
        ("omenx",      DojoKeyboardMcu.Effect.OmenX),
        ("raindrop",   DojoKeyboardMcu.Effect.Raindrop),
        ("audiopulse", DojoKeyboardMcu.Effect.AudioPulse),
        ("confetti",   DojoKeyboardMcu.Effect.Confetti),
        ("sun",        DojoKeyboardMcu.Effect.Sun),
        ("swipe",      DojoKeyboardMcu.Effect.Swipe),
    };

    private static readonly (string Name, DojoKeyboardMcu.ShowMode Mode)[] Themes =
    {
        ("single", DojoKeyboardMcu.ShowMode.SingleCustomColor),
        ("multi", DojoKeyboardMcu.ShowMode.MultipleCustomColors),
        ("volcano", DojoKeyboardMcu.ShowMode.Volcano),
        ("jungle", DojoKeyboardMcu.ShowMode.Jungle),
        ("ocean", DojoKeyboardMcu.ShowMode.Ocean),
        ("rainbow", DojoKeyboardMcu.ShowMode.Rainbow),
    };

    internal static int Run(string[] args)
    {
        bool commit = args.Contains("--commit");

        var logging = new LoggingService();
        using var backend = new DojoPerKeyBackend(logging);

        Console.WriteLine("=== OmenCore per-key backend ===\n");

        if (!backend.InitializeAsync().GetAwaiter().GetResult())
        {
            Console.WriteLine("  Backend unavailable. Neither mi_03 (MCU) nor mi_04 (LampArray) opened.");
            Console.WriteLine("  Candidates seen:");
            var candidates = DojoKeyboardMcu.DescribeCandidates();
            if (candidates.Count == 0) Console.WriteLine("    none - no Darfon keyboard on this machine");
            foreach (string line in candidates) Console.WriteLine($"    {line}");
            return 1;
        }

        Console.WriteLine($"  backend  : {backend.Name}");
        Console.WriteLine($"  keys     : {backend.KeyCount} addressable lamps");
        Console.WriteLine($"  effects  : {(backend.SupportsDeviceEffects ? "available (mi_03 open)" : "UNAVAILABLE - mi_03 did not open")}");
        Console.WriteLine();

        // Read first, always, and print it whether or not anything is about to be written. It is
        // the only "before" this keyboard offers, and restoring what was there needs it.
        PrintEffect("as found", backend.ReadDeviceEffect());
        Console.WriteLine();

        if (args.Contains("--read-effect")) return 0;

        int rc = 0;
        if (Arg(args, "--brightness") != null) rc |= Brightness(backend, args, commit);
        if (Arg(args, "--backlight") != null) rc |= Backlight(backend, args, commit);
        if (Arg(args, "--zones") != null) rc |= Zones(backend, args, commit);
        if (Arg(args, "--keys") != null) rc |= Keys(backend, args, commit);
        if (Arg(args, "--effect") != null) rc |= SetEffect(backend, args, commit);
        if (args.Contains("--restore-default")) rc |= RestoreDefault(backend, commit);
        if (args.Contains("--persist")) rc |= Persist(backend, commit);

        return rc;
    }

    // ── Effects ────────────────────────────────────────────────────────────────────

    private static int SetEffect(DojoPerKeyBackend backend, string[] args, bool commit)
    {
        string name = Arg(args, "--effect")!.Trim().ToLowerInvariant();
        var match = Effects.FirstOrDefault(e => e.Name == name);
        if (match.Name == null)
        {
            Console.WriteLine($"  Unknown --effect '{name}'. One of: {string.Join(", ", Effects.Select(e => e.Name))}");
            return 1;
        }

        var colors = ParseColors(Arg(args, "--colors"));
        string themeName = (Arg(args, "--theme") ?? (colors.Count > 0 ? "" : "jungle")).Trim().ToLowerInvariant();

        DojoKeyboardMcu.ShowMode showMode;
        byte colorNumber;

        if (colors.Count > 0 && themeName.Length == 0)
        {
            showMode = colors.Count > 1
                ? DojoKeyboardMcu.ShowMode.MultipleCustomColors
                : DojoKeyboardMcu.ShowMode.SingleCustomColor;

            // Zero-based. Four custom colours send 3, which is the field's single sharpest edge.
            colorNumber = (byte)(colors.Count - 1);
        }
        else
        {
            var theme = Themes.FirstOrDefault(t => t.Name == themeName);
            if (theme.Name == null)
            {
                Console.WriteLine($"  Unknown --theme '{themeName}'. One of: {string.Join(", ", Themes.Select(t => t.Name))}");
                return 1;
            }
            showMode = theme.Mode;
            colorNumber = DojoKeyboardMcu.ColorNumberPreset;
        }

        var record = new DojoKeyboardMcu.EffectRecord
        {
            Effect = match.Wire,
            ShowMode = showMode,
            ColorNumber = colorNumber,
            Speed = ParseSpeed(Arg(args, "--speed")),

            // Defaults to 0, which is what OGH sends and what every frame from here has ever
            // carried. THAT IS THE POINT OF THE FLAG: "no effect consumes brightness" was inferred
            // from [4] reading back 160 unchanged, but a field that is only ever sent 0 cannot be
            // distinguished from a field the firmware treats 0 as "leave alone". Send a non-zero
            // one and the readback answers it - under the merge rule, a field that updates is a
            // field the effect consumes.
            Brightness = ParseByte(Arg(args, "--effect-brightness"), 0),
            Direction = ParseDirection(Arg(args, "--direction")),
            RippleSize = ParseByte(Arg(args, "--size"), 1),
            RaindropFrequency = (byte)ParseSpeed(Arg(args, "--speed")),
            InnerBrightness = ParseByte(Arg(args, "--inner"), 0),
            OuterBrightness = ParseByte(Arg(args, "--outer"), 0),
            Colors = colors.ToArray()
        };

        Console.WriteLine("  plan     : install effect");
        PrintEffect("requested", record);

        if (!commit)
        {
            Console.WriteLine("\n  DRY RUN. Nothing written. Add --commit to send it.");
            return 0;
        }

        var result = backend.SetDeviceEffect(record);
        Console.WriteLine();
        Console.WriteLine($"  accepted : {result.BackendReportedSuccess}" +
                          (result.FailureReason == null ? "" : $"   ({result.FailureReason})"));
        Console.WriteLine($"  readback : {(result.SupportsVerification ? (result.VerificationPassed ? "matches" : "DISAGREES") : "the MCU did not answer")}");
        Console.WriteLine();
        PrintEffect("now", backend.ReadDeviceEffect());

        Console.WriteLine();
        Console.WriteLine("  LOOK AT THE KEYBOARD. The readback says the MCU installed it, not that");
        Console.WriteLine("  anything is visible - and the effect holds after this process exits, so");
        Console.WriteLine("  there is no rush.");

        return result.BackendReportedSuccess ? 0 : 1;
    }

    // ── Static colour ──────────────────────────────────────────────────────────────

    private static int Zones(DojoPerKeyBackend backend, string[] args, bool commit)
    {
        var colors = ParseColors(Arg(args, "--zones"));
        if (colors.Count == 0)
        {
            Console.WriteLine("  --zones takes 1-4 RRGGBB values, comma separated.");
            return 1;
        }

        // One colour means "the whole keyboard", which is what a caller typing one colour means.
        var zones = new Color[4];
        for (int i = 0; i < 4; i++)
        {
            var c = colors[Math.Min(i, colors.Count - 1)];
            zones[i] = Color.FromArgb(c.R, c.G, c.B);
        }

        Console.WriteLine("  plan     : paint " + backend.KeyCount + " lamps, zoned left to right by physical position");
        Console.WriteLine("             " + string.Join("  ", zones.Select(z => $"#{z.R:X2}{z.G:X2}{z.B:X2}")));

        if (!commit)
        {
            Console.WriteLine("\n  DRY RUN. Nothing written. Add --commit to send it.");
            return 0;
        }

        var result = backend.SetZoneColorsAsync(zones).GetAwaiter().GetResult();
        Console.WriteLine();
        Console.WriteLine($"  accepted : {result.BackendReportedSuccess}" +
                          (result.FailureReason == null ? "" : $"   ({result.FailureReason})"));
        Console.WriteLine();
        Console.WriteLine("  There is NO colour readback on this interface, so accepted is the whole");
        Console.WriteLine("  software claim. Only looking settles it.");
        Console.WriteLine();
        Console.WriteLine("    four bands, left to right  -> per-key colour works and the zoning is right");
        Console.WriteLine("    scattered patches          -> colour works, the zone mapping is wrong");
        Console.WriteLine("    the effect still running   -> host control did not take the lamps away from");
        Console.WriteLine("                                  the MCU. Try --effect off first.");
        Console.WriteLine("    rainbow, repainting        -> Windows Dynamic Lighting owns it; turn it off");
        Console.WriteLine();
        Console.WriteLine("  The keyboard is NOT handed back to its own effects - that is deliberate, and");
        Console.WriteLine("  it is what makes the picture survive this process exiting. To undo:");
        Console.WriteLine("    LightingProbe --effect wave --theme rainbow --commit    (or --restore-default)");

        return result.BackendReportedSuccess ? 0 : 1;
    }

    /// <summary>
    /// Colour named keys and LEAVE EVERYTHING ELSE ALONE.
    ///
    /// The difference from --key is the whole point. --key blanks the rest of the keyboard, which
    /// is the right shape for proving a lamp index is correct and the wrong shape for using the
    /// feature: a caller lighting WASD does not want the other 116 keys turned off. Nothing here
    /// touches a lamp that was not named.
    /// </summary>
    private static int Keys(DojoPerKeyBackend backend, string[] args, bool commit)
    {
        var map = backend.GetKeyMap();
        if (map.Count == 0)
        {
            Console.WriteLine("  No lamp map; the LampArray interface did not open.");
            return 1;
        }

        var wanted = new Dictionary<ushort, Color>();
        var unresolved = new List<string>();

        foreach (string pair in (Arg(args, "--keys") ?? "")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] halves = pair.Split('=', 2);
            string name = halves[0].Trim();
            string hex = halves.Length > 1 ? halves[1] : Arg(args, "--color") ?? "FF0000";

            var color = ParseColors(hex).FirstOrDefault();
            if (color == default && hex.TrimStart('#').ToUpperInvariant() != "000000")
            {
                unresolved.Add($"{name} (bad colour '{hex}')");
                continue;
            }

            if (!SetKey.TryParseUsage(name, out byte usage))
            {
                unresolved.Add($"{name} (unknown key)");
                continue;
            }

            // One usage can sit under several lamps - Space spans five on this keyboard, Shift
            // three. Lighting "Space" means lighting all of them, so every match is taken rather
            // than the first.
            var hits = map.Where(l => l.KeyUsage == usage).ToList();
            if (hits.Count == 0)
            {
                unresolved.Add($"{name} (usage 0x{usage:X2} carried by no lamp)");
                continue;
            }

            foreach (var lamp in hits)
                wanted[lamp.LampId] = Color.FromArgb(color.R, color.G, color.B);
        }

        foreach (string bad in unresolved) Console.WriteLine($"  skipped  : {bad}");

        if (wanted.Count == 0)
        {
            Console.WriteLine("  Nothing to write. Format: --keys W=FF0000,A=00FF00,S=0000FF,D=FFFF00");
            return 1;
        }

        Console.WriteLine($"  plan     : colour {wanted.Count} lamp(s), leaving the other {map.Count - wanted.Count} untouched");

        if (!commit)
        {
            Console.WriteLine("\n  DRY RUN. Nothing written. Add --commit to send it.");
            return 0;
        }

        bool ok = backend.SetKeyColors(wanted);
        Console.WriteLine($"  accepted : {ok}");
        Console.WriteLine();
        Console.WriteLine("  Untouched lamps keep whatever they were showing - so if an effect was");
        Console.WriteLine("  running, the rest of the keyboard is now a frozen frame of it.");

        return ok ? 0 : 1;
    }

    // ── The rest ───────────────────────────────────────────────────────────────────

    private static int Brightness(DojoPerKeyBackend backend, string[] args, bool commit)
    {
        int level = int.TryParse(Arg(args, "--brightness"), out int n) ? Math.Clamp(n, 0, 100) : 100;
        bool mcuOnly = args.Contains("--mcu-only");

        Console.WriteLine(mcuOnly
            ? $"  plan     : brightness {level}  (MCU command 0x0C ALONE - no repaint)"
            : $"  plan     : brightness {level}  (MCU command 0x0C, plus a re-scaled repaint of " +
              "whatever picture this process last painted - nothing to repaint on a fresh run)");

        if (!commit)
        {
            Console.WriteLine("\n  DRY RUN. Nothing written. Add --commit to send it.");
            return 0;
        }

        bool ok = mcuOnly
            ? backend.SetMcuBrightnessOnly((byte)level)
            : backend.SetBrightnessAsync(level).GetAwaiter().GetResult();

        Console.WriteLine($"  accepted : {ok}");
        Console.WriteLine();

        if (mcuOnly)
        {
            Console.WriteLine("  Nothing else was sent, so whatever the keyboard does now is 0x0C's doing.");
            Console.WriteLine("  Run this while a DEVICE EFFECT is displayed - over a host-painted picture");
            Console.WriteLine("  the lamps carry their own intensity and the attribution is muddied again.");
        }
        else
        {
            Console.WriteLine("  TWO levers moved: 0x0C and the lamp intensity. Over a static picture the");
            Console.WriteLine("  repaint alone explains any dimming, so this CANNOT tell you whether 0x0C");
            Console.WriteLine("  works. Add --mcu-only, over a running effect, to attribute it.");
        }

        return ok ? 0 : 1;
    }

    private static int Backlight(DojoPerKeyBackend backend, string[] args, bool commit)
    {
        string want = Arg(args, "--backlight")!.Trim().ToLowerInvariant();
        if (want is not ("on" or "off"))
        {
            Console.WriteLine("  --backlight takes on or off.");
            return 1;
        }

        Console.WriteLine($"  plan     : backlight {want}  (leaves the installed effect in place)");

        if (!commit)
        {
            Console.WriteLine("\n  DRY RUN. Nothing written. Add --commit to send it.");
            return 0;
        }

        bool ok = backend.SetBacklightEnabledAsync(want == "on").GetAwaiter().GetResult();
        Console.WriteLine($"  accepted : {ok}");
        return ok ? 0 : 1;
    }

    private static int RestoreDefault(DojoPerKeyBackend backend, bool commit)
    {
        Console.WriteLine("  plan     : restore HP's firmware default lighting");

        if (!commit)
        {
            Console.WriteLine("\n  DRY RUN. Nothing written. Add --commit to send it.");
            return 0;
        }

        bool ok = backend.RestoreFirmwareDefaults();
        Console.WriteLine($"  accepted : {ok}");
        Console.WriteLine();
        PrintEffect("now", backend.ReadDeviceEffect());
        return ok ? 0 : 1;
    }

    private static int Persist(DojoPerKeyBackend backend, bool commit)
    {
        Console.WriteLine("  plan     : store the current lighting to the MCU's flash");
        Console.WriteLine("             THIS IS A FLASH WRITE. It is what makes an effect survive a power");
        Console.WriteLine("             cycle, and it is not something to repeat casually.");

        if (!commit)
        {
            Console.WriteLine("\n  DRY RUN. Nothing written. Add --commit to send it.");
            return 0;
        }

        bool ok = backend.StoreToFlash();
        Console.WriteLine($"  accepted : {ok}");
        return ok ? 0 : 1;
    }

    // ── Printing and parsing ───────────────────────────────────────────────────────

    private static void PrintEffect(string label, DojoKeyboardMcu.EffectRecord? record)
    {
        if (record == null)
        {
            Console.WriteLine($"  {label,-9}: the MCU did not answer the readback");
            return;
        }

        var r = record.Value;
        string colors = string.Join(" ", (r.Colors ?? Array.Empty<(byte R, byte G, byte B)>())
            .Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}"));

        string palette = r.ColorNumber == DojoKeyboardMcu.ColorNumberPreset
            ? $"preset {r.ShowMode}"
            : $"{r.ColorNumber + 1} custom colour(s), {colors}";

        Console.WriteLine($"  {label,-9}: {r.Effect} ({(byte)r.Effect}), {palette}");
        Console.WriteLine($"             speed {r.Speed}, direction {r.Direction}, size {r.RippleSize}, " +
                          $"raindrop {r.RaindropFrequency}, brightness {r.Brightness}, " +
                          $"inner {r.InnerBrightness}, outer {r.OuterBrightness}");
    }

    private static List<(byte R, byte G, byte B)> ParseColors(string? spec)
    {
        var list = new List<(byte, byte, byte)>();
        if (string.IsNullOrWhiteSpace(spec)) return list;

        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string h = part.TrimStart('#');
            if (h.Length != 6) continue;
            try
            {
                list.Add((Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..], 16)));
            }
            catch { /* a malformed swatch is skipped, and the printed plan shows what survived */ }
        }

        return list.Take(4).ToList();
    }

    private static DojoKeyboardMcu.EffectSpeed ParseSpeed(string? s) => (s ?? "medium").Trim().ToLowerInvariant() switch
    {
        "slow" or "0" => DojoKeyboardMcu.EffectSpeed.Slow,
        "fast" or "2" => DojoKeyboardMcu.EffectSpeed.Fast,
        _ => DojoKeyboardMcu.EffectSpeed.Medium
    };

    private static DojoKeyboardMcu.EffectDirection ParseDirection(string? s) => (s ?? "right").Trim().ToLowerInvariant() switch
    {
        "inward" => DojoKeyboardMcu.EffectDirection.Inward,
        "outward" => DojoKeyboardMcu.EffectDirection.Outward,
        "left" => DojoKeyboardMcu.EffectDirection.RightToLeft,
        "up" => DojoKeyboardMcu.EffectDirection.Up,
        "down" => DojoKeyboardMcu.EffectDirection.Down,
        "cw" => DojoKeyboardMcu.EffectDirection.Clockwise,
        "ccw" => DojoKeyboardMcu.EffectDirection.CounterClockwise,
        _ => DojoKeyboardMcu.EffectDirection.LeftToRight
    };

    private static byte ParseByte(string? s, byte fallback) =>
        byte.TryParse(s, out byte b) ? b : fallback;

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
