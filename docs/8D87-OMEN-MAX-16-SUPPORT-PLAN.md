# Board `8D87` — OMEN MAX 16 (2025, AMD) Support Plan

**Status:** Tier 1 is largely **implemented** — see §8 for what has landed and what has not. Tier 2 (the AMD SMU transport) is fixed; Tier 3 (the adapter gate) is not started and is gated on evidence stated in §5.1.
**Target board:** `8D87` — HP OMEN MAX 16-ak0098nr, Ryzen AI 9 HX 375 (family `1Ah`/model `24h`, Strix Point) + RTX 5080 Laptop (`10DE:2C19`), BIOS F.07, EC 40.38.
**Sibling boards on the same HP platform (`Vibrance25C1`):** `8D88`, `8DD5`, `8DD6`.
**Related existing DB entry:** `AK0003NR` (`ModelNamePattern = "max 16 ak0"`), same family, different SKU.

---

## 0. Where this comes from, and how much to trust it

Two evidence classes, from an investigation conducted outside this repo on one physical machine:

| Class | Method | Good for | Not evidence for |
|---|---|---|---|
| **Measured** | ACPI decompilation plus **live instrumentation** — WMI replies, EC reads, `nvidia-smi enforced.power.limit`, Windows Energy Meter counters. | What the hardware actually does. | Layout of anything not exercised. |
| **Static** | Decompilation of HP's own OMEN Gaming Hub binaries — 309 assemblies, **not obfuscated**. | Field layout, arity, control flow, HP's own naming and thresholds. | Runtime behaviour. |

Where they conflict, **static wins on layout and naming; measurement wins on behaviour.** One such
conflict is load-bearing here: the `Default 0x28` `byte[0..1]` field was read behaviourally as a
status-flag struct and is, per HP's own accessor, watts.

> [!IMPORTANT]
> **The distilled evidence for every claim in this document lives in
> [`8D87-EVIDENCE.md`](8D87-EVIDENCE.md), in this repository.** It cites HP's assembly and line for
> each static claim, and the control that makes each measurement trustworthy.
>
> The original lab notebooks are outside this repo and are deliberately not linked: they are long (one
> is over 2,500 lines), they record method rather than conclusions, and they contain their own
> retractions. Notebook filenames are listed in `8D87-EVIDENCE.md` §6 for provenance.

**Two things to keep in mind while reading:**

1. **Five plausible findings on this board turned out to be false, and four failed the same way.** `PROH` at `0x8F`, `Default 0x10` as the arming command, `HPBA` as adapter-keyed, `P3TV` as a working lever, and "the exploit is a durable one-shot" were each established and then disproved — four of them because the *request* was checked instead of the *outcome*. A command that returns success is not a watt. That failure mode is directly relevant to how we implement this: see §5.2, and [`8D87-EVIDENCE.md`](8D87-EVIDENCE.md) §4 for the current statement of each closed question.
2. **This is one machine and one investigator**, not this project's normal field-report channel. `UserVerified` on this board's entry is now `true` because every board-specific field in it was measured on the hardware — see [`8D87-VERIFICATION-CHECKLIST.md`](8D87-VERIFICATION-CHECKLIST.md) for which claim rests on what, and what the flag does not cover. Individual items below carry their own evidence assessment.

---

## 1. The mechanism, compressed

Three independent clamps. Understanding that they are independent is the whole point — earlier work in this repo and upstream conflated them.

```
Stage 1  GPU 105 W -> 175 W     OGHP  (EC 0x59 bit 1)     "OMEN Gaming Hub Present"
Stage 2  GPU 175 W ->  35 W     PROH  (EC 0x90)           adapter verdict, on non-330 W supplies
Stage 3  CPU  73 W ->  25 W     AMD SMU limits            adapter-keyed, entirely outside NVPCF
```

**Stage 1 — `OGHP`.** `NVPCF._DSM`'s whole body is gated on `OGHP == 1`. At boot, `_REG` sees `OGHP == 0` (no OMEN software) and zeroes `NPCF.CTGP`/`NPCF.DTGP`, revoking the configurable-TGP adder. Result: 105 W base TGP instead of 175 W (105 + `ACBT` 560/8 = 70 W adder = 175). Repaired at runtime by holding `OGHP` from a ~2 ms loop while firing `GC22` (`Default 0x22`) so the driver's `_DSM` re-read lands inside the window. The resulting `Notify` is never retracted, so the hold can be dropped immediately after. **Measured, with zero OMEN processes running.**

**Stage 2 — `PROH`.** The EC classifies the adapter into `ADID` (3-bit class) and writes its verdict to `PROH` at EC `0x90`. `_Q73` copies `PROH` into `DSTA` and issues `Notify (PEGP, 0xD1..0xD5)`; the NVIDIA driver maps `0xD1` → 175 W and `0xD2` → 35 W. Holding `PROH = 1` from a ~2 ms loop and triggering `_Q73` via `GC22` took the 200 W adapter from **35 W to 175 W**. The EC reasserts `PROH` on a ~100 ms cycle, but the notify survives.

**Stage 3 — the CPU.** Not reachable through `PROH`, `ADID`, `RPWR`, or anything in NVPCF. It is three ordinary AMD SMU limits (`STAPM`, `PPT FAST`, `PPT SLOW`) pinned at exactly 25.000 W, plus `PPT APU` at 45. Writing all four with RyzenAdj took the APU from **25.02 W to ~51 W sustained**, with OGH not running and **no hold thread needed** — the EC does not fight back here.

**None of the three survives a reboot.** A replacement for OGH is a resident agent, not a one-shot script.

---

## 2. What this repo currently believes, and where it is wrong

Repo-wide grep for `OGHP`, `PROH`, `NVPCF`, `CTGP`, `DTGP`, `0xFE7006`, `_Q73`, `P3TV` returns **zero hits**. None of this mechanism is known to OmenCore today.

