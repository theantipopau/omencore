# OmenCore v3.6.2 - Release Changelog and Validation Notes

**Version:** 3.6.2
**Release Date:** May 18, 2026
**Release Status:** Stable
**Previous Release:** v3.6.1
**Type:** Core control stabilization, fan/performance/RGB reliability, runtime source-of-truth cleanup, and UI responsiveness release

## Release Artifacts and SHA256

- `OmenCoreSetup-3.6.2.exe`
	- SHA256: `E9D32804E2ACC0BA2C01E4D78FD3B384BFE486934708524C4A3E3C8AEB83B7E0`
- `OmenCore-3.6.2-win-x64.zip`
	- SHA256: `84D3EC3E4C5633F3B3DCA7825F43864F32FEF4A26A34796C2C03C1BD30C18BB0`
- `OmenCore-3.6.2-linux-x64.zip`
	- SHA256: `5B5332E761BF11692FF91F967956A62CB38858A7C2E9373E7F403EC3FDED90B6`

---

## Overview

v3.6.2 is a fundamentals-first stabilization release. The priority is not new surface area; it is making the core OmenCore promises more trustworthy: fan control should apply through one authority, performance modes should report the mode that actually stuck, RGB should fail gracefully when a provider cannot support an effect, and every visible surface should agree about the current state.

This release continues the v3.6.0 and v3.6.1 remediation path. The main shift is that tray, Quick Access, dashboard, OSD, General, Fan Control, and Lighting now prefer service-confirmed state instead of optimistic UI requests. A requested change can still be shown while it is in flight, but it should no longer masquerade as the confirmed hardware state if the backend rejects, delays, or normalizes the command.

The field theme behind this release was clear: users were not only reporting isolated bugs, they were reporting loss of trust. Fans could sound different from the selected mode, performance labels could drift, RGB scene failures could look successful, and the UI could feel sluggish under telemetry load. v3.6.2 addresses those as linked runtime ownership problems.

OmenCore remains an independent package. External projects such as OmenMon/OmenMon-Reborn and newer community HP/OMEN Linux work were used as hardware-behaviour references for mode readback, EC ownership, and Linux sysfs expectations only. OmenCore does not depend on those projects at runtime.

---

## Implemented From v3.6.1 and v3.6.2 Field Reports

