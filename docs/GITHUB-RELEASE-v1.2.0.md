# Arrakis Server Manager v1.2.0

Native Windows x64 management for an existing official Dune: Awakening self-hosted server. This unofficial community project is not affiliated with or endorsed by Funcom or Dune's rights holders. No proprietary game-server software is included or installed.

## What's new

- Independent automatic and manual GitHub checks for **Arrakis application updates**, with dynamic installed-version detection, stable-version comparison, 24-hour successful-result caching and an opt-out preference.
- Compact sidebar update notification and a dedicated release-details dialog with safe plaintext notes.
- Cancellable HTTPS ZIP downloads with progress, safe partial files, mandatory SHA-256 verification and revalidation/reuse of matching existing packages.
- Fixed version/status and download/action areas, independently scrollable release information, explicit canceled/failed states, Retry Download, Download Again and collapsed installation help.

These controls update Arrakis itself, not Funcom's dedicated-server software. Downloads never extract, install, execute, elevate, replace the running program or restart it automatically. GitHub checks require no account/token, administrator privileges or Dune-server connection. Offline failures do not block normal management. Demo makes no real GitHub update requests.

## Download and portable installation

Requires x64 Windows supported by .NET 10; Windows 11 with the existing Hyper-V/server setup is the intended administration host. The ZIP includes the runtime; no separate .NET installation is required. Hyper-V operations may require administrator rights; application update checks/downloads do not.

1. Download **ArrakisServerManager-v1.2.0-win-x64.zip** and **SHA256SUMS.txt** from this release. GitHub's generated source archives are not runnable packages.
2. Compare `Get-FileHash .\ArrakisServerManager-v1.2.0-win-x64.zip -Algorithm SHA256` with the full checksum in SHA256SUMS.txt.
3. Close Arrakis. Extract the complete ZIP into a new or chosen application folder; keep all adjacent dependencies, launchers, documentation and licenses together.
4. Launch **Launch Demo.cmd** first, then the executable or **Launch as Administrator.cmd** if your intended Hyper-V workflow requires elevation.
5. Verify v1.2.0 in the sidebar. Preserve your earlier application folder if desired.

Normal preferences, configuration history, host pins, encrypted backups and operation safety state remain separately under `%LOCALAPPDATA%\ArrakisServerManager`. Do not delete that state during a portable upgrade. Demo uses a separate data directory.

The executable remains **unsigned**. SHA-256 verifies correspondence with published asset bytes; a GitHub digest/checksum is **not an independently authenticated software signature**. Review Windows reputation warnings and verify your download before running it.

## Validation and acceptance limits

**256/256 automated tests passed, including 58 native WPF workflows; zero Release build warnings/errors.** All earlier configuration integrity, BusyBox locking/rollback, Kubernetes classification, VM gating and unknown-outcome regression coverage remains. The extracted self-contained ZIP passes the six-page native Demo smoke and simulated update-download scenarios.

The user accepted update detection, cancellation, verified downloads and the refined dialog UI. Live GitHub discovery was tested against the existing v1.1.2 release; available-future-release downloads, interruption and checksum failures used isolated simulations. This is not a claim that the updater has downloaded a future real public release.

VM shutdown/startup, battlegroup lifecycle transitions, map-resource modifications/reconciliation, database restore, Funcom server updates and other unexercised disruptive actions still require separate live acceptance and recovery planning. Physical mixed-monitor DPI transitions and UAC require target-machine checks. Existing configuration-save safeguards and durable unknown-outcome interlocks remain intact.

## Licensing and distribution

Original application code is MIT, Copyright (c) 2026 Steven Fox. Bundled Microsoft runtime components retain their separate terms/notices. The source-only template-derived test fixture is distinguished from original MIT contributions in **FIXTURE-PROVENANCE.md**, including its documented residual copyright uncertainty; no third-party ownership or additional permission is claimed.

This package is prepared for GitHub distribution first. No Nexus Mods upload or Nexus-specific build is included; optional GitHub download functionality may require separate Nexus policy review. No production server, VM, database or Funcom installation changes were made for this release finalization.
