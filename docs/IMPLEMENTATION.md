# Implementation plan and discovery

All stages are delivered; requirement-by-requirement evidence and live acceptance boundaries are recorded in RELEASE-REPORT.md.

The full user brief is the acceptance scope. Work proceeds through discovery, core/adapters, UI, simulation tests, release publishing and acceptance audit without production mutations.

1. Inspect all six supplied Funcom Windows/bootstrap files; record command contracts and hashes.
2. Inspect the Linux CLI and schemas read-only where existing trust and permissions permit. Unknown contracts receive an explicit official interactive workflow, never a guessed mutation.
3. Implement domain models, injectable process/Hyper-V/SSH/server/file/preferences adapters, serialized operations and retained sanitized history.
4. Implement loss-preserving INI editing, setting metadata, validation, conflict detection, automatic backups and restore.
5. Implement all six WPF pages, health diagnostics, maps/events/logs, lifecycle, backups, tools and secure connection setup, with a fully isolated demo mode.
6. Exercise units and simulated integration scenarios. Build/publish self-contained Windows x64. Launch and inspect desktop UI. Audit each acceptance requirement and document live acceptance boundaries.

Initial findings (2026-10-22): empty project; local Git initialized without remote. Installed runtime but no SDK; project-local Microsoft .NET 10.0.402 SDK provisioned under ignored .tools. Hyper-V module exists but current account lacks Hyper-V authorization even outside the filesystem sandbox. An existing SSH key and pre-trusted VM address were located without reading private key contents. No server setup or lifecycle commands have been executed.

Authoritative Windows script invokes /home/dune/.dune/bin/battlegroup through dune@guest; commands status/start/stop/restart/update/edit/edit-advanced/backup/import/logs-export/operator-logs-export. Update combines checking and applying; a safe standalone update-check flag must not be invented. Director port comes from service port 11717's nodePort. File browser uses 18888. Log exports stage /tmp/dune-bg-logs and /tmp/dune-operator-logs. Official edit is the fallback if resource schema cannot be verified. Upstream setup deletes/recreates infrastructure and is excluded entirely.

Reference: https://duneawakening.com/zh-hant/self-hosted-servers/ (Funcom): authoritative map resource is Battlegroup Kubernetes spec; Director edits do not persist; database backup/import should preferably run with battlegroup stopped. https://dotnet.microsoft.com/en-us/download/dotnet/10.0 (Microsoft toolchain).
