# Arrakis Server Manager v1.0.1 — configuration save reliability

This is a focused save-path and error-reporting patch. No production save, lifecycle operation, Kubernetes mutation, database restore, credential change, image update or automatic safety acknowledgment was performed during development. No commit or push was authorized or made.

## Root cause and failure ordering

The v1.0 remote command executed `sudo -n sh -c <save script>`. Read-only inspection of that exact context confirmed **Alpine Linux 3.23.4 in the VM host**, with PID 1 `init` and the same mount namespace as the SSH process. This is not a game container. `/bin/sh` and `/usr/bin/flock` resolve to `/bin/busybox`, **BusyBox 1.37.0** (installed build dated 2025-12-16).

The incompatible command was **`flock -w 10 9`**. BusyBox supports `-s`, `-x`, `-u`, `-n`, but not the util-linux timeout option **`-w`**. The installed error was `flock: unrecognized option: w`. The representative official BusyBox image reports the equivalent `flock: invalid option -- 'w'`.

The original order was:

1. Validate CLI/target/settings and compare a fresh original-file hash.
2. Create the DPAPI-encrypted local configuration backup.
3. Start the privileged shell, set umask, open FD 9 on the persistent lock file.
4. Execute `flock -w 10 9` under `set -e` — **the reported failure occurred here**.
5. Only afterward: original hash check, remote rollback copy, temporary write/verification, metadata preservation, final conflict check, atomic replacement and final hash.

Therefore the failing command could create/open the lock file and had already created the encrypted local backup, but could not reach remote backup, temporary-write or target-replacement commands. The target INI was not modified by that failed command. There was no post-replacement rollback in v1.0; its trap only removed temporary files.

The host then converted **every nonzero remote save exit** directly to OutcomeUnknownException, with no fresh readback. OperationCoordinator correctly retained the persistent safety guard for that exception. The protection was conservative but lacked a way to classify a conclusively unchanged, terminated failure. Earlier transport simulations replaced the whole shell operation and therefore missed the BusyBox option incompatibility.

Two further incompatible commands were found before reaching production: BusyBox `chmod` and `chown` do not support GNU `--reference`. Those are also corrected in this patch.

## Corrected transaction

- Preserve the existing preflight conflict/byte-hash checks and encrypted local backup.
- Open the same persistent lock inode on FD 9. Use **`flock -x -n 9`**, retrying once per second for up to **10 seconds**. No unlocked fallback, utility install, stale-file deletion or Funcom image modification. Kernel locks release when the descriptor/process closes.
- Under the exclusive lock: recheck the original hash; create a unique private remote rollback copy and verify its hash; write the proposal to a same-directory temporary file and verify its hash.
- Read mode/UID/GID with BusyBox-supported `stat -c '%a'` and `stat -c '%u:%g'`; apply numeric `chown` and `chmod` and verify metadata. No `--reference` calls remain.
- Recheck the target immediately before atomic `mv`, then verify the final digest while the lock is held.
- On an in-lock post-replacement failure, restore the verified original through a separate temporary file and atomic rename **only if the current target still matches this exact proposal**. Preserve all rollback copies. Refuse rollback over external content.
- Emit a request-bound terminal stage/rollback receipt after cleanup. Make a separate bounded **15-second fresh readback** of the exact file, comparing original/proposal bytes.

Terminal failures with verified original bytes are **Failed**, without creating a new unknown interlock. A verified restored original is reported explicitly. A conclusively updated file with a failing command is labeled Verified updated and warns against blind retry. A pre-replacement external conflict is reported without overwriting it. Lost transport without a terminal receipt remains **Unknown outcome even if an instant read still matches the original**, because the remote writer may still run. Missing readback or unrecognized contents after attempted replacement also retain the guard.

Advisory locking coordinates manager writers. External editors should still not run concurrently with saves; final conflict checks reject observed external edits, and rollback never intentionally overwrites unrecognized content.

## Diagnostics

Save errors now have separate concise summaries and technical details: affected file, failed step, explanation, underlying error, verified file state, recovery action, hashes, terminal/rollback receipt and sanitized transport/readback details. Primary messages do not display a stack trace. Error dialogs expand technical details with wrapping and a copy button. Selecting an Operation History row exposes its complete wrapped/copyable details. Old history entries still load compatibly; no startup acknowledgment or guard reset is performed.

## Production state preserved

Read-only checks at the start of this patch recorded:

| File | Current SHA-256 |
|---|---|
| UserEngine.ini | d1d50cd60fa170e072b2f6389a8349db02a70e74960bf9e31ef961d61536055d |
| UserGame.ini | 557b4f8761145008f2caa952f70a2fbc12375690a141a54c20bf39daf3cc6210 |
| UserServerCustomSettings.ini | b8bbf89c42b0f07b6b91ae41a389bd0fe788924ca2143f3aacbb7f6842b725b1 |