| Location | Current state | Reality on `8D87` |
|---|---|---|
| `ModelCapabilityDatabase.cs:928-954` | `8D87` entry, `UserVerified = false`, notes say "inferred from adjacent MAX ak/ah generation" | Now substantially characterised. See §3.5. |
| `PowerLimitController.cs:17-25` | EC power offsets `0xC0`–`0xC5`, self-described "EXAMPLE - varies by model!" | Meaningless on this board. `SupportsEcPowerLimits` must stay `false`. |
| `PawnIOEcAccess.cs:27-28` | EC via ACPI ports `0x62`/`0x66`, offset allowlist `0x2C`…`0xF4` | The ports **are** declared (`EC0._CRS`), and AML also reaches the EC through an MMIO alias at `0xFE700600`. The two views **are the same 256 bytes for reads** — measured, three sandwiched runs. **Writes through the ports remain unproven**, so nothing may write EC RAM that way yet. See §4 and §5.1. |
| `HpWmiBios.cs:192` | `BuildGpuPowerPayload` returns `peakTemperature = 0x00` | Byte 3 of `0x22` is a GPS temperature threshold in °C. HP passes 87 — but only as the *most permissive* output of a closed loop it drives from the chassis IR sensor, revising it to 75 on overheat. **Deliberately left at 0**: with no IR loop, sending 87 pins the firmware in the chassis-is-cool state, and byte 3 is not in the `0x21` readback so it cannot be verified by outcome. Decode recorded at `HpGpsTemperatureThresholdC`. |
| `HpWmiBios.cs:636-642` | `SetFanMode` sends `Default 0x1A` `{0xFF, mode, 0, 0}` — treated as fan-only | This is `GC1A`, which writes `NPCF.MODE` from **byte[1]**. It is the GPU/CPU power-profile selector, not just a fan knob. See §3.1. |
| `RyzenSmu.cs:68-126` | `Initialize()` opens PawnIO | Never calls `pawnio_load`, so no module backs `ioctl_pci_read_config_dword` / `ioctl_pci_write_config_dword`. Every other PawnIO consumer in the repo — `PawnIOEcAccess`, `LibreHardwareMonitorImpl`, `HardwareWorker` — resolves and calls it; `RyzenSmu` is the sole exception. **The AMD SMU path is dead today**, confirmed by inspection at `RyzenSmu.cs:102-104`. |
| `AmdUndervoltProvider.cs:300` | `SetStapmLimit` only | Three more limits are required; raising only stapm/fast/slow lands on a hard 46 W ceiling. |
| Adapter awareness | None. Grep for "adapter" across `src/` returns Linux battery paths and one ADL delegate name. | `Legacy 0x0F` reports the connected adapter's real wattage. See §3.3. |
| `DiagnoseCommand.cs:298-301` | Board `8D41` note: "Linux GPU TGP may stay capped near base power if hp-wmi does not send the … Dynamic Boost unlock that Windows OGH sends" | Correct symptom, now with a named cause (`OGHP`). The note can be replaced with the actual mechanism. |

---

## 3. Tier 1 — pure WMI. No new driver, no EC writes, no transport decision.

Everything in this tier uses `HpWmiBios.SendBiosCommand` as it exists today.

### 3.1 `MODE` is a power lever, and `Cool` currently costs 70 W of GPU

**Evidence:** firmware (decompiled `wmi_live.dsl` + `nvpcf_live.dsl`), mechanism doc §7a/§7p. Not yet measured on this board for MODE 4.

`GC1A` does `CreateByteField (Local2, One, PWMD); \_SB.NPCF.MODE = PWMD` — byte[1] of the `0x1A` payload. `NPCF` then selects a profile row on `MODE & 0x0F`. For `NVXX = 2` (this GPU):

| `MODE & 0x0F` | `ACBT` → GPU adder | `ATPP` → CPU limit w/ GPU | GPU `enforced` |
|---|---|---|---|
| `0` | 0 → **0 W** | `0x0118` → 35 W | 105 W |
| `1` | `0x0230` → **70 W** | `0x01E0` → 60 W | 175 W |
| **`4`** | `0x0230` → **70 W** | `0x0320` → **100 W** | 175 W |

Cross-referenced against the current `FanMode` enum (`HpWmiBios.cs:133-142`):

- `Default = 0x30` → low nibble **0**
- `Performance = 0x31` → low nibble **1**
- `Cool = 0x50` → low nibble **0**

**Consequence, and it is a live bug:** selecting **Cool** on this board sends `MODE = 0x50`, whose low nibble is 0, which revokes the entire configurable-TGP adder and drops the GPU to 105 W. That is not a fan setting, and users have no way to know.

**Work items:**

- **T1.1** Add a `MODE`-aware layer over the existing `0x1A` path, board-gated. Do not reuse `FanMode` for this — the coupling is the bug. Surface, at minimum, that Cool revokes the GPU adder on `Vibrance25C1` boards.
- **T1.2** Add `MODE 4` (`0x34`) as an option. Same GPU adder as MODE 1, `ATPP` 60 → 100 W.
  - **Caveat:** §7p measured the CPU already reaching `ATPP = 60 W` momentarily and settling at 45 W at 86 °C under a combined load. The 60 W ceiling is real and reachable but is *not* what holds the sustained figure down — so MODE 4 may buy nothing until cooling improves. Ship it as a control, not as a promised gain.
  - Both `MODE` and `OTPP` sit inside `If ((OGHP == One))`, so neither does anything without the Stage-1 arm.

### 3.2 This closes an open evidence gate already tracked in this repo

`docs/ROADMAP_v4.0.0.md:349-350` records that `8D87` and `AK0003NR` are **the only two 2025 MAX boards in the database without `AllowDecoupledWmiThermalPolicyFallback = true`**, that the one-line fix was identified, and that it was deliberately withheld pending field evidence — because nobody knew what the WMI thermal-policy write actually *did* on the AMD path.

We now know exactly what it does: `SetFanPerformanceModeSerialized` → `_fanController.SetPerformanceMode` → `SetFanMode` → `Default 0x1A` → `GC1A` → `NPCF.MODE`. With the flag off, `PerformanceModeService.Apply()` sends nothing at all on this board (EC limits blocked by `SupportsEcPowerLimits = false`, WMI fallback disabled), so `MODE` stays at its `Name (MODE, Zero)` initializer — low nibble 0 — and the GPU adder is never granted.

That is a complete mechanical explanation for the reporter's symptom ("OGH reaches higher wattage, OmenCore doesn't touch it").

