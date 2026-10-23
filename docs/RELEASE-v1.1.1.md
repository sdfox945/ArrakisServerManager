# Arrakis Server Manager v1.1.1 stabilization

Implementation delivery record for the focused patch on the completed v1.1.0 working source, developed on `feature/v1.1`. At delivery the source was uncommitted and `main` and annotated `v1.0.2` still referenced `51a0cbb47c99686f0a67606da86811e25771af5f`. The subsequent accepted stable baseline and remaining live-acceptance limits are documented in [ACCEPTANCE-v1.1.1.md](ACCEPTANCE-v1.1.1.md).

## Exact root cause and live evidence

Read-only, curated Kubernetes inspection confirmed that the reported `igw-database-utility` is a **DatabaseDeployment-controlled utility pod**, not a batch Job on this installation. Its metadata reports:

- Role: `igw-database-utility`.
- Controller: `igw.funcom.com/v1`, `DatabaseDeployment`.
- Restart policy: `Never`; restarts: 0.
- Pod phase: `Succeeded`.
- Container termination: `Completed`, exit code 0.
- Ready: `False`, reason `PodCompleted`.

No batch Jobs existed at inspection time. The installed API describes final Job conditions as Complete or Failed; positive/negative Job behavior is therefore tested with isolated fixtures, not a newly created production Job. Logs were not needed to infer the outcome, and no database commands were executed.

Three v1.1.0 paths caused the incorrect presentation:

1. `MapInstance.HasCurrentProblem` unconditionally included `Ready == false`. Successful terminal pods normally stop being Ready, so the completed utility became a current error despite Succeeded/exit 0.
2. The infrastructure role list omitted `igw-database-utility`, while the live pod projection discarded owner references. Neither its explicit role nor its DatabaseDeployment relationship could classify it correctly, leaving Unknown.
3. Dashboard counts reused the same incorrect health predicate, and selected-map details always appended a Current warning prefix even when Error was empty. This produced the bogus unclassified-workload issue and empty warning text.

The no-op work also exposed a separate native binding defect: switching from a choice to a numeric setting cleared the hidden choice ComboBox selection, sending null to SettingValue. v1.1.0 turned that null into an empty proposed value. A native capture reproduced `Current=0.100000`, `Proposed=""`, Add enabled. v1.1.1 ignores non-user null selection updates; deliberately typed empty text still reaches validation.

Read-only discovery records are retained under ignored `artifacts/v1.1.1/live-metadata-discovery.txt` and `live-schema-discovery.txt`. They omit environment variables, credentials and full configuration contents and are not bundled in the release.

## Corrected workload semantics

- Explicit failures and unmet required game-map instances take precedence. A confirmed successful terminal state is evaluated before readiness, so Succeeded/Completed with no contradictory failure is not unhealthy merely because Ready is false.
- Current nonzero exits, current OOM, failed/FailureTarget Job conditions, CrashLoopBackOff, pending allocation, live readiness failures and genuinely unknown status remain actionable. Missing or contradictory evidence is not converted into success.
- Known role metadata, a controlling DatabaseDeployment relationship, or a batch/v1 Job controller can establish Infrastructure. Authoritative configured game-map relationships retain priority. Names/prefixes alone never grant classification; an unclassified successful pod remains visible under All and does not inflate the issue count.
- Pod phase, controller identity, restart policy, current exits/reasons, and Job conditions remain available in wrapped/copyable selected-workload details. Utilities/Jobs have no game-map set index, so game-map resource inputs and Apply remain disabled.
- Job conditions bind to the exact namespace, name and UID. A stale same-name Job cannot clear a pod failure. Job Complete alone cannot hide an actively crashing/unready pod. Confirmed terminal failed attempts belonging to a completed Job become historical, retaining their failed state/details and historical notice.
- Jobs without retained pods remain visible from Job metadata; a Failed Job is not lost merely because its pod was removed. Jobs without conclusive terminal evidence stay nonterminal/unconfirmed.
- Full historical events remain in diagnostics. Events associated with completed/recovered workloads are not promoted into active errors. Current warnings always have explanatory text; healthy completion emits no Current warning entry.
- The same health rule drives notices and headline issue counts. Required replica availability is checked across each configured game map's observed running/ready instances, so successful termination cannot accidentally satisfy an ongoing replica requirement.

