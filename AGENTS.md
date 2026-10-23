# Arrakis Server Manager development rules

Build a native .NET 10 WPF application. Keep infrastructure behind interfaces and test mutations with simulated adapters only.

Production is read-only during development. Never start/stop/restart the live VM or battlegroup, write live configuration, import databases, update servers, change networking, credentials, Kubernetes objects or saves. Never run setup or modify the Steam installation. Never package private keys, credentials, game data or proprietary Funcom files.

Use strict SSH host-key checking. Require a fingerprint review for new hosts; changed identities fail closed. Use argument lists, validated resource names and shell escaping. Never log credentials or configuration values that could contain secrets.

Runtime disruptive actions need explicit confirmation. Configuration writes need a diff, optimistic concurrency, backup, atomic replacement and readback verification. Unknown upstream schemas must remain read-only with a clearly labeled official-editor fallback.

Run the test suite and Release build before delivery. Publish self-contained win-x64 under artifacts/release. Report simulated coverage separately from live read-only checks and untested live mutations. Do not create or push a remote repository.
