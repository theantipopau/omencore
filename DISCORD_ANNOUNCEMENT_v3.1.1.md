🎉 **OmenCore v3.1.1 — Hotfix Release** 🎉

This hotfix resolves several stability and fan-control issues introduced in earlier builds. It improves temperature reporting, prevents dangerous RPM drops during mode switches, tightens Linux RPM calibration, and removes intermittent installer corruption.

Highlights:
- Accurate CPU temperature fallback to reliable sensors
- Fans now follow the highest thermal demand (CPU/GPU) and avoid unsafe RPM drops during mode transitions
- Linux: corrected PWM→RPM calibration and proper 0% (fan-stop) behavior where supported
- Installer pipeline hardened with PE validation to prevent corrupted builds

Downloads (tag `v3.1.1`): https://github.com/theantipopau/omencore/releases/tag/v3.1.1 — checksums are included on the release page and in the changelog.

If you test this build, please report issues at: https://github.com/theantipopau/omencore/issues (use label: `v3.1.1-hotfix`).

Thanks for testing and helping improve OmenCore! 🚀
