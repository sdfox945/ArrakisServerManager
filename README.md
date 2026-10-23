# Arrakis Server Manager 1.0.2

Critical configuration-editor integrity patch: stable file selection, one authoritative draft, frozen preview/payload approval, visual-intent replay, protected INI structure, and explicit raw-removal authorization. The BusyBox locking/rollback and unknown-outcome safeguards from v1.0.1 remain intact. See [v1.0.2 root cause and acceptance](docs/RELEASE-v1.0.2.md) and the historical [v1.0.1 report](docs/RELEASE-v1.0.1.md).

A native Windows control center for an **existing** Dune: Awakening self-hosted installation. Built with C#/.NET 10 and WPF; no browser, Node.js or local web service is required for normal administration.

## Run

The self-contained Windows x64 build is under `artifacts/release/ArrakisServerManager-v1.0.2-win-x64/ArrakisServerManager.exe`. Keep the entire release folder together. No separately installed .NET runtime is required. Windows OpenSSH and the Hyper-V PowerShell module remain operating-system prerequisites for their respective integrations. The v1.0 and v1.0.1 release folders are preserved.

Double-click the executable for Live mode, or use **Launch Demo.cmd** for isolated simulated infrastructure and a separate Demo-v1.0.2 data directory. Live mode retains the existing preferences/history/safety guard. The executable is unsigned; there is no installer, service, telemetry collector or automatic updater.

On first launch the app looks for the Steam server installation, existing VM, SSH key and a recent management-log VM address. If connection fails, Tools & Settings opens with simple connection fields. Save connection changes before refreshing. Normal monitoring does not request elevation. Your account needs Hyper-V authorization for direct VM readings; a narrow UAC snapshot and separate UAC lifecycle helper are available. The GUI remains unelevated.

Existing Windows OpenSSH `known_hosts` identities are respected. A new host requires explicit fingerprint review against the VM console; changed pins are never silently replaced. No setup/reinstall operation is present.

## Features

- Live Windows RAM/CPU/client detection, Hyper-V information where permitted, guest RAM/swap/CPU/disk, Kubernetes readiness, dynamic maps/services, allocation/CrashLoop/OOM diagnostics and configurable pressure warnings.
- Confirmed VM start, graceful guest shutdown and a separate emergency power-off. Official battlegroup start/stop/restart/update operations have asynchronous output, timeouts and observed-state verification.
- Searchable maps, requests/limits/usage/restarts/minimum-instance information, resource-version guarded memory and Director MinServers changes, server-side validation, rollback copies and the official editor fallback.
- Visual settings catalog from inspected Funcom templates and a loss-preserving raw editor for UserGame.ini, UserEngine.ini and UserServerCustomSettings.ini. Reviewable differences, validation, undo, encrypted local backups, remote rollback copies, atomic replacement, readback and conflict detection.
- Official database backup/import, remote artifact listing, size/SHA-256 verified downloads, and separate configuration/diagnostic categories. Import requires an already stopped battlegroup and explicit destructive confirmation.
- Bounded log tails, selected pod logs, events, retained sanitized operation history, official log exports and complete diagnostic bundles. Read operations can be cancelled; submitted mutations cannot be cancelled unsafely.
- Director NodePort discovery from port 11717, checked File Browser shortcut, persisted preferences/window placement, configurable directories, native networking/password/experimental-swap consoles and isolated demo failure scenarios.

An unknown remote outcome locks subsequent mutations, including across restarts. Inspect current state, then explicitly acknowledge your review in Tools. App crashes during a mutation also retain this guard.

## Build and test

Install .NET SDK **10.0.402** (or a compatible patch) from Microsoft. A project-local SDK under `.tools/dotnet` is supported. The development tests also require a running Linux Docker engine and the official pinned BusyBox 1.37.0 image (not required by the application):

```powershell
docker pull busybox@sha256:bdf57e528e45e4433820e045b29b4597825a1c9e38353532d90a01445013f82e
```

Run from Windows PowerShell:

```powershell
.\scripts\build.ps1 -Publish
.\scripts\test-ui.ps1
```

The first command builds the complete Visual Studio solution, runs the executable regression suite, then publishes the self-contained x64 folder. `dotnet test` is not the test entry point; the dependency-free regression harness runs with `dotnet run --project tests/Arrakis.Tests -c Release`. Test evidence is written under `artifacts/tests`.

The full suite includes native WPF file-picker, focus, button, tab and modal-dialog interactions against an isolated recording gateway. The editor-only subset runs with `dotnet run --project tests/Arrakis.Tests -c Release -- --editor-native`. It uses a credential-sanitized representative INI fixture derived by read-only inspection; no native editor test connects to production.

An explicit, separate `--live-read-only` test-harness mode reads discovery, logs, configuration hashes, backup listings and web-tool availability. It contains no calls to mutation, trust, editor or restore methods. Do not exercise live changing controls during development.

See [architecture](docs/ARCHITECTURE.md), [troubleshooting](docs/TROUBLESHOOTING.md), [testing and manual acceptance](docs/TESTING.md), [compatibility](docs/COMPATIBILITY.md), and [release evidence](docs/RELEASE-REPORT.md).

## Important compatibility limits

Funcom's inspected CLI combines update checking and applying. The app can compare the running image with the already-downloaded revision; **online check-only is not supported**. Applying updates is a separate confirmed action. CLI-changing updates disable further automatic CLI mutations until compatibility is reviewed.

Automatic map writes support the inspected `igw.funcom.com/v1` Battlegroup layout. Unknown layouts or multiple ambiguous battlegroups fail safely and use Funcom's interactive editor. This release deliberately excludes direct Kubernetes pod patches, game database manipulation, client configuration writes, reinstall and automatic repairs.

SSH key rotation remains in Funcom's Windows tooling; its current implementation disables host verification and replaces local/remote keys in a way that needs a separate security/recovery review before GUI automation. Networking, password and experimental swap actions open explicitly labeled native consoles; follow their prompts and verify outcomes there.

Production-changing features are implemented and tested with simulation, **awaiting live acceptance**. Read-only live checks do not establish that backup, import, restart, update, resource edits or saves have been exercised against the actual world.
