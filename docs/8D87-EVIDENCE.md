# Board `8D87` — evidence behind the claims in this repo

Several field layouts asserted in OmenCore's source — `ShippingAdapterPowerRating`, the six-value
`SmartAdapterStatus` enum, `IsTwoBytePL4Support`, the `Default 0x22` GPS-temperature byte — cannot be
checked by reading OmenCore. They came from **static decompilation of HP's own OMEN Gaming Hub
binaries**, and from **live measurement** on one machine.

This document is the citation for those claims, so a reviewer can locate the original without a link
to a private tree. It is a summary of provenance, not a copy of HP's code: it names the assembly and
line where each field is read, and states what was measured, in what units, with what control.

**Scope.** Everything here is board `8D87`, one physical machine — HP OMEN MAX 16-ak0098nr, BIOS
**F.07**, EC **40.38**, Ryzen AI 9 HX 375 (Strix Point, family `1Ah` / model `24h`), RTX 5080 Laptop
(PCI `0x2C19`, subsystem `103C`). Nothing here should be assumed to generalise to another OMEN board
without re-measurement. Several findings *are* corrections to other projects' register maps that were
correct on their own hardware.

---

## 1. Two evidence classes, and what each is good for

| Class | Method | Good for | Not evidence for |
|---|---|---|---|
| **Static** | Decompilation of HP's shipping assemblies. 309 assemblies, **not obfuscated** — namespaces, method names and enum members intact. | Field layout, arity, control flow, HP's own naming and thresholds. | Runtime behaviour. A field HP reads may be gated by something never reached on a given SKU. |
| **Measured** | Live instrumentation on the machine: WMI replies, EC reads, `nvidia-smi enforced.power.limit`, Windows `\Energy Meter(*)\Power` counters. | What the hardware actually does. | Layout of anything not exercised. |

Where the two disagree, static wins on **layout and naming**; measurement wins on **behaviour**. One
such disagreement is recorded in §2.1 and resolved in favour of static.

The epicentre of the static work is `HP.Omen.Core.Common` (54,462 lines decompiled) and
`HP.Omen.Background.PerformanceControl`. Line numbers below are into those decompilations; they locate
a claim, they are not stable identifiers across HP releases.

---

## 2. Static: field layouts taken from HP's own accessors

### 2.1 `Default 0x28` `byte[0..1]` is the SKU's adapter wattage

`HP.Omen.Core.Common.cs:34384` — HP's own accessor:

- reads exactly `systemDesignData[0] | (systemDesignData[1] << 8)`
- is named **`ShippingAdapterPowerRating`**
- is compared against the literals **200** and **280** (`:34409`, `:34482`) — BIOS performance-mode
  support, and TGP/PPAB enable

This machine reads `0x014A` = **330**, on a laptop whose shipping adapter is 330 W.

A bitwise reading of the same field as a 16-bit status-flag struct circulates and renders those
thresholds as `>= 0x00C8` and `>= 0x0118`. Those are the decimal wattages 200 and 280. **The field is
watts.**

Consequence for this repo: with `Legacy 0x0F` (§2.2) giving the *connected* rating, OmenCore can read
**both halves** of the comparison HP's adapter gate makes — required vs connected — rather than
accepting a binary verdict.

### 2.2 `Legacy 0x0F` is fully decoded

Four-byte reply. HP's accessors, `HP.Omen.Core.Model.Device.cs:9460-9481`:

| Byte | HP's accessor | Meaning |
|---|---|---|
| `[0]` | `GetSmartAdapterStatus` | the enum in §2.3 |
| `[1]` bit 7 | `GetSupportBarrel` | `(data[1] & 0x80) > 0` — barrel jack supported |
| `[2]` | `GetUsbcDesignRating` | `data[2] * 5` — USB-C **design** rating, W |
| `[3]` | `GetPowerRating` | `data[3] * 5` — **connected adapter rating, W** |

**`0xFF` on `byte[3]` is a sentinel meaning *unknown*, decoding to 0** — check it before treating a
`0` rating as "no adapter".

Measured across three physical adapters, exact each time:

| Adapter | Reply | `byte[3]` × 5 |
|---|---|---|
| 330 W barrel | `01 C2 00 42` | **330** |
| 280 W barrel | `02 C2 00 38` | **280** |
| 200 W barrel | `02 C2 00 28` | **200** |

### 2.3 `SmartAdapterStatus` has six values, not five

`HP.Omen.Core.Common.cs:36545`:

