# OmenCore v1.0.0.4 (2025-12-10)

## Highlights
- New tray badge overlays live CPU temperature on the notification area icon while keeping tooltip/context telemetry.
- Cleaner shutdown logging: writer thread is joined so tail logs are flushed before exit.
- Cleanup toggles fixed: legacy installer removal now maps to the correct option and UI label.
- Auto-update flow is safer and clearer: missing SHA256 hashes now block in-app install with guidance instead of throwing.
- UI copy polish: update banner/non-HP warning text render correctly on all systems.

## Technical Notes
- Tray icon drawing uses a 32px RenderTargetBitmap with accent ring and centered CPU temp text; falls back to base icon if unavailable.
- LoggingService now retains the writer thread handle and joins it for up to 2 seconds during dispose.
- SystemControl cleanup bindings align with `OmenCleanupOptions.RemoveLegacyInstallers`.
- Auto-update download skips when release notes omit `SHA256:` and marks install blocked; banner status reflects the requirement.

## Upgrade Guidance
- Replace prior builds with the 1.0.0.4 installer/zip; configs remain compatible.
- If using auto-update, ensure future releases publish SHA256 in notes to enable in-app install.
- No database/config migrations are required for this release.