- v3.6.2 core runtime fix: tray, dashboard, OSD, and General profile state now follow confirmed `FanService` and `PerformanceModeService` events rather than selected UI labels. This closes the class of bugs where OmenCore looked like it had applied a fan or performance mode before the backend confirmed it.
- v3.6.2 fan control fix: direct Quick Access fan operations now publish the same `PresetApplied` confirmation path as normal preset application, including Auto, Quiet, Max, and duplicate Max requests. This keeps hotkeys, tray, OSD, and dashboard state in the same update stream.
- v3.6.2 fan control fix: WMI V1 Auto/default handoff now clears the old manual floor with `SetFanLevel(0, 0)` after returning control to firmware. This targets the older 3.2.5 -> 3.3.0 class of regressions where fans could remain pinned or transition poorly after Performance/Max/manual paths.
- v3.6.2 fan control fix: built-in Gaming, Extreme, Quiet, Auto, Max, and Custom runtime identities are now kept canonical across hotkeys and Quick Access. Custom is skipped when there is no real saved or active custom curve, so the OSD no longer announces a fake Custom state.
- v3.6.2 fan control fix: built-in curve-backed presets apply their real curves immediately on user action instead of displaying a curve in the editor while waiting for the next monitoring cadence to write the fan target.
- v3.6.2 fan control fix: edited custom curves and ad-hoc custom curves now persist through apply/restart paths, and deleting a custom preset clears stale `LastFanPresetName` / `CustomFanCurve` state instead of resurrecting a deleted curve on the next launch.
- v3.6.2 fan UX fix: the custom curve editor and custom preset management controls now appear only when the Custom preset is selected. Saved preset selection already filters to user-created presets only, which keeps built-in Quiet/Gaming/Auto/Extreme modes from looking like duplicate custom profiles.
- v3.6.2 fan safety fix: diagnostics and calibration restore the previous fan state more deliberately. Diagnostics restore the prior curve immediately after verification, and calibration retries BIOS auto-control cleanup so the final 100% step is less likely to leave fans audibly pinned.
- v3.6.2 performance-mode fix: WMI performance readback now uses EC register `0x95` when EC access is available, giving OmenCore a stronger hardware readback path before reporting the active mode.
- v3.6.2 performance-mode fix: model-specific TDP overrides now update the stored effective mode after overrides resolve, so the service state reflects the mode actually being held rather than the pre-override request.
- v3.6.2 performance-mode fix: decoupled WMI thermal-policy fallback is now opt-in through `AllowDecoupledWmiThermalPolicyFallback`. This avoids silently using a fallback path that can make fan/performance ownership look coupled when the user expects independent control.
- v3.6.2 quick-profile fix: the Performance quick profile now uses the bounded Gaming/Extreme cooling curve instead of forcing Max cooling as the first-choice fan behaviour. This addresses user feedback that normal performance testing could sound like OmenCore had taken over the fans too aggressively.
- v3.6.2 tray UI fix: tray Quick Profile wording now says `Gaming cooling + Performance mode`, matching the backend behaviour instead of promising Max cooling.
- v3.6.2 RGB fix: `TurnOffAllAsync()` now targets only providers that advertise Off capability, preventing unsupported providers from receiving an Off request they cannot perform.
- v3.6.2 RGB fix: RGB effect fanout now classifies additional payload forms before provider dispatch, including `breathing:#RRGGBB`, `pulse:#RRGGBB:1000`, `wave`, and `off`. Static-only providers are skipped with explicit logs instead of producing noisy failure chains.
- v3.6.2 RGB scene fix: provider prepare failures now block that provider's commit write, `LastSyncResult` records the sync outcome, and failed scene applies no longer become `CurrentScene` or raise successful `SceneChanged` notifications.
- v3.6.2 RGB reliability fix: RGB, fan, performance, and runtime event subscribers are now isolated so one failed listener cannot stop other UI surfaces from receiving the confirmed state.
- v3.6.2 monitoring fix: dashboard uptime and last-sample timestamps no longer advance while dashboard telemetry projection is disabled. Hidden or minimized surfaces stay dormant instead of continuing to mutate chart/history state.
- v3.6.2 UI responsiveness fix: remaining synchronous `Dispatcher.Invoke` paths in service and view-model callbacks were moved to non-blocking dispatcher scheduling. This reduces UI stalls from OSD, hotkey, notification, lighting, optimizer, fan calibration, and curve recovery callbacks.
- v3.6.2 UI responsiveness fix: tray, Quick Access popup, General, dashboard, and summary bindings skip redundant rendered state where possible, lowering redraw pressure during monitoring ticks.
- v3.6.2 battery telemetry fix: battery capacity health no longer falls back to a fake `100%` value when design/full-charge capacity cannot be read. The dashboard now reports capacity health as unavailable instead of implying a perfect battery.
- v3.6.2 GPU telemetry fix: implausible laptop GPU power readings are normalized or suppressed for RTX 40-series and RTX 50-series laptop GPUs before they reach tray, dashboard, or OSD surfaces.
- v3.6.2 model support: added exact capability and keyboard mappings for OMEN 16-ap0xxx ProductId `8E35`, Victus 16-s0xxx ProductId `8BD4`, and Victus e0xxx ProductId `88EC`, using conservative defaults where physical validation is still pending.
- v3.6.2 Linux fix: hp-wmi hwmon manual fan duty now writes the paired `pwmN_enable` and `pwmN` files through the CLI and daemon curve path, while preserving safety blocks against fan-off writes.
- v3.6.2 Linux diagnostics: diagnose output now captures hp-wmi board-support evidence such as multiplex hints, `pwm*_enable`, `pwm*`, fan inputs, AP-series notes, and NVIDIA ACPI/D3cold clues without recommending unsafe legacy EC writes.
- v3.6.2 game profile fix: Create/Edit Profile now opens the intended profile editor, and profile saves validate the edited profile at save time before refreshing process tracking.
- v3.6.2 hotkey fix: Win+F12 now opens OmenCore even when focused hotkeys are disabled for background operation, giving users a reliable recovery/open shortcut.
- v3.6.2 versioning fix: app, worker, Linux, Avalonia, desktop project metadata, installer fallback metadata, and `VERSION.txt` now report v3.6.2 / 3.6.2.0.

---

## User-Reported Issues Addressed

### Fan and Performance State Drift
**Severity:** High
**Reported via:** Discord field feedback and v3.6.1 evidence packs, especially the fundamentals-first feedback around fan/performance trust and UI consistency.

