# Architecture

`Arrakis.Core` contains domain snapshots, contracts, settings metadata, INI editing, validation, health rules and the serialized operation coordinator. `Arrakis.Infrastructure` implements process execution, Windows metrics, narrow Hyper-V elevation, strict SSH, Kubernetes JSON parsing, persistent-volume discovery, configuration/database workflows, preferences/history and simulated adapters. `Arrakis.Desktop` is the WPF shell, six views, theme, confirmation/review dialogs and MVVM view models. `Arrakis.Tests` is a dependency-free STA regression executable with a dispatcher for view-model tests.

Composition is explicit constructor injection. `IServerGateway` is the complete desktop infrastructure boundary. `IProcessRunner`, `IPreferencesStore`, `IHistoryStore` and `IUserInteraction` support isolated tests. No third-party runtime dependencies or proprietary Funcom files are embedded. CommunityToolkit is optional and was not needed for these small observable/command helpers.

## Discovery and read flow

Windows memory/CPU uses native kernel APIs, avoiding CIM elevation. Client detection checks the known game executable name and never assumes a process is connected to the world. Hyper-V reads use encoded PowerShell and structured JSON. When access is denied, the address may be resolved from a recent Funcom transcript opened with read/write sharing, or the saved manual IPv4 override. The source is treated only as an address hint; strict SSH identity validation still applies.

Guest readings use `/proc/meminfo`, sampled `/proc/stat` and `df`. Kubernetes JSON is curated before transfer to avoid unnecessary environment variables and service tokens. Maps join authoritative Battlegroup sets with ServerStats/pod identity and optional metrics. Configured resources are distinct from observed usage; existing pod limits may lag spec reconciliation. Unknown/default metrics are displayed as Unavailable/Not set, not estimated. Events and log tails are bounded.

The single-battlegroup CLI contract is checked immediately before mutations, including the matching namespace count. The reviewed CLI SHA-256 is checked against the installed symlink target. A mismatch blocks automatic CLI actions while monitoring remains available.

## Mutation boundary

v1.1 adds shared OperationalRules for verified VM/battlegroup availability and a preparation lease for lifecycle/map reviews as well as configuration review. The existing action adapters remain the only mutation implementation. The administrator launcher requests Windows UAC explicitly; asInvoker remains the application manifest, so Demo does not require elevation.

PresentationRules also provides range-derived slider increments, qualified workload health and bounded in-memory resource samples. ProposalSlider emits editing intent only for native user/automation choices, never for a binding/range update. Document categories are stable identities computed from configured recognized entries and search, with absent supported entries kept separately available to add. The v1.0.2 authoritative document and frozen save transaction remain unchanged.

Event parsing preserves first/last observation and recurrence, correlates current object state and groups duplicate messages. Historical/recovered and unconfirmed events remain in full diagnostics; historical container OOM evidence does not by itself mark a recovered pod failed. Known role metadata/configured relationships classify workloads; unknowns remain visible under All. Verified database downloads add local identity/hash receipts; inventory refresh rehashes the newest matching copy before claiming local verification.

Every UI mutation requires confirmation. Configuration/map changes use a detailed review window. A process argument list transports PowerShell/SSH parameters; PowerShell literals and POSIX shell words are escaped independently. User-facing fields cannot become command verbs. Kubernetes names, artifact names, IP addresses and paths are validated. Private configuration bytes are sent over SSH stdin, never as logged command arguments.

The operation coordinator admits one foreground/background infrastructure operation at a time. A durable safety record is written before mutations and cleared only on a known completion/failure. Unknown transport outcomes retain the lock across restarts. The latest guard survives bounded history retention. Read-only polling does not erase mutation output or crowd out successful operations. Local user review explicitly clears unresolved guards; the app never retries an uncertain mutation automatically.

PowerShell elevation runs only an application-generated Hyper-V command and its state poll. It uses a random local receipt, no general elevated shell, and a timeout. There is one UAC request per lifecycle workflow. Graceful shutdown uses the default `Stop-VM -Name` behavior; forced power-off is a separate `-TurnOff -Force` command. A hung elevated command is not forcibly terminated or assumed rolled back.

## Configuration transaction

The server PVC is selected by `role=igw-server`, checked against its bound PV, and resolved to `Saved/UserSettings`. The conventional `/home/dune/server/...` path is never assumed: it does not exist on the inspected installation. Only the three discovered files can be saved. Symlink targets and unrecognized volume roots fail safely.

