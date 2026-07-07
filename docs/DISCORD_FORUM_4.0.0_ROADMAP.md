# OmenCore 4.0.0 — Where We're Headed (Want Your Input)

Posting the plan before locking it in. No release date — some of this is quick, some is multi-release work.

## 3.9.0 bugs found since ship

- **General tab fan selector** shipped broken — fixed, artifacts rebuilt. Thanks OsamaBiden for the repro.
- **Max Fan buried 3 clicks deep in tray menu** — getting promoted.
- **OSD shows stale "Auto" at Max fan** — traced the code, should work, doesn't. Need a repro log (tray → Max → open overlay) before fixing blind.
- **FPS overlay "not working"** — probably needs RTSS running; messaging will say so instead of looking dead.
- **8BCD long-load fan slowdown** — evidence-gated, need per-poll RPM logs first.

## What 4.0.0 is aiming at

- **RGB** — Corsair/Razer/Logitech all uneven (fake status data, Razer near-placeholder). Real work, plus a Sync-All double-write fix.
- **OC/UV** — AMD GPU has no save-on-restart; AMD undervolt is manual-only. Want self-checking power-limit changes, not manual field reports.
- **Less admin dependency** — split hardware writes into a limited-privilege service instead of running fully elevated. Biggest lever on AV false-positives.
- **Trust/security** — not code-signed; updater's hash comes from the same channel as the binary; zero firmware-path tests.
- **New-model bottleneck** — every model needs a hand-written DB entry. Want a community contribution path + a first-run "what your model supports" screen.
- **Linux** — `omencore-gui` has no tray icon, no saved settings.
- **Translations** — nothing localized, all hardcoded English.
- **Accessibility** — near absent.
- **Under the hood** — one oversized class, 21 uncoordinated timers, wasted allocations.

## What we want from you

- Missing anything? Drop it in #design-feature-requests.
- Pick 2-3 above — what goes first?
- On a flagged board? Diagnostics/logs move things forward, not vibes.

Full writeup: [ROADMAP_v4.0.0.md](https://github.com/theantipopau/omencore/blob/main/docs/ROADMAP_v4.0.0.md)
