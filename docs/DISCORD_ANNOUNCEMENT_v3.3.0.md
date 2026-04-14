🎉 **OmenCore v3.3.0 is now live** 🎉

Largest stability-and-polish release since v3.1.0.

**Critical fixes**
- Monitoring halt → watchdog failsafe fans (fans spiking to 100% mid-session)
- Fan curve stops working after first save (OGH coexistence / RPM false-fail)
- Fan verification silently destroying fan state on failure
- Restore Defaults deadlock / app freeze requiring End Task
- Bloatware Manager Remove/Restore buttons never enabled after scan

**Fan / OSD / UI**
- OSD: gradient, shadow, corner radius; horizontal layout; density modes; metric group toggles
- Quick Access: Quiet→Auto→Curve→Max order; curve preset tooltip; decoupled-fan badges
- Disabled buttons now show tooltips across the whole app
- OGH unsupported-command WARNs (Fan:GetData etc.) permanently silenced after 5 occurrences

**Features**
- GPU OC: range labels, adaptive presets, 30s Test Apply auto-revert
- Resume recovery timeline + status card in Settings
- Model identity confidence card in Settings
- Game Library manual add; optional Lite Mode
- Startup hardware restore guardrails (off by default on OMEN 16/Victus)
- MSI Afterburner coexistence: fixed frozen CPU/GPU load

**Downloads:** https://github.com/theantipopau/omencore/releases/tag/v3.3.0

**SHA256:**
```
483D4CAEB66DF3923F6152ECE98B128F3F9A0B3A2E0A5CE42403C43BB9F12D9E  OmenCoreSetup-3.3.0.exe
F07B9435DCCB2771672BDE0E44CC2A2B980859AC0A576B8E6BCCC93A61D064C9  OmenCore-3.3.0-win-x64.zip
AD5A2C37583EB9D8E486431B8AD9F7BEFBA2847325F5F21C4ABFB0B63C04AA1C  OmenCore-3.3.0-linux-x64.zip
```

Regressions → https://github.com/theantipopau/omencore/issues (model + BIOS + logs). Thanks all! 🚀
