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

## Investigated, Not Fixed

- **#218 (`878A`):** the 4.5.0 export shows the firmware refusing direct fan-level writes (return code 46) while Max
  and fan-mode writes succeed, so Guided Fan Verification cannot pass here. Recorded in the model notes. The
  keyboard is a single fixed red backlight per the reporter. Flags unchanged.
- **#236 (`8DD0`, NVIDIA Power Unlock):** Apply CURRENT is "Not ready" when the installed NVIDIA driver is not in the
  trusted-driver list; this is a driver match, not a laptop limit. Waiting on the reporter's export.

## Needs field confirmation

| Board | What to check |
|---|---|
| `8F5C` (#235) | Performance mode under load now ramps the fans / raises CPU power |
| Any V0 board | Switching Performance / Cool / Default changes fan behaviour |