The current LootRespawnSpeed is **0.100000**. The retained encrypted pre-save snapshot from 19:20 contains **0.500000**, hash `c00fa3e5c6e76390f1ed1335e1537e5b42cc652aae1e34dff0d9fa2edb9129ed`. Thus the current file differs from that historical snapshot; the patch makes no claim about subsequent external edits. It preserves the current file, not an older assumed baseline. Do not restore the old snapshot simply to clear the guard.

The existing history initially hashed `54e529af41f1717763f44f3b240c6f1686a0493ba6634f2039c39905f90f000c` and contained the Locked guard from the reported attempt. It was read only. Final preservation checks and release evidence are recorded below after the release gate.

## Safely review and clear the existing guard

1. Close the old manager when idle, then open v1.0.1. Keep preferences/history; do not delete them. The new version retains the existing Locked guard.
2. Use read-only configuration loading and Operation History to inspect the current file and the old failure. Confirm the current configuration is the one you intend to keep. To compare the encrypted backup, preview its diff through Restore backup and **Cancel**; do not save or restore it as part of acknowledgment.
3. Check that no other save/remote mutation is pending. The specific recorded v1.0 `-w` failure exited before replacement; its error alone does not establish the history of later external edits.
4. Only after review, choose **Tools & Settings → Advanced recovery → Acknowledge reviewed unknown outcome…**, and confirm. This is a deliberate user action; it permits later changes without writing the INI or deleting the old error record. Do not clear a genuinely unresolved operation.
5. Reload the latest configuration before making a new draft. No automatic acknowledgment or retry is performed by this patch or its tests.

## Minimal reversible live acceptance (for the user, not executed during development)

After the review above, load current UserServerCustomSettings.ini, record its current bytes/value, and confirm LootRespawnSpeed is the expected **0.100000**. Change only it to **0.200000**, review the exact one-line diff and correct VM/file target, then save. Expect Succeeded, a verified fresh readback and both encrypted-local and remote rollback copies. Reload to confirm 0.200000. Review and save the reverse one-line change back to the exact **0.100000** representation, then reload and verify original bytes/hash where practical. Do not restart the battlegroup for this file-save acceptance test; gameplay adoption can be accepted separately in a maintenance window. If the result is Unknown, stop and review rather than retrying or acknowledging blindly.

## Tests and delivery

The full suite includes all 57 existing regressions plus save-path/diagnostic regressions, with real generated scripts executed in disposable **BusyBox 1.37.0** containers. Image is pinned to `busybox@sha256:bdf57e528e45e4433820e045b29b4597825a1c9e38353532d90a01445013f82e`. Its build date differs from the Alpine host, but the relevant applet options match read-only host discovery. Containers have no network, a read-only root and an isolated `/work` tmpfs; no production/host configuration is mounted. Fault cases override individual commands only inside test scripts. No test hooks are included in the production writer.

Required scenarios covered: unsupported -w; successful compatible locking/save; real lock contention; failed temporary write; failed replacement; failed final/host readback; external modification before/after replacement; successful atomic rollback with metadata preservation; confirmed unchanged failure; genuinely uncertain outcome with durable interlock. Additional cases protect legacy JSON/guard compatibility, updated-file classification and primary/history diagnostics.

Interrupted/signal exits (HUP/INT/KILL/TERM-style exit statuses) remain uncertain even if an exit trap returned a receipt and an instant read is unchanged: an inherited foreground utility may not be quiescent yet. This conservative case also has a regression test.

Runnable output: `artifacts/release/ArrakisServerManager-v1.0.1-win-x64/ArrakisServerManager.exe`, a self-contained Windows x64 folder. The previous v1.0 release is preserved. The included Demo launcher uses a separate Demo-v1.0.1 data directory. Native smoke checks render all pages plus the expanded save error and selected-history diagnostics, entirely in isolated Demo mode.

## Final verified release gate

- Full solution Release build: **0 warnings, 0 errors**.
- Complete regression suite: **73/73 passed**, including all 57 prior cases and 16 patch cases. Evidence: `artifacts/tests/20261022-205246-442edf/results.json`; actual BusyBox transcripts are in the same directory as `busybox-*.txt`.
- Published self-contained Windows x64 executable: native smoke passed and exited normally; six pages plus error/history diagnostics rendered without WPF binding errors. Evidence: `artifacts/ui-20261022-205305/`, including `configuration-save-error.png` and `configuration-save-history.png`. Wrapped error/history screenshots were visually inspected.
- Final production hashes exactly match the three patch-start hashes above. The operation-history SHA-256 is still `54e529af41f1717763f44f3b240c6f1686a0493ba6634f2039c39905f90f000c`, and the latest safety record remains **Locked**. No acknowledgment, production write or rollback deletion was performed.
- No commit, staging or push was performed. The previous v1.0 output was not replaced. This is implemented and verified with isolated real BusyBox/fixture tests; an actual production save remains the user's explicitly chosen acceptance step.
- Release checksums: `artifacts/release/SHA256SUMS-v1.0.1.txt`.