| Value | Name |
|---|---|
| `-1` / `0xFF` | `Error` |
| `0` | `NotSupported` |
| `1` | `MeetsRequirement` |
| `2` | `BelowRequirement` |
| `3` | `BatteryPower` |
| `4` | `NotFunctioning` |
| **`5`** | **`ConnectedTypeC`** |

Value `5` is the one most third-party sources omit. It is **not** a fault state — it is USB-C PD
connected, and HP gives it its own comparison logic in `IsLowWattage`
(`HP.Omen.Core.Model.Device.cs:9484-9493`): the connected rating is compared against the USB-C
*design* rating, instead of the binary meets/below test used for the barrel path. There is also a
special case where barrel support is present and the USB-C design rating is 0.

So `ConnectedTypeC` must not be folded into "anything that is not `MeetsRequirement`".

### 2.4 `Default 0x22` byte 3 is a GPS temperature threshold

`HP.Omen.Background.PerformanceControl.cs:5970` builds the payload as
`[cTGP, ppab, dState, gps]`, and every caller passes a temperature into the fourth byte
(`:1720`, `:1661`).

OmenCore's existing `peakTemperature` naming was therefore **right**, and a "spare" reading is wrong.

The value is the output of HP's `IRHandler`, a closed loop driven by the chassis **infra-red skin
sensor**, bounded by two `PlatformSettings` values: `GpsMaxTemperature = 87` (un-throttled) and
`GpsMinTemperature = 75` (IR-overheat response only).

**Why this repo still sends 0.** A replacement with no IR loop is permanently in the un-throttled
state. Sending the un-throttled bound `87` would assert to the firmware that the chassis is cool — a
claim we have no sensor to justify — and byte 3 is **not** in the `0x21` readback, so the write cannot
be verified by outcome. The decode is recorded at `HpWmiBios.HpGpsTemperatureThresholdC` and the byte
is deliberately left at 0. See `docs/8D87-OMEN-MAX-16-SUPPORT-PLAN.md` §2.

### 2.5 `Default 0x28` full decode

Reply is 128 bytes, 11 non-zero here: `4A 01 3A 01 03 00 01 07 3C 00 03 00`.

| Byte | Mask | HP's name | Here | Meaning |
|---|---|---|---|---|
| `[0..1]` | 16-bit LE | `ShippingAdapterPowerRating` | `0x014A` | **330 W** (§2.1) |
| `[3]` | whole | `GetThermalPolicyVersion` | `0x01` | **V1** |
| `[4]` | `0x01` | `IsSwFanControlSupport` | `0x03` | true |
| `[4]` | `0x02` | `IsExtremeModeSupport` | | **true** |
| `[4]` | `0x04` | `IsExtremeModeUnlock` | | **false** |
| `[4]` | `0x08` | `IsDTBiosControl` | | false |
| `[4]` | `0x10` | **`IsTwoBytePL4Support`** | | **false** |
| `[5]` | whole | `PL4DefaultValue` | `0x00` | 0 |
| `[6]` | `0x01` | `IsBiosDefinedOcSupport` | `0x01` | true |
| `[7]` | whole | `GpuModeSwitch` | `0x07` | capability bitmask, **not** current mode |
| `[8]` | whole | `DefaultCpuPowerLimitWithGpu` | `0x3C` | **60 W** concurrent CPU budget |
| `[9]` | `0x0F`/`0xF0` | `LoadLineSupportLevels` / `DefaultLoadLine` | `0x00` | 0 / 0 |
| `[10]` | `0x01`,`0x02`/`0x04`/`0x08` | `ChangeIrSensorToBoard` / `IsPchOverheatSupport` / `IsVrSensorSupport` | `0x03` | false / false / false |

**`IsTwoBytePL4Support` is the entry that matters beyond this board** — it changes the wire format of
`Default 0x29 SetPL4`. Getting it wrong is a silent write-corruption class of bug on any board where
the bit is set.

**The reply is static.** Byte-identical on the 330 W and 200 W adapters, and byte-identical in Hybrid
and Discrete GPU modes. It describes the SKU — not the connected supply, and not the current graphics
mode. Read the connected adapter from `Legacy 0x0F` and the GPU mode from `Legacy 0x52`.

`byte[0..1] = 4A 01` also establishes the board strap **`BYID = 0`** on this machine, because the
firmware writes those bytes only on the `BYID == Zero` branch.

### 2.6 Where HP's capability data actually lives

Two structural findings that bear on this repo's design, both from the static work:

