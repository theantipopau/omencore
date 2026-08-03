# OmenCore v4.1.6 – EC Power-Limit Safety, Max-Fan Latch Fix, GPU Boost Diagnostics Clarity

**Release Date:** TBD
**Release Status:** Code-complete and test-verified in this environment (1005/1005 tests, 0 build warnings); artifacts not yet built or tagged.
**Type:** Patch release — a safety-relevant fail-safe default, a fan-control bug fix, and two diagnostics-clarity fixes, found while triaging field reports (GitHub #159, Discord SAINTOP/board `8DCD`)
**Base Version:** v4.1.5
**Tracking doc:** `docs/ROADMAP_v4.0.0.md` — see "Newly Reported (2026-08-02, Post-4.1.5): GPU Power Boost Follow-Up, GitHub #159, and Two Smaller Items" and "Newly Reported (2026-08-03, Post-4.1.5): Board `8DCD` Fans Stuck at Max After Leaving Max Mode" for the full traces this release acts on.

---

## Fixed: Unconfirmed EC Power-Limit Register Writes Were Attempted by Default

`PerformanceModeService.DirectEcPowerLimitWritesBlocked` gated `PowerLimitController`'s CPU PL1/PL2 and GPU TGP writes on `ModelCapabilities.SupportsFanControlEc` — a flag that also controls real, working EC fan control on many boards and **defaults to `true`**. `PowerLimitController`'s own header comment already documents its EC register addresses (`0xC0`-`0xC5`) as unconfirmed: `// HP Omen EC register addresses (EXAMPLE - varies by model!)`, with an explicit warning that incorrect values `can cause system instability or hardware damage`. Reusing the fan-control flag as the gate meant any board whose database entry didn't explicitly opt out got this unconfirmed, higher-risk write path attempted by default — including boards that have never had these specific register addresses confirmed for their hardware.

Found by tracing GitHub #159 (ShantanuVasagadekar, OMEN 16-n0xxx AMD, board `8A44`): Performance Mode logged `Power limits applied: CPU=95W, GPU=140W` and reported success unconditionally, while the reporter's measured GPU power stayed well below a previously-observed baseline (~75W vs. ~115-120W they'd seen on the same hardware before).

**Fix:** added a new, dedicated `ModelCapabilities.SupportsEcPowerLimits` flag, defaulting `false` and fully decoupled from `SupportsFanControlEc`. `PerformanceModeService.DirectEcPowerLimitWritesBlocked` now checks the new flag instead of the shared one — every board's unconfirmed EC power-limit write path is blocked by default (fail-safe) until a model's real register addresses are confirmed and the flag is deliberately set `true` on that entry. No existing `ModelCapabilities` entry sets it today, so this is a pure safety tightening, not a regression for any currently-shipping board — the EC power-limit write path has never been confirmed correct for any model in this database. Real EC fan control (a completely different, already-working feature on many boards) is untouched, since it no longer shares a gate with this. 2 new tests: one confirms a board with `SupportsFanControlEc = true` (real fan control works) still blocks EC power-limit writes when `SupportsEcPowerLimits` is left unset; one confirms the explicit opt-in still works.

## Fixed: GPU Power Boost Status Text Claimed "NVAPI Power Limits Available" When They Weren't

`SystemControlViewModel`'s GPU Power Boost status message had a branch that appended `" (NVAPI power limits available)"` whenever `GpuNvapiAvailable` was true — but that property only means NVAPI itself initialized (a GPU was detected via NVAPI), not that power-limit writes are actually supported. The correct property for that, `GpuPowerLimitAvailable`, already existed and was used correctly one branch above it. GitHub #159 showed exactly this mismatch in practice: the log reported `NVAPI: NVAPI returned no writable power policy entries` and `Supports Power Limit: False`, while the UI simultaneously displayed the misleading "available" note.

**Fix:** the branch now checks `GpuPowerLimitAvailable` instead of `GpuNvapiAvailable`, matching the property that's actually being described. Pure text-logic fix, no control-availability behavior changed.

## Fixed: Switching Performance Mode While Max Fans Was Active Left Fans Stuck at Maximum

Reported by Discord user SAINTOP (HP Victus 15 fa2082wm, board `8DCD`): enabling Performance Mode + Maximum Fans worked as expected, but switching back to Balanced — from either the General tab or Custom tab with fan control set to Auto — left the fans running at maximum speed. Only fully closing OmenCore released them. A related symptom was also reported: after eventually reaching Auto via Quick Access, fans would later spike to maximum and cycle repeatedly between maximum and auto on their own.

**Root cause:** `WmiFanController.SetPerformanceMode(string)` sent `_wmiBios.SetFanMode(...)` and, on success, cleared the internal `_isMaxModeActive`/`IsManualControlActive` tracking flags — but never sent `_wmiBios.SetFanMax(false)`. These are two independently-required BIOS commands (`ResetFromMaxMode()`'s own Step 1/Step 2 structure already establishes this). With the flag falsely cleared, every subsequent `RestoreAutoControl()` call saw `_isMaxModeActive == false` and skipped the reset that would have released the real hardware latch — matching the report exactly, since both the General-tab Balanced switch and the Custom-tab Balanced+Auto switch route through `SetPerformanceMode` first.

**Fix:** `SetPerformanceMode` now sends `SetFanMax(false)` before clearing its tracking flags whenever `_isMaxModeActive` was true, actually releasing the hardware latch instead of only the in-memory state. This is a one-way risk-reduction fix (can only cause a needed release to be sent, never a new write behavior) and doesn't require new field validation under this project's evidence-gate rule, consistent with the board-`8A18` Max-mode fix shipped in 4.1.0. 3 new tests in `WmiFanControllerPerformanceModeMaxReleaseTests.cs` cover: the latch is released when switching away from an active Max hold, no redundant `SetFanMax` call is sent when Max mode was never active, and `RestoreAutoControl()` correctly finds nothing left to do afterward. Likely also resolves the spontaneous max/auto cycling symptom as a side effect (the firmware fighting a contradictory latched-but-told-otherwise state), though that hasn't been independently confirmed — see `docs/ROADMAP_v4.0.0.md` for detail.

## Fixed: GPU Power Boost Status Didn't Flag When the Displayed Level Doesn't Match Hardware

When a user has a saved GPU Power Boost preference (e.g., "Maximum"), `DetectGpuPowerBoost()` deliberately doesn't overwrite the UI's selected level with what's actually detected on the hardware, so a user's choice isn't silently reverted by a routine capability re-detection. GitHub #159 showed the consequence of this on a system where startup restore is disabled (or a reapply attempt fails): the level selector still shows "Maximum," the saved config still says "Maximum," but the hardware can genuinely still be at "Minimum" until the user manually reapplies it — and the status text read as a plain "Minimum (detected via WMI)," easy to miss as contradicting the selected level next to it.

**Fix:** when a saved preference exists and doesn't match the freshly-detected hardware level, the status text now says so explicitly — `"Minimum (detected via WMI; saved preference 'Maximum' not yet applied)"` instead of just naming the detected level. The selected level itself is unchanged (still respects the user's saved preference); only the status text's honesty improved. No test coverage added — `DetectGpuPowerBoost()` is deep, hardware-entangled code (concrete `HpWmiBios`/CIM dependencies, not behind a mockable interface) with no existing test infrastructure, consistent with how the Logitech and Razer fixes earlier this cycle were verified (build-clean + code review only).

---

## Traced, Not Fixed: GPU Power Boost Wattage Mismatch (Victus `8A25` and OMEN `8A44`)

Neither this release nor the prior one changes the actual GPU-boost apply path. Full trace, including confirmation via git history that the WMI payload bytes are stable and OmenMon-reference-aligned (not the cause), is in the roadmap. Holding for more field evidence — specifically, whether the already-implemented "Extended" boost tier (`ppab=2`, one step above "Maximum") reaches the wattage OMEN Gaming Hub does on these boards.

## Not Actioned This Release

- **`SystemControlViewModel.TryApplyEcGpuBoost()`'s EC fallback path** writes to register `0xCE` — the same general performance-mode register `PowerLimitController.ApplySimplifiedMode` uses — as a crude GPU-boost proxy, gated only by a broad `model.Contains("OMEN")` substring match with no capability-database flag at all (not even the new `SupportsEcPowerLimits`). This is the same class of "unconfirmed EC write, over-broad gate" risk fixed above for `PowerLimitController`, just in a different call site. Not touched this release — flagging for a dedicated pass rather than expanding scope here.
- GitHub #159's remaining findings (CPU/GPU temperature freeze-detection sensitivity, the RPM-readback structural gap already documented for other boards) are consistent with already-tracked items elsewhere in the roadmap, not new.
