# Tuning Subsystems Review: GPU Power Boost, GPU OC/Undervolt, CPU OC/Undervolt

**Date:** 2026-08-13
**Reviewed against:** `main` @ v4.1.7 (post-`4a1876d`)
**Scope:** `NvapiService`, `AmdGpuService`, `CpuUndervoltProviderFactory` / `IntelUndervoltProvider`, `AmdUndervoltProvider`, `RyzenSmu` / `RyzenControl`, `PawnIOMsrAccess` / `IMsrAccess`, `TuningGuardrails`, `TuningRollbackCoordinator`, `TuningStartupRecoveryGuard`, and the tuning surface of `SystemControlViewModel`.

## How to read this document

This is a **code review, not a field report**. Every claim below is marked:

- **Confirmed** — verified by reading the code path end to end. Cited with `file:line`.
- **Latent** — the code is currently safe by accident (nothing calls it that way today), but the structure invites a specific future bug.
- **Unverified** — a suspected problem that needs either hardware or a test to settle. Called out as such, never asserted.

No hardware behavior was validated for this review. Nothing here should be treated as evidence about how a board actually responds — that distinction is the same one `field-validation-script.txt` enforces for model promotion, and it applies to this document too.

---

## 1. What is actually there today

### 1.1 CPU undervolt — the best-structured of the three

There is a real abstraction: `ICpuUndervoltProvider` (`Hardware/CpuUndervoltProvider.cs:11`) with `ApplyOffsetAsync` / `ResetAsync` / `ProbeAsync`, implemented by `IntelUndervoltProvider` (MSR via PawnIO) and `AmdUndervoltProvider` (Curve Optimizer via SMU mailboxes). `CpuUndervoltProviderFactory` picks between them from `Win32_Processor`.

**The AMD SMU work is the highest-quality code in this whole area.** Message IDs are named constants cited verbatim to RyzenAdj's `lib/api.c` rather than inferred (`AmdUndervoltProvider.cs:340-344`), with the explicit reasoning that "a silently changed message ID is the kind of thing that returns Ok and does nothing." `FamilySupportsPptLimits` / `FamilySupportsApuSlowLimit` (`:352`, `:371`) list only families whose IDs are sourced, returning `UnknownCmd` for the rest instead of guessing. TDC/EDC are deliberately absent with a written risk rationale (`:526-530`) — power limits are self-limiting and clear on reboot, current limits are not. `SendWithPsmuFallback` (`:443`) documents a real fixed bug where chaining MP1→PSMU on success made an accepted write report as failed.

Most importantly, `ApplyPowerLimits`'s doc comment (`:513-524`) states plainly that accepted ≠ in force, and that **the platform takes the limits back** — observed on board 8D87, limits reading `71/71/60/45 W` after load ended, where 60 W is that board's NVPCF ATPP value. That is exactly the kind of honesty this codebase should be graded on.

### 1.2 GPU OC/undervolt — two parallel implementations, no shared abstraction

Unlike CPU, there is **no `IGpuTuningProvider`**. There are two unrelated concrete services:

| | NVIDIA (`NvapiService`) | AMD (`AmdGpuService`) |
|---|---|---|
| Core/mem clock offset | `SetCoreClockOffset` `:743`, `SetMemoryClockOffset` `:824` | `:253`, `:296` |
| Voltage offset | `SetVoltageOffset` `:903` | *(none)* |
| Power limit | `SetPowerLimit(percent)` `:1127` — **absolute**, 100 = stock | `SetPowerLimit(percentOffset)` `:338` — **offset**, 0 = stock |
| Clamp source | `MinCoreOffset`/`MaxCoreOffset`/… fields, driver-refined | hardcoded ±500; power from driver OD8 |
| Persisted to config | Yes — `GpuOcSettings` | **No** — VM-only fields |
| Test-apply → keep flow | Yes | **No** |
| Startup recovery guard | Yes — `TuningStartupRecoveryGuard.ShouldSafeReset(GpuOcSettings)` | **No** |
| Rollback coordination | Yes — `TuningRollbackCoordinator` | **No** |
| VM commands | `ApplyGpuOc…` | separate `ApplyAmdGpuOcCommand` `SystemControlViewModel.cs:2611` |

