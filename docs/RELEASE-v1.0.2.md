# Arrakis Server Manager v1.0.2 — configuration editor integrity

Focused configuration document, selection and reviewed-save correction. v1.0/v1.0.1 outputs are retained. Production is read-only throughout development; no live configuration save, restore, lifecycle change, map edit or automatic safety acknowledgment is used for testing. No commit or push is authorized.

## Reproduced root cause

The unchanged v1.0.1 editor was exercised with real WPF controls and a simulated gateway. Evidence: `artifacts/tests/20261022-222013-784613/native-v1.0.1-reproduction.json`. Before saving, the active custom-settings draft was nonempty and dirty. After a successful simulated save, it had the same filename, **zero text length**, and remained dirty. One unrelated discard/switch confirmation occurred. A second native Review & Save opened the deletion preview.

Exact chain:

1. Load registered ConfigFile records and bind ComboBox.SelectedItem TwoWay to SelectedFile.
2. Visual Add to draft changes RawText; the original ConfigFile is retained as its baseline.
3. Save captures original/draft and opens the review. After confirmation it calls the save service.
4. On successful return, `Files[index] = saved` replaces the currently selected ComboBox item **while the old draft is still dirty**.
5. WPF selection invalidation sends a transient null to the side-effecting SelectedFile setter. That setter treats this programmatic null as a requested file switch and asks to discard.
6. Choosing Yes sets selectedFile to null and assigns `RawText = value?.Text ?? ""`, emptying the document.
7. The save callback directly assigns selectedFile=saved and raises notifications, but never restores RawText. File identity and text are now inconsistent. Dirty remains true because empty text differs from the saved snapshot.
8. The next Save captures empty text. v1.0.1's changed-known-setting validation did not reject an empty document, so the large deletion diff represented a genuinely dangerous next payload, not merely a cosmetic preview issue.

Thus the reproduced switch warning occurs in the **successful-save completion path**, under the operation gate, explaining Operation running during that unexpected prompt. Independently, the background refresh timer could run during an ordinary review modal because the review had no preparation lease. Both sources are corrected. This report does not claim that the user accepted or wrote the dangerous production payload.

## Corrected lifecycle

- A stable ConfigurationFileChoice contains only name/path. It is separate from original/proposed bytes and is not replaced after saving.
- A ConfigurationDraft owns cloned original bytes, one draft text, document ID/revision and explicit visual/raw intent. Visual and raw controls share this document; changing tabs has no initialization/clearing behavior.
- File selection is OneWay. Only a committed picker interaction or explicit keyboard file request calls RequestFileSelection. Programmatic/null updates do not select or clear a document. Picker resynchronization runs after native close/selection processing so No/Cancel restores the actual control as well as the model.
- Genuine dirty switches ask once; No/Cancel preserves identity and every draft byte; Yes adopts the requested original and resets dirty state. Reload has a separate explicit discard question and replaces state only after all new snapshots validate.
- Settings rows remain stable during edits; updating a value no longer clears/reselects the entire visual grid. Raw TextChanged is an explicit focused-editor input path, not a TwoWay binding setter.
- Successful saves advance only the snapshot/draft baseline. No Files item replacement, null-file setter or empty-document intermediary occurs.

## Fail-closed review and save

Original snapshots must have a supported filename, matching absolute target path, valid hash/encoding, valid INI structure and bounded byte size. Empty, malformed or unexpectedly truncated documents are rejected. UserServerCustomSettings.ini must retain its exact required section and exactly one DifficultyLevel=Custom.

For visual changes, the explicit intended setting operations are replayed onto the original bytes. The replay must equal the entire proposed encoded file, so unrelated settings, comments, blank lines, unknown keys and line endings cannot disappear or change. Only catalog settings can be applied visually. UTF-8/UTF-16 byte order/BOM and LF/CRLF are preserved.

For intentionally broader raw edits, structural/required-entry protection still applies. Removing existing keys, sections or comments is rejected by ordinary Save until a separate **Review raw removals** dialog is accepted by typing the exact filename. Authorization binds to the exact draft hash/revision and is invalidated by further edits. A final complete Save review is still required. Empty/truncated protected structure cannot be authorized this way.

ConfigurationReview freezes cloned original/proposed bytes, target, document ID/revision, intent and full diff. Approval is checked again before dispatch; the actual service payload is a defensive copy of those frozen bytes. The review diff is computed from that same payload and is never silently truncated. Service/transaction-level structural validation independently rejects empty or damaged replacements before backups or remote command execution. Returned saved identity/hash/bytes must match the approval or the outcome remains uncertain.

