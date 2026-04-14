# [Release] OmenCore v3.2.5 - Stability, Model Support, and UX Improvements

Release link:
https://github.com/theantipopau/omencore/releases/tag/v3.2.5

OmenCore v3.2.5 is now live. This release focuses on reliability, better model handling, and clearer control behavior across fan, updater, and diagnostics workflows.

## Highlights

- Hardware worker reliability hardening
  - Fixed repeated worker reattach/reconnect spam behavior in restart and attach scenarios.
  - Improved parent registration behavior and recovery logic.

- Fan control decoupled from performance mode
  - Switching performance profiles no longer silently overrides manually selected fan mode or curve.
  - Optional LinkFanToPerformanceMode setting remains available for users who want legacy coupling.

- Quick Access improvements
  - Added Custom fan mode to apply active OMEN-tab fan curves directly from tray UI.
  - Reordered performance mode buttons to Quiet -> Balanced -> Perform.

- Model/capability fixes for shared Product ID 8BB1
  - Added model-name disambiguation so Victus 15-fa1xxx is no longer treated as OMEN 17.
  - Correct keyboard/capability profiles now apply per model family.

- Updater hardening
  - Stronger installer asset selection for installed builds.
  - SHA256 requirement enforcement when applying updates.
  - Better download diagnostics for content-type and redirect outcomes.

- Fan diagnostics and watchdog improvements
  - Better RPM calibration assumptions for modern high-RPM models.
  - Added backend/source visibility in diagnostics output.
  - Fixed post-sleep watchdog race that could force sustained high fan writes.

## Downloads

Windows:
- OmenCoreSetup-3.2.5.exe
- OmenCore-3.2.5-win-x64.zip

Linux:
- OmenCore-3.2.5-linux-x64.zip

## SHA256

9BA9A36111358F24912174D341932DE1666260F8A5140A73418E7EB472EA8072  OmenCoreSetup-3.2.5.exe
01CF69CE5BB6A8A435C6816265029E14F9A24EB651E0098B4E86436AECA7C0D7  OmenCore-3.2.5-win-x64.zip
768F94CB97A8B684728E3C619C490AA10DE4F0541A4640A30E5CAFDD7F342AB0  OmenCore-3.2.5-linux-x64.zip

If you hit a regression, please open an issue with your laptop model, BIOS version, and logs so we can reproduce quickly.

Issues:
https://github.com/theantipopau/omencore/issues

Thanks to everyone who reported bugs and tested fixes across Discord, Reddit, and GitHub.