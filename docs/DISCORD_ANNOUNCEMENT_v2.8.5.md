# OmenCore v2.8.5 — Bug Fix Update

**Download:** https://github.com/theantipopau/omencore/releases/tag/v2.8.5

---

**9 bug fixes + 4 code quality improvements**

- Fan diagnostic 30% fail — adaptive RPM tolerance with 500 RPM min floor
- Fans stuck at max after test — auto control now restored on exit
- Fan test error text truncated — messages now wrap
- Bloatware removal broken — fixed AppxPackage name extraction
- Fn+F2/F3 toggles OmenCore — brightness keys now filtered out
- 4-zone KB not detected (xd0xxx) — fixed init order + model name matching
- OMEN Hub not detected — added 3 missing process names
- Startup on shutdown+start — Fast Startup compatible task trigger
- Update checker stuck at 2.7.1 — version string updated
- 4 stale version strings, 4 Process leaks, fault handling, orphaned test removed

---

**Windows:** `OmenCoreSetup-2.8.5.exe` | `OmenCore-2.8.5-win-x64.zip`
**Linux:** `OmenCore-2.8.5-linux-x64.zip`

```
E2765026C8E35ABE05D729973AE52074236C9EBDEE3886E2ACC1E59A40714C21  OmenCoreSetup-2.8.5.exe
319CDCFA839D67117CAFFBEA6AC3149009C75A4BDBD9D300F9536DE3A30E7A21  OmenCore-2.8.5-win-x64.zip
FEDB7D37DEE9772437123231AB057DE70F9D5D88778B5DAEAE92A395BC1D8E44  OmenCore-2.8.5-linux-x64.zip
```

Changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.8.5.md

If "Start with Windows" is enabled, toggle it off/on to apply the new task. 57 tests pass, 0 fail.

Thanks to everyone who reported bugs! 🙏