- **There is no EC register map anywhere in OGH.** A grep across all four decompiled roots for the EC
  MMIO base, `EmbeddedController`, `Ec{Read,Write}`, `{Read,Write}EcRam`, `LpcACPIEC` and raw port I/O
  returns **zero hits**. HP reaches the EC only through AML `GCxx`, and the AMD SMU through AMD's own
  driver. A per-board table of EC offsets has no counterpart in HP's software to copy.
- **No power decision in OGH is keyed on board ID.** The board table gates hotkeys, chassis and
  lighting only. HP's capability source is the runtime `Default 0x28` query. This repo's per-board
  capability model is its own invention — legitimate, but it should not be mistaken for HP's.

Consequence, and it is why the board list in `ModelCapabilityDatabase.Vibrance25C1BoardIds` is scoped
the way it is: use board ID as a **safety scope for raw EC offsets**, and prefer `0x28` for
**capability**.

### 2.7 A driver that is not a stub, and one that is

- `hpomencustomcapdriver.sys` imports exactly `DbgPrintEx` and `RtlCopyUnicodeString` plus WDF loader
  stubs — no `MmMapIoSpace`, no port I/O, no `IoCreateDevice`. It **cannot** touch the EC by
  construction. Do not investigate it as an arming agent.
- `HpReadHWData.sys` is the one that is not a stub. It exposes a kernel-mode WMI passthrough whose
  validator checks the literal `SECU` tag and executes via `IoWMIExecuteMethod` on device
  `ACPI\PNP0C14\0_0`. It also enforces a **caller-identity allowlist of eight named HP binaries** —
  the only privilege asymmetry located between HP's writes and a third party's.

---

## 3. Measured: the numbers, and the controls that make them trustworthy

### 3.1 Performance modes are two power profiles, not three

`Default 0x1A` writes `NPCF.MODE`, and the firmware consumes **`MODE & 0x0F`**. The named mode bytes
therefore collapse onto low nibbles. Measured in `nvidia-smi enforced.power.limit`:

| Byte | Name | `& 0x0F` | `enforced` |
|---|---|---|---|
| `0x30` | Default | 0 | ~102 W |
| `0x31` | Performance | 1 | **175.00 W** |
| `0x50` | Cool | 0 | ~103 W — **same power profile as Default** |
| `0x34` | *(unnamed)* | 4 | **175.00 W** — reachable, named by no consumer project |

All of these return `RTCD 0`, **including the two that select the same profile** — so acceptance says
nothing about which profile you got. `Cool` is a fan/thermal policy, not a power tier.

**The control that makes this valid.** `OGHP` decays to 0 within ~2 minutes of OMEN userland being
killed, and a gate-down machine reads 80.00 W while `MODE 0` reads ~102 W — close enough that a
dropped gate mid-sweep reads as "this mode selects MODE 0". `Performance` was therefore re-run as a
positive control between **every pair** of readings, and the run is void unless every control hits
175 W. The first sweep attempted here produced exactly that false reading, and its `Cool` row was
discarded.

`"L5P"` is not `MODE 4` or anything else: the string has no byte anywhere in the WMI path. It was
removed from this board's profile because `SetPerformanceMode("L5P")` fell through to
`FanMode.Default` — asking for L5P silently gave you Default.

### 3.2 Fan tachometers are `0x70` and `0x5C`

Both 16-bit raw RPM in the EC state window.

`0x7E`/`0x5E` are **commanded setpoints, not tachometers**, gated by the level byte `0x5B`. The
discriminating measurement, against a full OGH "Full fan speed" ramp: `0x70`/`0x5C` climbed 0 → 4980
rpm in step with the audible spin-up, while `0x7E`/`0x5E` read 2000/2200 throughout and then **fell to
0/0 while the fans were still turning at 4980**. A tachometer cannot be invariant while its fan
changes speed.

This was asserted the wrong way round in an earlier draft of this work and corrected before any PR
opened.

### 3.3 `MaxFanLevel` is 60, not 100

This board reports thermal policy **V1** and refuses the V2 fan commands outright — `0x37` returns
`RTCD 6` and `0x38` returns `RTCD 4`, at every input and output buffer size tried. Fan levels
therefore arrive through the V1 `0x2D` path in krpm/100, **not percent**.

With `MaxFanLevel = 100`, `MapFanPercentToWmiLevel` became an identity: a request for 50% wrote raw
level 50 ≈ 5000 rpm — near maximum, not half.

Sampled against the EC tachometers and OGH's own readout at four points — `0`/0, `22`/2220, `47`/4680,
`60`/6000 rpm — linear within ~1%, ceiling **60**.

