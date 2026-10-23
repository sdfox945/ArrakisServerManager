# Arrakis Server Manager 1.0 delivery report

Delivered 2026-10-22 as a runnable native Windows x64 application for manual acceptance. The initial feature set is implemented within the verified interface boundaries below. Production-changing controls remain **awaiting manual acceptance**, as required by the development safety boundary.

## Locations and build

- Project: `C:\Dev\ArrakisServerManager`
- Solution: `ArrakisServerManager.sln` (Core, Infrastructure, Desktop and Tests)
- Executable: `artifacts\release\ArrakisServerManager-win-x64\ArrakisServerManager.exe`
- Demo launcher: `artifacts\release\ArrakisServerManager-win-x64\Launch Demo.cmd`
- Self-contained win-x64: Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 10.0.13 included. Keep the whole folder together; no separately installed runtime is needed.
- Toolchain: Microsoft .NET SDK 10.0.402. Release solution build: **0 warnings, 0 errors**.
- Local Git initialized. No commit, remote repository or push was created; source is in the working directory.
- Unsigned portable release folder; no installer. Build/test/publish and native UI-test scripts are included.

## Validation

**57/57 automated tests passed.** Evidence: `artifacts/tests/20261022-183030-ca4b01/results.json`. Tests cover high-level simulated-server scenarios and real adapter logic driven by a simulated process/SSH/Kubernetes transport. Positive/negative cases include map application/readback and commit races, configuration conflicts and rollback, backup completion/artifacts, stopped-only import, archive integrity, durable unknown outcomes and map drafts surviving refresh.

The published **native executable** launched, rendered all six pages and the 1040×700 minimum layout, checked all three raw-editor files including LF-only input for unwanted changes, and exited with code 0. No WPF binding errors were recorded. PNGs and evidence: `artifacts/ui-20261022-183043/`. Earlier native UI failures were repaired; view-model passes alone were not treated as launch proof.

Read-only live evidence: `artifacts/tests/20261022-181924-55953a/live-read-only.json`:

- Trusted SSH successful, actual Windows/guest metrics available, game client detected.
- Battlegroup Healthy; Kubernetes Nodes Ready; CLI available and matching its reviewed digest.
- 45 dynamically discovered map/service/configuration entries. Resources, optional usage, ServerStats, pod states and events read through JSON.
- All three active INI files read from their verified PVC/PV mapping; reports contain only names, paths, hashes and key counts.
- Running/downloaded revision: 2162777-0-shipping.
- Battlegroup/operator log reads and backup listing successful. No matching existing .backup artifacts were found in that probe.
- Director NodePort discovered as 30893; File Browser at 18888. Both availability checks succeeded. Director port is dynamically discovered.
- Direct Hyper-V queries are denied to the current Windows account. The app reports Unavailable and retains SSH monitoring through address discovery; narrow UAC snapshot/actions are implemented. Live Hyper-V mutations were not exercised.

## Definition-of-done audit

