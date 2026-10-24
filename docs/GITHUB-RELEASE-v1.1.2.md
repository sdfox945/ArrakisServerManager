# Arrakis Server Manager v1.1.2

First public Windows x64 release, based on the accepted v1.1.1 implementation with a narrow WaterExtractionRate guidance correction. Unofficial native .NET 10/WPF management for an existing official Dune: Awakening self-hosted server; not affiliated with or endorsed by Funcom or Dune's rights holders. No proprietary server software, Steam/game content or credentials are bundled or installed.

## Correction

**WaterExtractionRate: higher = faster water extraction; lower = slower.** Catalog details and slider labels now agree. This is based on user-reported gameplay observations contradicting Funcom's supplied template comment; it has not been independently verified during development. The original 0.1–10 range, key, numerical values and save behavior are unchanged. Existing server comments are preserved and no values are inverted or migrated.

## Included features

- Configuration Editor 2.0 with precise typed/sliding proposals, categories, semantic no-op detection and synchronized visual/raw drafts.
- Full diff review, frozen payload integrity, concurrency/hash checks, BusyBox-compatible locking, encrypted local backups, remote rollback, atomic replacement and final verification.
- State-aware Dashboard VM/battlegroup controls and resource history.
- Game Maps/Infrastructure/All views, validated resource proposals, completed utility/Job health and strictly confirmed Idle / On demand states.
- Current versus historical diagnostics, bounded logs, verified backup download reporting, administrator launcher and durable unknown-outcome guidance.

## Install

Download `ArrakisServerManager-v1.1.2-win-x64.zip` and `SHA256SUMS.txt`. Compare the complete SHA-256 using PowerShell's `Get-FileHash`, extract the entire folder and read LICENSE, BUNDLED-RUNTIME-TERMS.md and the included documentation. Start **Launch Demo.cmd** for isolated simulation. Use **ArrakisServerManager.exe** for Live mode, or **Launch as Administrator.cmd** when Hyper-V permissions require elevation. Check installation/key/IP settings and verify new SSH fingerprints through a trusted channel.

Requires x64 Windows supported by .NET 10, the existing official server/VM, Windows OpenSSH and existing SSH key, plus Hyper-V PowerShell permissions for VM integration. The .NET/Windows Desktop runtime is included; keep all adjacent files. The program is unsigned, with no installer or automatic updater. A checksum verifies download integrity, not publisher identity.

## Validation and known limitations

The five correction cases extend all 154 v1.1.1 regressions to 159 tests, including 29 native WPF workflows. They check wording/provenance/bounds, actual rendered endpoint labels and byte-identical simulated review/save/reopen. Native smoke validates the complete packaged application in Demo. Detailed final results and the ZIP checksum accompany the publication review.

v1.1.1 remains preserved at `5361122e7c949242517ab5fb81b0ac670dbc742f`; v1.1.2 is a separate release, not a substitution into that stable tag.

The user accepted the v1.1.1 live UI/configuration preview and earlier v1.0.2 configuration save. This does not establish live testing of v1.1.2 gameplay behavior, VM shutdown/startup, battlegroup lifecycle, map-resource reconciliation, database restore, server updates or other disruptive actions. Those require separate acceptance and recovery preparation. Development mutation tests use isolated simulations/files/containers, never production. Real mixed-monitor DPI and UAC require target-machine acceptance.

Compatibility remains limited to the inspected Funcom CLI/Kubernetes layouts. Changed hashes, ambiguous targets and unknown schemas fail safely or require the official editor. No automatic reinstall/repair, SSH-key rotation, arbitrary local database upload or fabricated update-check-only operation is provided. Resolve unknown outcomes before acknowledging the guard or retrying any mutation.

## Licensing and privacy

Original application code: MIT, Copyright (c) 2026 Steven Fox. Microsoft and third-party runtime components retain their included terms/notices. The source-only representative configuration fixture retains third-party-origin functional text, separately documented in FIXTURE-PROVENANCE.md; that material is not relicensed by the application MIT notice. No rights to proprietary Funcom software or trademarks, and no Funcom endorsement, are claimed.

For support, provide sanitized steps and version information. Never post keys, service tokens, passwords, production INIs, databases or unreviewed diagnostic archives. No production server/VM/Funcom-installation changes occurred during this correction.