The NVIDIA side carries the full safety apparatus. The AMD side is a bare pass-through.

### 1.3 GPU Power Boost — the most recently hardened path

This is the HP-side TGP/PPAB control (`SystemControlViewModel.ApplyGpuPowerBoost` `:3807`), distinct from GPU OC. It tries WMI BIOS → OGH proxy → EC, in that order.

The WMI path was fixed this release cycle and is now good: it verifies with a post-success readback (`VerifyGpuPowerReadback`, `:3825`), and when the BIOS accepts a command that changes nothing it says so plainly rather than printing a checkmark (`:3836-3844`). The "NVAPI power limits available" claim is correctly gated on `GpuPowerLimitAvailable` rather than mere NVAPI presence, with the GitHub #159 reasoning inline (`:3846-3852`). The "Extended" fallback message explains *why* there's no EC equivalent instead of implying missing support (`:3923-3928`).

### 1.4 Shared safety machinery — genuinely good, and well tested

`TuningRollbackCoordinator` (`Models/TuningRollbackCoordinator.cs`) and `TuningStartupRecoveryGuard` (`Models/TuningStartupRecoveryGuard.cs`) are pure, dependency-free policy objects — live hardware calls stay in the VM/services, so the config contract is testable and shared with diagnostics. Test coverage exists and is real: `TuningRollbackCoordinatorTests`, `TuningStartupRecoveryGuardTests`, `TuningStartupRecoveryCoordinatorTests`, `TuningGuardrailsTests`, `GpuOcSafetyGuardTests`, `UndervoltPreferencesTests`, `AmdPowerLimitTests`, `RyzenSmuTransportTests`, `NvapiServiceTests`, `AmdGpuServiceTests`, `StartupRestorePolicyTests`, `TuningConflictGuardTests`.

The design pattern to preserve: **the unconfirmed-state-on-startup reset.** If the app died mid-test-apply, startup resets to safe and records `LastStartupHadUnconfirmedState = true`. That is the correct shape for this risk class.

---

## 2. Findings

Ranked by consequence, not by effort.

### F1 — Per-core undervolt is plumbed end-to-end and then silently dropped **(Confirmed, high)**

`UndervoltOffset.PerCoreOffsetsMv` is carried through the UI VM (`SystemControlViewModel.cs:2052`, `:5207`), clamped by `TuningGuardrails.ClampCpuUndervoltOffset` (`:35`), persisted to config (`:5223`), reset by both safety coordinators, and **exported into diagnostics bundles** (`DiagnosticExportService.cs:2200-2201`).

`IMsrAccess` has no per-core method at all (`Hardware/IMsrAccess.cs`, whole file). `IntelUndervoltProvider.ApplyOffsetAsync` calls only `ApplyCoreVoltageOffset` and `ApplyCacheVoltageOffset` (`CpuUndervoltProvider.cs:136-137`). `AmdUndervoltProvider.ApplyOffsetAsync` never reads the array either.

So the array is accepted, validated, saved, and reported — and never written to hardware. `ApplyUndervoltAsync` then reports `"Undervolt applied: Core X mV, Cache Y mV"` unconditionally (`:5231`).

Mitigating: there is **no XAML surface** for per-core (no matches under `Views/`), so it is reachable only by hand-editing `config.json`. That limits blast radius but does not make the diagnostics output honest — a support bundle can state `PerCoreEnabled: True` with offsets listed, for a machine where none were applied.

This is the same class of defect this project has spent the whole cycle removing (EC GPU-boost false success, "NVAPI power limits available", PawnIO reboot advice on AMD).

### F2 — Nothing reads back what was applied; the UI reports intent as state **(Confirmed, high)**

