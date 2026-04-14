**OmenCore — Windows Install Guide**

📥 **Download**
https://github.com/theantipopau/omencore/releases/latest
Pick the latest `OmenCoreSetup-*.exe` (installer) or `OmenCore-*-win-x64.zip` (portable).

🪟 **Installer** ← recommended
1. Right-click → *Run as administrator*
2. **SmartScreen warning?** → *More info* → *Run anyway* (no EV cert; it's open-source)
3. During setup:
   ✅ **Install PawnIO Driver** ← do this; Secure Boot compatible, enables EC/MSR access
   ✅ Create Start Menu shortcut
   ☐ Desktop shortcut — optional
   ☐ Start with Windows — optional (configurable inside the app later)
4. Launch from Start Menu — always run as Administrator

📦 **Portable ZIP**
1. Extract to any folder (e.g. `C:\OmenCore`)
2. Right-click `OmenCore.exe` → *Run as administrator*
> PawnIO can be installed separately: https://pawnio.eu

✅ **Requirements**
• Windows 10 (build 19041+) or Windows 11
• HP OMEN 15/16/17, Victus, or Transcend (2019–2025)
• No extra .NET install — runtime is bundled

⚙️ **Fan control method** (auto-detected, in priority order):
1. WMI BIOS — default, zero drivers needed
2. PawnIO EC — Secure Boot compatible
3. WinRing0 — legacy, may need Secure Boot disabled

📁 **Paths**
• Config: `%APPDATA%\OmenCore\config.json`
• Logs: `%LOCALAPPDATA%\OmenCore\`

🦠 **Antivirus false positive?** WinRing0 backend can trigger (`HackTool:Win64/WinRing0`).
Use PawnIO instead (Secure Boot compatible) — full FAQ:
https://github.com/theantipopau/omencore/blob/main/docs/ANTIVIRUS_FAQ.md

📖 **Full install guide:** https://github.com/theantipopau/omencore/blob/main/docs/INSTALL.md

🐛 **Need help?**
Post in <#support> with logs or:
https://github.com/theantipopau/omencore/issues