### 3.4 The AMD Curve Optimizer reaches the silicon

Verified by outcome, not by SMU status. Three alternating pairs under sustained all-core load
(`tools/SmuProbe --outcome`):

| | Effective clock | Core power |
|---|---|---|
| CO 0 | 3148 MHz | 21.40 W |
| CO −25 | 3301 MHz | 20.10 W |
| **paired delta** | **+4.9% mean** (range +4.8…+4.9%, consistent sign in all 3 pairs) | −1.3 W |
| sham control (CO 0 both phases) | **−0.1%** | — |

More clock at less core power is the signature of a real undervolt, and the baseline returns to
~3148 MHz every time the offset is removed.

**Why it is paired.** A single before/after comparison measures thermal drift as readily as an effect:
an earlier version of this harness reported **+4.8% and −5.3% for the same offset** on consecutive
runs, depending only on how hot the machine started. Alternating and pairing each offset phase against
the baseline immediately before it makes monotonic drift cancel. The sham control establishes the
noise floor rather than assuming one.

Note also that the SMU returns `Ok` for **both** MP1 `0x4C` and PSMU `0x5D` on this part, and both
measurably work — so the message id was never the reason Curve Optimizer did nothing. The transport
was.

### 3.5 Legacy EC offsets do not port to this board, and one of them lies

`0x95`, `0xBA`, `0xCE`, `0xCF` are the pre-2024 OMEN layout. This board's EC is memory-mapped at
`0xFE700600`.

The instructive one is a third-party map placing single-byte krpm fan RPM at `0x34`/`0x35`. On this
board those two bytes are the ASCII characters `A` and `0` from the serial number `6LJCA0HTZLC9G0`,
and a `<= 80` plausibility test passes them — yielding a confident **6500 / 4800 rpm** from string
data.

**A wrong offset that returns a believable number is worse than one that returns `0xFF`.** This is why
raw EC offsets in this repo are board-scoped rather than shared.

### 3.6 There is a mux; it is not Advanced Optimus

Two different claims, and this board splits them.

- **The mux is real.** In Discrete the internal panel is driven by the RTX 5080, and `Legacy 0x52`
  reads `01`. Measured across a reboot in each direction: Hybrid → `00`, Discrete → `01`, back to
  Hybrid → `00`. So `HasMuxSwitch = true`.
- **Advanced Optimus is off.** That is the *dynamic* mux, moving under live ACPI control with no
  reboot. The firmware block stays disabled through a **successful** mode switch — Smart Mux Support,
  Acpi Control, MDM Support and Display Panel Multiplexer all read `0` in Hybrid, Discrete *and* after
  returning to Hybrid. Routing is decided at boot. So `SupportsAdvancedOptimus = false`.

`Default 0x28` byte 7 = `0x07` does **not** settle either claim: it is byte-identical in all three
modes, so it describes the SKU family, not the current state.

### 3.7 An idle dGPU is not iGPU-only mode

A dGPU in D3 reports `Win32_VideoController` `Availability: 8` (Off Line) with no resolution, refresh
rate or bit depth — indistinguishable, to an adapter-activity check, from a machine with the dGPU
switched off in firmware. On this board that made Hybrid render as "Integrated GPU Only" with a
healthy RTX 5080 enumerated.

Read the mode from `Legacy 0x52`. The only way to make an adapter-activity check agree is to wake the
dGPU, which spends real power to answer a question the firmware already answers.

Related instrument trap: `nvidia-smi` reports `P0`/25 W at idle **because the query itself wakes the
GPU**, then holds D0 for 120–150 s. Polling to observe D3 re-entry is what prevents it. Read
`DEVPKEY_Device_PowerData`, which does not wake the device.

---

## 4. Closed questions — do not re-derive these

Each was established and is closed. Recorded because the natural next step for anyone picking this up
is to rediscover a dead end and mistake it for a lead.

