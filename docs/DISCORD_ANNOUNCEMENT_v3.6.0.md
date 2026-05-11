# OmenCore v3.6.0 — Discord Announcement

---

## Short version (for #announcements / #releases channel)

> **OmenCore v3.6.0 is out!**
> 
> This release focuses on lightweight resource usage and reliability. Key highlights:
> 
> - **Crash fix** – Settings page no longer crashes on startup for affected users (#125-adjacent)
> - **Fan curve wake pulse** – Custom curves now self-recover from the v3.5.0 "zero RPM" stall instead of requiring manual mode switching (GitHub #125)
> - **Startup diet** – Dashboard, RGB, and conflict-scan startup deferred; tray-only sessions can now reach the 10 s ultra-low cadence tier
> - **Memory Optimizer** – before/after cleanup deltas, game-aware quiet window toggle, per-process exclusion from context menu
> - **Fan curve rebalance** – Extreme/Gaming curves no longer ramp near-Max at 70–80 °C; explicit Max still hits 100 %
> - **Calibration safety** – Wizard always restores Auto fan control after completion/cancel so fans can't be left pinned
> - **New model support** – OMEN 16-am0xxx (Intel Core Ultra + RTX 5070, #124) and OMEN Max 16-ah0xxx RTX 50-series TGP guidance (#123)
> 
> 500 / 500 unit tests. Full notes on GitHub.
> 
> **Download:** https://github.com/[your-repo]/releases/tag/v3.6.0

---

## Long version (for #dev-log / #changelog channel)

> ## OmenCore v3.6.0 Released
> 
> After v3.5.0's hardware coverage push, v3.6.0 is the resource & reliability follow-up. Here's what changed:
> 
> ### Bug fixes
> - **Settings crash** (real-device smoke blocker) — WPF TwoWay writeback against read-only cadence status properties; now `OneWay`.
> - **Custom curve zero-RPM hang (GitHub #125)** — EC sometimes accepts a write but leaves RPM at 0. The curve engine now detects this and sends a bounded 35–60 % one-shot wake pulse (60 s cooldown) to kick the hardware back into motion without user intervention.
> - **Auto handoff regression (BEAM)** — Auto presets with explicit curve payloads were getting overwritten by BIOS-default restore after Max transitions. Fixed.
> - **Fan curve ramp (Hades / snowfall hateall)** — Built-in Auto/Gaming/Extreme curves were hitting near-Max behavior at 70–80 °C. Rebalanced so moderate temps stay moderate; Max is still the go-to for full cooling.
> - **Stale Extreme override removed** — Runtime code was forcing 100 % fans at 75 °C, ignoring the rebalanced Extreme curve. Removed.
> - **Calibration pins fans (ZeroMentu)** — Wizard now always restores BIOS auto fan control after finish/cancel/failure. Added manual Restore Auto action for extra safety.
> - **Model identity conflict (Hades / 8A43)** — OMEN 16-n0xxx `8A43` was resolving as `8A44` via WMI name pattern. Exact ProductId now wins.
> - **GPU boost false-success (BEAM)** — Accepted-but-ignored WMI writes now fail verification. `Extended` mode added to WMI and OGH paths.
> - **WMI fan 100 % ceiling** — Full-speed requests now map to protocol ceiling so firmware can clamp to true hardware max.
> - **Custom curve delete requery (BEAM)** — Delete re-enables on selection instead of needing extra UI activity.
> 
> ### New model/hardware support
> - **OMEN 16-am0xxx (Intel Core Ultra + RTX 5070, GitHub #124)** — Safe fallback for ProductId-missing boards: EC writes disabled, WMI fan/performance/GPU boost/4-zone keyboard preserved.
> - **OMEN Max 16-ah0xxx 8D41 (GitHub #123)** — RTX 50-series TGP guidance when `nvidia-powerd` reports Dynamic Boost disabled by SBIOS; `nvidia-powerd` log now captured in diagnostics.
> 
> ### Startup & resource
> - Dashboard and SystemControl deferred to first open (no longer built at tray startup).
> - RGB stack (Corsair/Logitech/Razer/OpenRGB) deferred to first RGB tab access.
> - Conflict/tuning software scan deferred to first OMEN/Tuning/Monitoring/Optimizer tab.
> - Tray quick-popup, tray icon loop, OSD stats, and network timers are now visible-only and unregister when off-screen.
> - Tray-only sessions now settle to 10 s ultra-low cadence tier when no fan/OSD blockers are active.
> - Dashboard history capped; chart rendering no longer allocates `ToList()` on every frame.
> - Adaptive static-tray sampling reduces GPU telemetry cost at idle.
> 
> ### Memory Optimizer
> - Cleanup result deltas for physical/standby/cache/commit/pagefile/modified list.
> - Game-aware quiet window now uses fullscreen/borderless detection; toggle in UI.
> - Top-process rows can be added to working-set exclusions from context menu.
> - Dynamic exclusion suggestions based on currently high-memory processes.
> - Min-gap override for Auto Clean under sustained pressure.
> 
> ### RGB
> - Page header now shows ownership, HP backend status, provider details, and OMEN Light Studio/Gaming Hub conflict warnings.
> - "Restore Keyboard" action added.
> - Write serialization across all keyboard operations to prevent concurrent backend races.
> - Model-aware fallback retries for brightness/backlight.
> 
> ### Diagnostics
> - `resource-footprint.txt` in all exports (process footprint, cadence, active timers, GC).
> - `tuning-safety.txt` in all exports (undervolt/CO/GPU OC pending-test metadata).
> - `rgb-control-path.txt` in all exports.
> - Settings live cadence card (tier, reason, blockers).
> - Kernel ACPI/NVIDIA errors and service readiness in Linux `diagnose`.
> 
> ### Tests
> 500 / 500 pass. New coverage: WMI verification, fan smoothing/handoff, GPU power semantics, cadence telemetry, timer lifecycle, adaptive sampling, settings compatibility, and dashboard history pruning.
> 
> **Full release notes + download:** https://github.com/[your-repo]/releases/tag/v3.6.0
