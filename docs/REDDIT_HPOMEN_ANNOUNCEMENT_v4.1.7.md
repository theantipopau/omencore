# v4.1.7 Reddit Announcement — r/HPOmen (intro post)

This is an introductory post for a subreddit where most readers won't know the project, unlike r/OmenCore. It leads with what OmenCore is and how far it's come since v4.0.0, then covers v4.1.7 specifically. See `REDDIT_ANNOUNCEMENT_v4.1.7.md` for the r/OmenCore version (assumes familiarity, full changelog detail).

Title:

```text
OmenCore — a free, open-source alternative to OMEN Gaming Hub (fan control, RGB, undervolting, no telemetry, Windows + Linux) — v4.1.7 just shipped
```

Body (Reddit markdown):

```markdown
Most of you have probably never heard of this, so a proper intro first, then what's new.

## What is OmenCore

**OmenCore** is a free, open-source, local-first control center for HP OMEN and Victus laptops — basically an independent replacement for OMEN Gaming Hub, built by a community developer (not HP) because OGH itself has a long track record of being heavy, buggy, and account/cloud-gated for features that should just be local settings.

It runs completely standalone — OGH doesn't need to be installed, or even present — and talks to the hardware through the same local interfaces OGH itself uses: WMI BIOS calls, direct EC access where safe, [PawnIO](https://pawnio.eu/) for MSR-level CPU work on Windows, and native `hwmon`/`sysfs` paths on Linux (yes — full Linux support, both a CLI and a GUI app, which OGH obviously never had).

**What it does:**

| Area | What it provides |
|---|---|
| Fan and thermal control | BIOS fan profiles, Max/Auto handoff, custom fan curves where the model safely supports them |
| Performance profiles | Quiet / Balanced / Performance, custom profile routing |
| GPU controls | MUX switching, GPU Power Boost on supported firmware |
| Overclocking / undervolting | GPU core/memory/power offsets, CPU undervolt (Intel + AMD) where the platform allows it |
| RGB | OMEN keyboard zone lighting, per-key/per-LED on supported boards, plus Corsair/Razer/Logitech/OpenRGB integration |
| Monitoring | CPU/GPU temp, load, fan telemetry, history, and honest capability diagnostics |
| OSD and tray | Click-through in-game overlay, hotkey toasts, quick-access popup |
| Cleanup | OMEN Gaming Hub / HP bloatware detection and removal |

**Why people switch to it:**

* **No telemetry, no account, no ads.** Nothing phones home by default.
* **Local-first.** No cloud dependency for settings that should just live on your machine.
* **Open source (MIT).** Every EC/WMI write it makes is auditable in the actual source — nothing is a black box.
* **Safety-gated.** Unsupported EC/fan/RGB paths on your specific board stay hidden or diagnostic-only rather than guessing and risking your hardware. Support is built board-by-board from real field reports (GitHub issues, Discord, Reddit), tracked by exact board ID — currently 60+ dedicated board profiles, plus a conservative family-fallback path for boards that aren't individually verified yet.
* **Honest about what it doesn't know yet.** If your exact board hasn't been field-confirmed, the app tells you that instead of pretending everything works.

It's not affiliated with or endorsed by HP — it's a from-scratch, independent project.

## What's changed since v4.0.0 (the last "major" milestone)

A quick sense of how far it's come, for anyone who tried it a while ago or is looking at it fresh:

* A real **community model-database pipeline** — new board support no longer has to funnel through one person; anyone can submit a validated PR for their model
* **Per-LED keyboard lighting extended to every supported board** (was limited to specific models before), plus per-key RGB support and a fix for reactive lighting effects that were silently non-functional everywhere
* An **AMD SMU transport fix** and broader AMD Curve Optimizer/undervolt support
* A **"Model Capabilities" diagnostics panel** — shows exactly what your specific board does and doesn't support, and whether that's field-confirmed or inferred, before you go hunting for a setting that isn't there
* **RAM Smart Clean** — one-click safe memory clean from Quick Access, plus opt-in auto-clean per game profile
* A real **safety pass on EC power-limit writes** — writes to unconfirmed register addresses are now blocked by default until a model is field-verified, closing a gap where an unconfirmed write could have been attempted on hardware it was never tested against
* Screen-reader accessibility labeling added across the whole app
* Multiple rounds of UI-responsiveness fixes for a standing "feels laggy" complaint
* The Linux side (CLI + GUI) has had real, board-specific fan-control and keyboard-RGB bugs found and fixed from actual community hardware reports, not just Windows

## What's new in v4.1.7 specifically (released today)

This is the largest patch to date — it grew across three waves of field reports and 16 reviewed community PRs before it could tag.

* **Safety:** unconfirmed EC power-limit register writes (CPU/GPU power limits) were being attempted by default on boards that didn't explicitly opt out — now blocked everywhere until a model's addresses are field-confirmed. No board has ever had them confirmed, so this closes a real gap with zero behavior loss for anyone.
* **Fan fix:** switching Performance Mode away from an active Max Fan hold could leave fans stuck at maximum speed until the app was closed — the actual hardware latch release was missing. Fixed.
* **Linux fan control fix:** the Linux GUI never actually controlled fans on boards that only expose a coarse on/off fan toggle rather than variable speed — every fan command silently did nothing. This was reported alongside a real overheat/automatic-shutdown incident. Fixed.
* **Linux RGB:** three more keyboard-lighting hardware interfaces now supported, plus a brightness-control fix for another class of board.
* **Big community contribution batch (16 PRs from one contributor, `tempestnano`):** new board support, per-LED lighting rolled out further, an experimental (opt-in, fully disclosed) fix for laptops that under-clamp GPU/CPU power due to a lower-wattage charger, and a real crash-inducing bug fix in the background monitoring process.
* Misidentification fixes for two specific CPU/board combos, a locale-based number-formatting bug, and a full honesty pass on GPU/CPU overclock and undervolt status reporting (the app no longer reports something as "applied" when it silently wasn't).

Full technical changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v4.1.7.md

## Get it

* **Website:** https://omencore.info
* **GitHub / source:** https://github.com/theantipopau/omencore
* **Latest release:** https://github.com/theantipopau/omencore/releases/tag/v4.1.7
* **Discord:** https://discord.gg/9WhJdabGk8
* **Report a bug / request board support:** https://github.com/theantipopau/omencore/issues

If your model isn't listed as verified yet, it still generally works via a conservative fallback profile, and reporting your hardware (a diagnostics export from the app, or just a description of what worked/didn't) is exactly how new boards get added.

^(Independent project. Not affiliated with or endorsed by HP.)
```

---

**Status:** safe to post once the `v4.1.7` GitHub Release is published (same artifact/link dependency as the r/OmenCore post). The "60+ dedicated board profiles" figure was counted directly from `ModelCapabilityDatabase.cs` at time of writing — re-verify before reposting a future version of this template, since it will keep growing.
