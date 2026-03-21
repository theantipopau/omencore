🎉 **OmenCore v3.2.0 — Stability & Performance Release**

⚡ **Major Fixes:**
🔥 **High CPU Usage** — Adaptive polling (500ms active → 3s orphaned) = 70-80% idle reduction
🌡️ **Stuck Temps** — Auto-detects frozen sensors & recovers automatically
🔇 **Fan Lockups** — 0% commands now safely restore auto control
🎮 **Victus GPU** — Removed "API error"; controls hidden on unsupported hardware
⌨️ **Victus Keyboard** — Ryzen 2024+ (8C2F) now supported with 4-zone RGB
💥 **Linux GUI** — Fixed Ubuntu crashes; now starts successfully
🛡️ **Defender** — Updated docs on kernel driver detection (safe)

✨ **Improvements:**
🎚️ Polling Profiles (Performance/Balanced/Low Overhead/Custom)
📊 Stale Telemetry Banner on Dashboard
💾 Fan Preset Save (Avalonia GUI)
📝 Async Logging (60% I/O reduction)
🎨 UI De-duplication (no more flicker)

📥 **Downloads:**
Windows: `OmenCore-3.2.0-win-x64.zip` (104.3 MB) | `OmenCoreSetup-3.2.0.exe` (101.1 MB)
Linux: `OmenCore-3.2.0-linux-x64.zip` (43.6 MB)

**Checksums:**
```
WIN ZIP: 5428BAE69931B62C7BB637452FDDC7FC2F4CEA3CE3F735A7EA4A575C89B99B9D
SETUP:   05282E9EA6FDC73EA63558D90E747D3176DBD7E19D56722E552BDE6B2A9A077B
LINUX:   CBC49B7AEB0B8C2209C3D94B67C36F69517B6C1041B93B73793F9F4675B6883C
```

⚠️ Known: Defender may flag WinRing0 path (safe; exclude if needed). OMEN MAX 8D41 GPU power logging enhanced for diagnosis.

Build: ✅ All 5 projects, 0 errors, 0 warnings

Enjoy! 🚀