- `NvapiService`: every setter assigns the cached property from the **requested** value after a success return — `CoreClockOffsetMHz = offsetMHz` (`:777`), `MemoryClockOffsetMHz` (`:857`), `VoltageOffsetMv` (`:936`), `PowerLimitPercent` (`:1157`, `:1192`). There is no read-applied-offset call anywhere in the file. (`GetCurrentClocks()` `:1588` reads live clocks — a different thing, and not comparable to a requested offset.)
- `AmdGpuService`: same pattern (`:276`, `:318`, `:360`).
- `AmdUndervoltProvider.ProbeAsync` returns `_lastAllCoreCO * 4` (`:140`) — the last *requested* CO, not a hardware read.

On its own that's optimistic. Combined with `ApplyPowerLimits`'s own documented finding that **the ACPI power path overwrites these registers after load ends** (`:519-524`), it means the UI can display a tuning state that the platform silently reverted, indefinitely, with no signal to the user.

The GPU Power Boost path already solved this correctly with `VerifyGpuPowerReadback`. The same treatment has not reached GPU OC or CPU UV.

### F3 — GPU power-limit semantics differ between vendors, and "safe rollback" is AMD-unsafe **(Latent, high if triggered)**

`NvapiService.SetPowerLimit` takes an **absolute** percentage where 100 = stock, clamped 50–125 (`:1127`, `:1135`). `AmdGpuService.SetPowerLimit` takes a **percentage offset** where 0 = stock (`:338`, doc comment `:336`), and reset sets it to 0 (`SystemControlViewModel.cs:4583`).

These are not currently crossed — AMD uses VM-only `AmdPowerLimitPercent`, NVIDIA uses `GpuOcSettings.PowerLimitPercent`. But:

- `TuningRollbackCoordinator` hardcodes `PowerLimitPercent = 100` as the **safe** value (`:134`, `:146`). If AMD GPU OC is ever wired into `config.GpuOc` — the obvious next step for giving it persistence, see F4 — then "emergency rollback to safe" would request **+100% power limit** on AMD.
- Two sliders in `TuningView.xaml` look alike but mean different things (`-50…50` for AMD at `:1170`; absolute percent for NVIDIA).

The trap is set; nothing has stepped on it yet.

### F4 — AMD dGPU users get none of the OC safety machinery **(Confirmed, high)**

AMD GPU OC values are never written to config (grep for `AmdCoreClockOffset` shows VM + XAML only). Consequences: no persistence, no `ApplyOnStartup`, no test-apply→keep confirmation flow, no `PendingTestApply`/`StartupPendingConfirmation` unconfirmed-state recovery, and no participation in `TuningRollbackCoordinator`.

An AMD user who applies an unstable clock offset and hard-locks gets no startup safe-reset, because there is no persisted state to recognise as unconfirmed. The equivalent NVIDIA user is protected.

### F5 — `AmdUndervoltProvider.SetTctlTemp` is ~45 lines of unreachable code **(Confirmed, low severity / high maintenance hazard)**

`SetTctlTemp` returns `Failed` immediately unless the family is `StrixPoint` (`:581-584`) — then falls into a `switch` with cases for `Zen1Plus`, `Raven`, `Picasso`, `Dali`, `RenoirLucienne`, `VanGogh`, `CezanneBarcelo`, `Rembrandt`, `Phoenix`, `Mendocino`, `HawkPoint`, `StrixHalo`, `Matisse`, `Vermeer`, `RaphaelDragonRange`, `FireRange` (`:592-629`). Every one of those is dead.

The gate itself is deliberate and correct (documented at `:574-578`: the transport fix newly activated real writes on seventeen families with no field evidence). The problem is the shape — a reader has to notice a guard 40 lines above to know the table below it can never execute, and a future edit to the table will appear to work and do nothing.

### F6 — The AMD CO ↔ millivolt conversion is a fabricated constant, round-tripped **(Confirmed, medium)**

