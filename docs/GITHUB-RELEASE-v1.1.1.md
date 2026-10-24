# Arrakis Server Manager v1.1.1

**Superseded local publication draft.** The proposed first public package is now v1.1.2, correcting WaterExtractionRate guidance based on user-reported gameplay observations. Use GITHUB-RELEASE-v1.1.2.md for publication review. The committed/tagged v1.1.1 and its original binaries remain preserved.

First public Windows x64 package of the accepted v1.1.1 source baseline. Native .NET 10/WPF management for an existing official Dune: Awakening self-hosted server. This unofficial project is not affiliated with or endorsed by Funcom or Dune's rights holders. The official server, Steam content, game assets and credentials are not included or installed.

## Included

- Configuration Editor 2.0: precise numerical sliders and typed input, dynamic categories, saved/draft/proposed values and semantic no-op detection.
- Loss-preserving visual/raw INI editing with frozen diff/payload integrity, conflict detection, BusyBox-compatible locking, encrypted local backups, remote rollback copies, atomic replacement and readback verification.
- State-aware Dashboard VM/battlegroup controls and resource history.
- Game Maps, Infrastructure and All views, validated map-resource proposals, completed utility/Job health, and strict Idle / On demand classification.
- Current versus historical Kubernetes diagnostics, bounded/filterable logs, verified backup download reporting, administrator launcher and unknown-outcome recovery guidance.

## Install

Download `ArrakisServerManager-v1.1.1-win-x64.zip` and `SHA256SUMS.txt`. Compare the complete SHA-256 using PowerShell's `Get-FileHash`, then extract the whole folder. Read `LICENSE`, `BUNDLED-RUNTIME-TERMS.md` and the included documentation. Start `Launch Demo.cmd` to explore isolated simulation. For Live use, run `ArrakisServerManager.exe` or `Launch as Administrator.cmd` if your Hyper-V permissions require elevation. Review the installation/key/IP settings before connecting, and verify any new SSH host fingerprint through a trusted channel.

Requires x64 Windows supported by .NET 10, an existing official server/VM, Windows OpenSSH, the existing SSH key, and Hyper-V PowerShell permissions for VM integration. The .NET runtime is included; no separate .NET install is required. Keep all adjacent runtime files. The program is unsigned and supplied as a ZIP, with no installer or automatic updater. The checksum is an integrity check, not a signature.

## Validation and limitations

The accepted source is `5361122e7c949242517ab5fb81b0ac670dbc742f`, identified by the existing annotated `v1.1.1` tag. The existing accepted application executable is reused without rebuilding; its FileVersion is 1.1.1.0. Its informational revision suffix records the earlier build base, not the later source-baseline commit. Packaging adds public documentation and license notices without changing application binaries. Historical stable tags are not moved.

The established validation baseline is 154 automated tests, including 27 native WPF workflows and the original 129 regressions, plus a six-page native Demo smoke. The user reviewed the live UI and configuration preview; the earlier v1.0.2 configuration save also received live acceptance. This does **not** establish live acceptance for VM shutdown/startup, battlegroup lifecycle transitions, map-resource changes/reconciliation, database restore, server updates or other unexercised disruptive actions. These require separate acceptance and recovery preparation. Native tests use isolated adapters, files and BusyBox containers. Real multi-monitor DPI changes and UAC need target-machine acceptance.

Compatibility is bound to the inspected Funcom CLI and Kubernetes layout. Changed CLI hashes, ambiguous resources and unknown schemas fail safely and require review or the official-editor fallback. The installed CLI combines update checking/applying; there is no fabricated online check-only operation. SSH key rotation, arbitrary local database upload, reinstall/setup and automatic repair are outside automated support.

Back up independently before administration. Review every exact diff and confirmation. If an outcome is uncertain, inspect and reconcile it before acknowledging the guard or attempting another mutation. See README, Compatibility, Troubleshooting and the acceptance document in the ZIP.

## Licensing and support

Proposed original-code license: MIT, Copyright (c) 2026 Steven Fox, subject to final publication review. Microsoft runtime and third-party components retain their separate included terms/notices. No rights to proprietary Funcom material or trademarks are granted.

For issues, provide version and sanitized steps; never post credentials, private keys, production configuration, database files or unreviewed diagnostic archives. No production operations were performed during public-release preparation.
