# Contributors

OmenCore is maintained by **Matthew Hurley ([@theantipopau](https://github.com/theantipopau))**.

It is built on other people's laptops, logs and patience. This file credits the people whose code,
research or hardware testing is in the project. Git history records authorship for merged commits;
this file also covers work that arrived as a fork, a PR that was ported rather than merged, or
field evidence that became a board entry. If you contributed and are missing, open an issue or a PR.

## Code and fork contributions

| Contributor | What they brought |
|---|---|
| **Jeremy** ([@tempestnano](https://github.com/tempestnano)) | The OMEN MAX 16 (`8D87`) per-key lighting work (keyboard MCU path, Fn-cycle plans, Windows Dynamic Lighting handling, per-key brightness and colour map), AMD power-limit / PM-table readback and Curve Optimizer corrections, hardware-worker lookup and process-monitoring fixes. The largest external contribution by commit count. |
| **Eric Parsley** ([@WoofahRayetCode](https://github.com/WoofahRayetCode)) | PR #216 (guided RGB check in Guided Fan Verification, and the controller handoff that stops fans staying latched after verification), and the `8E35` field testing: controlled Balanced-vs-Performance power runs, SMU/PM-table readback, fan and RGB verification. |
| **ujjawalkaushik1110** ([@ujjawalkaushik1110](https://github.com/ujjawalkaushik1110)) | The `8DD0` (Victus 15-fb3xxx) entry and diagnostics (PRs #200, #210): two fans, 55-level ceiling, backlight-only keyboard; the watchdog-failsafe review. |
| **Neutron-0** ([@Neutron-0](https://github.com/Neutron-0)) | PR #147: the log-buffer change that cut UI-thread work. |
| **saikiranworks** ([@saikiranworks](https://github.com/saikiranworks)) | The OMEN Slim 16 (`8D40`) Linux fork: the DKMS `fourzone_color` / `fourzone_brightness` interface and the brightness-gate finding that the Linux keyboard support is built on; the keyboard suspend hook and the reliability ideas (single fan writer) taken for 4.4.5. |
| **bobshmo** ([@bobshmo](https://github.com/bobshmo)) | The OmenCore-Prophecy fork: found and fixed saved Fan Max being restored at startup and a stale Max preset coming back after sleep/resume (`RestoreMaxFanOnStartup`, resume preset update); the Prophecy NVIDIA power backend (MAX/CURRENT, VBIOS and driver resolvers, safety checks) included in 4.4.5 with permission, updated to Prophecy Power Unlocker 1.2.9 in 5.0.2. |
| **timmyy123** ([@timmyy123](https://github.com/timmyy123)) | `nvidia-power-control`: the original research into the NVIDIA driver's `romOverride` MAX policy and the internal call used for CURRENT, which the 4.4.5 NVIDIA power unlock is built on. Used with permission. |
| **mbilykov** ([@mbilykov](https://github.com/mbilykov)) | #189 and `omen-fanctl`: the Gaming Hub automatic fan-curve research (factory tables, the `0x2F` fan mapping, the IR sensor) behind the 4.4.5 curve engine, and Linux per-key RGB findings (#179). |
| **Rave-TZ**, **Dsedan15**, **JeanHX5**, **MENTRAUZ** | Requests and test runs behind the OMEN key actions, the HP telemetry switch, boards `8A13` and `8E9F`, and the 8BB3 request. |
| **mecaniko-tallerOS** ([@mecaniko-tallerOS](https://github.com/mecaniko-tallerOS)) | #246: per-fan firmware tachometers on the Victus `8C2F` (BIOS F.08), merged for 5.0.1 with their hardware verification (BIOS kept in Auto at every checkpoint); the BIOS Auto handoff and independent calibration patches under review for 5.1. |
| **philipphoh** ([@philipphoh](https://github.com/philipphoh)) | #123: the OMEN Max 16 (`8D41`) GPU power analysis: OGH's WMI sequence per profile, the decompiled `NVPCF` ACPI logic, the Windows proof that the kernel's bytes lift the GPU to 175 W, and the Linux finding that `nvidia-powerd` carries the boost budget. |
| **murilopontes** ([@murilopontes](https://github.com/murilopontes)) | OMEN 15-dc0xxx (`84DB`) EC fan-boost test results for the Linux guide. |
| **pkq66882** ([@pkq66882](https://github.com/pkq66882)) | The Simplified Chinese (`zh`) UI translation. |

## Research this project learns from

Protocol facts and measurements published by these projects have informed fixes, with the facts
re-implemented independently (several of them are GPL-licensed and OmenCore is MIT, so no code is
copied):

- **Ohman** ([P4R1H/ohman](https://github.com/P4R1H/ohman)): documented HP WMI command semantics and the Primax per-key keyboard protocol.
- **OmenMon** ([OmenMon](https://github.com/OmenMon/OmenMon)): HP BIOS command documentation.
- **openomen**, **omen-fan** and others referenced in code comments where a path or format was taken.

## Reporters and testers

Many board entries exist only because someone attached a diagnostics export and then reran a test when
asked. Particular thanks to the reporters named in the changelogs and issues, including Harshal-0278,
alvintb07, Enoquio, RazrV33, VoRtam4, jblanc55, der-Yak, vkeerthivikram, Eremos, Nova-8056,
GrumpyFries, Trirez, RobRobM, AtlasC0R3 and the Discord testers who never filed an issue.

## How to contribute

See [Contributing in the README](README.md#contributing). The most useful contribution is a fresh
diagnostics export from hardware that isn't covered yet, plus a Guided Fan Verification run.