Users reported that the app, tray, OSD, and OMEN/Fan pages could disagree about the active mode. v3.6.2 routes state projection through confirmed service events, isolates subscriber failures, and updates General/Tray/OSD/dashboard from the same canonical runtime state. This does not make every firmware write instantly verifiable on every model, but it does stop OmenCore from confidently displaying a mode simply because it was requested.

### Fan Control Regressions From the v3.2.5 -> v3.3.0 Era
**Severity:** High
**Reported via:** earlier Discord/GitHub fan reports, later rechecked during the v3.6.2 audit.

The audit rechecked the underlying shift from coupled fan/performance behaviour into more decoupled profile control. The risky fundamentals were preset identity, Auto/default handoff, manual floor cleanup, Max/Performance transitions, and UI labels becoming authorities. v3.6.2 tightens these by using confirmed runtime state, restoring the V1 Auto floor clear, applying curve-backed presets immediately, and keeping Gaming/Extreme/Quiet/Custom identities explicit.

### Performance Mode Accuracy
**Severity:** High
**Reported via:** field reports around GPU power caps, mode labels, Linux performance mode availability, and OmenMon-style readback comparisons.

Performance mode readback now has a stronger EC-backed path where available, model override application updates effective service state, and risky decoupled WMI thermal-policy fallback is disabled by default. The intent is simple: if OmenCore says Performance, Balanced, or Quiet is active, that label should come from the best confirmed state available rather than a stale UI selection.

### RGB Scene and Effect Reliability
**Severity:** Medium
**Reported via:** GitHub #130 and RGB capability/degradation reports.

RGB dynamic effects now degrade more cleanly on providers that only support static lighting. Unsupported dynamic/off requests are filtered before provider fanout, provider prepare failures prevent commit writes to that provider, and failed scenes no longer become the active scene in the UI. Thanks to the GitHub #130 reporter for narrowing the unsupported-effect path.

### Thermal Authority and Diagnostics
**Severity:** Medium
**Reported via:** GitHub #129 and low-temperature/high-load telemetry reports.

CPU thermal authority now defers attribution until fallback evaluation completes, records source/reason more clearly, and prefers stronger fallback data under suspicious low-temp/high-load divergence. Diagnostics export includes clearer authority state so support can distinguish sensor drift from real thermal behaviour.

### Model Identity and Capability Fallbacks
**Severity:** Medium
**Reported via:** GitHub #128, GitHub #127, Linux board-class feedback, and Discord device reports.

Exact and conservative model mappings were expanded for newly reported ProductIds, while Linux capability classification and diagnostics were tightened for hp-wmi/hwmon boards. Thanks to the reporters behind GitHub #127, #128, and the 8E35/8BD4/88EC field logs for the model evidence.

### UI Slowness and Sluggishness
**Severity:** High
**Reported via:** Discord feedback from OsamaBiden / BEAM, Ethernet / .swf, Azathoth, and follow-up field reports describing high GPU usage, high focused-window temperatures, and the UI feeling like `10hz` or even `0.3fps` in real sessions.

v3.6.2 continues the responsiveness work by suppressing hidden-surface projection, reducing redundant tray/popup/dashboard redraws, moving service callbacks away from blocking dispatcher calls, and adding runtime performance counters for support snapshots. This is a major mitigation pass, not the end of the UI architecture work; deeper simplification remains a v3.7.0 follow-up.

### Custom Curve UX
**Severity:** Medium
**Reported via:** Discord feedback from OsamaBiden / BEAM.

The custom curve area was too busy for normal fan-mode switching and made built-in modes feel duplicated beside saved custom presets. v3.6.2 keeps saved preset selection scoped to user-created presets and now hides the custom editor/settings controls unless the Custom preset is active.

### Low-Temperature Thermal-Limit / Power-Lock Reports
**Severity:** High
**Reported via:** Discord feedback from Mr.Carrot and OsamaBiden / BEAM.

The reported symptom was CPU package power being capped around 20-30W while thermal-limit indicators showed 100% at only 40-50C, with older custom fan preset flows suspected as a trigger. v3.6.2 adds thermal authority hardening and diagnostics for low-temperature/high-load divergence, but this remains a hardware-validation scenario rather than a fully reproduced local bug.

### Linux hp-wmi and Board Support
**Severity:** Medium
**Reported via:** Discord feedback from Eric [GOG], Loco Motivo, and Linux board-class users, plus GitHub #127.

Linux diagnostics now explain more of the board-specific hp-wmi/multiplex state, and manual fan duty support follows kernel hwmon PWM paths where available. The release keeps the guidance conservative: use known sysfs/hp-wmi paths where exposed, avoid unsafe legacy EC writes, and report exact board evidence when support remains incomplete.

