# OmenCore v4.5.1

**Release Date:** unreleased
**Type:** Field-report fix release. Follow-up to 4.5.0.

## Added

- **Board entries:** `8BC8` (Victus 15-fa1xxx, #241: two fans, level 55, backlight only; the family entry said one fan) and
  `8786` (OMEN 15-en0002AX, #239: stops inheriting the 8787 sibling's curves, MUX and GPU boost; the firmware refuses
  direct fan-level writes).
- **54 more boards named** (no field report yet): the OMEN 15/17 2018-2021 boards, `8A42`, `8A4C`, `8A4D`, `8BAA`, `8C78`, `8D3F`,
  `8F1C` and the Victus `8A3D`, `8A3F`, `8B2F`, `8D07`, `8DCF`, `8DE1`, `8E5C`. They come from the board tables of Ohman and the Linux
  hp-wmi driver (whose OMEN list is HP's own OMEN Command Center capability list). They get a proper name in the log and export
  and exactly the conservative family-default behaviour they had before; they never replace an exact entry or a model-name match.
  Send an export to have a board promoted to a full entry.
- **Firmware fan table in the diagnostics export** (read-only, `Default 0x2F`, documented by the Linux hp-wmi driver): the firmware's
  own fan speeds per row, so the real top speed of a board is on record instead of being estimated from a reporter's test.
- **Field-confirmed:** `88F8` (#207), `8E35` (#237) and `8BC8` (#241): Guided Fan Verification 6/6 on 4.5.0 and every step audible.

## Fixed

- **Performance / Cool / Default fan-mode writes on older (thermal policy V0) firmware** (#235, board `8F5C`).
  OmenCore sent the newer mode bytes (0x30 / 0x31 / 0x50) to every board when switching performance mode, and again
  from the Max-fan release and keepalive paths. V0 firmware only understands the legacy bytes (0x00 / 0x01 / 0x02), so
  the write was accepted and then ignored: the log said "Performance applied" and the fans did nothing. The byte is
  now chosen once, in `HpWmiBios.SetFanMode`, from the firmware's reported policy, so every caller gets it. Fan
  *presets* already did this; performance-mode switching did not. **Implemented, pending confirmation on 8F5C.**
  Boards on V1 or newer firmware are unchanged.

- **Keyboard backlight died on 8E10 after updating to 4.5.0** (#130). The 4.5.0 one-zone colour layout (colour at offsets
  1 to 3 of the table) was applied to every board whose firmware answers "one zone". It is only proven on 8BD4; on 8E10 it
  left the backlight dead, even in the BIOS screen, until the battery was disconnected. The layout is now opt-in per board
  and on only for 8BD4. Every other board gets the payload 4.4.5 sent. **Implemented, pending confirmation on 8E10.**
  If your backlight stays dark after a reboot, disconnect the battery (or hold power for 30 seconds on a sealed machine) to reset it.
- **Quick Colors turned the 8BD4 keyboard off** (#212). Quick Colors used the older per-zone writer, which declares four
  zones, so on a one-zone board it undid the Apply to Keyboard fix. That writer now sends the one-zone table on 8BD4.
  **Implemented, pending confirmation on 8BD4.**
- **The RGB payload probe left the keyboard dark** (#244). Several probe payloads write black or a wrong zone count and the
  probe never undid them, so the backlight stayed off until the keyboard shortcut. It now saves your colour table first and
  restores it, and switches the backlight on, when it finishes. **Implemented, pending confirmation.**
- **8BCD keyboard colours never reached the firmware** (#244). The usual four-zone write (colours from byte 25) reads back black on this board, and in the owner's probe only the no-pad four-zone steps moved every zone, so 8BCD now sends its colours directly after the count byte. Only that board uses it. **Implemented, pending confirmation on 8BCD.**
- **Process timeouts could not fire** (#245): the Linux command probe, the `sc.exe` helper and the nvidia-smi power query read
  the child's output before waiting, so a hung child blocked past the timeout. They now read asynchronously.

- **Fan shutdown order and monitor start** (from a code read by MahammadADA, #245): on exit the fans were handed back to the BIOS
  before the curve and monitor loop were stopped, so a tick in between could write a manual speed again; the loop now stops first.
  The monitor task also captured its cancellation token late and could throw unseen if stopped first; it is captured up front.
- **Update hash mismatch was swallowed** (#245): a failed SHA256 check threw a `SecurityException` that the same method then
  caught and turned into a plain "download failed"; it now reaches the caller, so the user is told the package was rejected.
- **Unreadable config.json was overwritten** (#245): when the file failed to parse, defaults were loaded and the next save replaced
  it. A copy is kept as `config.json.corrupt-<timestamp>` first.

## Investigated, Not Fixed

- **#238 (`8EEC`, Linux):** the kernel `hp_wmi` driver fails thermal-profile setup (-22) because the firmware's `WMBA` method
  overruns its buffer. A firmware/kernel fault; recorded in the model notes, no OmenCore change possible. The reporter's
  custom out-of-tree driver may register its fan files where OmenCore does not look; waiting on their sysfs paths.
- **#243 (`8A4D`, Battery Care):** the firmware refuses the old `0x24` write but implements the standard `0x2B` charge mode.
  The reporter is preparing a pull request; not in 4.5.1.
- **#218 (`878A`):** the 4.5.0 export shows the firmware refusing direct fan-level writes (return code 46) while Max
  and fan-mode writes succeed, so Guided Fan Verification cannot pass here. Recorded in the model notes. The
  keyboard is a single fixed red backlight per the reporter. Flags unchanged.
- **#236 (`8DD0`, NVIDIA Power Unlock):** Apply CURRENT is "Not ready" because only one NVIDIA driver (617.14) is a reviewed
  exact match; the reporter's 621.01 is not, and no validation step is offered in the app yet. NVAPI also reports no
  writable power-policy entries on this GPU, so a trusted driver may still not be enough. No code change.

## Needs field confirmation

| Board | What to check |
|---|---|
| `8F5C` (#235) | Performance mode under load now ramps the fans / raises CPU power |
| `8E10` (#130) | Keyboard backlight stays working after installing 4.5.1 |
| `8BD4` (#212) | Quick Colors keep the keyboard lit |
| Any V0 board | Switching Performance / Cool / Default changes fan behaviour |
