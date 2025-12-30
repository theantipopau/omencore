# CHANGELOG v1.6.0-alpha — 2025-12-25

## Summary
Phase 3 (RGB) initial spike: provider abstractions and wiring, Corsair preset mapping, an experimental system-level RGB provider (RGB.NET), and tests.

### Added
- **Rgb provider framework** (`IRgbProvider`, `RgbManager`) — a small, extensible provider model to register multiple RGB backends and apply effects across them.
- **Corsair provider** (`CorsairRgbProvider`) — wraps `CorsairDeviceService` and adds support for the `preset:<name>` and `color:#RRGGBB` semantics. Preset names are read from `ConfigurationService.Config.CorsairLightingPresets`.
- **Logitech provider** (`LogitechRgbProvider`) — wraps `LogitechDeviceService` and supports `color:#RRGGBB` static color application across discovered devices.
- **Razer provider** (`RazerRgbProvider`) — adapter that uses the existing `RazerService` Synapse detection and can map simple effects to the service (placeholder for full Chroma integration).
- **System generic provider (experimental)** (`RgbNetSystemProvider`) — uses `RGB.NET` to enumerate and attempt control of any RGB.NET-supported desktop devices. Supports `color:#RRGGBB` and is designed as a fallback for brands without bespoke integrations.
- **Provider wiring on startup** — Providers are created and registered (priority: Corsair → Logitech → Razer → SystemGeneric) and initialized from `MainViewModel` so the `Lighting` subsystem can present a unified entrypoint.
- **Unit tests** — Added `CorsairRgbProviderTests` and test wiring for provider initialization and preset application.

### Changed
- **LightingViewModel** now accepts an `RgbManager` instance so UI commands can call the unified provider stack.
- **MainViewModel** initializes and registers RGB providers during service initialization.

### Notes & Limitations
- `RgbNetSystemProvider` is experimental — RGB.NET requires platform-specific drivers and may not control every device; robust error handling and fallbacks are present.
- Razer Chroma SDK integration remains a future step (synapse detection already present; effect API is currently a placeholder).
- Two unrelated fan tests are still being investigated (file-lock and re-apply behavior); these are tracked separately.

### Developer Notes
- Effect syntax for providers:
  - `color:#RRGGBB` — apply a static color to all supported devices in the provider.
  - `preset:<name>` — lookup a named preset in configuration (only implemented for Corsair provider in this spike).

### Next steps
1. Implement preset UI mapping and enable applying saved presets from the Lighting UI.
2. Extend Logitech provider with breathing/brightness effects and add tests.
3. Harden `RgbNetSystemProvider` and add integration tests that run in CI using emulated device layers when needed.
4. Add "Apply to System" action in the Lighting UI and a small Diagnostics view for RGB testing.

---

For design details, see `docs/V2_DEVELOPMENT.md` (Phase 3 section).