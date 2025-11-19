# WinRing0-Compatible EC Bridge Stub

This directory documents the expected kernel driver interface used by the OmenCore `WinRing0EcAccess` bridge.

## Requirements

- Provide a signed KMDF/UMDF driver that exposes an IoControl device such as `\\.\WinRing0_1_2`.
- Support two IOCTLs:
  - `IOCTL_EC_READ (0x80862007)` – input/output buffer is `EC_REGISTER_ACCESS` (`USHORT Address`, `BYTE Value`). Driver must read a byte from the embedded controller and return it in the same structure.
  - `IOCTL_EC_WRITE (0x8086200B)` – input buffer is `EC_REGISTER_ACCESS`. Driver writes `Value` to the EC register pointed by `Address`.
- Serialize access internally; the user-mode app throttles writes, but the driver should still guard against flooding.

## Building a Minimal Driver

1. Start from the open-source [OpenHardwareMonitor WinRing0 driver](https://github.com/openhardwaremonitor/openhardwaremonitor/tree/master/Source/WinRing0) or the [`RwDrv.sys`](https://rweverything.com/) sample.
2. Strip the MSR/PCI/GPIO handlers you do not need and keep only EC read/write hooks.
3. Ensure the `EC_REGISTER_ACCESS` structure matches the user-mode layout:

```c
#pragma pack(push, 1)
typedef struct _EC_REGISTER_ACCESS {
    USHORT Address;
    UCHAR Value;
} EC_REGISTER_ACCESS, *PEC_REGISTER_ACCESS;
#pragma pack(pop)
```

4. Sign the driver (EV certificate recommended) or enable test-signing for development.
5. Deploy with `pnputil /add-driver driver.inf /install` and confirm the device link using `winobj.exe`.

## Safety Notes

- Incorrect EC writes can instantly shut down the laptop or permanently damage fans/VRMs. Always validate values on a sacrificial unit before shipping.
- Consider exposing an allowlist of EC addresses inside the driver to avoid arbitrary register access.
- Gate IOCTLs behind an admin-only device ACL. OmenCore already requires elevation, but the device node should enforce it regardless.