`ApplyOffsetAsync` converts `CoreMv / 4.0` to CO counts with the comment "CO is roughly 3-5mV per count, we'll approximate" (`:49-51`). `ProbeAsync` converts back with `_lastAllCoreCO * 4` (`:140`). The UI then formats that as `"Core {x:+0;-0;0} mV eq."` (`SystemControlViewModel.cs:5230`).

The `mV eq.` label is honest, and clamping happens on both sides. But the divide-then-multiply round trip through a made-up divisor means the number shown is derived from an assumption, quantised (`/4` truncates), and presented in a physical unit. `AmdCurveOptimizerEquivalentMinMv = -120` (`TuningGuardrails.cs:12`) maps to CO -30, which matches the hard clamp in `SetAllCoreCO` (`:184`) — so the ranges line up, but only because both were chosen against the same guess.

Related: `ApplyOffsetAsync` reuses `CacheMv` to mean **iGPU** CO on AMD (`:52`), and `ProbeAsync` reuses `CurrentCacheOffsetMv` for the same (`:141`, commented). Documented, but a field meaning two unrelated things by vendor is how unit bugs like F3 start.

### F7 — Guardrail policy is split across three places with no single source of truth **(Confirmed, medium)**

`TuningGuardrails` (`Models/TuningGuardrails.cs`) owns CPU undervolt clamps and GPU **voltage** offset clamps — and nothing else. GPU **clock** and **power** clamps live inside each GPU service as instance fields (`NvapiService.cs:269-284`, refined per-architecture at `:648-657`) or hardcoded literals (`AmdGpuService.cs:264`, `:306`). AMD CO has a third clamp inside `SetAllCoreCO` (`:184`) and `SetIgpuCO` (`:286`).

A reviewer asking "what is the maximum this app will ever ask of the GPU?" has to read four files. That is also why F3 was possible.

### F8 — GPU Power Boost readback verification only covers the WMI path **(Confirmed, medium)**

`VerifyGpuPowerReadback` is called only in the WMI BIOS branch (`SystemControlViewModel.cs:3825`). The OGH proxy branch (`:3886-3901`) and the EC branch (`:3907-3921`) both print `"✓ …"` and log `"✓ GPU Power Boost set to: …"` purely on the setter's return value.

The EC branch is self-described as experimental with undocumented, model-varying registers (`:3904-3906`). It is the *least* trustworthy path and has the *least* verification.

### F9 — Intel external-controller detection reports meaningless offsets and re-probes on every call **(Confirmed, low–medium)**

`DetectExternalController` (`CpuUndervoltProvider.cs:250`) returns `Offset = { CoreMv = 0, CacheMv = 0 }` in every branch (`:273`, `:304`). Callers then populate `status.ExternalCoreOffsetMv` / `ExternalCacheOffsetMv` from it (`:217-218`), so the UI/diagnostics can state "external controller detected, offset 0 mV" — which reads as "XTU is applying 0 mV" rather than "we did not read XTU's value."

It is also called from `ProbeAsync` with no caching: four `ServiceController` constructions plus two `Process.GetProcessesByName` sweeps per probe.

### F10 — Vendor detection silently defaults to Intel, and now gates more than it used to **(Confirmed, low–medium)**

`DetectCpu` falls through to `CpuVendor.Intel` for unrecognised CPUs (`:88`) and on any exception (`:95`), with no logging and no `Unknown` terminal state — `DetectedVendor != Unknown` is also the memoisation check (`:62`), so a failed first probe is cached permanently as "Intel."

This was low-stakes when it only chose an undervolt provider. As of the GitHub #172 fix it also gates `ModelCapabilityDatabase` name-pattern matching via `RequiredCpuVendor`, so a misdetection now silently changes which capability profile a board resolves to.

### F11 — `SetVoltageOffset` skips the wrapper path its siblings prefer **(Confirmed, low)**

`SetCoreClockOffset` and `SetMemoryClockOffset` try `NvAPIWrapper` first and fall back to legacy P/Invoke, with a comment that the wrapper is "more reliable for RTX 40 series" (`:769`, `:849`). `SetVoltageOffset` (`:903`) only ever uses the legacy path (`:924`). If the stated reliability claim is true, GPU voltage offset is the one control that doesn't get it.