INI documents retain original lines, comments, newlines, unknown keys and disabled settings. Visual changes modify one unique scalar key; duplicate scalar definitions require the raw editor. Existing unrecognized values are retained; only changed known values are range/type validated. Shipped template values are labeled separately from unknown engine defaults, so Reset is disabled without a verified default.

The v1.0.2 editor separates stable file-picker choices from document snapshots. A ConfigurationDraft owns original bytes, authoritative text, identity/revision and visual/raw intent. Explicit picker commits own file switches; programmatic binding updates never clear a draft. Visual intentions must replay to the entire proposed encoded payload exactly. Required custom-settings structure, valid snapshots and nonempty/nontruncated proposals are checked in the UI and save service. Broader raw removals require typed filename authorization bound to the exact draft, followed by final review. ConfigurationReview freezes the exact bytes, target, revision and diff; the service receives those same bytes after approval. Preparation leases suspend competing refreshes/actions during modal review without reporting a remote operation as running. The operation coordinator takes over only after final confirmation.

Before writing, compare original bytes with a fresh remote read and save a DPAPI-encrypted local rollback copy. The v1.0.1 transaction executes a tested BusyBox-compatible script: open the persistent lock inode on FD 9, retry nonblocking exclusive `flock -x -n 9` for at most 10 seconds, recheck the original, copy and verify the remote rollback artifact, create/verify a same-directory temporary file, preserve numeric owner/mode through `stat` plus `chown`/`chmod`, recheck the original and atomically rename. A failing in-lock final verification restores the original atomically only if the live digest still equals this exact proposal; an external change is never intentionally overwritten by rollback. Copies are retained. Never delete the lock inode to release a lock; kernel locks release with the descriptor/process.

The exit trap emits a request-bound stage/terminal receipt after rollback and cleanup. A separate bounded (15-second) fresh SSH read classifies exact original/proposal bytes. A conclusively terminal failure with verified original bytes is Failed, without a new unknown guard. Lost transport without a terminal receipt remains Unknown even if an instant read matches the original: the remote writer could still be running. Failed readback and unrecognized post-replacement contents also retain the interlock. Existing locked history is never auto-acknowledged. `ConfigurationSaveScript` generates the real command, and `ConfigurationSaveTransaction` owns reconciliation; both are exercised with actual BusyBox in isolated containers.

Application writers coordinate through flock; other editors should not be used concurrently with a save. The final hash check is immediately before replacement; no generic filesystem transaction can lock unrelated, uncooperative editors automatically.

Local `.asmconfig` files are Windows-user DPAPI encrypted. Restore decrypts and verifies the backup hash, checks exact target identity, and loads it into a draft. Saving the reviewed draft takes another backup. Server backups stay on the verified volume with restricted directory permissions. Raw plaintext configuration is not added to app operation history.

## Map changes

Only inspected authoritative fields are supported: set resources at `/spec/serverGroup/template/spec/sets/<index>/resources` and MinServers within the Battlegroup-owned Director INI. JSON Patch tests resourceVersion and map identity; Kubernetes server-side dry-run validates admission before application. A private guest rollback record is saved before applying. The app verifies the resulting spec and explains reconciliation/restart behavior; it never patches live pod resources. Unsupported schema/options use the official editor.

## Database and diagnostics

Database actions invoke the inspected CLI rather than modifying PostgreSQL directly. Backup completion also requires a nonempty discovered artifact. Downloads use binary pipes, local `.part` files, size verification and SHA-256. Import validates the selected remote artifact, requires Stopped, passes only the reviewed filename and official confirmation, and requires CLI completion markers. It is never exercised live by the test suite.

Official log exports are downloaded as intact tar.gz archives without extracting potentially unsafe archive entries. Complete diagnostic zip bundles include available status/history and both official archives. Displayed/application logs are sanitized; intact official archives can contain sensitive data and require review before sharing. Retention bounds: 200 application history records including safety state, 150 events, 60 KB displayed log text, bounded per-pod tails.

Demo mode creates no real process, SSH, Hyper-V or web service calls. Its backups/exports use a dedicated Demo subtree. Tests also drive the real adapters with a simulated process transport, separately from the high-level demo adapter.
