🎉 OmenCore v3.2.5 is now live 🎉

This release focuses on stability, hardware compatibility, and day-to-day usability improvements.

Highlights:
- Fixed hardware worker reattach/reconnect spam and hardened worker-parent handling
- Fan control is now decoupled from performance mode switching by default
- Quick Access now includes Custom fan mode and improved performance mode ordering
- Added model disambiguation and capability fixes for shared Product ID 8BB1 systems (OMEN and Victus variants)
- Updater hardening: stricter asset selection plus SHA256 enforcement
- Fan diagnostics reliability and UX improvements
- Sleep/wake watchdog race fix for post-sleep high fan RPM edge cases

Downloads (v3.2.5):
https://github.com/theantipopau/omencore/releases/tag/v3.2.5

SHA256:
9BA9A36111358F24912174D341932DE1666260F8A5140A73418E7EB472EA8072  OmenCoreSetup-3.2.5.exe
01CF69CE5BB6A8A435C6816265029E14F9A24EB651E0098B4E86436AECA7C0D7  OmenCore-3.2.5-win-x64.zip
768F94CB97A8B684728E3C619C490AA10DE4F0541A4640A30E5CAFDD7F342AB0  OmenCore-3.2.5-linux-x64.zip

If you test this build, please report regressions with model details and logs in GitHub Issues:
https://github.com/theantipopau/omencore/issues

Thanks to everyone who reported issues and helped test fixes. 🚀