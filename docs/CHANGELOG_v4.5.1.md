# OmenCore v4.5.1

**Release Date:** unreleased
**Type:** Field-report fix release. Follow-up to 4.5.0.

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

## Investigated, Not Fixed

- **#218 (`878A`):** the 4.5.0 export shows the firmware refusing direct fan-level writes (return code 46) while Max
  and fan-mode writes succeed, so Guided Fan Verification cannot pass here. Recorded in the model notes. The
  keyboard is a single fixed red backlight per the reporter. Flags unchanged.
- **#236 (`8DD0`, NVIDIA Power Unlock):** Apply CURRENT is "Not ready" because only one NVIDIA driver (617.14) is a reviewed
  exact match; the reporter's 621.01 is not, and no validation step is offered in the app yet. NVAPI also reports no
  writable power-policy entries on this GPU, so a trusted driver may still not be enough. No code change.
- **#237 (`8E35`):** Guided Fan Verification 5/6; the GPU fan stayed at level 16 when asked for 33 at 60% (CPU fan honoured
  33). Not reproduced elsewhere on this board (#195 passed 30/60%); asked for a re-run.

## Needs field confirmation

| Board | What to check |
|---|---|
| `8F5C` (#235) | Performance mode under load now ramps the fans / raises CPU power |
| `8E10` (#130) | Keyboard backlight stays working after installing 4.5.1 |
| `8BD4` (#212) | Quick Colors keep the keyboard lit |
| Any V0 board | Switching Performance / Cool / Default changes fan behaviour |
