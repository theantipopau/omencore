# OmenCore v3.6.0

**Lightweight resource pass · Reliability follow-up · Fan/RGB/Memory improvements**

---

## What's New

### Bug Fixes
- **Settings crash fix** – `MonitoringCadenceTier`, `MonitoringCadenceReason`, and `MonitoringCadenceBlockers` bindings are now explicit `OneWay`, preventing a WPF writeback crash when the Settings page opened on real hardware.
- **GitHub #125 / Custom fan curve zero-RPM hang** – Custom curves now detect the v3.5.0 "write accepted, RPM stays at 0" failure mode and send a bounded one-shot wake pulse (35–60%, 60 s cooldown) instead of requiring manual mode switching to recover.
- **GitHub #124 / OMEN 16-am0xxx (Intel Core Ultra + RTX 5070)** – Added a safe fallback for ProductId-missing boards that disables direct EC writes while preserving WMI fan/performance, GPU boost, and 4-zone keyboard detection.
- **GitHub #123 / OMEN Max 16-ah0xxx (8D41)** – Added guidance for RTX 50-series TGP caps where `nvidia-powerd` reports Dynamic Boost disabled by SBIOS/client request, including `nvidia-powerd` log capture in diagnostics.
- **Auto handoff regression (Discord: OsamaBiden/BEAM)** – Auto presets with explicit curve payloads no longer get overwritten by BIOS-default restore after `Max`/policy transitions.
- **Fan curve extreme ramp (Discord: Hades/snowfall hateall)** – Rebalanced built-in Auto/Gaming/Extreme curves so moderate 70–80 °C operation no longer ramps into near-Max behavior; explicit Max remains 100 % full-cooling mode.
- **Model identity conflict (Discord: Hades/8A43)** – Exact capability ProductId matches now win over broad WMI model-name patterns, preventing 8A43 OMEN 16-n0xxx systems from resolving as the sibling 8A44.
- **Fan calibration leaving fans pinned (Discord: ZeroMentu)** – Calibration wizard now always restores BIOS auto fan control after completion, cancellation, or failure so fans cannot be left pinned at Max until reboot.
- **GPU power boost false-success (Discord: OsamaBiden/BEAM)** – Hardened WMI verification; accepted-but-ignored writes now fail verification instead of reporting success.
- **WMI fan 100 % ceiling** – Full-speed requests now map to protocol ceiling, allowing firmware clamp to true hardware max.
- **Custom curve delete requery (Discord: OsamaBiden/BEAM)** – Delete command re-enables immediately on selection without requiring additional UI activity.

### Startup & Resource Improvements
- Dashboard and SystemControl are no longer eagerly loaded at tray startup — only constructed when the relevant page is opened.
- RGB/peripheral (Corsair, Logitech, Razer, OpenRGB) initialization deferred to first RGB-tab access.
- Conflict/tuning software scan deferred until Monitoring, OMEN, Tuning, or Optimizer tabs are opened.
- Tray quick-popup refresh, tray icon loop, OSD stats, and network timers are now visible-only — they unregister automatically when not on screen, and appear in the resource-footprint diagnostics export.
- Dashboard metric history now capped by age and count; chart rendering avoids `ToList()` allocations on every frame.
- Memory Optimizer page refresh timer pauses when the tab is not open; Settings schedule enforcement timer starts only when schedule rules exist.
- Adaptive static-tray sampling reduces GPU telemetry refresh cost during tray-only idle sessions.
- Tray-only sessions can now settle to the 10 s ultra-low cadence tier when no fan-ownership or OSD blockers are active.

### Memory Optimizer
- Auto Clean now exposes a persisted minimum-gap override for sustained-pressure tuning.
- Game-aware quiet window now uses monitor-aware fullscreen/borderless detection; working-set-only path used for foreground games unless memory pressure is critical.
- Game-aware quiet window toggle now user-configurable.
- Top-process rows can be added to working-set exclusions directly from the context menu.
- Dynamic exclusion guidance suggests currently high-memory processes not yet excluded.
- Cleanup result details now show before/after deltas for physical used/available, standby list, system cache, commit, page file, and modified page list.

