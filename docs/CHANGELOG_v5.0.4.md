# OmenCore v5.0.4

**Release Date:** not yet released
**Type:** Fix release: keyboard lighting repainted red by performance modes, Linux fan speed capped on the 8D40, Custom
tab always available, clearer OMEN Gaming Hub guidance.

## Fixed

- **Switching performance mode repainted the keyboard "Omen Red"** (Discord). "Remember current settings" on a mode
  always recorded the first built-in lighting profile ("Omen Red"), whatever the keyboard was showing, including with
  lighting turned off. Every switch to that mode then painted the keyboard red, and it did so even with keyboard
  lighting turned off in Settings. Modes no longer record lighting, a mode's lighting is skipped while keyboard
  lighting is turned off, and the lighting name already saved in existing modes is cleared when 5.0.4 first loads
  the config (a copy of the old config is kept as `config.json.schema1.bak`).
- **OMEN Gaming Hub guidance disagreed with itself** (Discord). The standalone check said OmenCore "can coexist" with
  a running Gaming Hub, the Settings warning said it conflicts and should be removed, and Bloatware Manager said to
  keep it. All three now say the same thing: OmenCore does not need Gaming Hub, a running Gaming Hub can override
  OmenCore's fan and lighting settings, and it is best removed once OmenCore's fans, lighting and OMEN key are checked
  on that laptop.
- **Linux GUI: fan curves topped out at 60 % on the OMEN Slim 16 `8D40`** (#248). The GUI drove the fans through
  `fan1_target`, but this driver runs the fans from the manual duty (`pwm1`), which stayed at 153 of 255 from an
  earlier write, so Aggressive plateaued at about 3600 RPM of a 6000 RPM maximum. The GUI now writes the manual duty
  first, as the CLI already does. *Implemented, pending confirmation on 8D40.*
- **The Custom tab disappeared when switching away from the Custom profile** (Discord). Its settings, such as GPU
  boost, apply in every mode, so it is now shown with the other advanced tabs.
- **System Optimizer: reverting a network tweak could leave the registry value in place.** When the registry key
  existed but the value did not, no backup was recorded, so revert did not remove the value. Value names are also
  matched without regard to case now, as Windows does.