---

## UI Improvements

- Quick Access now includes a direct dashboard button for users who want to jump from the compact tray workflow into the full monitoring view.
- Tray Quick Profile wording now matches actual behaviour: Performance uses Gaming cooling plus Performance mode, not Max cooling.
- Quick Access dashboard action text now uses the clearer `Dashboard` label in the compact action row.
- Fan Control now hides the custom curve editor and custom preset management row until the Custom preset is selected, reducing normal-mode clutter.
- Settings General rows wrap long descriptions so toggles remain visible at narrower widths and higher display scaling.
- Hidden General and dashboard surfaces stop consuming telemetry projection work while not visible.
- Tray and Quick Access render caches skip no-op updates to reduce visible stutter during monitoring.
- OSD mode updates use non-blocking dispatcher scheduling and receive confirmed fan/performance state through the same service-owned path as tray and dashboard.
- Battery capacity wording now avoids confusing capacity health with charge-limit state.
- Game Profile create/edit flows now open the intended editor state instead of appearing inert.

---

## Diagnostics and Validation

Validation completed during v3.6.2 stabilization:

```powershell
dotnet build src\OmenCoreApp\OmenCoreApp.csproj -c Debug --no-restore -p:UseSharedCompilation=false
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj -c Debug --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~FanPresetVerificationTests|FullyQualifiedName~PerformanceModeServiceTdpOverrideTests|FullyQualifiedName~RgbManagerTests|FullyQualifiedName~RgbSceneServiceTests|FullyQualifiedName~RuntimeStateEngineTests|FullyQualifiedName~GeneralViewModelTests"
```

Observed outcomes:

- Windows app Debug build: PASS, 0 warnings, 0 errors.
- Final GUI sweep build after tray/Quick Access wording and custom-curve visibility cleanup: PASS, 0 warnings, 0 errors.
- Focused core-control regression run: PASS, 63/63.
- Earlier focused fan/performance/OSD/tray/RGB/dashboard validation slices passed during the RC hardening cycle.
- `git diff --check` passed; only line-ending warnings were reported.

Validation added or expanded:

- Fan preset confirmation and direct quick-mode event coverage.
- Performance mode model-override/effective-state coverage.
- RGB manager provider filtering and scene failure coverage.
- Runtime state subscriber isolation coverage.
- GeneralViewModel confirmed-state sync coverage.
- Tray/popup render dedupe and hidden-surface projection coverage.

Known validation caveats:

- Final release packaging should run the full Windows test suite and Release build in the operator environment.
- Physical hardware sign-off is still required for the newest exact model mappings and Linux hp-wmi board paths.
- Parallel build/test invocations can still collide on WPF intermediate files; serial reruns have passed.

---

## Known Limitations

- v3.6.2 greatly improves UI responsiveness, but the long-term UI architecture cleanup remains tracked for v3.7.0. The app should now do less redundant work, but the final shape still needs a smaller MainViewModel and fewer cross-surface update paths.
- Firmware and OMEN Gaming Hub can still override or normalize some fan/performance writes. v3.6.2 improves honesty and recovery, but it cannot make every BIOS accept every command.
- Some hardware-specific RGB effects remain provider-dependent. Unsupported effects should now degrade cleanly rather than pretending to succeed.
- Linux support remains board/kernel dependent. hp-wmi, hwmon, ACPI platform profiles, and distro packaging vary enough that diagnostics evidence is still required for new boards.

---

## Thanks

Thank you to the Discord and GitHub users who kept pushing on the fundamentals instead of letting these bugs stay vague. In particular: OsamaBiden / BEAM for the responsiveness, fan-control, battery OSD, and custom-curve feedback; Ethernet / .swf and Azathoth for UI lag/high-GPU/high-temperature feedback; Mr.Carrot for the low-temperature thermal-limit/power-lock report; Hades / snowfall hateall for fan-curve and thermal behaviour reports; ZeroMentu for fan calibration and hotkey reliability reports; Eric [GOG] and Loco Motivo for Linux hp-wmi/multiplex follow-up; and the GitHub reporters behind #127, #128, #129, and #130 for the model, Linux, thermal authority, and RGB evidence.

This release is better because those reports included the awkward details: screenshots, logs, exact ProductIds, symptoms that only appeared after a few transitions, and honest descriptions of when the app felt slow or untrustworthy.
