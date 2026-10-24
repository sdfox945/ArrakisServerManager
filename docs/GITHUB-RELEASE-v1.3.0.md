# Arrakis Server Manager v1.3.0 — Custom Configuration Management

Native Windows x64 management for an existing official Dune: Awakening self-hosted server. This is an unofficial community project, not affiliated with or endorsed by Funcom or Dune's rights holders. No proprietary game-server software, Steam installation or game assets are bundled or installed.

## New

- Automatic discovery of safely represented active scalar variables in UserGame.ini, UserEngine.ini and UserServerCustomSettings.ini, including variables added through Raw editing.
- Guided **Add Variable**: existing or explicitly created INI section, exact key validation, friendly name/category, confirmed Boolean/integer/decimal/string/choice type and literal draft preview. Existing variables cannot be accidentally duplicated.
- Persistent custom definitions, names, categories, descriptions, optional examples/defaults and other user annotations, stored separately from the portable application.
- Reviewed metadata import/export with conflict handling, schema 1 compatibility, atomic local writes/recovery and isolated Demo/Live catalogs.
- Wider, resizable Visual Settings Editor with persistent divider width, source indicators, dynamic category counts, expanded search and copyable technical keys.

## Improved

- Independently labeled **Example Value** and **User-Defined Default**, each optional and distinct from the saved value, draft value and any genuinely verified engine default.
- **Reset User Default** changes Proposed Value only. It never edits the draft, writes a server file or restarts anything by itself.
- Context-sensitive type fields, Boolean True/False without manual enum setup, field-specific validation, disabled invalid saves and focus on the affected input.
- Clear metadata-only Manage Definition workflow with locked target identity, a fixed preview/action area and collapsed advanced metadata.

## Safety and confidence

Discovery never changes configuration. Unknown types/ranges/defaults/game effects are not automatically verified; discovered scalars begin as text until the user confirms metadata. Complex, repeated or ambiguous directives stay **Raw-only**; secret-like values are masked in general lists. Local annotations cannot bypass built-in validation or protected required INI entries.

Metadata changes do not alter INI bytes or dirty the draft. Actual visual changes still require **Add to Draft → exact-diff review → explicit final save confirmation**, with stable document identity, original hash/conflict checks, intended-change replay and frozen approved payloads. BusyBox-compatible locking, encrypted local backups, remote rollback, atomic replacement, readback and durable unknown-outcome interlocks remain intact.

BattlEye.Enabled is a user/community-supplied management example, not an independent verification of anti-cheat behavior. Arrakis does not automatically disable BattlEye or apply custom values.

## Download and portable installation

Requires x64 Windows supported by .NET 10. Windows 11 with an existing official Hyper-V/server installation remains the intended administration host. The ZIP is self-contained; no separate .NET installation is required. Hyper-V actions may require administrator authorization; application update checks/downloads and metadata editing do not require elevation by themselves.

1. Download **ArrakisServerManager-v1.3.0-win-x64.zip** and **SHA256SUMS.txt** from this release. GitHub-generated source archives are not runnable packages.
2. Compare `Get-FileHash .\ArrakisServerManager-v1.3.0-win-x64.zip -Algorithm SHA256` with the full ZIP checksum in SHA256SUMS.txt.
3. Close Arrakis and extract the complete ZIP into a new or chosen application folder. Keep all adjacent DLLs, runtime files, launchers, documentation and licenses together.
4. Start **Launch Demo.cmd** first, then the executable or **Launch as Administrator.cmd** when your intended Hyper-V workflow needs UAC elevation.
5. Confirm v1.3.0 in the sidebar. Keep an earlier release folder if desired.

Normal preferences, history, encrypted configuration backups, host pins, safety state and custom catalogs remain under `%LOCALAPPDATA%\ArrakisServerManager`, outside the portable folder. Do not delete that state during an upgrade. Custom metadata remains schema 1 and preserves existing Example/Default values. Demo catalogs are isolated; Demo INI values are simulated in memory and rebuild between sessions.

The existing optional GitHub updater concerns **Arrakis itself**, not Funcom server updates. It checks stable releases and downloads SHA-256-verified ZIPs; it never automatically extracts, installs, executes, replaces the running application or restarts it.

## Validation and limitations

**334/334 automated tests passed, including 88 native WPF workflows; zero Release build warnings/errors.** All 312 original v1.3.0 cases and earlier configuration, BusyBox, Kubernetes, VM and updater regressions remain. The extracted Windows ZIP passes native six-page Demo smoke and isolated custom-draft/update scenarios.

The user accepted the configuration editor and final metadata refinements. Automated mutation tests use isolated fixtures/simulations/containers, not production. The native BattlEye default workflow proves unchanged saved/draft bytes after metadata edits, proposal-only reset and the exact single-line diff, canceled before a simulated save. This does not independently establish gameplay effects of custom variables.

The executable remains **unsigned**. SHA-256 establishes correspondence with published bytes; it is **not an independently authenticated software signature**. Funcom compatibility remains limited to supported/inspected CLI and schema layouts; changed/unknown layouts fail safely or require official tools. VM shutdown/startup, battlegroup lifecycle, map-resource changes/reconciliation, database restore, Funcom updates and other unexercised disruptive actions still require separate live acceptance and recovery preparation. Physical mixed-monitor DPI transitions and UAC remain target-machine checks.

## Licensing and distribution

Original project contributions are MIT, Copyright (c) 2026 Steven Fox. Microsoft runtime components retain their bundled terms/notices. The source-only template-derived fixture has a separately documented third-party boundary and residual rights uncertainty in **FIXTURE-PROVENANCE.md**; it is not relicensed by the application MIT notice. No ownership or redistribution right over proprietary Funcom software or trademarks is claimed.

This release is prepared for GitHub first. Nexus Mods distribution/policy review is separate; no Nexus-specific build or upload is included. No production server, VM, credentials, databases or Funcom installation were changed during release finalization.