## Idle terminology and no-op editing

Idle / On demand requires all of: a verified game-map set, no observed pod, explicit `MinServers=0`, explicit desired replicas 0, Healthy battlegroup and no configured stop. Missing minima, missing replica data, stopped core, real pending/error states and unavailable pod status remain distinct. Local inspection confirmed explicit zero counts for some live maps; maps lacking that configuration are not labeled on demand.

Add to draft compares the proposed value with the **current draft**, not just the saved baseline. Numerically equivalent formats such as `0.100000`, `0.1`, `.1000` and `1e-1` are no-ops. Validated integer formats also normalize. Decimal coefficient/exponent comparison preserves typed precision and large integer distinctions without guessed tolerances, slider rounding or lossy binary equality. Boolean/choice comparisons follow their validated case-insensitive semantics; quoted strings remain case sensitive.

No-op Add is disabled and the handler independently refuses it. A real change, reverse change or explicitly adding an absent supported entry remains possible. Selection/category/mode/slider-focus changes do not alter the authoritative draft. Invalid changed input still uses the existing validation path; this patch does not bypass validation.

The v1.0.1/v1.0.2 configuration transaction, frozen reviewed bytes, required sections, locking, conflict/hash checks, encrypted backups, remote rollback, atomic replacement, verification and durable unknown-outcome safeguards remain intact.

## Patch components

- `src/Arrakis.Core/Models.cs`: workload evidence, terminal-first health and nonempty detail messages.
- `src/Arrakis.Core/PresentationRules.cs`: corrected notices and historical Job-attempt reporting.
- `src/Arrakis.Core/SettingValueComparison.cs`: validated semantic no-op comparison.
- `src/Arrakis.Infrastructure/WorkloadParser.cs`, `KubernetesParser.cs`: owners, exits, Job conditions, historical recovery, required availability and strict idle classification.
- `src/Arrakis.Infrastructure/LiveServerGateway.cs`: retain ownership/restart policy and perform read-only Job discovery.
- `src/Arrakis.Infrastructure/SimulatedServerGateway.cs`: representative completed utility and confirmed idle-map data, isolated from production.
- `src/Arrakis.Desktop/ViewModels/ConfigurationViewModel.cs`, `ConfigurationEnhancements.cs`: no-op CanExecute/handler guard and transient-null proposal protection.
- `src/Arrakis.Desktop/ViewModels/ShellViewModel.cs`, `Views/MapsView.xaml`: consistent instance summary/filtering and wrapped/copyable lifecycle details.
- Regression fixtures/tests: `StabilizationRegression.cs`, `StabilizationAdapterRegression.cs`, `NativeStabilizationRegression.cs`, sanitized `completed-database-utility.json`, test registration/project data copying. Existing native helpers now wait for the real enabled state after a changed input; their prior integrity assertions are retained.
- Version/sidebar/manifest, build/UI scripts, README, changelog and this report: separate v1.1.1 packaging. Previous release outputs are retained.

## Validation

The complete suite has **154 tests**: all 129 existing cases plus 25 stabilization cases. **27 native WPF workflows** are included, four new for this patch. No regression is removed or skipped.

New cases cover the observed completed utility, unknown successful pod, role/controller classification, successful/failed/partial/stale-UID Jobs, Jobs without pods, active crashes, contradictory exits, init failures, unknown/readiness states, historical recovery/events, explicit idle versus unavailable configuration, unmet replicas, empty warning fields, numeric precision/types/culture, no-op command/UI state and absent-setting addability. The actual live adapter is exercised through a recording simulated transport, including its new read-only projection/query.

All mutation tests use fixtures/temporary files or the existing isolated BusyBox containers. Native tests use real WPF controls, UI Automation, focus, ComboBoxes, sliders and dialogs with recording fixture gateways. Production is not an automated mutation fixture.

Final delivery gate: **154/154 passed**, including all 129 prior cases, 25 stabilization cases and **27 native WPF workflows** overall. Release build: **0 warnings, 0 errors**. The self-contained executable smoke rendered all six pages and the minimum 1040 x 700 layout with **no binding errors**. No tests were skipped.

Evidence paths are relative to the project root:

| Evidence | Path |
|---|---|
| Full named results | `artifacts/tests/20261023-154405-079514/results.json` |
| Build/test/publish log | `artifacts/v1.1.1/final-delivery-validation.log` |
| Published executable smoke/screenshots | `artifacts/ui-20261023-154508/` |
| Native completed utility, healthy Dashboard and retained failed Job | `artifacts/tests/20261023-154405-079514/native-editor/v111-completed-utility/` |
| Disabled no-op Add, changed draft and exact initial proposal state | `artifacts/tests/20261023-154405-079514/native-editor/v111-no-op/` |
| Native idle filtering | `artifacts/tests/20261023-154405-079514/native-editor/v111-idle/idle-on-demand.png` |
| Pre-fix transient-null capture | `artifacts/tests/20261023-152615-beddfb/native-editor/v111-no-op/initial-proposal.json` |
| Source audit, exact patch file list and prior-release preservation | `artifacts/v1.1.1/source-audit.json`, `patch-files.txt`, `preserved-releases.json` |
| Consolidated delivery evidence | `artifacts/v1.1.1/acceptance.json` |
| New package manifest | `artifacts/release/SHA256SUMS-v1.1.1.txt` |

Executable FileVersion is 1.1.1.0; ProductVersion is `1.1.1+51a0cbb47c99686f0a67606da86811e25771af5f`. The informational suffix records the existing Git base, not a new release commit. Executable SHA-256: `8a1db5477a8e8c191cdc2ca6357c300ea3b548832ddbdabc1956da8f544dca69`. The application DLL matches the win-x64 Release build byte-for-byte.

The source audit covers 85 eligible project files; matches are ordinary source identifiers and synthetic redaction tests, with no actual credentials found. No INI fixtures, credentials, database files or diagnostic captures are bundled in the 418-file runtime release. All 412 v1.0 files, 413 v1.0.1 files, 414 v1.0.2 files and 417 v1.1.0 files match their prior manifests. The seven protected document/contract/operation/writer/SSH files match the completed v1.1.0 snapshot exactly.

## Safe manual acceptance

1. Close the older manager when idle and launch v1.1.1 under the same Windows account. Retain preferences/history and any unresolved safety guard; do not acknowledge merely to continue.
2. Refresh read-only. Under Infrastructure and All, inspect the completed utility: Infrastructure, Succeeded, DatabaseDeployment owner, exit 0, no current warning and resource editing disabled. The Dashboard must not count it as an unclassified issue. Genuine independent failures must remain visible.
3. Inspect existing historical events without creating faults. Completed/recovered warnings remain accessible as historical. If actual failed/pending workloads exist, their warnings must still be current.
4. Confirm Idle / On demand only for known zero-minimum/zero-desired maps with healthy core. Missing minimum information must remain unconfirmed rather than inferred as idle.
5. Load an INI read-only. Select an unchanged setting: Add is disabled, including equivalent numeric formatting. Check categories, slider focus and raw/visual tabs without changing draft bytes. Propose a different value: Add enables; after adding, the same draft value disables Add again. Review the exact one-line diff and **Cancel**. No live save is required for this acceptance procedure.

Do not restart services, create Jobs, change allocations, restore databases or clear guards to test this patch. Live configuration mutations and disruptive operations remain separate manual acceptance decisions.

## Preservation and limits

Only curated read-only Kubernetes discovery/schema commands were run against production. No VM/battlegroup lifecycle operation, INI write, database operation, Kubernetes mutation, credential change, Funcom installation change or safety acknowledgment was performed. No production Job was created. Job/failure mutations are simulated.

Job relevance requires current metadata; unavailable Job reads cannot establish successful recovery of a failed attempt. Unrecognized workloads remain visible under All. Numeric equality deliberately does not invent an undocumented server rounding tolerance. Existing v1.1 physical mixed-DPI/UAC/live-operation acceptance limits remain unchanged.

The self-contained Windows x64 release is `artifacts/release/ArrakisServerManager-v1.1.1-win-x64/ArrakisServerManager.exe`, with `SHA256SUMS-v1.1.1.txt`. Keep adjacent dependencies. All prior executable releases and checksum manifests remain local and unchanged. This report records pre-baseline delivery; see the acceptance record for the authorized local Git baseline and its scope.