| Item | Implementation and evidence |
|---|---|
| 1. Native launch | Published self-contained WPF executable launched and exited in UI smoke |
| 2. Installation/VM discovery | Installation, key and recent Funcom VM address detected; Hyper-V adapter simulated and installed command syntax inspected; live query permission denied and reported |
| 3. Actual dashboard data | Windows and live guest/cluster readings verified; missing Hyper-V/first CPU sample remains Unavailable |
| 4. Lifecycle controls | Confirmed real Hyper-V/official CLI adapters, async output, deadlines and state verification; simulated changes pass; live mutations pending |
| 5. Map/pod status | Dynamic live discovery plus searchable/filterable grid and ServerStats/pod joins verified |
| 6. Resources/diagnostics | Memory requests/limits/optional usage, restarts, events, OOM/scheduling/pressure warnings implemented and tested |
| 7. Safe map settings | Authoritative schema inspected; CAS/map tests, server dry-run, rollback and readback tested through simulated Kubernetes; official editor fallback wired |
| 8. Three INI editors | Visual/raw, metadata, validation, preservation, diff/undo, conflict detection, encrypted backups, atomic guest save/readback and restore draft implemented/tested; live reads verified |
| 9. Database backup/import | Official workflows, unique names, success markers/nonempty artifacts, size/hash downloads and stopped-only import implemented/simulated; none exercised live |
| 10. Logs/diagnostics | Actual tails/events verified; bounded filtering/copy/history; official exports, tar/gzip checks and complete bundles implemented with fixture/simulation coverage |
| 11. Web shortcuts | Actual Director discovery and both service availability checks passed; Windows shell actions wired |
| 12. Offline/error states | Explicit unavailable states, demo failures, timeout/cancellation, recovery and durable unknown-outcome guard tested |
| 13. Tests | 57/57 pass; clean Release solution build |
| 14. Polished UI | Dark/gold six-page WPF shell, selected navigation, status/progress, reviews, empty states and persisted sizing; rendered PNGs inspected |
| 15. Runnable release | Self-contained x64 executable/runtime folder, docs and demo launcher produced |
| 16. Production safety | Only read-only discovery executed; three INI hashes match initial/final reads; no production-changing controls tested |

## Awaiting live acceptance

VM start/graceful shutdown/emergency power-off and UAC transitions; battlegroup start/stop/restart/update; configuration writes/restores; map memory/minimum changes and reconciliation; database backup/download/import; official export staging/download; networking/password/experimental-swap consoles; official interactive editor. These are implemented and wired with simulation/fixture coverage, without claims of live mutation testing. Native rendering is verified; actual multi-monitor/DPI transitions and all user gestures remain manual checks.

## Limitations and specification differences

- Inspected CLI has no read-only online update check. The app compares running/downloaded revisions and provides a separate confirmed download/apply action.
- Automatic changes require the reviewed CLI and one unambiguous battlegroup. Unknown map/PV layouts or advanced options fail safely and use official tools. Exact supported contracts are in COMPATIBILITY.md.
- Engine defaults are not independently verified. Template examples are labeled separately; default-reset controls appear only for verified defaults. Unknown values and multi-valued Unreal arrays remain available in the raw editor.
- Networking/password/swap use supervised native consoles. SSH key rotation remains in Funcom's tools because their current host-verification/replacement/recovery behavior needs a separate security review.
- Restore selects a discovered remote dump. Upload/staging arbitrary local dumps is not automated. A signed installer is not supplied; the portable self-contained folder is runnable.
- No client edits, automatic repair, setup/reinstall, character-database manipulation or arbitrary pod patches are included.

## Production and security record

No live VM/battlegroup lifecycle, Kubernetes patch, configuration write, database backup/import, update, networking, credential or save-data change was executed. Read-only SSH/Kubernetes activity can appear in normal guest access logs.

Initial/final INI SHA-256 values match:

| File | SHA-256 |
|---|---|
| UserEngine.ini | d1d50cd60fa170e072b2f6389a8349db02a70e74960bf9e31ef961d61536055d |
| UserGame.ini | 557b4f8761145008f2caa952f70a2fbc12375690a141a54c20bf39daf3cc6210 |
| UserServerCustomSettings.ini | c00fa3e5c6e76390f1ed1335e1537e5b42cc652aae1e34dff0d9fa2edb9129ed |

One early schema-inspection response included a service token in the tool trace. Subsequent queries transfer curated fields. No real token, private key, password, save data or proprietary server script was added to application source/release files; production credentials were not rotated. Treat the development trace as sensitive. Synthetic redaction test strings are fixtures, not credentials.

## Manual acceptance order

Start with the Demo launcher and failure scenarios. Then use Live for read-only dashboard/maps/INI/log/tool checks; cancel a configuration review and verify no save. In a chosen maintenance window, establish and verify a database backup before testing disruptive lifecycle, configuration, resource, update or restore actions. Keep Hyper-V recovery access available. Do not test emergency power-off on a healthy world. TESTING.md provides the detailed sequence.