| Claim | Status |
|---|---|
| `Default 0x10` is the `OGHP` arming command | **RETRACTED.** `GC01`/`GC10`/`GC28`/`GC2B` are all 0-arg read-only methods. If a capture shows OGH sending `0x10`, that is a read. |
| `DSTA` via `Default 0x22` is a usable lever | **NO.** `GC22`'s own tail calls `_Q73`, which overwrites `DSTA` from `PROH`. Writes snap back within a second on *either* adapter. |
| `OGHP` has an AML writer | **NO.** Across all four decompiled tables it appears only as an `External`, a field declaration, and two `== Zero` reads. The EC owns it. |
| `OGHP` survives a reboot | **NO.** And **the bit is not a valid readout** — it read `1` on a boot where the grants had been revoked. Test with `Default 0x21` or delivered watts. |
| The mailbox block at `0xF5`–`0xF8` is a power lever | **NO.** It is the EC *publishing* a decision; forcing it to the 330 W values changed nothing. The SMU never reads it back. |
| `PROH`/`DSTA` values 3/4/5 are intermediate power tiers | **NO.** They are `SmartAdapterStatus` (§2.3). There is no middle tier. |
| A matched fan-level readback proves the fan changed speed | **NO.** It proves the firmware accepted the command. `0x38` is refused on this board, so no physical tachometer reached the old verification path at all — which made "level matched" the only evidence checked, and incapable of failing. Measured: level 28 echoed back while five consecutive samples read 0 rpm. |
| A mode sweep can be run sequentially | **NO.** See the `OGHP` decay control in §3.1. |
| `ZenStates-Core` can read this SMU | **NO.** No map for family `1Ah`/model `24h`. Use RyzenAdj ≥ 0.19.0, which recognises Strix Point. |
| `GetSystemFirmwareTable(ACPI,'SSDT')` enumerates the loaded SSDTs | **NO.** It returns only the first table per signature — an enumeration loop yields 41 copies of one table. Read `HKLM:\HARDWARE\ACPI` instead: 37 distinct tables. |

---

## 5. Method rules these came from

Four separate false positives in this work had the same shape. They are recorded as rules because the
same shapes recur in this repo's own history.

1. **Check the outcome, not the request.** A WMI command returning `RTCD 0` means the firmware
   accepted it, not that the hardware did anything. Verify fan claims in RPM and power claims in
   watts. On this platform `RTCD 0` is especially weak: the ACPI returns `0` on its own timeout path,
   so an all-zero buffer at `RTCD 0` is indistinguishable from no answer. **Non-zero is the
   trustworthy direction.**
2. **Compare against the requested value, not the previous sample.** A write the EC has already undone
   looks stable if you only diff consecutive polls.
3. **Adjacency in a high-rate capture is not causation.** The `Default 0x10` claim was published and
   retracted on exactly this error, and one grep of the static tables refuted it. **When a capture and
   a decompiled table disagree, the table wins.**
4. **Two agreeing snapshots are not a reproducibility filter.** One adapter-keyed byte passed that bar
   and was still wrong. Vary the input across more than two states.
5. **`RTCD 5` means the output buffer was the wrong size**, not "unsupported". A genuine refusal
   persists across every buffer size tried.
6. **Reads can be corrupted by OMEN Gaming Hub.** The EC command mailbox is a single shared buffer with
   no locking between OS agents, and OGH polls it continuously. Repeated identical reads returned OGH's
   replies to *its* questions until OGH was stopped. Distrust a value that changes between
   back-to-back identical reads.
7. **Check the instrument against a known value before believing it.** PowerShell's `-shl` preserves
   the left operand's type, so `[byte]0x0A -shl 8` is `0`. A 16-bit EC field assembled that way
   silently returns only its low byte — one tool reported a fan at 200 rpm while the raw window held
   `C8 0A` = 2760. Nothing in the output looked wrong on its own.

---

## 6. Provenance

The full lab notebooks are outside this repository — they are long (one is over 2,500 lines), they
record method and measurement rather than conclusions, and they contain their own retractions. This
document is the distilled citation; the source tree separates `reference/` (current truth) from
`investigation/` (how it was established), and its notebook filenames are:

| Document | Covers |
|---|---|
| `01-bios-f07-static.md` | static firmware analysis |
| `02-power-gate-measurements.md` | live power measurement |
| `03-nvpcf-tgp-mechanism.md` | the NVPCF/TGP mechanism, ACPI-decompiled |
| `04-ogh-binaries.md` | static decompilation of HP's OMEN Gaming Hub |
| `05-keyboard-lighting.md` | lighting topology |
| `06-hpreadhwdata-driver.md` | `HpReadHWData.sys` |
| `08-ec-port-aliasing.md` | ACPI EC port ↔ MMIO aliasing |
| `09-capability-verification.md` | per-field capability verification |

Two things to carry from them if this is ever reopened:

- **Several findings there were corrected more than once before settling.** Prefer a distilled
  statement over a raw section, and treat anything marked untested as untested.
- **The port↔MMIO aliasing is established for *reads only*, on one adapter state.** Nothing may write
  EC RAM on that basis. See `docs/8D87-OMEN-MAX-16-SUPPORT-PLAN.md` §5.1.