### RGB
- Lighting page now surfaces control ownership, HP keyboard backend, provider status, and OMEN Light Studio/Gaming Hub conflict warnings in the page header.
- Dedicated "Restore Keyboard" action reapplies saved HP keyboard colors through the active backend.
- Keyboard lighting writes serialized across profile/zone/test/brightness/backlight operations to prevent concurrent backend-switch races.
- Brightness and backlight operations now use model-aware fallback retries.
- Diagnostics export includes `rgb-control-path.txt`.

### Fan Control UX
- Fan Control now shows a fan ownership panel (firmware / OmenCore Max / constant duty / managed curve) with backend/status context.
- Fan curve editor drag uses canvas-level mouse capture and tracks dragged `FanCurvePoint` by reference for better responsiveness on dense/boundary nodes.
- Safety floors shown as requested-vs-effective preview with live refresh on temperature changes.

### Diagnostics
- `resource-footprint.txt` included in all diagnostics exports with process footprint, cadence state, active timers, and GC/runtime info.
- `tuning-safety.txt` included with undervolt/CO/GPU OC pending-test metadata and confirmed values.
- Settings now shows a live monitoring cadence card (tier, reason, and active blockers).
- CPU undervolt/EDP and tuning loops now registered with `BackgroundTimerRegistry` so they appear in exports.
- Linux: `omencore-cli diagnose` reports recent kernel ACPI/NVIDIA errors and service/package readiness.

### Linux
- Performance-hold readback treats Default/Balanced as equivalent to reduce unnecessary reasserts.
- `daemon --install` / `daemon --generate-service` now share one systemd unit generator.
- `daemon --status` reports service/config state plus active performance-hold settings.

### Other
- Shared first-party status glyphs and badges for confirmed/degraded/blocked/overwritten states across Dashboard, Fan, RGB, and Performance surfaces.
- Global hotkey registration de-duplicates queued actions and rejects conflicting chord mappings before Win32 registration.
- Removed obsolete runtime polling-interval UX; settings reflects the real automatic cadence policy.
- Refreshed Inno Setup wizard images with updated OmenCore branding.
- Version metadata aligned across app, hardware worker, Avalonia, and installer (all report `3.6.0` / `3.6.0.0`).

---

## Test Coverage

500 / 500 unit tests pass. New tests added this cycle cover: WMI verification semantics, fan preset/Auto handoff, fan smoothing/diagnostic state cleanup, fan command requery, GPU power semantics, fan-level mapping, monitoring cadence telemetry, deferred conflict monitoring startup, Memory/Settings timer lifecycle, adaptive static-tray sampling, settings compatibility persistence, live cadence tier/blocker summaries, and dashboard metric history pruning.

---

## Artifacts

| File | Description |
|------|-------------|
| `OmenCoreSetup-3.6.0.exe` | Windows installer (recommended) |
| `OmenCore-3.6.0-win-x64.zip` | Windows portable (self-contained) |
| `OmenCore-3.6.0-linux-x64.zip` | Linux CLI + Avalonia GUI |
| `OmenCore-3.6.0-linux-x64.zip.sha256` | Linux package SHA-256 |
| `SHA256SUMS-3.6.0.txt` | All artifact checksums |

---

## Notes

- Windows: run the installer as Administrator. PawnIO kernel driver is bundled.
- Linux: extract the zip, run `sudo ./install.sh`, then `omencore-cli --help`.
- Physical hardware validation (fan ownership, curve wake pulse, calibration restore) is recommended before deploying in production environments.

**Full detailed changelog:** [docs/CHANGELOG_v3.6.0.md](docs/CHANGELOG_v3.6.0.md)
