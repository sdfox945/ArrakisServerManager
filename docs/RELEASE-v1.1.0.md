# Arrakis Server Manager v1.1.0

Historical report: v1.1.2 subsequently corrects WaterExtractionRate guidance to higher = faster extraction and lower = slower, based on user-reported gameplay observations contradicting the supplied template comment, not independent verification. See RELEASE-v1.1.2.md; the original v1.1.0 package remains unchanged.

Native Windows usability, monitoring and operational-control enhancement, based on the accepted v1.0.2 baseline. Source work is on `feature/v1.1`, based on `51a0cbb47c99686f0a67606da86811e25771af5f`. `main` and annotated `v1.0.2` remain unchanged. No release commit, tag, remote or push is created for v1.1.

## Configuration Editor 2.0

- Numerical sliders appear only for numeric/integer catalog settings with finite verified lower and upper bounds. Interaction increments derive from range magnitude and type; integer settings step by one. Typed input remains invariant-culture text, so `0.375` remains exact even when the slider normally steps by `0.1`.
- The custom native Slider distinguishes input gestures and UI Automation choices from binding/coercion updates. Initialization, focus, selecting another category and changing modes cannot become a slider edit. Slider choices update only Proposed value; Add to draft remains explicit.
- Documented effect labels cover water extraction, chest respawn, resource/NPC respawn and stamina drain. Other effects stay neutral. Descriptions retain source, bounds, zero semantics, restart/client requirements and unknown default/effect labels. Template examples are not represented as verified defaults.
- Categories/counts derive from recognized entries actually present in the authoritative draft and matching the search. Stable category identities survive updates. Empty categories disappear; invalid selection falls back to All categories. Supported absent settings remain available through the explicit include-to-add checkbox, distinct from Saved values.
- Saved, draft and proposed values are separate. Modified rows are marked. Search includes friendly name, key, category and description, with instructional empty states. The filename/server caption is short; verified full paths remain in state and are accessible through tooltip/Copy path.
- Existing stable file choices, explicit file-switch commits, shared raw/visual document, required entries, visual-intent replay, frozen reviewed bytes and cancellation behavior remain intact. The core document/review, configuration writer, lock, rollback and reconciliation implementations are unchanged.

## Dashboard and operations

- VM Start, graceful shutdown and Refresh are on the Hyper-V card. The same existing action handlers remain in Tools. Start requires verified Off; shutdown requires verified Running; inaccessible/unknown/transitional states disable both. The application rechecks VM state in the existing generated Hyper-V command immediately before acting.
- Shutdown review explains player disconnection, stopping all instances, unavailable battlegroup services and potential duration. It suggests stopping the battlegroup first without silently issuing that additional operation. Force power-off remains an advanced confirmed action.
- Battlegroup Start/Stop/Restart use shared state rules across Dashboard and Maps. Reviews hold the existing preparation lease, suspending polling and competing actions before actual operation dispatch. The durable unknown-outcome coordinator remains authoritative.
- Once Hyper-V verifies Off, status discovery avoids SSH entirely. Expected SSH loss during shutdown does not create a discovery error. Access-denied/unavailable VM status is distinct from Off and from an independently reachable guest.
- The headline qualifies core health with current map, infrastructure and unclassified workload problems. Warnings show severity, target, observation time, current/historical status and an investigation action. Selecting a notice opens its map or diagnostics.
- Binary sizes use GiB/MiB. Root disk storage is labeled. Guest RAM is explicitly included in host RAM. Configured requests/limits are distinguished from measured usage; a large memory limit alone is not an exhaustion warning. Uptime uses readable days/hours/minutes.
- Host/guest RAM and swap charts use 15/30-minute windows, at most 361 in-memory samples, and unavailable gaps. No history database or sampling disk writes are added. Samples follow completed status refreshes; no overlapping refresh queue is created. A whole-refresh deadline is 55 seconds. A failed/cancelled refresh preserves prior data and marks it stale; partial refreshes are labeled.

## Maps, events and logs

- Game Maps is the default view, with Infrastructure and All alternatives. Configured Battlegroup set/ServerStats relationships establish game maps. Known inspected role labels classify database/message-queue/Director/operator services. Unrecognized resources remain visible under All; name prefixes alone do not establish a type.
- The selected-instance panel shows identity, pod, state, request, limit, usage, minimum instances, restarts and warnings, with separate log/event navigation. Filters preserve selection and pending resource drafts.
- Game-map editing requires a supported authoritative set/resource version and available original resource quantities. Apply requires a valid real change; unavailable/malformed quantities, request above limit, invalid minimum count and infrastructure/unknown resource edits fail closed. Existing JSON Patch identity/version tests, dry-run, rollback and verification remain in place.
- Kubernetes events retain first/last observation, count, reason, object, full message and current status. Identical event identities/messages are grouped. Current object problems are actionable; recovered objects are historical; missing object status is unconfirmed. Historical events remain in the bounded full events view and selected wrapped/copyable details.
- A past OOM termination on an otherwise running/ready pod is historical; an active crash/readiness/allocation problem remains visible. Event correlation describes current object relevance, not proof that an old event caused a present failure.
- Log source, search and explicit severity filters are visible. Recognized severity markers are differentiated; unrecognized lines remain available under All severities. Source timestamps are retained and retrieval time is labeled separately. Long lines wrap; selected text can be copied. The tail is bounded to 60 KB. Cancellation stops live tail; failures label and retain previous text. Auto-scroll follows only while the user remains near the end.

