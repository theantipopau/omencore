// LightingProbe - hardware verification harness for OmenCore's keyboard lighting detection.
//
// Drives the shipping OmenCore.Hardware types directly, so what is verified is what ships.
// Requires administrator rights for the WMI half.
//
//   LightingProbe.exe               both sections below                     [read-only]
//   LightingProbe.exe --wmi         BIOS lighting topology and capability   [read-only]
//   LightingProbe.exe --lamps       HID LampArray descriptors               [read-only]
//   LightingProbe.exe --map         walk every lamp and build an id -> key map [read-only]
//   LightingProbe.exe --autonomous <on|off>  device effect engine on/off  [no colour written]
//   LightingProbe.exe --self-test   drive a pattern a human can check       [WRITES colours]
//   LightingProbe.exe --key <key>   light ONE key, blank the rest    [WRITES, needs --commit]
//   LightingProbe.exe --read-effect     what the keyboard MCU is holding        [read-only]
//   LightingProbe.exe --effect <name>   install a device-side animation  [WRITES, --commit]
//   LightingProbe.exe --zones <colors>  static per-key colour, 4 bands  [WRITES, --commit]
//   LightingProbe.exe --keys W=FF0000,A=00FF00   colour named keys only  [WRITES, --commit]
//   LightingProbe.exe --lightbar <colors>        the LIGHT BAR, not the keyboard  [--commit]
//
// The default modes write nothing: no colour is set, no brightness is changed, no BIOS state is
// modified. --self-test and --key are the exceptions, and they exist because the LampArray spec has
// no colour readback - so whether a write reached the keys is a question only a person looking at
// the keyboard can answer. Both restore the device to its own effects on the way out.
//
//   --hold <seconds>   run a repaint loop for N seconds (--self-test defaults to 10; --key does
//                      ONE write unless this is given)
//   --static           --self-test writes once and leaves it, with no hold loop
//   --key <key>        key name (F4, Esc, A, Space), HID usage (0x3D), or a bare lamp id
//   --color RRGGBB     colour for --key (default FF0000)
//   --commit           required by --key; without it the key is resolved and nothing is sent
//
// --key is the narrower test: bands prove reports land somewhere, one lit key proves the lamp INDEX
// is right, which is what a per-key feature needs. Run --map first for the authoritative table.
//
// Measured 2026-08-06 on 8D87, after the keyboard was recovered from its stuck state: --key F4
// lights exactly F4, the map is correct, and ONE report is enough - it stays lit after the process
// exits. So per-key control does not need a repaint thread. The 30 Hz loop in --self-test was a
// workaround for a stuck keyboard plus a strobe bug in this tool, not for the protocol.
//
// BOTH INTERFACES WORK, and they do different jobs. mi_04 (the LampArray, above) is the static
// per-key colour path. mi_03 is the MCU's own command surface, where the animation engine lives:
// all twelve effects OMEN Gaming Hub offers are one command-3 frame each, rendered device-side. It
// is reached through --effect / --read-effect below, which drive OmenCore's DojoPerKeyBackend - the
// shipping type - rather than a copy of the protocol. Map and evidence:
// omen-max-16/reference/keyboard-mcu.md.
//
// An earlier note here said the MCU acknowledges colour writes and displays none of them, and that
// neither path could light this keyboard. Both halves were artefacts of a stuck EC state that has
// since been cleared, and of reading the wrong HP SDK for the protocol.

using OmenCore.Tools.LightingProbe;

if (args.Contains("--self-test"))
    return SelfTest.Run(args);

// Drives the per-key editor's view-model, whose failures were all above the transport.
if (args.Contains("--map-editor"))
    return MapEditor.Run(args);

// The light bar is a different device on a different transport, so it gets its own entry rather
// than a flag inside the keyboard path.
if (args.Any(a => a.StartsWith("--lightbar", StringComparison.Ordinal)))
    return LightBar.Run(args);

// Ahead of --key: --read-effect and friends are the MCU path, and --key is the LampArray one.
if (args.Contains("--effect") || args.Contains("--read-effect") || args.Contains("--watch-effect") ||
    args.Contains("--zones") || args.Contains("--keys") || args.Contains("--brightness") ||
    args.Contains("--backlight") || args.Contains("--restore-default") || args.Contains("--persist"))
{
    return PerKey.Run(args);
}

if (args.Contains("--key"))
    return SetKey.Run(args);

if (args.Contains("--map"))
    return LampMap.Run();

if (args.Contains("--autonomous"))
    return Autonomous.Run(args);

bool wmi = args.Contains("--wmi");
bool lamps = args.Contains("--lamps");
if (!wmi && !lamps) { wmi = lamps = true; }

int rc = 0;
if (wmi) rc |= Wmi.Run();
if (lamps) rc |= Lamps.Run();
return rc;
