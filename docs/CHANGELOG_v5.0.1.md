# OmenCore v5.0.1

**Release Date:** not yet released
**Type:** Fix release for 5.0.0.

## Added

- **Board entry `8E5F`** (Victus 15-fb3185AX, Ryzen 7 260 + RTX 5050, #249), from the reporter's export: two fans,
  backlight-only keyboard, modes through the WMI thermal-policy fallback. Previously matched by name. Curves stay off
  until a Guided Fan Verification run.
- **Recorded Guided Fan Verification passes** (6/6 on 4.4.5, fan level estimates, not tachometer readings):
  `8A13` (#225) and `88F7` (#215).

## Fixed

- **Upgrading from 4.x to 5.0.0 hung, with the old version on screen.** The 5.0.0 installer hands the fans back to
  the BIOS before it copies the new files, by running `OmenCore.exe --restore-fans` from the install folder. On an
  upgrade that folder still holds the 4.x exe, which does not know the flag: it opened its normal window, and the
  installer waited for it to close. That affected the in-app update and a manual install over 4.x alike. The installer
  now checks the installed exe's version first and skips the hand-back for anything older than 5.0.0. A guard test
  pins the order. Reported on Discord on the day of release; tracked in #257.
  - If you are stuck on it with 5.0.0: close the 4.x window completely (tray icon → Exit) and the installer carries on.
    Uninstalling and then installing 5.0.0 also works; settings live in `%APPDATA%\OmenCore` and are kept.
  - The 5.0.0 uninstaller is not affected: it runs its own 5.0.0 exe.
  - The 5.0.0 release notes carry a known-issue note with the same workaround (added 2026-11-14).

- **Linux: fan RPM was fake on boards outside the known EC map** (#248, OMEN Slim 16 `8D40`). Three read-path bugs,
  no change to any fan write:
  - The CLI only picked up hp-wmi's `fan*_input` when the same hwmon device also offered `pwm1_enable`, and fell back
    to the EC whenever both fans read 0. On 8D40 that reached EC register `0x34`, which reads `0xFF` there, and
    `0xFF × 100` printed as 25,500 RPM. The RPM inputs are now found on their own (hp-wmi's hwmon, then any hwmon
    device named `hp`), a real 0 RPM is kept, and an EC `0xFF` reads as no value.
  - The GUI ignored which fan it was asked for and returned the first positive `fan*_input` of any hwmon device for
    both, counted `pwm*` duty files as RPM, and read sysfs through a cached reader, so the number froze at whatever
    the fan did at launch. It now reads `fan1_input` for the CPU fan and `fan2_input` for the GPU fan, unbuffered.
  - *Implemented, pending confirmation on 8D40.* New unit tests cover the `0xFF` case.

- **Linux: "Max" could stop the fans, and the GUI asked for RPM in percent** (#238, `8EEC` with the current
  `hp-wmi` hwmon driver: `fan*_target`, `fan*_max`, `pwm1`, `pwm1_enable`).
  - `omencore-cli fan --speed 100` and the Max profile wrote `pwm1_enable=0`, which the hwmon ABI defines as full
    speed and which is the confirmed Max path on toggle-only boards (#174) and `8D87` (#183). On this driver it
    stopped both fans: 3600 RPM at 60 % manual duty, then 0 RPM, audibly. Max still writes `pwm1_enable=0` first, then
    checks the RPM 3 s later; if the fans stopped or slowed, it switches to manual duty 100 %, the path the reporter
    confirmed works. Boards where `pwm1_enable=0` already works are unchanged.
  - The GUI wrote the requested percentage straight into `fan*_target`, which takes RPM, so 60 % asked for 60 RPM.
    It now scales by `fan*_max`, and skips the target file when there is no readable maximum.
  - *Implemented, pending confirmation on 8EEC.* Unit tests cover the RPM check.

## Investigated, not fixed yet

- **GPU Power Boost on the OMEN MAX 16 `8D41` may be sending the wrong last byte** (#123, #181; Windows data from
  philipphoh). HP's app and the Linux kernel both send `0x22` as `01 01 01 57` for Performance, and on Windows, after
  a boot without OMEN Gaming Hub, those two calls (`0x1A FF 31`, then `0x22 01 01 01 57`) take the GPU from 80 W to
  175 W. The decompiled ACPI tables show the firmware stores byte 3 as the GPU slowdown temperature. OmenCore sends
  `0` there, which may be setting a 0 °C slowdown point.
  - Our own code comment (`HpWmiBios.HpGpsTemperatureThresholdC`) says byte 3 cannot be read back. That is wrong:
    the `0x21` readback returns it as a fourth byte (87 on the reporter's machine). `GetGpuPowerDetailed` currently
    discards it.
  - Proposed change: read the current byte 3 through `0x21` and send it back unchanged, the way the kernel's
    read-modify-write does, instead of a constant. Not made yet: it changes a hardware write, so it waits for a
    before/after on a real `8D41` (watts under the same load, plus the `0x21` readback). The reporter has offered to
    test.
- **Spanish translation requested** (#256). Belongs with the remaining translation work in 5.1; the shell and
  Settings window/tray strings are already externalised, so a Spanish resource file can start there.

## Dependency updates

- Dependabot's 2026-11-13 batch: the GitHub Actions group (#250) and the test-only packages (`coverlet.collector`
  #252, `Microsoft.NET.Test.Sdk` #254) pass CI. `Microsoft.Extensions.DependencyInjection` 11 (#253) also passes, but
  it is a major update to a runtime dependency, so it is held for 5.1. The NuGet group (#251) and
  `System.CommandLine` 3.0 (#255) fail on the CLI's beta API; see below.

## Housekeeping

- Dependabot no longer offers `System.CommandLine` updates. The CLI uses the 2.0.0-beta4 API, which the stable
  releases removed, so those PRs could only fail; the pin comes off when the CLI is ported.