## Backups and administrator workflow

- Official database backup/import behavior remains unchanged, including stopped-only restore. Inventory summary distinguishes nonempty database artifacts from configuration copies and diagnostics, showing time, size, identity and latest operation result. An unchecked inventory and an empty database inventory are explicit.
- Verified downloads receive an identity/size/SHA-256 receipt. The newest matching local database copy is rehashed on an explicit inventory refresh before it is described as verified. Missing, changed or mismatched files fail this check. Nonempty remote inventory and local byte verification do not prove database restorability.
- Common OneDrive/Dropbox/Google Drive/iCloud destinations receive a sensitive/large-archive warning; paths are not automatically changed. No production backup was created to exercise this feature.
- `Launch as Administrator.cmd` requests normal Windows UAC for the Live manager under the same account. An already elevated manager reuses the existing fixed Hyper-V command directly; the narrow helper remains for standard-user operation. The app manifest stays asInvoker, preserving unelevated Demo. No permissions, credentials or Funcom installation files are changed.
- Tools explains administrator access and manual unknown-outcome acknowledgment. Acknowledgment permits new mutations after independent review; it does not repair or prove the earlier result. No guard is cleared automatically.
- Search placeholders, focus borders, consistent disabled controls, wrapped technical details and minimum-window scrolling preserve the dark/gold design.

## Source components

| Area | Principal files |
|---|---|
| Numeric/category rules and shared operational presentation | `src/Arrakis.Core/PresentationRules.cs`, `Models.cs`, `SettingCatalog.cs`, health facade in `Operations.cs` |
| Editor controls and state | `src/Arrakis.Desktop/Controls/ProposalSlider.cs`, `ViewModels/ConfigurationEnhancements.cs`, `ConfigurationViewModel.cs`, configuration XAML/code-behind |
| Dashboard/history/diagnostics/maps/backups | `Controls/ResourceChart.cs`, `ViewModels/ShellEnhancements.cs`, `ShellViewModel.cs`, native view XAML/code-behind and theme |
| Installed adapters | `KubernetesParser.cs`, read-only discovery in `LiveServerGateway.cs`, existing Hyper-V helper in `WindowsHost.cs`, `BackupVerification.cs`, verified download receipt in `DatabaseBackups.cs` |
| Isolated infrastructure/testing | `SimulatedServerGateway.cs`, `EnhancementRegression.cs`, `NativeEnhancementRegression.cs`, extended native recording fixture and suite registration |
| Packaging/docs | Version metadata, version-specific build/UI scripts, administrator launcher, README/changelog/architecture/testing/release notes |

`ConfigurationDraft.cs`, `Contracts.cs`, `ConfigurationFiles.cs`, `ConfigurationSaveScript.cs`, `ConfigurationSaveTransaction.cs` and `SshTransport.cs` retain their v1.0.2 implementations. OperationCoordinator retains its durable guard behavior; only the adjacent health-reporting facade changes.

## Validation and evidence

The complete suite contains **129 tests**: all 90 baseline tests plus 25 rules/adapter tests and 14 additional native WPF workflows. This includes **23 native WPF workflows** overall. The baseline BusyBox 1.37.0 tests execute the real script in isolated containers with no production mounts; all other mutation tests use temporary fixtures and recording simulated gateways.

Native coverage includes actual WPF Slider UI Automation/keyboard actions, exact numerical entry, category/file ComboBoxes, modal approval/cancellation, raw/visual modes, VM/battlegroup buttons, map review/payload, source/severity/search, cancellation, resize, keyboard traversal and 1040 x 700 layouts. Screenshots at 96/120/144 render DPI exercise 100/125/150 percent raster rendering. The machine's OS scaling is not changed; physical mixed-DPI monitor transitions remain manual acceptance.

Final delivery gate: **129/129 passed**, **23 native WPF workflows passed**, **zero Release build warnings/errors**, successful self-contained publish and six-page executable smoke with **no WPF binding errors**. No tests were skipped. The published application DLL matches the win-x64 Release build byte-for-byte.

Evidence paths below are relative to the project root:

| Evidence | Path |
|---|---|
| Complete named test results | `artifacts/tests/20261023-132416-2a86c7/results.json` |
| Build/publish/test output | `artifacts/v1.1-delivery-validation.log` |
| Published executable smoke result and all six pages | `artifacts/ui-20261023-132456/` |
| Native workflow screenshots/transcripts | `artifacts/tests/20261023-132416-2a86c7/native-editor/` |
| Exact slider draft and approval/cancellation evidence | `native-editor/v11-slider-precision/` within the test directory |
| Dashboard controls, resource charts and health | `native-editor/v11-vm-controls/dashboard.png` and `history-and-health.png` |
| Maps, events and backup summary | `native-editor/v11-map-modes/maps.png`, `v11-diagnostics/events.png`, `v11-backups/backups.png` |
| Minimum layout / 125 / 150 percent render scaling | `native-editor/v11-layout/minimum-1.00.png`, `minimum-1.25.png`, `minimum-1.50.png` and `scaling.txt` |
| Consolidated delivery/preservation evidence | `artifacts/v1.1.0/acceptance.json` |
| New package checksums | `artifacts/release/SHA256SUMS-v1.1.0.txt` |

Executable FileVersion is 1.1.0.0. Its informational ProductVersion is `1.1.0+51a0cbb47c99686f0a67606da86811e25771af5f`; the suffix is the unchanged base commit recorded by the SDK, not a new release commit. Executable SHA-256: `29c38c3765435e979a8cb1385274fd8695ea90fba08e1c1f07feaae207f4c91b`. The folder includes .NET 10 runtime dependencies, both launchers, README, changelog and release documentation, with no INI fixtures, credentials, database files or diagnostic logs.

All 412 files in the original v1.0 manifest, 413 in v1.0.1 and 414 in v1.0.2 match their existing manifests. The source audit covers all 78 eligible project files; credential-pattern matches are synthetic redaction tests or ordinary source identifiers, not live credentials. Generated/history/tool traces remain excluded from Git and publication. The staging index remains empty and baseline refs remain unchanged.

## Known limits

- Live v1.1 mutations, UAC interaction, backup restoration and physical monitor/DPI transitions were not executed during development. The prior user-confirmed v1.0.2 live acceptance is separate evidence.
- Charts need actual observation time to fill their window and reset when the connection changes or the application closes. Unavailable metrics remain gaps. They do not predict future capacity.
- Unknown workload labels/schemas remain conservative and visible under All. Event/object correlation does not establish event causality, and events without a current object are unconfirmed.
- Settings without verified bounds/defaults retain typed controls/Unknown labels. No new limits, defaults or undocumented effect directions were invented.
- Verifying the newest local backup can take time for a large archive; it is cancellable. Backup listing/hash verification is not a test restore.
- Cloud-folder warnings recognize common path names; customized synchronization roots may need manual review, and a similarly named unsynchronized folder may also be warned about.
- The application remains unsigned. Existing requirements for Windows OpenSSH, Hyper-V authorization, reviewed Funcom CLI/schema and strict SSH pins remain in force.

## Manual acceptance order

1. Start Launch Demo.cmd unelevated. Check all pages, keyboard navigation, resize/minimum size and 125/150 percent Windows scaling. Switch simulated failure scenarios and inspect state-aware controls, warning navigation and charts.
2. In Demo, select LootRespawnSpeed, type `0.375`, focus the slider and change tabs: precision must remain exact and the document must remain clean until Add. Move the slider explicitly, Add, filter categories/search, and review. Confirm only the intended line differs. Cancel first, then use a simulated save/reload. Check dirty file-switch No/Yes and raw-removal protection.
3. Close the old Live manager only when idle. Keep existing preferences/history/guards. Use Launch as Administrator.cmd under the same Windows account, accept UAC, and perform read-only monitoring first. Verify Hyper-V state versus SSH reachability, core/map/service health, units, storage and last-refresh labels. Let history accumulate at the configured interval.
4. Load live INIs read-only; test categories, typed/slider proposals, tabs and exact diff, then Cancel. Confirm the required custom section and DifficultyLevel=Custom remain present. Do not acknowledge an existing unknown outcome merely to proceed.
5. Inspect Game Maps/Infrastructure/All, selected-instance logs/events, recovered warnings and bounded/cancellable log tail. Refresh official backup inventory and inspect categories/summary/destination warnings. Do not create a production backup solely for UI acceptance.
6. When ready for a live save, make one reversible INI change, approve only its exact one-line diff, reload/verify, then review/save the exact reverse change. Keep rollback copies; do not restart merely to test file saving.
7. Schedule disruptive acceptance separately: battlegroup/VM stop/start, resource reconciliation and official backup/restore require a maintenance window, explicit reviews and independent recovery preparation. No disruptive acceptance is automated by this release.

## Production and Git preservation

No real server connection or production-changing operation is used in v1.1 development/tests. No VM/battlegroup lifecycle operation, INI/map/database/network change, update, restore, credential rotation, safety acknowledgment or Funcom script edit is executed against production. The published smoke mode forcibly uses Demo with an isolated data directory. Tests and screenshots contain simulated/sanitized data only.

The existing v1.0/v1.0.1/v1.0.2 release folders and checksum manifests are retained and checked for exact preservation. v1.1 is published separately at `artifacts/release/ArrakisServerManager-v1.1.0-win-x64/ArrakisServerManager.exe`, with `SHA256SUMS-v1.1.0.txt`. Keep adjacent runtime files. Source changes remain unstaged/uncommitted on feature/v1.1; no v1.1 tag or push is created.