- **T1.3** Set `AllowDecoupledWmiThermalPolicyFallback = true` on `8D87` and `AK0003NR`, matching their Intel siblings `8D41`/`8D42`.
  - **Evidence class:** the mechanism is now firmware-derived rather than inferred-by-symmetry, which is a materially higher bar than the roadmap had when it deferred. It is still a hardware-behaviour change on `UserVerified = false` boards. Recommend shipping it **with** T1.4's verification so the next field report carries before/after wattage automatically, rather than shipping it blind.
  - **Arithmetic worth checking against the field report:** the roadmap records the reporter seeing 71 W default → 105 W with OGH. MODE 1's `ATPP` is 60 W and MODE 4's is 100 W. If OGH drives MODE 4 rather than MODE 1, that fits. **Hypothesis, not a finding** — it needs one measurement to settle, and it changes which value T1.1 should send for "Performance".

### 3.3 Adapter awareness — `Legacy 0x0F`

**Evidence:** measured on three physical adapters (330/280/200 W), exact both times. Field layout confirmed against HP's own accessors (findings §1d). **Strongest item in this document.**

`BiosCmd.Legacy` (`0x00000001`), command `0x0F`, 4-byte reply:

| Byte | HP's accessor | Meaning |
|---|---|---|
| `[0]` | `GetSmartAdapterStatus` | `SmartAdapterStatus` enum — see below |
| `[1]` bit 7 | `GetSupportBarrel` | barrel jack supported (`0xC2` here ⇒ true) |
| `[2]` | `GetUsbcDesignRating` | `× 5` = USB-C **design** rating, W |
| `[3]` | `GetPowerRating` | `× 5` = **connected adapter's rating, W**; `0xFF` means unknown ⇒ 0 |

`SmartAdapterStatus`, from HP's own enum (findings §1c — note this extends OmenMon-Reborn's table, which stops at 4):

`-1` Error · `0` NotSupported · `1` MeetsRequirement · `2` BelowRequirement · `3` BatteryPower · `4` NotFunctioning · **`5` ConnectedTypeC**

Measured: 330 W → `01 C2 00 42` (`0x42` × 5 = 330). 200 W → `02 C2 00 28` (200). 280 W → `02 C2 00 38` (280).

**Work items:**

- **T1.4** Add `GetAdapter()` to `HpWmiBios` — the concrete class, **not** `IHpWmiBios`. Every consumer holds the concrete type, and a defaulted interface member would make "silently return null" the default for a future implementer, which renders as the affirmative claim *this board does not report an adapter*. Read-only, works on any board that answers the `Legacy` class. Note that this repo had **never issued the `Legacy` class for anything but `0x52`** — handle unsupported returns cleanly.
- **T1.5** Surface it: adapter wattage + status in Diagnostics, and a clear explanation when status is `BelowRequirement`. Today a user on a 200 W adapter sees "GPU stuck at 35 W" with no explanation available anywhere in the app.
- **T1.6** Feed it into the diagnostics export. Every future field report from a MAX-series board should carry the adapter verdict.
- **T1.7** Handle `ConnectedTypeC` (5) separately — HP's `IsLowWattage` uses different comparison logic for it (findings §1c): `powerRating > 0 && powerRating < usbcDesignRating`, plus a barrel-support special case. Do not fold it into "not MeetsRequirement".

### 3.4 `0x22` payload and arity

**Evidence:** HP's own source (findings §1b), `PerformanceControl.cs:5970` and callers.

- **T1.8** Byte 3 of the `0x22` payload is a **GPS temperature threshold**, not a spare. HP's `SetTgpPpabAsync` hardcodes 87 (°C). `BuildGpuPowerPayload` currently returns 0. Fix, and name it correctly.
  - Credit where due: OmenCore's existing `peakTemperature` naming was *right* and the firmware doc's "spare" was wrong — findings §1b says so explicitly. Only the value is wrong.
- **T1.9** `0x22` has a **second 1-byte arity** (`SetTGPAsync`, `new byte[1] { enable }`). Nothing should assume it is always 4 bytes.
- **T1.10** `dState` is hardcoded `0x01`, which matches HP — but per §7c/§7i, `GC22` sets `DSTA` and then calls `_Q73`, which immediately overwrites it from `PROH`. `DSTA` is **not independently settable on either adapter.** Document this at the call site so nobody re-derives it.

### 3.5 `0x28 SystemDesignData` is badly under-decoded

**Evidence:** HP's own bitwise accessors (findings §3c). Applies to **every** board, not just this one.

`QuerySystemData` reads 128 bytes and parses one (ThermalPolicy). HP's map, applied to this machine's reply `4A 01 3A 01 03 00 01 07 3C 00 03 00`:

| Byte | Mask | HP's name | Here |
|---|---|---|---|
| `[0..1]` | 16-bit LE | `ShippingAdapterPowerRating` | **330 W** |
| `[3]` | whole | `GetThermalPolicyVersion` | V1 |
| `[4]` | `0x01` | `IsSwFanControlSupport` | true |
| `[4]` | `0x02` | `IsExtremeModeSupport` | **true** |
| `[4]` | `0x04` | `IsExtremeModeUnlock` | **false** |
| `[4]` | `0x08` | `IsDTBiosControl` | false |
| `[4]` | `0x10` | **`IsTwoBytePL4Support`** | **false** |
| `[5]` | whole | `PL4DefaultValue` | 0 |
| `[6]` | `0x01` | `IsBiosDefinedOcSupport` | true |
| `[7]` | — | `GpuModeSwitch` | `0x07` |
| `[8]` | whole | `DefaultCpuPowerLimitWithGpu` | **60 W** |
| `[9]` | `0x0F`/`0xF0` | `LoadLineSupportLevels` / `DefaultLoadLine` | 0 / 0 |
| `[10]` | `0x01`,`0x02`/`0x04`/`0x08` | `ChangeIrSensorToBoard` / `IsPchOverheatSupport` / `IsVrSensorSupport` | false / false / false |
| `[11]` | `0x01`/`0x02` | `IsHotkeySupportFnP` / `IsHotkeySupportFnF1` | false / false (but true by board table) |

**Work items:**

