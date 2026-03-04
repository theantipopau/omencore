# Privilege Separation Spike

This document collects notes and ideas for reducing or removing the `requireAdministrator` manifest
requirement currently used by OmenCore.

## Goals

* Determine if fan control and telemetry features can run from user context with an agent or service
  handling privileged operations (fan writes, WMI access, NVAPI, etc.).
* Outline data flow and IPC mechanism (named pipes, local socket, COM) for client–service communication.
* Identify components that truly need elevation (EC writes, ACPI/KVM calls) and minimize surface area.
* Evaluate implications for auto-update, installer, and Windows Defender.

## Preliminary Findings

* Current `FanController` and `HpWmiBios` classes assume admin rights for direct registers; these should be
  abstracted behind an interface and moved to a Windows service with limited privileges.
* Telemetry and diagnostics export already operate on user‑writeable areas; they can remain in the UI process.
* A small background service started at logon could handle fan/PWM writes and WMI queries. UI would talk
to it via named pipes with simple request/response messages.
* Installer would need to register the service; service itself could run as `LOCAL SERVICE` or a restricted
  account.

## Next Steps

1. Prototype a minimal service with a single "SetFanSpeed" RPC and validate it works from non-elevated user.
2. Audit all uses of `new ManagementObjectSearcher` or PInvoke that currently require elevation.
3. Consult HP audit team for code review and ensure compliance with service design guidelines.
4. Consider packaging changes (MSI/Wix) to install the new service only if elevation is allowed.

(Continued research and implementation to follow in v3.x or later.)