The one-line fixture preview is exactly:

```diff
- LootRespawnSpeed=0.100000
+ LootRespawnSpeed=0.200000
```

The section header, DifficultyLevel, other values/comments/blank lines and unknown fixture key remain byte-identical. The native recording gateway compares every actual payload byte with the intended complete file.

## UI review versus execution

Configuration discard/reload/preview/save/removal dialogs hold a preparation lease with a Reviewing… label, not Operation running. Background polling, connection switches and competing configuration actions cannot run in that interval. There is no remote write before the final positive confirmation. Cancellation preserves the draft and sends no write. After approval, the operation gate owns the actual save; editor controls remain frozen until verification completes. v1.0.1's durable unknown-outcome guard, bounded BusyBox lock, conflict checks, encrypted backup, remote rollback, atomic replacement and fresh verification remain in place.

## Changed components

- Core: ConfigurationDraft / ConfigurationReview / ConfigurationIntegrity; reviewed-save contract.
- Desktop: ConfigurationViewModel lifecycle, ConfigurationView native input/selection adapter and binding modes, ShellViewModel preparation lease, typed raw-removal review.
- Infrastructure: independent integrity validation before the existing save transaction. BusyBox writer and reconciliation behavior are preserved.
- Tests: representative sanitized custom-settings fixture, core integrity cases, native WPF interaction/modal/recording-gateway cases, and retained v1.0.1 regressions.
- Packaging: version 1.0.2, separate release/manifest/Demo data paths; no overwrite of prior release folders.

Primary source files:

| File | Change |
|---|---|
| `src/Arrakis.Core/ConfigurationDraft.cs` | Authoritative draft, frozen review and integrity validation |
| `src/Arrakis.Core/Contracts.cs` | Reviewed-save contract |
| `src/Arrakis.Desktop/ViewModels/ConfigurationViewModel.cs` | Selection, reload, editing, preview and save lifecycle |
| `src/Arrakis.Desktop/Views/ConfigurationView.xaml` and `.xaml.cs` | OneWay bindings and explicit native input/selection adapter |
| `src/Arrakis.Desktop/Views/Views.cs` | ConfigurationView constructor moved into its own code-behind |
| `src/Arrakis.Desktop/ViewModels/ShellViewModel.cs` | Preparation lease and isolated gateway injection for native tests |
| `src/Arrakis.Desktop/ReviewWindow.cs` | Exact-filename authorization for raw removals |
| `src/Arrakis.Desktop/MainWindow.xaml.cs` and `ViewModels/Binding.cs` | Raw-removal interaction contract and dialog |
| `src/Arrakis.Infrastructure/LiveServerGateway.cs` and `ConfigurationSaveTransaction.cs` | Independent validation before the existing remote writer |
| `tests/Arrakis.Tests/ConfigurationIntegrityRegression.cs` and `NativeConfigurationRegression.cs` | Core and native WPF regression coverage |
| `tests/Arrakis.Tests/Program.cs`, `.csproj`, fixtures and existing fake interaction implementations | Suite registration and representative isolated input |
| `src/Arrakis.Desktop/UiSmoke.cs`, `Directory.Build.props`, `MainWindow.xaml`, build/UI scripts and documentation | Smoke adaptation and separate v1.0.2 packaging |

## Regression coverage

| Requested case | Evidence |
|---|---|
| Load all three files | Native picker sequence; validated originals |
| One-line visual change | Exact two-line diff; full payload byte comparison |
| Review without switch | Zero switch prompts across repeated successful native saves |
| Genuine dirty switch / cancel / confirm | Real picker selection and modal answers; actual selection, document ID/text and dirty assertions |
| Visual/raw modes | Native tab selection, raw text input, shared values and unchanged text/identity on mode-only changes |
| Reload dirty draft | Cancel retains draft and avoids fetch; Yes adopts verified current snapshot |
| Headers/required entries | Protected custom section and DifficultyLevel checks; malformed/duplicate/truncated rejection |
| Comments/unknowns/encoding/endings | Representative 174-line source copy plus fixture-only unknown key; UTF8/UTF16 BOM and LF/CRLF byte assertions |
| Empty/truncated replacement | Rejected before native final review and before lower transaction/write delegate |
| Unrelated visual changes | Replay rejects extra keys, missing entries and comment changes |
| Preview/payload equivalence | Frozen bytes/hashes, defensive-copy and stale-revision checks; actual native save delegate capture |
| Final review cancellation | Zero save calls and unchanged fixture server bytes; dirty draft retained |
| Review/save races | Native modal/background refresh, selection/raw edits and mode/connection attempts; delayed in-flight save |
| Reopen after save | New original and draft equal saved bytes; second diff uses updated baseline |
| v1.0.1 safety | Existing full regression suite, including actual isolated BusyBox locking/failure/rollback cases, retained |