### F12 — `Math.Clamp(percentOffset, MinPowerLimit, MaxPowerLimit)` can throw if the driver reports an inverted range **(Unverified, low)**

`AmdGpuService.SetPowerLimit` clamps against `MinPowerLimit`/`MaxPowerLimit` read from OD8 at init (`:238-239`, used at `:348`). `Math.Clamp` throws `ArgumentException` when `min > max`. If OD8 ever reports an inverted or partially-populated range, this throws rather than degrading. The surrounding `try/catch` (`:346`) does catch it, so the practical outcome is a logged error and `false` — but the failure would be misattributed to the driver call rather than our own argument validation. Cheap to guard; not worth hardware time to reproduce.

---

## 3. Recommendations

Sequenced so that everything shippable without hardware lands first. **The evidence gate applies throughout:** anything that changes what gets written to EC/SMU/MSR/NVAPI on real hardware needs field validation before shipping. Anything that only changes what we *say*, *store*, or *refuse to do* does not.

### Phase 0 — Ship now (no hardware risk, no behavior change on the wire)

These are all one-way-safe: they make the app more honest or stricter, never looser.

1. **Resolve F1 (per-core) by subtraction, not addition.** Do not implement per-core MSR writes to close this gap — that is a real hardware-behavior change on the highest-risk path we have. Instead: have `IntelUndervoltProvider`/`AmdUndervoltProvider` explicitly report that per-core offsets were **not applied** when the array is non-null, surface that in `UndervoltStatus.Warning`, and make `DiagnosticExportService` print `SavedPerCoreOffsets: <values> (NOT APPLIED — no backend support)`. Then decide separately whether to implement or delete the surface.
2. **Fix F5 (dead code)** by deleting the unreachable `switch` arms and keeping the `StrixPoint` gate with its existing rationale comment. Pure deletion; the gate's behavior is unchanged.
3. **Fix F9 (meaningless offsets)** — leave `ExternalCoreOffsetMv`/`ExternalCacheOffsetMv` null rather than 0 when the value was not read, and cache the probe result for a few seconds.
4. **Fix F10 (vendor default)** — add an explicit log line when detection falls through to the default, and do not memoise a result that came from the exception path.
5. **Fix F11 (voltage wrapper) and F12 (clamp guard)** — both are small, local, and low-risk. F11 does change a write path; gate it behind the existing wrapper-then-legacy fallback shape so a wrapper failure still reaches the current code.
6. **Consolidate F7** — move every clamp constant into `TuningGuardrails` as the single source of truth, with the GPU services reading from it. Keep the driver-reported refinement (`NvapiService:648-657`) as a *narrowing* step only: driver limits may tighten our policy, never widen it. Add a test asserting that.
7. **Add the missing regression tests** — in particular one asserting per-core offsets are reported as unapplied (locking in the honesty fix), and one asserting driver-reported limits cannot widen `TuningGuardrails` bounds.

### Phase 1 — Design work, no field data required

8. **Introduce `IGpuTuningProvider`**, mirroring `ICpuUndervoltProvider`: `ApplyAsync` / `ResetAsync` / `ProbeAsync`, with `NvapiService` and `AmdGpuService` behind it. This is the structural fix that dissolves F3 and most of F4 — one config shape, one command set, one clamp policy, one status surface.
   **Normalise power limit to absolute percent (100 = stock) at the interface boundary**, and have the AMD implementation convert to its offset form internally. That makes `TuningRollbackCoordinator`'s existing `PowerLimitPercent = 100` correct for both vendors instead of a live trap.
