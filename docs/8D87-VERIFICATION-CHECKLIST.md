# Board 8D87 — what is verified, and what is not

`ModelCapabilities.UserVerified` is a single flag over a profile with ~20 fields. On board `8D87`
every board-specific claim in the entry has now been confirmed on real hardware, so the flag is
**true**.

It did not start that way. This document was written while fan curves and performance modes were
still inherited from the adjacent MAX generation, and it recorded the flag as `false` on the rule
below that the flag is all-or-nothing. Those rows have since been measured — see
[Was not verified, now is](#was-not-verified-now-is) — and the flag was flipped in the same commit
that measured the last of them.

This document says exactly which claim rests on what, so the next person does not have to re-derive
it, and so that the flag is a decision with a checklist behind it rather than a guess.

## Verified on hardware

Board `8D87`, HP OMEN MAX 16-ak0098nr, BIOS **F.07**, EC **40.38**, by the machine's owner,
2026-08-01..05. Method was ACPI decompilation plus live measurement, cross-checked against three
physical power adapters, the EC tachometers and OMEN Gaming Hub's own readout.

| Field | How it was established |
|---|---|
| `SupportsEcPowerLimits = false` | The EC mailbox power block at `0xF5`–`0xF8` was forced to its high-power values with no effect on delivered watts — it is a mirror the SMU never reads back. |
| `SupportsFanControlEc = false` | The EC is memory-mapped at `0xFE700600`, not at the legacy port offsets. The legacy register map does not apply. |
| `SupportsUndervolt = false` | AMD Ryzen AI part; the Intel MSR undervolt path does not exist here. |
| `MaxModeDropChecksBeforeReapply = 1` | Corroborated by the observed performance-mode decay on this machine. |
| `Family`, `ModelYear`, `ProductId` | Firmware identification. |
| Thermal policy | `Default 0x28` byte 3 reads `0x01` (V1). The app drives this board as V2 via a model-name force-switch; the diagnostics export prints both and flags the disagreement. See the caveat below. |
| `FanZoneCount = 2` | `Default 0x10` returns 2, and two EC tachometers track independently. |
| **`MaxFanLevel = 60`** | Measured, was 100. `0x2D` sampled against the EC tachometers and OGH's readout at four points — `0`/0, `22`/2220, `47`/4680, `60`/6000 rpm — linear within ~1 %. |
| `SupportsRpmReadback = true` | Same four samples; the V1 `0x2D` level tracks the tachometers at every point. |
| **`HasPerKeyRgb = true`** | Topology probe — class `0x00020008`, command `0x2B`, null input, 4-byte return — returns `0x03` = `RgbPerKey`, stable across repeated reads. |
| `HasKeyboardBacklight = true` | Same probe: a lighting type is reported and it is neither `None` nor `Normal`. |
| **`HasFourZoneRgb = false`** | Was defaulted `true`. HP's own `FourZoneHelper.IsSupported` returns false for lighting type 3, so the four-zone path does not drive a per-key keyboard. |
| `HasMuxSwitch = true` | Inherited, now **measured** by switching modes and rebooting. In Discrete the internal panel is driven by the dGPU. See below. |
| **`SupportsAdvancedOptimus = false`** | Was inherited `true`. There *is* a mux, but it is routed at boot — the Smart Mux block stays disabled through a working switch. See below. |

### There is a mux; it is not Advanced Optimus

These are two different claims and this board splits them. BIOS setup offers **Hybrid / Discrete /
UMA**, taking effect on reboot; OMEN Gaming Hub exposes the same selector. Captured in Hybrid, in
Discrete, and again after returning to Hybrid.

| Reading | Hybrid | Discrete | Restored |
|---|---|---|---|
| `Legacy 0x52` | `00 00 00 00` | **`01 00 00 00`** | `00 00 00 00` |
| `AMD_PBS_SETUP 0x05` Primary Video Adaptor | 1 = IGD | **2 = PEG** | 1 = IGD |
| Internal panel driven by | Radeon 890M | **RTX 5080** | Radeon 890M |
| `nvidia-smi display_active` | Disabled | **Enabled** | Disabled |
| Smart Mux block (`0xB6`/`0xC7`/`0xC8`/`0xCA`) | all 0 | **all 0** | all 0 |

**`HasMuxSwitch = true`** because the internal panel changes which GPU drives it — in Discrete the
sole display is the built-in `CMN1652` at its native 2560×1600, attached to the RTX 5080, with the
Radeon reporting no active mode. Reversible: the restored capture matches the baseline on every WMI
and `AMD_PBS_SETUP` field.

**`SupportsAdvancedOptimus = false`** because Advanced Optimus means the mux moves under **live ACPI
control, without a reboot**, and the entire Smart Mux block reads zero in all three states. It stays
off through a switch that demonstrably works, so the routing decision is taken at boot by firmware
and there is no runtime control surface.

`Default 0x28` byte 7 = `0x07` ("graphics switching supported") is a **capability bitmask, not the
current mode** — byte-identical in all three states, so it describes the SKU family. Read the mode
from `Legacy 0x52`.

`Legacy 0x52` is genuine state rather than an ACPI timeout: a field that only ever returns zero
cannot return `0x01`. The size-gate control agrees — `Default 0x28` returns code `5` at the 4-byte
output size while `0x52` returns `0`, so the gate is live and `0x52`'s declared reply really is four
bytes.

No byte in the EC state window at `0xFE700600` tracks the mode; the 25 bytes that differ across the
three captures are fan and thermal values, and none returns to its Hybrid value.

Fan tachometers are at EC `0x70` (fan 1) and `0x5C` (fan 2). **`0x7E` is not a reliable second
tachometer** — it reads 0 at every manual fan setting while that fan is spinning, and under auto it
shadowed fan 1. `0x9F` is battery remaining capacity in mAh, **not** a GPU tachometer — a mistake
other projects have made on this board. OmenCore has never carried it.

### Caveat: this board is V1 and is driven as V2

`Default 0x28` byte 3 reports thermal policy **V1**, and the firmware **rejects the V2 fan
commands** — `0x37` returns `RTCD 6` and `0x38` returns `RTCD 4`, at every input and output buffer
size. Fan levels reach the app only because `GetFanLevel()` falls back to the V1 command `0x2D`.

That fallback is correct and works. But the model-name force-switch to V2 (`HpWmiBios.cs:858-866`)
also drives `MaxFanLevel = 100` (`:931-935`), which is wrong here because V1 levels are krpm/100.
This entry now sets `MaxFanLevel = 60` explicitly, and the model override returns from
`DetectMaxFanLevel` **before** the V2 branch, so the correct value is what takes effect. The
underlying force-switch logic is untouched and still affects any OMEN MAX board without an entry.

## Was not verified, now is

These three rows were the reason this document originally kept `UserVerified = false`. All three
have since been measured on the hardware.

| Field | Then | Now |
|---|---|---|
| `PerformanceModes` | inherited, included `"L5P"` | Measured in **delivered watts**, not return codes, with the `OGHP` gate armed: `Default 0x30` → ~102 W, `Performance 0x31` → 175.00 W, `Cool 0x50` → ~103 W. `SetFanMode` writes `NPCF.MODE` and the firmware consumes `(MODE & 0x0F)`, so Default and Cool both select nibble 0 and deliver the same power profile — Cool differs as *fan* policy, not as a power tier. All three bytes return `RTCD 0`, including the two that select the same profile, so acceptance was never the evidence. **`"L5P"` removed:** there is no such member in `HpWmiBios.FanMode`, so `SetPerformanceMode("L5P")` fell through to `FanMode.Default` — asking for L5P silently gave you Default. |
| `SupportsFanCurves = true` | inherited | Verified by RPM readback against the EC tachometers, not by return code. |
| `SupportsIndependentFanCurves = false` | inherited | Confirmed impossible on this board, so the `false` is now a measurement rather than a default. |

Method note worth keeping: `Performance` was re-run as a **positive control between every pair of
readings**, because a decayed `OGHP` gate reads 80.00 W and would otherwise masquerade as MODE 0.
Without that control the Default/Cool result would have been indistinguishable from a gate that had
simply lapsed mid-measurement.

## Rules for this entry

- **Verify by outcome.** A WMI command returning success does not mean the hardware did anything.
  For fan claims that means RPM readback; for power claims, watts. On this platform `RTCD == 0` is
  especially weak evidence — the ACPI returns `0` on its own timeout path.
- **Reads can be corrupted by OMEN Gaming Hub.** The EC command mailbox is a single shared buffer
  with no locking between OS agents, and OGH polls it continuously. During this verification,
  repeated identical reads returned OGH's replies to *its* questions until OGH was stopped. Prefer
  the EC tachometers (direct MMIO) for fan cross-checks, and distrust a value that changes between
  back-to-back identical reads.
- **`RTCD = 5` means the output buffer was the wrong size,** not "unsupported". A genuine rejection
  persists across every buffer size.
- **The flag is all-or-nothing.** It was flipped only once every board-specific row was measured. If
  a future field is added to this entry by inheritance rather than measurement, `UserVerified` goes
  back to `false` until it is checked. If the flag ever gains per-field granularity, revisit this.
- **What `UserVerified = true` does not claim.** It covers the board-specific fields in this entry.
  Fields that are platform-generic and untested here — `SupportsNetworkBoost`, and anything driven by
  a subsystem this board routes elsewhere — are not evidence-backed by the flag.
- **Do not widen to sibling boards.** `8D88`, `8DD5` and `8DD6` share this platform's firmware but
  none of them has been measured. See `ModelCapabilityDatabase.Vibrance25C1BoardIds`, which is a
  scope list for raw EC offsets and explicitly not a capability claim.
