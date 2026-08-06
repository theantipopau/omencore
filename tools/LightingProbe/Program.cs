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
//
// The default modes write nothing: no colour is set, no brightness is changed, no BIOS state is
// modified. --self-test is the exception, and it exists because the LampArray spec has no colour
// readback - so whether a write reached the keys is a question only a person looking at the
// keyboard can answer. It restores the device to its own effects on the way out.
//
//   --hold <seconds>   how long --self-test holds the pattern (default 10)
//   --static           --self-test writes once and leaves it, with no hold loop
//
// The vendor per-key path on interface mi_03 is NOT here. It speaks HP's own DojoPerKeyRGB format
// straight to the MCU and verifies nothing in this repo, so it lives with the machine
// investigation that produced it: omen-max-16/tools/hid/. What it found matters to this code
// though - that MCU acknowledges every colour write and displays none of them, and the mi_04
// LampArray below accepts writes it does not honour - so neither path can light this keyboard yet.

using OmenCore.Tools.LightingProbe;

if (args.Contains("--self-test"))
    return SelfTest.Run(args);

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