- **T1.11** Decode the full block into a typed struct. **`IsTwoBytePL4Support` is the one that matters beyond this board** — it changes the wire format of `Default 0x29 SetPL4`. Getting it wrong is a silent write-corruption class of bug on any board where the bit is set.
- **T1.12** Expose `ShippingAdapterPowerRating` alongside T1.4's connected wattage. Together they give the app both halves of the comparison the firmware is making — required wattage vs actual — which is what lets a replacement size a TGP against the real ratio instead of accepting HP's binary verdict.
- **T1.13** Note `IsExtremeModeSupport = true` with `IsExtremeModeUnlock = false`. Unexplained; findings §3c calls tracing `IsExtremeModeUnlock`'s consumers the most promising unexplored lead in that section. Record, don't act.
- **T1.14** HP caches this block at `HKCU\Software\HP\OMEN Ally\Settings\SystemDesignData` and **never invalidates it** — not even on a BIOS update (findings §0a). If we ever read that key as a convenience, treat it as a convenience only; prefer the live `0x28`.

### 3.6 Model database entry

- **T1.15** Update the `8D87` entry with what is now known:
  - Thermal policy **V1** (measured — `0x28` byte 3 = `0x01`). Cross-check against `HpWmiBios`'s `Contains("MAX") && Contains("OMEN")` V2 force-switch, which `ROADMAP_v4.0.0.md:256` already flags as a broad name-substring match rather than a per-ProductId decision. **These may currently disagree on this board.**
  - Fan tachometers at EC `0x70` (`FASP`) and `0x5C`, both 16-bit raw rpm, corroborated against
    `Default 0x2D` at four operating points. **Not `0x7E`** — that is a commanded setpoint, a strict
    function of the level byte at `0x5B`, and it reads 0 whenever nothing has been commanded even
    while the fans turn. Same for `0x5E` (fan 2's setpoint, always `0x7E` + 200); there is no third
    fan. Measured across all 28 EC captures in the research tree.
  - **`0x9F` is `BRC0`, battery remaining capacity in mAh** — not a GPU tachometer. OmenMon-Reborn has this wrong on `8D87` and is showing its users battery charge in the GPU RPM field. Worth reporting upstream; OmenCore has not inherited the error, so this is a note, not a fix.
  - `SupportsEcPowerLimits` stays **false**. `SupportsUndervolt` stays **false** (AMD, no Intel MSR path).
  - `MaxModeDropChecksBeforeReapply = 1` is already present and is independently corroborated by the observed `MODE` decay.
- **T1.16** Add the sibling board IDs `8D88`, `8DD5`, `8DD6` (HP platform `Vibrance25C1`, findings §3a) as the **safety scope** for anything in Tier 3.
  - **But note the deeper point:** findings §3a establishes that **no power decision in OGH is keyed on board ID.** HP gates power on the runtime `0x28` capability query; the board table gates only hotkeys, chassis and lighting. This repo's per-board capability model has no counterpart in HP's own software. Use the board list to scope *raw offsets*, and prefer `0x28` for *capability*.

---

## 4. Tier 2 — the CPU 25 W clamp. Plumbing fixed; limits implemented.

> **Status, 2026-08-05.** **T2.1, T2.2 and T2.3 are done** and measured on hardware. T2.1 landed as the AMD SMU transport fix; T2.2 and T2.3 as the fast/slow/apu-slow limits and a silicon-scoped ceiling. **T2.4 stands — TDC/EDC remain untouched.** T2.5 (re-assertion) is now known to be *required*, not optional: see the box at the end of this section. T2.6's verification guidance was wrong and has been corrected below.
>
> Measured with `tools/SmuProbe --limits`, using an external RyzenAdj as an independent reader. Every phase pinned to its requested limit within 10 mW, and the returning phase came back exactly, so the excursion is the limit rather than drift:
>
> | phase | requested | read back | SMU drawing | TDC | Tctl |
> |---|---|---|---|---|---|
> | A stock | 45 W | 45.000 W | 44.99 W | 41.3/70 A | 66 °C |
> | B low | 20 W | 20.000 W | 19.99 W | 21.2/70 A | 54 °C |
> | A' stock | 45 W | 45.000 W | 45.00 W | 41.3/70 A | 68 °C |
> | C high | **70 W** | 70.000 W | **70.00 W** | 57.5/70 A | 85 °C |
>
> Stock was genuinely binding — 44.99 W drawn against 45.000 W — which is what makes the upward direction answerable rather than merely unrefuted.

## 4a. Tier 2 detail

**Evidence:** measured end-to-end with RyzenAdj v0.19.0 on the physical machine (§7m). 25.02 W → 45.73 W → ~51 W sustained / 60.7 W peak.

The strongest single piece of evidence in the entire investigation is here and worth restating: raising `stapm` + `fast` + `slow` but **not** `apu-slow` produced a hard ceiling at ~46 W, which is exactly the untouched `PPT LIMIT APU = 45`. A cap landing precisely on the one limit left unraised is a much better check that these are the real controls than any single before/after number.

**And the EC does not fight back here.** Unlike `PROH` and `OGHP`, the SMU limits were still set a minute later, and the EC's own mailbox block went on reading 25/25 while the SMU ran at 100/125 — they demonstrably disagree, and the SMU wins. No hold thread, no race.

**Work items:**

- **T2.1 (blocker)** Fix `RyzenSmu.Initialize()`. It opens a PawnIO handle but never calls `pawnio_load`, so `ioctl_pci_read_config_dword` / `ioctl_pci_write_config_dword` have no module behind them. Compare `PawnIOEcAccess.LoadEcModule()`, which does load `LpcACPIEC`. **This is a confirmed defect, not a hypothesis** — `RyzenSmu.cs:102-104` resolves only `pawnio_open`/`pawnio_execute`/`pawnio_close`, and it is the only PawnIO consumer in the repo that skips `pawnio_load`. Gate the fix so it does not enable an untested path for every AMD board.
- **T2.2** Add `fast-limit`, `slow-limit`, `apu-slow-limit` alongside the existing `SetStapmLimit`. Message IDs must come from RyzenAdj's Strix Point table — **do not guess them**, and do not infer them from the existing `0x14`/`0x31` stapm pair.
- **T2.3** Raise `SetStapmLimit`'s clamp. It currently caps at 54,000 mW (`AmdUndervoltProvider.cs:302`); the measured target is 100 W with a 125 W fast limit. Pick the new ceiling deliberately and document why.
- **T2.4** **Do not raise TDC/EDC.** The risk argument is unchanged and should be preserved verbatim in the code comment: a power limit is self-limiting (throttle or brownout, both fixed by a reboot); a current limit governs how hard the VRM is driven, and sustained overcurrent is not self-correcting.

> **T2.4 correction (2026-08-06).** This item said the part becomes current-bound at 53.977 A against a **54.000 A** limit, and that the 330 W reading was unknown. The limit is **not a fixed property of the part.** `TDC`/`EDC LIMIT VDD` reads **70.000 A** idle at stock — on the 330 W adapter *and* on the 280 W adapter, which is the same adapter the 54 A figure came from — so 54 A is what it read under that particular raised-limit load, not a rating. **Do not ship 54 A as a constant** (`AmdUndervoltProvider`'s old 15–54 W clamp was a symptom of treating it as one), and do not raise either number.
- **T2.5** Re-assert after power-source changes, sleep/resume, and adapter swaps — the EC recomputes everything on those transitions. **Stronger than originally written: re-assertion is required, not just advisable.**

> **T2.5 correction (2026-08-05).** "Steady-state operation needs nothing" is not what was observed. After a `--limits` run that restored all four limits to 45 W, they later read **71 / 71 / 60 / 45 W**, stable across samples. Nothing in OmenCore wrote that — every write sets all four to the same value — and the numbers are not arbitrary: **60 W is exactly this board's NVPCF `ATPP` (`0x1E0` = 480/8) and 70 W is `ACBT` (`0x230` = 560/8)**. The ACPI power path pushes its own values into the same registers.
>
> The limits held solidly for the whole of a sustained load and read back exactly, then were taken back once the load ended. So a user-set limit that is meant to persist needs a re-assertion loop; one call and an `Ok` status will silently drift back.
- **T2.6** Verify by measured power, not by SMU return code. **The guidance here has been corrected — see the box below.**

> **T2.6 correction (2026-08-05).** This item originally said to verify with `\Energy Meter(Apu Power)\Power`. That counter is **a different and narrower domain than the SMU's PPT, and must not be compared against a watt figure the SMU was given.** Measured on this machine at three limits: 28.82 W counter against 44.99 W SMU, 12.01 against 19.99, 43.11 against 70.00 — a consistent ~62 %. It tracks direction faithfully and absolute level not at all.
>
> A first pass at this measurement used the counter as its only axis and concluded the load "was not power-bound" at 27 W against a 45 W limit — while the SMU read 44.994 W drawn against 45.000 W at that same moment. That is this repo's recurring proxy-instead-of-outcome mistake, reproduced inside the harness built to avoid it.
>
> **Use the SMU's own `STAPM VALUE` / `PPT VALUE` as the measurement**, read over an independent transport, with the Windows counter kept as a direction-only witness. `tools/SmuProbe --limits` does exactly this.
- **T2.7** Do not attempt to reproduce HP's mechanism. How the EC gets 25 W into the SMU is still unknown and is excluded from AML, AMD PMF, NVPCF/Dynamic Boost, Windows PPM, HP's userland, and the EC mailbox block — leaving SMM as the surviving hypothesis. It is a curiosity, not a blocker.

---

## 5. Tier 3 — the adapter gate. Transport decided; write path still gated.

This is "ungating the power adapter issue", and the mechanism is fully solved and demonstrated. **As of 2026-08-04 the transport question is also settled** (§5.1): the ports alias for reads and are fast enough for both levers. What remains gated is narrower — confirming the aliasing holds for *writes* and on a second adapter state, and building a hold path that does not inherit `PawnIOEcAccess.WriteByte`'s per-call sleep.

### 5.1 The transport problem

`PROH` (`0x90`) and `OGHP` (`0x59` bit 1) live in the EC's **memory-mapped** window at `0xFE700600`. The investigation wrote them with `inpoutx64` + `Marshal.WriteByte`.

OmenCore cannot do that:

- WinRing0/inpoutx64 were **deliberately removed** from this project over Defender's `VulnerableDriver:WinNT/Winring0` detections (`CHANGELOG.md:34, 840, 879`). Reintroducing them reverses a decision made for good reasons and would re-break every user who upgraded to escape those alerts.
- The bundled PawnIO module is `LpcACPIEC`, which whitelists exactly ports `0x62`/`0x66` and cannot touch physical memory.

**Three options, in order of preference:**

**Option A — test the aliasing first. Read-only, cheap, and it decides everything.**

> [!IMPORTANT]
> **T3.1 has now been run. The ports alias the MMIO window for reads, and the latency objection
> below did not survive measurement.** Full method and limits:
> the `08-ec-port-aliasing.md` notebook; distilled for this repo in
> [`8D87-EVIDENCE.md`](8D87-EVIDENCE.md).

- **T3.1 — DONE (2026-08-04).** Three runs sandwiching a PawnIO/`LpcACPIEC` port sweep between two MMIO captures, so that only bytes which held still across the whole window got a vote. **100 % / 100 % / 99.6 % agreement on judged bytes; 8 of 8 entropy-carrying anchors matched**, including both fan tachometers at 3000 rpm under deliberate CPU load and `PROH` itself. Correlation falls from 99.6 % at shift 0 to ~33 % at ±1 byte, which is what rules out coincidental resemblance rather than mere agreement. The one diverging byte was a temperature sensor that ticked mid-sweep — tracked across all three runs and explained, not waved away.
  - The warning not to accept partial agreement was honoured: §7h's withdrawn `CPUT`/`0x57` argument rested on a single static byte, and that argument is **not** what re-established this.
  - The port path here is byte-for-byte the one `PawnIOEcAccess.ReadByte` uses, so this is a result about the shipping code path.
  - **Reads only, one adapter state (330 W).** Writes through the ports remain unproven, and the anchors should be re-checked on the 200 W supply — where `PROH` → 2 and `P3TV` → 185 — before this is more than provisional. Two agreeing states is the `HPBA` error.

`PROH` and `OGHP` are therefore addressable through the module OmenCore **already ships**, and Stage 2 costs an allowlist entry plus a hold thread.

**The caveat that was expected to be decisive, and what measurement did to it.** The plan predicted the port protocol would be far slower than an MMIO store, making `OGHP` unreachable. Measured over 200 samples: PawnIO's bare ioctl round trip is **~6 µs**, and a full EC read transaction is **0.325 ms median** (p95 1.86 ms, max 4.03 ms).

| Target | Reassert cadence | Predicted | **Measured** |
|---|---|---|---|
| `PROH` | ~100 ms | Plausible | **~300 transactions per cycle.** Comfortable. |
| `OGHP` | EC clears it on 98 % of 2 ms cycles | Very unlikely | **~6 transactions per window.** Feasible. |

**So the transport is not the barrier — `PawnIOEcAccess` is.** `WriteByte` (`PawnIOEcAccess.cs:530-533`) ends every write with `Thread.Sleep(1)` and acquires the `Global\Access_EC` mutex per call. `Thread.Sleep(1)` yields the rest of a scheduler quantum — commonly 1–15 ms — which alone can exceed the entire `OGHP` hold window. A hold loop needs a dedicated path that takes the mutex once for its duration and omits the per-write sleep. **That is an application-layer fix, not a transport limitation**, and it is the single most consequential thing this test changed.

Two operational notes for any hold loop: port reads time out on 1–3 % of offsets at randomly varying addresses, so retry rather than single-shot; and the `OGHP` p95 of 1.86 ms means a minority of iterations overrun 2 ms — tolerable, since the MMIO race already loses ~98 % of cycles and is won by repetition, but it means the loop is measured by `enforced ≥ 170 W` and never by iteration count.

**Option B — a custom PawnIO module** exposing that one physical page, allowlisted to the two offsets. Technically the right answer. Friction is module signing: official PawnIO modules are signed by the project author. **Needs verifying** whether the release driver loads third-party modules, or whether upstreaming an `HpOmenEcMmio` module is the realistic path.

**Option C — accept Stage 1 as out of scope in-app**, and keep `OGHP` arming as a documented external step (the investigation's `Invoke-OmenArm.ps1`). Least satisfying, but honest, and it still leaves Stage 2 and Tier 2 fully in-app if Option A succeeds.

### 5.2 Design rules that apply regardless of transport

These come from the investigation's own retractions. They invert how parts of this codebase currently work.

**5.2.1 — Verify by outcome, never by readback of what we wrote.**
`GC22` returns `RTCD = 0` whether or not the driver was listening. §7o records the exact failure: a textbook-clean `GC22` with `CTGP = 1`, `DTGP = 1`, `RTCD = 0` — and `enforced` still at 80 W, because the driver's `_DSM` re-read landed on one of the EC's zeros. The tool reported that as `GRANTS RESTORED`. It now requires `enforced ≥ 170 W`.

That was the **fourth** wrong-reference false positive in that investigation, after `P3TV`, the `STABLE` bug, and the retracted `Default 0x10` arming command. The recurring error is checking the request instead of the outcome.

`HpWmiBios.VerifyGpuPowerReadback` (`HpWmiBios.cs:986-1006`) is precisely this pattern. It is fine for what it currently does, but **must not** be the verification for anything in this tier. Verification means delivered watts, via NVAPI (`NvapiService` already exists) or `nvidia-smi`.

**5.2.2 — This is a resident agent, not a one-shot.**
- No stage survives a reboot. `_REG` zeroes the grants ~2 s into boot, before any userland exists.
- Stage 2's 175 W **reverts spontaneously while idle** — `_Q73` re-runs on EC events and power-state transitions, re-deriving the limit from whatever `PROH` then reads. An earlier revision of the doc concluded from a single 20 s observation that the unlock was durable; that was retracted.
- Stage 3 survives steady state but not a power-source change.

So: a supervised re-applier with hooks on adapter change, resume, and a slow watchdog. Not a button.

**5.2.3 — Safety gating is not optional here.**
Forcing 175 W on a 200 W supply is out-of-spec by design. §7j observed the GPU entering a degraded state after a forced unlock — `nvidia-smi` at 23–27 s per call, FurMark unable to render — which **persisted after the manipulation stopped** and cleared only on reboot, on a machine with a history of `nvlddmkm` power-IRP BSODs. The GPU-under-load comparison at forced `PROH = 1` is therefore **unmeasured**, not merely unfavourable.

Requirements: explicit opt-in with the risk stated plainly; adapter wattage from T1.4 so the feature can offer a **proportional** cap against the real supply rather than all-or-nothing 175 W; and a rollback path.

**5.2.4 — Board scoping.**
Every offset here (`0x59`, `0x90`, `0xE6`, `0xF6`/`0xF7`) came from *this* DSDT. Gate on `8D87` + `8D88`/`8DD5`/`8DD6`, never generally. This is the same class of error as `PowerLimitController`'s `0xC0`–`0xC5` and OmenMon-Reborn's `0x9F`.

**5.2.5 — EC access discipline.**
`PawnIOEcAccess` already has the `Global\Access_EC` mutex. OmenMon-Reborn additionally uses a graduated spin → yield → sleep backoff (added after relentless polling was implicated in ACPI-timeout BIOS panic shutdowns, their issue #88) and a torn-read guard requiring two consecutive identical 16-bit reads (their issue #86). A hold loop running at tens of Hz makes both of those relevant to us for the first time.

### 5.3 Work items (conditional on T3.1)

- **T3.2** MMIO or port-based access to the two offsets, single-purpose and allowlisted. Follow the investigation's pattern: `Write-EcProh.ps1` writes `0x90` **only** — the offset is not a parameter. Fan control (`MFAN` at `0x4A`), thermal trip points and battery charge parameters share the same 256-byte window.
- **T3.3** Stage-2 unlock: hold `PROH = 1`, fire `GC22` to run `_Q73`, release. Verify against delivered watts. Re-apply on revert.
- **T3.4** Stage-1 arm, **only if** a fast enough transport exists: hold `OGHP` (mask `0x02`, preserving `DBST` at bit 4 — it is a read-modify-write, unlike `PROH`) from a ≤2 ms loop, fire `GC22` inside the window, release. Measured minimums: 2 ms hold, 5000 ms settle before re-read. 5 ms hold fails; 1200 ms settle fails.
- **T3.5** Proportional TGP mode: read the connected wattage (T1.4), pick a cap the supply can plausibly sustain, apply it driver-side rather than requesting the full 175 W. This is the *responsible* version of the feature and it is only possible because `Legacy 0x0F` exposes a real number.

---

## 5a. Keyboard lighting — per-key, and over an open standard

**Not in the original three tiers.** It turned out to be independent of the power path and far
cheaper than expected, so it is recorded here rather than deferred.

### 5a.1 This board is per-key, and the four-zone path is a decoy

The capability gate HP's own software uses is **`Default 0x2B`** — class `0x00020008`, no input
payload, 4-byte reply whose byte 0 is `NbKeyboardLightingType`. Measured **`0x03` = `RgbPerKey`
on ten of ten reads**.

The trap, and it is a good one: the four-zone colour block (`Keyboard 0x02`) **still reads back
plausibly on a per-key board**. HP computes `ZONE_NUM` as `(uint)(type - 4) <= 1`, so type 3
falls through to 4 zones. Reading that block alone yields four tidy RGB triples at offset 25 and
a confident wrong answer. Gate on the topology probe first.

On this chassis the four-zone commands drive the **light bar**, not the keyboard — owner-observed.
So "four-zone returned success" was never a null result here; it was landing on a different
lighting surface than the one being watched, which is more misleading than a refusal.

### 5a.2 `Keyboard 0x01` is an accumulator, not a keyboard-type getter

Widely documented as `GetKbdType`. It is not. Ten identical consecutive calls returned:

```
0x0F, 0x1F, 0x3F, 0x7F, 0xFF, then 0xFF for every call after
```

It grows until it saturates. Nothing about the hardware changed between those calls. An earlier
note in this repo recorded "returns an 0xFF sentinel" — that was a saturated counter, not a
sentinel. And once saturated, its bit 0 reads "lighting supported" whether or not that is true,
so even HP's own `& 1` reading is untrustworthy here.

### 5a.3 The keyboard is a standard HID LampArray — no reverse engineering needed

The internal keyboard (`0D62:54BF`, "HP Gaming Keyboard II", Darfon — HP's ODM) implements
**HID usage page `0x59`, "Lighting And Illumination"**: the standardised per-key surface that
Windows Dynamic Lighting drives.

```
LampCount        120
Kind             1 = Keyboard
BoundingBox      342 x 125 x 1 mm
MinUpdateInterval 33 ms
per lamp         8-bit R/G/B/intensity, programmable, carries the HID usage of its key
```

Geometry is self-consistent — the numpad lamps sit at x = 279–328 mm, Tab at x = 12 mm.

**This changes the cost of the feature entirely.** The plan had been to ask the GitHub #151
reporter for HID captures and infer a vendor command set. That is no longer the critical path
for this board: the protocol is specified, and `HidLampArray` implements it.

### 5a.4 What is verified, and what cannot be

The device **accepted** the control report (id 6) and range-update reports (id 5) — it took them
without stalling, so the report layouts are structurally right.

**That is not proof the keys changed colour, and no software check can be.** The LampArray spec
has no colour readback anywhere. `tools/LightingProbe --self-test` exists for exactly this: it
drives the keyboard in thirds (red / green / blue, so a *partial* result stays readable) and
tells a person what they should be seeing.

### 5a.5 The open question — arbitration with Windows Dynamic Lighting

Dynamic Lighting is **on** on this machine (`AmbientLightingEnabled = 1`) and owns the LampArray;
it is why `LampArray.FromIdAsync` (WinRT) times out on the keyboard interface while direct HID
works. Three options, and they differ in what OmenCore does to a Windows feature the user may
want running:

1. WinRT `LampArray`, playing by Windows' arbitration rules.
2. Direct HID, contending with Dynamic Lighting at ~30 Hz.
3. Ask the user to turn Dynamic Lighting off, then direct HID.

**Deliberately unresolved.** The mechanism for (2) is built because it is the one proven to work
here; nothing is wired into the UI until the policy is chosen.

### 5a.6 Work items

- **T5a.1** *(done)* Topology probe `Default 0x2B`; capability detection reads it instead of assuming four-zone.
- **T5a.2** *(done)* `HasBacklight` asks for a 128-byte reply. A 4-byte request returns **RTCD 5 = wrong output buffer size**, which the old code read as "unsupported" — reporting no backlight on a lit keyboard.
- **T5a.3** *(done)* `HidLampArray` + `tools/LightingProbe`.
- **T5a.4** Decide the arbitration policy (§5a.5), then wire per-key control into the UI.
- **T5a.5** Map lamp id → key. Each lamp reports the HID usage of its key, so a usable key-name mapping is a table lookup rather than a calibration exercise. **Note the free-running response**: this keyboard's lamp-attributes reply ignores the requested id (asking 0–3 returns 41–44), so read the id the device reports.
- **T5a.6** Settle `8D41`, the Intel sibling. It claims per-key *and* four-zone and is marked `UserVerified`, so it is somebody's real report and is left alone. One command from an owner resolves it: `LightingProbe --wmi`.

---

## 6. Explicitly do not

**The full list of settled dead ends lives in one place:
[`8D87-EVIDENCE.md`](8D87-EVIDENCE.md) §4.** Read it before proposing
any experiment on this board — it also records two claims that were retracted and then
*un*-retracted, which is the layer most likely to be read wrong.

Specific to this repo, on top of that list:

- **Do not** add EC power offsets `0xC0`–`0xC5` for this board. `SupportsEcPowerLimits` stays `false`.
- **Do not** look for an EC register map in OGH's binaries. There is none — a grep across all four decompiled roots for `0xFE7006*`, `EmbeddedController`, `Ec{Read,Write}`, `LpcACPIEC` and raw port I/O returns zero hits. HP reaches the EC only through AML `GCxx` and the SMU only through AMD's own driver.
- **Do not** investigate `hpomencustomcapdriver.sys`. It imports exactly `DbgPrintEx` and `RtlCopyUnicodeString` plus WDF loader stubs — no `MmMapIoSpace`, no port I/O, no `IoCreateDevice`. It is a stub and cannot touch the EC by construction. (`HpReadHWData.sys` is the one that is *not* a stub — see [`8D87-EVIDENCE.md`](8D87-EVIDENCE.md) §2.7.)
- **Do not** raise TDC/EDC.
- **Do not** reintroduce WinRing0 or inpoutx64. Note the upstream investigation's own `tools/lever/` scripts use `inpoutx64` for MMIO — **that does not port here**, and this repo removed it deliberately over Defender detections.

Three from the shared list are worth repeating because they are easy to re-derive from the code:

- **Do not** try to write `DSTA` directly via `GC22`. It sets `DSTA` and then calls `_Q73`, which overwrites it from `PROH`. Measured on both adapters.
- **Do not** treat `EWDS` (mailbox `0x0E`) as a size field. Declared once, referenced by no AML, reads 0 in 1666/1666 captured events.
- **Do not** treat `Default 0x28` as *connected* adapter state — it is byte-identical on 330 W and 200 W, and describes the SKU. But it is **not** contentless: `byte[0..1]` is `ShippingAdapterPowerRating` in watts (330 here), and `Legacy 0x0F` `byte[3] × 5` gives the connected rating. See [`8D87-EVIDENCE.md`](8D87-EVIDENCE.md) §2.1 and §2.5.

---

## 7. Open questions

Carried forward from both documents, with our own added.

**Blocking for us:**

1. **Do ports `0x62`/`0x66` alias the MMIO window for *writes*, and on a second adapter state?** They do alias for **reads** — measured, three sandwiched runs; see §5.1 and [`8D87-EVIDENCE.md`](8D87-EVIDENCE.md) §6. The write direction is unproven, and nothing may write EC RAM through the ports until it is closed.
2. **Does the PawnIO release driver load third-party modules?** Decides Option B. **Now much less urgent** — Option A succeeded, so Option B is no longer on the critical path for either stage.
3. **Does the `OGHP` race actually win over ports?** Feasible on the latency numbers; unproven in fact. Requires the dedicated hold path described in §5.1.

**Carried from the investigation, unresolved:**

4. **How does OGH set `OGHP` durably?** OGH's arm stays up until the app is killed; ours is cleared on 98% of 2 ms cycles. The EC treats OGH's arm as authoritative and ours as noise. Not a blocker (a momentary hold suffices) but it is the one OGH behaviour that cannot be reproduced, only worked around. Every `GCxx` command OGH has been observed to send is a 0-arg read, so passive mailbox capture is the wrong instrument.
5. **What transports the EC's 25 W decision into the SMU?** SMM is the surviving hypothesis. Not needed — the SMU limits are directly writable.
6. **Why does `GC22`-zeroing give 80 W while `_REG`-zeroing gives 105 W?** Both set `CTGP = DTGP = 0`. 105 W is explained (base TGP, adder absent); 80 W is not. Suggests the historical 80 W baseline was never the clean "grants revoked" state it was taken for.
7. **`OTPP`** — a one-shot arbitrary override of `ATPP`, consumed on use, writable in the same SSDT scope as `MODE`/`CTGP`/`DTGP`. Finest-grained lever found. Untested.
8. **PPAB values above 1** — OmenCore's `Extended3`/`Extended4` pass `ppab = 2`/`3`, documented as "+25 W or more (RTX 5080+)". Unverified on this board, and there is no headroom to expose them while `enforced` already equals `power.max_limit`.
9. **Why is NVPCF variant A selected?** Eight `Nvd*` SSDTs ship; something picks one. If it is a setup variable, that is a directly writable lever.
10. **`STS0` at EC `0xBB`** — the one surviving unexplained adapter-keyed byte, and suspect by association after `HPBA` was retracted.
11. **The V2 fan-command force-switch.** `HpWmiBios` switches this board to V2 fan commands via a `Contains("MAX") && Contains("OMEN")` name-substring match, but `0x28` byte 3 reports thermal policy **V1**. `ROADMAP_v4.0.0.md:256` already flags the substring match as a problem. Do these disagree on `8D87`?

---

## 8. Suggested sequencing

**Phase 0 — decide the transport. ✅ DONE (2026-08-04).** T3.1 came back positive: the ports alias for reads and the transport is fast enough for *both* levers, not just `PROH`. Tier 3 exists. Two things moved as a result — Option B (custom PawnIO module) leaves the critical path, and the `OGHP` blocker is now a wrapper fix (`Thread.Sleep(1)` + per-call mutex in `PawnIOEcAccess.WriteByte`) rather than a transport dead end. The remaining gate before any EC write is confirming the aliasing holds **for writes** and on a **second adapter state**.

**Phase 1 — Tier 1. ✅ LARGELY DONE.** Landed: adapter awareness (`Legacy 0x0F`), the full `0x28`
decode, the `0x22` payload correction, the model-DB entry with `UserVerified = true`, real fan
tachometers at `0x70`/`0x5C`, a V2-capability *probe* replacing the model-name force-switch (which
closes open question 11), the GPU mode read from `Legacy 0x52`, and the performance-mode measurement
that removed `"L5P"`.

Not landed from Tier 1: `MODE 4` as a user-facing control (T1.2), and `OTPP` (§7.7) which remains
untested.

**Phase 2 — Tier 2. ⚠️ TRANSPORT DONE, LIMITS NOT.** T2.1 turned out to be three faults, not one: no
`pawnio_load` call, ioctl names no bundled module exports, and a stale bundled module that rejected
this CPU outright. All three are fixed and the transport is verified end to end — Curve Optimizer
measures **+4.9% sustained clock at CO −25** against a ±0.1% sham control
([`8D87-EVIDENCE.md`](8D87-EVIDENCE.md) §3.4).

Still open in Tier 2: **T2.2/T2.3 — the four SMU power limits** (`stapm`, `fast`, `slow`, `apu-slow`)
that lift the 25 W APU clamp to ~51 W. The transport they need now works; the limits themselves are
not implemented. T2.4 stands: **do not raise TDC/EDC.**

**Phase 3 — Tier 3.** Phase 0 succeeded, so this is live. T3.2 → T3.3 (Stage 2) → T3.5 (proportional). **T3.4 (Stage 1) is no longer conditional on transport speed** — 0.325 ms per transaction clears the 2 ms window — but it does depend on the dedicated hold path in §5.1, and it stays behind the safety gating in §5.2.3 regardless.

Phases 1 and 2 are independent and can run in parallel. Phase 3 benefits from T1.4 and is now gated on write-side confirmation rather than on Phase 0.