9. **Give AMD GPU OC the same safety machinery as NVIDIA (F4)** — persist to `GpuOcSettings`, join the test-apply→keep flow, join `TuningStartupRecoveryGuard` and `TuningRollbackCoordinator`. Do this *after* step 8, so it is a consequence of the abstraction rather than a second copy of the logic.
10. **Extend readback verification to the OGH and EC GPU Power Boost paths (F8)**, reusing `VerifyGpuPowerReadback`. If a path genuinely cannot be verified, say that in the status string rather than printing a checkmark — the pattern the WMI path already sets.

### Phase 2 — Needs field validation before shipping

11. **Readback for GPU OC and CPU UV (F2).** NVAPI can read applied P-state deltas; the AMD SMU path on PawnIO reportedly cannot, which is why `ApplyPowerLimits` points at `tools/SmuProbe --limits` over an independent transport. The design question is what the UI should show when applied ≠ requested — and specifically whether to **detect the documented platform re-assert** (8D87's `71/71/60/45 W`) and either warn or re-apply. Re-applying is a behavior change on the SMU write path and must not ship on inference.
12. **Calibrate or retire the CO↔mV conversion (F6).** Either measure the real per-count delta on at least two families and document it with the same rigor as the RyzenAdj message-ID citations, or drop the millivolt presentation for AMD entirely and show CO counts — which is what the hardware actually takes. The second option needs no hardware and is the honest default if measurement time isn't available.
13. **Only then** revisit whether `SetTctlTemp` and the broader power-limit families should be un-gated, per family, on evidence.

---

## 4. What not to do

- **Do not implement per-core undervolt writes to "finish" F1.** The gap is currently harmless because nothing reaches the hardware. Closing it by adding MSR writes converts a cosmetic dishonesty into a live risk on the most dangerous path in the app, to serve a feature with no UI and no demand behind it.
- **Do not unify AMD and NVIDIA GPU OC config before normalising the power-limit unit (F3).** That ordering is what arms the `PowerLimitPercent = 100` trap.
- **Do not add TDC/EDC current limits.** `AmdUndervoltProvider.cs:526-530` already argues this correctly: power limits are self-correcting and clear on reboot; current limits are neither. That reasoning should stay in force.
- **Do not treat SMU `Ok` as verification** anywhere new. It is already documented as meaning only "the mailbox accepted the message" (`:513-518`).

---

## 5. Open questions

These need answers from hardware, a reporter, or a deliberate product decision — not from more code reading.

1. **Is per-core undervolt wanted at all?** No UI, no config default, no field request found. If not, deleting the surface is strictly better than any amount of honesty plumbing.
2. **Can the PawnIO transport read back applied CO?** Determines whether F2 is fixable for AMD CPU UV or whether the UI must permanently label those values as "last requested."
3. **How often does the platform re-assert power limits?** The 8D87 observation is a single documented instance. Whether this warrants active re-assertion, a passive warning, or nothing depends on how common it is across boards.
4. **What is the real mV-per-CO-count on the families we support?** Needed for F6 option one; if unanswerable, take option two.
5. **Does any shipped board actually expose a working AMD dGPU OD8 path?** `AmdGpuService` is fully implemented but I found no field report exercising it. If it is untested in the wild, F4's priority drops and the honest move may be to gate the AMD GPU OC UI behind a confirmed-capability check.

---

## 6. Suggested order of work

| Step | Items | Risk | Gate |
|---|---|---|---|
| 1 | F1 honesty, F5 dead code, F9, F10 | None — logging/reporting only | Full suite |
| 2 | F7 guardrail consolidation + tests | None — narrowing only | Full suite |
| 3 | F11, F12 | Low — local, fallback preserved | Full suite |
| 4 | `IGpuTuningProvider` (F3 root fix) | Design | Full suite + review |
| 5 | AMD GPU OC parity (F4), F8 verification | Medium — new write paths for AMD | Field validation |
| 6 | Readback + re-assert policy (F2), CO calibration (F6) | High — changes what reaches silicon | Field validation, per family |

Steps 1–3 are independently shippable and would close every finding that can be closed without touching hardware behavior. That is roughly two thirds of the findings by count, and all of the ones where the app currently tells the user something untrue.
