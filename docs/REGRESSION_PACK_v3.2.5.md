# OmenCore v3.2.5 Regression Pack

**Purpose:** Pre-release sign-off checklist for regressions reported against v3.2.1 and fixed during the v3.2.5 stabilization cycle.

**Status:** Ready for QA execution

**Related branch:** `dev/v3.2.5`

**Reference commits:**
- `2086e82` - model collision, fan/perf decoupling, updater hardening, diagnostics UX
- `be66a19` - watchdog sleep/wake fix, optimizer UX, bloatware verification, quick access updates

---

## Test Environments

- [ ] Installed Windows build (setup-installed path)
- [ ] Portable Windows build (zip extraction path)
- [ ] Victus 15-fa1xxx or equivalent 8BB1 collision target
- [ ] OMEN 17-ck1xxx or equivalent worker-CPU-temp override target
- [ ] System capable of sleep/resume validation

---

## 1. Updater Regression Pack

**Target issue:** In-app updater downloads an invalid executable while manual download works.

### Installed build path
- [ ] Update check offers installer `.exe` asset only.
- [ ] Updater log records download URL, final redirect URL, HTTP status, and content type.
- [ ] Install action is blocked if SHA256 is missing or mismatched.
- [ ] Successful download produces a valid Windows executable.
- [ ] Repeat the full installed-build update path 3 times without an invalid-executable failure.

### Portable build path
- [ ] Portable update check does not misclassify `.zip` payload as installer.
- [ ] Portable path does not surface installer-specific validation errors.
- [ ] Portable update flow matches expected artifact type and messaging.

**Evidence to capture**
- [ ] Updater log snippet for installed path
- [ ] Updater log snippet for portable path
- [ ] Screenshot of success or blocked-with-reason state

---

## 2. Fan and Thermal Regression Pack

**Target issues:** Quiet mode overriding manual fan mode, sticky high-fan behavior, post-sleep watchdog regression, confusing diagnostics.

### Fan/performance arbitration
- [ ] Apply a manual fan mode or custom fan curve.
- [ ] Switch performance modes in this order: Quiet, Balanced, Performance.
- [ ] Manual fan selection remains effective unless linked mode is explicitly enabled.
- [ ] Main window, tray, and Quick Access show the same effective fan/performance state.

### Sleep/wake watchdog validation
- [ ] Put system to sleep for at least 10 minutes.
- [ ] Resume and observe fan behavior for 2 minutes.
- [ ] No watchdog-triggered 90% fan failsafe occurs immediately after wake.
- [ ] Logs show suspend/resume handling without duplicate failsafe writes.

### Diagnostics recovery
- [ ] Run Fan Verification Sequence.
- [ ] Result reports backend and RPM source.
- [ ] Diagnostic run restores expected pre-test state after completion.
- [ ] Result panel remains readable at minimum supported window width.

**Evidence to capture**
- [ ] Fan diagnostics result screenshot
- [ ] Post-resume log excerpt
- [ ] Screenshot of main window or Quick Access showing consistent state

---

## 3. System Optimizer Regression Pack

**Target issue:** Toggles appear unresponsive or revert silently.

- [ ] Toggle a single optimization item on.
- [ ] UI shows a local pending state instead of freezing the full page.
- [ ] Toggle ends in a deterministic success or failure state.
- [ ] Failure state surfaces a usable reason instead of snapping back silently.
- [ ] Repeat under a non-admin session and confirm behavior is clear and intentional.

**Evidence to capture**
- [ ] Screenshot of active `Applying...` state
- [ ] Screenshot or log of failure reason in non-admin mode

---

## 4. Bloatware Manager Regression Pack

**Target issue:** Removal reports success while the app remains installed.

- [ ] Attempt removal of at least one Win32 app with a known uninstall string.
- [ ] If uninstall path uses MSI, verify `/I` is not preserved as modify/repair behavior.
- [ ] UI does not report verified success while the app is still installed.
- [ ] Bulk remove clearly reports per-item outcomes.
- [ ] Non-admin session blocks removal before operation start with explicit reason.

**Evidence to capture**
- [ ] Before/after app presence proof
- [ ] Removal status screenshot
- [ ] Relevant log excerpt for verified success or failure

---

## 5. Hardware Identity and Telemetry Regression Pack

**Target issues:** 8BB1 identity collision and OMEN 17-ck1xxx CPU temp drop-to-40 behavior.

### 8BB1 identity resolution
- [ ] On Victus 15-fa1xxx hardware, resolved identity is Victus rather than OMEN 17.
- [ ] Capability gating, keyboard profile, and RGB/fan behavior align with Victus profile.
- [ ] Diagnostic export includes enough identity context for support triage.

### CPU temperature stability
- [ ] On OMEN 17-ck1xxx, CPU temperature does not repeatedly fall back to 40°C under active load.
- [ ] Worker-backed source remains stable during repeated monitoring refreshes.

**Evidence to capture**
- [ ] Diagnostic export or log excerpt showing resolved identity
- [ ] CPU temperature graph or monitoring screenshot under load

---

## 6. Quick Access Regression Pack

**Target issues:** performance button order and missing custom fan option.

- [ ] Quick Access performance buttons are ordered Quiet, Balanced, Perform.
- [ ] Tray performance submenu uses the same order.
- [ ] Quick Access includes a Custom fan mode option.
- [ ] Custom mode applies the active non-built-in OMEN fan preset, or the first saved custom preset when no active custom preset is selected.
- [ ] Active-state highlighting matches the applied fan mode.

**Evidence to capture**
- [ ] Quick Access screenshot
- [ ] Tray submenu screenshot

---

## Sign-Off

- [ ] Regression pack executed on required hardware
- [ ] Logs and screenshots archived with release artifacts
- [ ] Any failures converted into tracked bugs before v3.2.5 release
