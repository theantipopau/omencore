# Draft Replies — Post-4.0.0 Field Reports (#151-#155)

Drafts only, for review before posting. Written from the triage in `docs/ROADMAP_v4.0.0.md` ("Newly Reported (Post-4.0.0 Release)") and `docs/CHANGELOG_v4.1.0.md`. All fixes referenced here are committed on `main` (`0c41b7f`) and will ship in 4.1.0.

---

## #152 — "problems with temp"

Thanks for the detailed screenshots — that made this easy to root-cause.

You were right that something was wrong, and it was worse than a display quirk. The sidebar's "Live Temp" indicator and the General tab's main cards were reading from two independently-wired paths for the same telemetry stream. The General tab receives a fully **normalized** sample (range-clamped, stale-state-filtered, spike-rejected). The sidebar was subscribed directly to the raw hardware event and bypassed all of that — so it was showing you unfiltered sensor output, spikes included, while the main cards showed the cleaned-up number. That's exactly the gap in your screenshots (sidebar 65°C/54°C vs. cards 53°C/45°C).

Fixed in the 4.1.0 branch: the sidebar no longer listens to the raw event at all. It now receives the same normalized sample the General tab does, so both surfaces agree by construction instead of by luck. Added regression tests pinning this.

Separately — your log also showed the "temperature appears frozen" warning firing a lot. That turned out to be a false-positive bug, not a sensor problem; see the note on #153 below, since the same fix applies to your log too. It's very likely part of what made temps *feel* unreliable even beyond the sidebar/card mismatch.

Both fixes will be in 4.1.0. Appreciate you filing this with screenshots — it made the mismatch obvious.

---

## #153 — "rpms are also stuck at max after overheat alert"

Traced this fully from your `OmenCore_20260720_192827.log` — thanks for attaching diagnostics, it made this solvable without needing to reproduce it locally.

Two separate things were going on:

**1. The "external fan reset suspected" loop was a false positive, and it was mathematically unwinnable on your board.** The healthy-floor check for Max mode was `MaxFanLevel * 0.90`. Your board's nominal `MaxFanLevel` is 55, so the floor was 50 — but your hardware holds a genuinely steady 46-48 while Max mode is actually working (your log shows `levels=46/48 floor=50` every single cycle, never trending down). The check could never pass, so OmenCore kept concluding "sustained drop" and re-forcing max, forever, against firmware that was already at max. That's the ~2.5 minutes of repeated warnings you saw.

Fixed: the strict check stays the default, but once a board demonstrates over several reads that it never reaches the nominal floor, the check falls back to that board's own observed peak (with a tight tolerance), plus an absolute backstop so a genuine collapse toward idle is still caught. This can only make OmenCore write to the EC *less* often than before, never more.

**2. The "temperature appears frozen" warnings (48 of them in your session) were also a false-positive heuristic**, not evidence of a bad sensor. HP's WMI BIOS reports temp in whole degrees C, so a GPU sitting at steady load and steady temp (your log shows `GPU temperature appears frozen at 48,0°C for 21 readings (load=100%)` — full load, stable temp, that's just equilibrium) legitimately repeats the same reading many times. That's not a stuck sensor, it's good cooling. Fixed: the warning now only fires when load swings ≥15 points without the temperature moving, so equilibrium stays silent but a genuine stall still gets caught.

Both fixes are on `main` and will be in 4.1.0. I can't rule out from telemetry alone whether there's *also* a real external actor resetting your fans underneath the noise (your board has no RPM readback, so we're going on commanded-level only) — but the false-positive loop is now gone either way. If you get a chance to test 4.1.0 when it's out, I'd genuinely like to know whether the repeated re-assertion behavior you were seeing/hearing stops. Also interesting that OmenMon-Reborn didn't show this on the same hardware — makes sense in hindsight, since this was our detection logic being wrong, not your EC doing anything unusual.

---

## #154 — HP ENVY Laptop 14-eb0xxx (Linux/Fedora)

Really appreciate the effort that went into this diagnostics bundle — the `hp-wmi`/`ec_sys`/`hwmon` probing and ACPI kernel-log correlation is some of the most thorough we've seen from a report.

That said, this one's outside OmenCore's current scope rather than a bug: the ENVY line runs different, non-gaming HP firmware, and `hp-wmi` on your board doesn't expose a `thermal_profile` or fan-target interface for us to hook into — which your own diagnostics confirm. OmenCore currently targets OMEN and Victus hardware specifically, where that WMI surface exists.

I don't want to promise ENVY support since it'd need real firmware-level investigation to know what's even controllable, but I'm leaving this open as a scope question rather than closing it outright. If there's enough interest (or you're up for helping probe what your firmware *does* expose), it's worth a real look down the line. Thanks again for the diagnostics quality either way.

---

## #155 — Victus 15-fb2082wm diagnostics

Thanks for sending the diagnostics — no description needed, the log told the story on its own.

Your board resolves via an exact ProductId match to our `8C2F` database entry, but that entry was named/documented as a 16" Victus board (added from an earlier 16" report). Turns out HP reused the same board ID `8C2F` across both a 15" and 16" Victus chassis, which our entry didn't reflect at all — a naming/provenance gap, not a functional bug. Your session log is clean otherwise (0 errors); the only warnings are an already-suppressed battery-telemetry WMI call and the same freeze-heuristic false positive fixed for #152/#153 above.

Fixed: the entry is renamed to reflect both chassis sizes, and its notes now explicitly record that the capability flags were inferred from the 16" report and are **not yet confirmed** on the 15" chassis you have. No capability flags were changed — this was a labeling fix only, so nothing about your board's actual behavior should change.

One ask if you're willing: could you confirm whether fan/RGB/thermal behavior on your 15-fb2xxx actually matches what we'd expect from the 16" assumptions? That'd let us actually verify (rather than just guess) that the shared entry is safe to keep using for both chassis sizes.

---

## #151 — Board 8D41 keyboard RGB (Darfon `0d62:54bf`)

Thanks for the follow-up, especially the VM DMI-spoofing test confirming HP's own OMEN Light Studio doesn't support this exact Darfon controller either — that's genuinely useful corroboration.

Good news on part of this: your raw-sysfs proof that the light bar (zones 0-3) is writable led us to find a real bug — `omencore-cli` was only ever writing to `hp-wmi/zoneN_color`, but your board's driver actually exposes those zones through a separate `hp-rgb-lighting` device with plain `zoneN` filenames (no `_color` suffix). That's fixed in the 4.1.0 branch now — `omencore-cli` tries both paths, so the light bar should work through it once that build is out. I don't have Linux hardware to test this on directly, so I'd appreciate you confirming it actually lights up on your end when 4.1.0 lands.

The keyboard zones (4-7) are still the separate, harder problem: that's the Darfon USB HID controller our current architecture doesn't drive at all, and that part still needs the HID feature-report capture you offered (`usbhid-dump`/Wireshark+usbmon while toggling effects in Light Studio) before we can reverse-engineer the write format for a proper backend. Whenever you have a window to do that capture, that's the thing that actually unblocks it — happy to walk through exactly what to grab if useful.
