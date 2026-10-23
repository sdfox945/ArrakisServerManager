# Arrakis Server Manager v1.1.1 acceptance

v1.1.1 is accepted as the stable source baseline following successful development validation and the user's review of the live UI and configuration preview. This acceptance includes the v1.1 enhancements and v1.1.1 stabilization; it does not claim every operational action has been tested live.

## Accepted evidence

- The complete 154-test suite retains the earlier 129 regressions and adds 25 stabilization cases, including 27 native WPF workflows overall.
- Release builds passed with zero warnings/errors. The self-contained Windows x64 executable passed its native six-page Demo smoke, minimum-window and binding checks.
- The user reviewed the live UI and configuration preview. The earlier v1.0.2 configuration-save workflow also received user-confirmed live acceptance; that evidence is separate from a claim of complete v1.1.1 operational acceptance.
- Prior development inspected completed-utility metadata read-only. Job failures, resource changes, lifecycle operations and other potentially mutating automated scenarios use isolated fixtures, simulated transports or private BusyBox containers.

The final baseline verification reruns `scripts/build.ps1` without publication and `scripts/test-ui.ps1` against the existing accepted package. Its current evidence is retained under ignored `artifacts/baseline-v1.1.1/`. The historical implementation reports and their evidence remain available in RELEASE-v1.1.0.md and RELEASE-v1.1.1.md.

## Separate live acceptance still required

The following are not established as live-tested by this baseline:

- VM graceful shutdown and startup.
- Battlegroup lifecycle transitions.
- Individual map resource modifications and reconciliation.
- Database restore.
- Server update operations.
- Any other disruptive or potentially destructive operation not independently exercised and verified.

These activities require separate manual acceptance with explicit review, appropriate recovery preparation and a suitable maintenance window. Passing simulated tests or reviewing a preview does not prove a disruptive operation succeeded live. Existing unknown-outcome guards remain in force; no acknowledgment is automatic or justified merely by establishing a Git baseline.

## Stable source and preserved packages

The annotated `v1.1.1` tag identifies the accepted source commit on `main`; retained `feature/v1.1` points to the same commit after a fast-forward-only integration. The original annotated `v1.0.2` remains at `51a0cbb47c99686f0a67606da86811e25771af5f`. The exact release commit is available through `git rev-parse v1.1.1^{commit}`.

The accepted executable remains under `artifacts/release/ArrakisServerManager-v1.1.1-win-x64`, with its existing SHA256SUMS-v1.1.1.txt manifest and adjacent runtime files. It is not republished for this source-control task. Its informational version contains the pre-baseline Git revision; this does not change its 1.1.1 application version. Baseline acceptance documentation is tracked in source without rewriting the accepted package or prior releases.

The baseline task makes no application-code changes, production-server connection, production operation, credential rotation, safety acknowledgment, Git remote configuration or push. Generated outputs, local state, credentials, diagnostics, backups and proprietary server assets remain excluded from Git.
