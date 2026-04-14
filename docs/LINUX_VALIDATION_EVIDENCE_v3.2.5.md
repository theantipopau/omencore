# Linux Validation Evidence - v3.2.5

## Scope
This document tracks Step 2 validation evidence for Linux package/version integrity and capability behavior.

## Environment
- Host OS: Windows
- WSL status: not installed on this machine (`wsl --status` reports install required)
- Native Linux host: pending

## Completed in this workspace
1. Verified targeted F11/hotkey regression suite is green after strict interception hardening.
2. Confirmed Linux package verifier script is present and wired into build pipeline.
3. Confirmed Linux startup diagnostics and render-mode fallback documentation are in place.

## Validation commands run
```powershell
dotnet test .\src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --filter HotkeyAndMonitoringTests -v minimal
wsl --status
```

## Command outcomes
- `HotkeyAndMonitoringTests`: Passed (`Passed: 6, Failed: 0`)
- `wsl --status`: WSL not installed, so Linux binary execution checks cannot be completed on this machine.

## Pending Linux-host evidence
Run these on Ubuntu (or Windows with WSL installed) and attach outputs:

```bash
./omencore-cli --version
./omencore-gui --version
./omencore-cli status
./omencore-cli diagnose
```

```powershell
pwsh ./qa/verify-linux-package.ps1 -Version <version> -Runtime linux-x64 -ArtifactsDir ./artifacts -CliPath <cli-path> -GuiPath <gui-path>
```

## Pass criteria for Step 2 closure
- CLI and GUI report identical target version.
- `qa/verify-linux-package.ps1` passes without `-SkipBinaryExecution`.
- At least one partial hp-wmi/profile-only or telemetry-only board run includes `status` and `diagnose` evidence with capability reason text.
