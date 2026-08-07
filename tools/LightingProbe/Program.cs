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
// The vendor per-key path on interface mi_03 is NOT here. It speaks HP's own DojoPerKeyRGB format
// straight to the MCU and verifies nothing in this repo, so it lives with the machine
// investigation that produced it: omen-max-16/tools/hid/. What it found matters to this code
// though - that MCU acknowledges every colour write and displays none of them, and the mi_04
// LampArray below accepts writes it does not honour - so neither path can light this keyboard yet.

using OmenCore.Tools.LightingProbe;

if (args.Contains("--self-test"))
    return SelfTest.Run(args);

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
