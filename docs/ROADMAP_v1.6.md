# OmenCore v1.6 Roadmap

**Target Release:** Q4 2025 / Q1 2026  
**Status:** Planning  
**Last Updated:** December 17, 2025

---

## Overview

Version 1.6 focuses on platform expansion, performance optimization, and architectural improvements identified in the v1.5 engineering audit.

---

## Bug Fixes

### Auto-Update File Locking Issue
**Priority:** High  
**Source:** User Report (Dec 2025)

**Problem:** Auto-update download fails with `IOException: The process cannot access the file because it is being used by another process` when computing SHA256 hash after download.

**Root Cause:** `AutoUpdateService.ComputeSha256Hash()` tries to open the downloaded file before the `HttpClient` response stream is fully disposed/closed.

**Fix:**
- Ensure download stream is properly disposed with `using` blocks
- Add small delay or retry logic before hash computation
- Use `FileShare.Read` when opening for hash computation
- Consider downloading to temp file first, then moving

---

## Major Features

### 1. Linux Support 🐧
**Priority:** High  
**Complexity:** Very High

Port OmenCore to Linux for dual-boot users and Linux-native OMEN laptop owners.

**Technical Approach:**
- Cross-platform UI framework (Avalonia UI or .NET MAUI)
- Linux kernel driver interface for fan/thermal control
- `/sys/class/hwmon` for temperature sensors
- `hp-wmi` kernel module for HP-specific WMI access
- DBus integration for system tray
- Flatpak/AppImage distribution

**Challenges:**
- HP WMI BIOS may not be fully exposed on Linux
- EC access requires root or custom kernel module
- Different distros have varying support for hardware interfaces

**Milestones:**
- [ ] Research hp-wmi kernel module capabilities
- [ ] Prototype Avalonia UI cross-platform shell
- [ ] Implement Linux fan control backend
- [ ] Test on Ubuntu 24.04, Fedora 40, Arch
- [ ] Package as Flatpak and AppImage

---

### 2. Consolidated Hardware Polling
**Priority:** Medium  
**Source:** Engineering Audit (Dec 2025)

Currently FanService and HardwareMonitoringService run separate polling loops, causing duplicate LibreHardwareMonitor updates.

**Goals:**
- Single source of truth for temperature data
- Reduce DPC latency from frequent LHM updates
- Lower CPU/memory overhead on idle

**Implementation:**
- Create `ThermalDataProvider` shared service
- FanService subscribes to temperature events
- HardwareMonitoringService publishes at configured interval
- Fan curve logic reacts to events, not polling

---

### 3. HPCMSL Integration for BIOS Updates
**Priority:** Medium  
**Source:** Engineering Audit (Dec 2025)

Replace fragile HP API scraping with official HP Client Management Script Library.

**Benefits:**
- Authoritative BIOS version information
- Proper SoftPaq metadata (release notes, dependencies)
- Works across all HP regions

**Implementation:**
- Detect HPCMSL installation (`Get-HPBIOSUpdates` cmdlet)
- Use HPCMSL when available, fall back to support page link
- Rename button to "Open HP Support Page" when scraping
- Never auto-download BIOS executables

---

### 4. Per-Fan Curve Support
**Priority:** Low  
**Source:** Engineering Audit (Dec 2025)

Allow independent fan curves for CPU and GPU fans.

**Current State:**
- WMI `SetFanLevel(cpu, gpu)` accepts separate values
- UI applies same level to both fans

**Implementation:**
- Add "Link fans" toggle (default: linked)
- When unlinked: show separate CPU/GPU curve editors
- Store separate curve points in config
- Apply different levels based on CPU vs GPU temp

---

### 5. Curve Apply Verification
**Priority:** Medium  
**Source:** Engineering Audit (Dec 2025)

Use closed-loop verification to detect when WMI fan commands are ineffective.

**Implementation:**
- After `SetFanLevel()`, read back actual RPM
- If deviation > 20%, log warning
- After 3 consecutive failures, prompt user to try EC backend
- Integrate with `FanVerificationService` primitives

---

## UI/UX Improvements

### BIOS Update Experience
- Rename "Download BIOS Update" → "Open HP Support Page"
- Add warning if direct SoftPaq link is returned
- Show SoftPaq metadata when HPCMSL available
- Clear "Current: F.14 | Latest: F.15" comparison

### Performance Mode Clarity
- When EC unavailable: "⚠️ Limited control (Power Plan + Fan only)"
- When EC available: "✓ Full control (Power Plan + Fan + CPU/GPU Limits)"
- Tooltip explaining what each mode actually changes

---

## Performance Optimizations

### Reduce Allocations
- Avoid per-loop `.ToList()` in hot paths
- Reuse `ThermalSample` objects instead of creating new ones
- Pool string builders for logging

### Fan Telemetry Optimization
- Only update UI when RPM changes by >50 (implemented in v1.5)
- Further reduce to >100 RPM threshold if UI feels noisy
- Batch telemetry updates to reduce binding notifications

---

## Technical Debt

### Timer Lifecycle Audit
- Verify all timers disposed on shutdown
- HpWmiBios heartbeat timer
- WmiFanController countdown timer
- FanService monitoring timer

### Backend Selection Documentation
- Clarify when OGH proxy vs WMI BIOS is used
- Update comments to match runtime behavior
- Remove outdated "OGH preferred for 2023+" references

---

## Testing Plan

### Linux Testing Matrix
| Distro | Desktop | HP-WMI | Status |
|--------|---------|--------|--------|
| Ubuntu 24.04 | GNOME | TBD | |
| Fedora 40 | GNOME | TBD | |
| Arch | KDE | TBD | |
| Pop!_OS 24.04 | COSMIC | TBD | |

### Performance Benchmarks
- Idle CPU usage (target: <1%)
- Memory footprint (target: <100MB)
- DPC latency impact (target: <50µs)

---

## Timeline

| Phase | Target | Items |
|-------|--------|-------|
| Research | Jan 2026 | Linux hp-wmi capabilities, Avalonia feasibility |
| Prototype | Feb 2026 | Linux fan control PoC, consolidated polling |
| Alpha | Mar 2026 | Linux alpha build, HPCMSL integration |
| Beta | Apr 2026 | Cross-platform testing, per-fan curves |
| Release | May 2026 | v1.6.0 stable |

---

## Dependencies

- Avalonia UI 11.x (cross-platform)
- .NET 8 (already in use)
- HPCMSL (optional, for BIOS updates)
- Linux: `hp-wmi` kernel module, `lm-sensors`

---

*Created: December 17, 2025*
