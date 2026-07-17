# AUR Packaging (Draft)

Tracked as a 4.0.0-cycle roadmap item (`docs/ROADMAP_v4.0.0.md`, Phase C). This directory
is a reviewed **draft** — the PKGBUILD is structurally correct and follows the same
self-contained-binary shape the project already builds and documents, but **it has not
been built or tested on real Arch/CachyOS/Manjaro hardware**, because this was written in
a Windows dev environment with no Arch machine or `makepkg` available. Treat everything
here as "ready for someone on Arch to test," not "ready to publish."

## What's here

- `omencore-bin/PKGBUILD` — a `-bin` style package: downloads the pre-built self-contained
  release archive from GitHub Releases (the same `OmenCore-<version>-linux-x64.zip` /
  `-linux-arm64.zip` that `build-linux-package.ps1` already produces and the install guide
  already tells users to `wget` by hand) rather than compiling from source. This keeps the
  .NET SDK out of `makedepends` entirely — end users only need the binary.
- `omencore-bin/omencore.service` — static systemd unit, a copy of what
  `omencore-cli daemon --generate-service` (`src/OmenCore.Linux/Commands/DaemonCommand.cs`)
  already generates at runtime, with the dynamically-detected executable path fixed to
  `/usr/bin/omencore-cli`. **If `DaemonCommand.cs`'s generator changes, mirror the change
  here too** — nothing currently keeps these two copies in sync automatically.
- `omencore-bin/omencore.desktop` — desktop entry for `omencore-gui`.
- `omencore-bin/omencore.png` — **placeholder icon** (`Assets/logo-small.png`, 367×432,
  ~330KB). Not square, not sized for a pixmap/icon theme. Needs a proper square icon
  (256×256 or an SVG) from whoever owns the branding before real submission — this is the
  one piece of the package that's a known gap, not just untested.
- `omencore-bin/omencore-bin.install` — post-install/upgrade message (kernel module +
  systemd enable instructions) and a pre-remove hook that stops/disables the service
  cleanly.

## What was checked before writing this

- The Linux CLI and GUI (`OmenCore.Linux`, `OmenCore.Avalonia`) have **no in-app
  self-update mechanism** — confirmed by grepping both projects for update-check code.
  (The Windows app's `AutoUpdateService` does not exist on this platform.) This means
  there's no auto-update-vs-pacman conflict to design around for this package, unlike a
  naive assumption might suggest.
- `OmenCore.Linux.csproj` already builds `SelfContained=true`, `PublishSingleFile=true`,
  `PublishTrimmed=true` for `linux-x64`/`linux-arm64` — exactly the shape a `-bin` AUR
  package wants, no .NET runtime dependency at install time.
- `build-linux-package.ps1` already assembles one release ZIP containing both
  `omencore-cli` and `omencore-gui` together and strips the CLI's framework-dependent
  sidecar files, leaving only the self-contained binary — this PKGBUILD's `package()`
  function relies on exactly that shape.

## Before this can actually be submitted to AUR

1. **Pick a maintainer.** Someone needs to own the AUR submission and keep it updated on
   new releases — that's a project-ownership decision, not something resolved by writing
   the PKGBUILD. Could be the project owner, or a community volunteer.
2. **Replace the icon** (see above).
3. **Fill in real checksums.** Every `sha256sums`/`sha256sums_x86_64`/`sha256sums_aarch64`
   entry is `SKIP` right now. Once a real release exists at the `pkgver` the PKGBUILD
   points at, run `updpkgsums` (or hand-compute `sha256sum` on the downloaded archives) and
   commit the real hashes — AUR rejects packages with unverified sums.
4. **Build-test on real Arch hardware**: `makepkg -si` in this directory, then sanity-check
   `omencore-cli status`, `omencore-gui` launching, and `systemctl enable --now omencore`
   actually working end to end. None of that has been exercised yet.
5. **Generate `.SRCINFO`**: `makepkg --printsrcinfo > .SRCINFO`, required by AUR and not
   included here since it must be regenerated from a real `makepkg` run, not hand-written.
6. **Bump `pkgrel`/`pkgver`** to track whatever version actually ships, and set up the
   normal AUR update cadence (new `pkgrel`/`pkgver` + fresh checksums per OmenCore release).

## Why CLI+GUI together, not CLI first

The original plan was "ship a CLI-only package first, GUI later" since the GUI has no
`.desktop` file/icon today and the roadmap has an open item for Linux GUI tray/config work.
That reasoning still holds for *readiness*, but doesn't change the packaging shape: the
official release ZIP already bundles both binaries in one download regardless (see
`build-linux-package.ps1`), so there's no build-cost reason to split them. This PKGBUILD
installs both; if the project owner wants a CLI-only package instead, drop the
`omencore-gui`/`omencore.desktop`/icon lines from `package()` and the `depends` list can
shrink to just `glibc`.