Native tests use WPF automation peers for navigation, file items, buttons and tabs; real focus changes and real modal WPF review dialogs run through the dispatcher. The server gateway is isolated and records payloads. The fixture was obtained read-only and screened for credential keys before inclusion; it is not packaged as live game configuration in the application.

## Safe manual acceptance

1. Close an older manager when idle and open v1.0.2. Keep existing preferences/history and safety guards. Review any unresolved prior outcome manually; do not reset state or restore a backup simply to bypass it.
2. Load current UserServerCustomSettings.ini. Confirm file identity, required header and DifficultyLevel=Custom. Change only LootRespawnSpeed from its current 0.100000 to 0.200000 and click Add to draft.
3. Switch visual/raw modes and verify that the full file remains present. Review & Save must show only the two lines above and no switch prompt. First choose Cancel; verify the server did not change and the draft remains.
4. When ready, review the same exact diff and confirm. Expect a verified successful save and a clean draft without a switch/discard question. Reload and verify the required entries and all other content remain present.
5. Review/save the reverse one-line change to the exact original representation, reload, and compare original bytes/hash where practical. Do not restart the VM or battlegroup merely to test file saving. If any unrelated diff, invalid state or unknown outcome appears, stop and retain the evidence rather than confirming or retrying blindly.

## Validation and production preservation

The final `scripts/build.ps1 -Publish` gate passed: **90/90 tests**, **zero build warnings/errors**, and a successful self-contained Windows x64 publish. This includes all 73 existing tests, eight additional core-integrity tests and nine native WPF workflow tests. The native subset is also independently runnable with `--editor-native`.

Evidence, relative to the project root:

- Full named results: `artifacts/tests/20261022-225725-94eaa8/results.json`.
- Native controls, modal screenshots and dialog transcripts: `artifacts/tests/20261022-225725-94eaa8/native-editor/`.
- Exact one-line review: `native-editor/one-line/dialog-1-review.png` and `dialogs.json` in that test directory. `owner-during-review.png` shows Reviewing configuration before submission; `saved.png` shows the retained complete document after save.
- The unchanged v1.0.1 reproduction is retained at `artifacts/tests/20261022-222013-784613/native-v1.0.1-reproduction.json`.
- Published executable smoke: `artifacts/ui-20261022-225755/result.json` reports six rendered pages, minimum window size 1040 x 700, and no binding errors. Save error/history screenshots are retained there too.
- Release: `artifacts/release/ArrakisServerManager-v1.0.2-win-x64/ArrakisServerManager.exe`, ProductVersion 1.0.2 / FileVersion 1.0.2.0. Keep its adjacent files; .NET and Windows Desktop runtime 10.0.13 are included. The published application DLL matches the win-x64 Release build byte-for-byte.
- Package checksums: `artifacts/release/SHA256SUMS-v1.0.2.txt`. Earlier release folders and checksum manifests are retained.
- Consolidated acceptance evidence: `artifacts/v1.0.2/acceptance.json`.

Read-only SSH SHA-256 checks before and after development are identical:

| Production file | SHA-256 |
|---|---|
| UserGame.ini | `557b4f8761145008f2caa952f70a2fbc12375690a141a54c20bf39daf3cc6210` |
| UserEngine.ini | `d1d50cd60fa170e072b2f6389a8349db02a70e74960bf9e31ef961d61536055d` |
| UserServerCustomSettings.ini | `b8bbf89c42b0f07b6b91ae41a389bd0fe788924ca2143f3aacbb7f6842b725b1` |

Local history remains `43ace9446b8217fd52b0708f77e06d505bed508ed4dfa8103fd28443f38705fd`; preferences remain `ee0cdda58236239ea76ab44bb7d73a6728b466a6f82399e97b96c697979b2ce5`. Both are byte-identical to the patch-start baseline, so the current safety state was not cleared. Test data and writes were isolated; the release contains no INI fixtures, private keys, preferences or history.

No production file was written or restored; no VM/battlegroup restart, Kubernetes mutation, Funcom installation change or production-changing test was executed. Live save acceptance is deliberately left to the manual procedure above. Source remains uncommitted and unpushed.
