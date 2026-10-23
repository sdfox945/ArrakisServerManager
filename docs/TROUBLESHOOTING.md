# Troubleshooting

## Hyper-V information says Unavailable

The current non-elevated Windows account may lack permission even though the Hyper-V module is installed. The app still reads Windows metrics and can connect directly to a trusted VM address. In Tools, use **Read Hyper-V details with UAC** for a narrow read-only snapshot, then save the discovered address if needed. For regular automatic VM information, configure the account's Hyper-V authorization through Windows administration and sign in again. The app never changes group membership or networking automatically.

VM start and shutdown have a separate UAC workflow. Declining UAC does not launch a command. A timed-out elevated operation can still be running; inspect Hyper-V before acknowledging the unknown outcome. Graceful shutdown never falls back to power-off. Emergency force power-off has a separate data-loss confirmation.

## SSH fails

Save the VM IPv4 override when discovery cannot read Hyper-V or a recent Funcom transcript. Confirm the VM is running and the existing key path is correct. The app uses BatchMode, IdentitiesOnly, connection/keepalive deadlines and strict host checking. It will not prompt for a password or generate a replacement key automatically.

For a new identity, compare the displayed SHA-256 fingerprint independently with the VM console (`ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`) before trusting it. Keyscan is only a candidate identity. Existing identities in the app or Windows SSH known_hosts cannot be replaced through the app. Investigate changed keys independently; do not disable strict checking as a workaround.

## CLI compatibility changed

Monitoring may continue after a Funcom update while automatic changing actions are disabled. Compare the new script with the reviewed command contract and update/test the adapter before changing the pinned digest. Do not simply replace the hash to bypass the check. Funcom's interactive editor is available for supported manual operations. Unknown map/PV layouts are never guessed.

## A map remains Allocating or restarts

Search its exact map name, inspect configured request/limit, usage, restart count and errors, then open selected-map logs and recent events. OOMKilled indicates a container memory termination; FailedScheduling with insufficient memory indicates the request cannot be placed. Host, guest and swap pressure are separate measurements. Increasing a limit does not create physical memory. Review the request/limit/minimum changes together and apply only after confirmation. The application does not silently change any allocation.

Configured values are authoritative spec values. Existing pods may retain old allocations until reconciliation or an explicit restart. Unknown quantities, map variants, or advanced options should be edited in Funcom's editor. A blank/Not set minimum means the installed default is unknown, not zero.

## Configuration save conflict or failure

Reload the current files and reapply your draft after reviewing the differences. The app refuses a changed original, an unsupported path, ambiguous key or incompatible CLI. It keeps encrypted local rollback files under the configured backup directory's Configuration subfolder and private remote rollback files under UserSettings/.arrakis-backups. A failed/cancelled transport may have completed remotely: reload and compare before retrying.

Restore an `.asmconfig` file from Server Configuration. It must match the exact loaded target and Windows user. Restore loads a draft; it does not write the server until Review & Save. Windows-user DPAPI backups are not portable disaster-recovery artifacts: keep official database backups and independent host backups too. Unknown encodings are rejected; UTF-8 and BOM-marked UTF-16 are preserved.

## Unknown outcome / application interrupted

The safety guard survives application restarts and history retention. Refresh, inspect the relevant Hyper-V state, Battlegroup spec, DatabaseOperation, file or official console, and determine whether the command is still running. Only after resolving it use **Acknowledge reviewed unknown outcome**. Do not submit an automatic retry based on an empty output or dropped connection.

## Database backups / restore

The app distinguishes database dumps, configuration copies and diagnostic bundles. In-game Base Reconstruction settings are unrelated. Stop the battlegroup before restore; backup is preferably done stopped too. Backup success requires an actual nonempty artifact. Downloads verify SHA-256 and byte count; a mismatch retains `.unverified`, and an existing local file is never silently replaced. The supported import uses a selected artifact already in Funcom's remote backup directory. Uploading arbitrary local dumps is not automated in this release; stage them through supported tools and refresh before selecting.

## Logs and exports

Live tail replaces a bounded tail at the configured interval while the diagnostics page is open. It is not a lossless central log collector. Copy selected text with Ctrl+C, or copy filtered lines. Complete bundles include both official archives without extraction. Those archives may contain sensitive data even though displayed operation output is sanitized. Inspect before sharing. No logs are sent externally by the app.

## Demo and data locations

Demo mode is visibly labeled and isolated; unavailable external web/editor tools report that they are unavailable in Demo. Failure scenarios are selectable in Tools. Normal preferences/history/SSH pins live under `%LOCALAPPDATA%/ArrakisServerManager`; demo artifacts are under its Demo subtree. `--data-dir <absolute path>` isolates acceptance runs. Back up preferences separately if moving computers; private keys are referenced, never copied into release output.

Keyboard: Ctrl+R refreshes; Ctrl+S reviews/saves the configuration draft; native grids, menus, editors and dialogs support Windows keyboard navigation. If a window was saved off-screen, delete only the window-placement fields in preferences.json or move the window using Windows shortcuts. The app clamps restored placement against the virtual desktop.
