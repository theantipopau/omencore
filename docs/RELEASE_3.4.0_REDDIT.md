# OmenCore v3.4.0 — Bug-Fix Release Now Available

**OMEN Core v3.4.0 is officially released**, bringing ~80 bug fixes to address longstanding issues reported by our community over the past month.

## Major Fixes

**Custom Fan Curves No Longer Cap CPU at ~25 W**

We discovered and fixed a silent regression: when users selected a "Custom" fan profile, the system would incorrectly map it to a thermal policy that capped CPU power. Users with quieter custom curves would experience significant CPU performance drops without realizing why. This is now fixed — custom curves apply at your intended fan speeds without any hidden TDP restrictions.

**Fan Profile Selector Is Visible Again**

The fan profile card selector (Max, Extreme, Gaming, Auto, Silent, Custom) was accidentally hidden behind the curve editor starting in v3.3.x. Both UI elements were rendering at the same grid position. We've reorganized the layout so you can see and access all fan profiles.

**Print Screen and Brightness Keys Work Correctly**

Fixed two keyboard-hook issues:
- **Print Screen** — OmenCore's low-level keyboard hook no longer interferes with Windows 11's Snipping Tool
- **Fn+F2/F3 brightness keys** — Prevented accidental OmenCore toggle on affected models. These keys now work for brightness control as intended.

**OMEN Max 16-ak0xxx (Model 8D87) Support Added**

This model is now properly recognized in our capability and keyboard profile databases, eliminating fallback behavior.

## Additional Improvements

- **Fan safety disclaimer added** — Clarifies that the BIOS firmware controls physical fan behavior
- **RPM sanity-check warning** — Alerts users if duty is active but RPM stays at zero for extended periods
- **Linux GUI startup hardening** — Improved X11 display recovery and sudo-session handling
- **TDP detection improvements** — Better power limit detection on 2025 OMEN 16-am1xxx models
- **Auto-update reliability** — Fixed concurrent download handling and improved temp file cleanup
- **Code quality hardening** — Bare exception handling improved, process-crashing async-void patterns fixed

## Download

**Windows:**
- Installer: `OmenCoreSetup-3.4.0.exe`
- Portable ZIP: `OmenCore-3.4.0-win-x64.zip`

**Linux:**
- CLI + GUI: `OmenCore-3.4.0-linux-x64.zip`

All available on [GitHub releases](https://github.com/theantipopau/omencore/releases)

## SHA256 Verification

```
91F7032D6ECA31515261A8E8412039ACBDA25E672B0F2641DC34CD7AB03039EA  OmenCoreSetup-3.4.0.exe
55A26693471E0E16312EFDFDD4E6D89CD0475DB4168AF3F9C95B2F2CED8FB7B6  OmenCore-3.4.0-win-x64.zip
943928F2273FA6A4959AFC08AF57D3DE26F55222C8C793622D960DB01413BECC  OmenCore-3.4.0-linux-x64.zip
```

## Upgrade Information

- **No breaking changes** — Drop-in replacement for v3.3.1
- **No configuration migration** — Your existing settings will work as-is
- **Users on custom fan curves** — You should see improved CPU performance after updating

For detailed release notes, see the [full changelog](https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.4.0.md).
