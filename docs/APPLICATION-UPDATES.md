# Arrakis application updates (v1.2.0)

These updates concern the Windows **Arrakis Server Manager application**. They do not check, apply or install Funcom/Dune server software. The existing **Funcom server updates / Check downloaded revision** controls retain their separate gateway, status and confirmations.

## Checking for updates

The sidebar version is read from the Desktop assembly's actual informational/product metadata, with assembly-version fallback. Build metadata does not change semantic precedence. Numeric major/minor/patch and prerelease ordering are compared as semantic versions, never alphabetically. Optional leading v is accepted; prerelease releases are excluded from the stable channel. Supported version components are nonnegative 32-bit numbers and complete version text is bounded to 128 characters.

The first automatic check runs asynchronously after the window renders, without waiting for the Dune connection, Hyper-V or elevation. A successful result and timestamp are cached locally for 24 hours; an hourly timer consults that cache and requests GitHub again only when it is expired. Dashboard refreshes do not request application-update metadata. **Tools & Settings → Check for application updates** bypasses the cache and shows a fresh result.

**Automatically check GitHub for Arrakis updates** defaults to enabled. Turn it off to prevent automatic requests; manual checks remain available. This checkbox persists only the application-update preference and does not save other unsaved connection-form changes. Turning it off cancels an in-flight automatic check. Check/download failures do not enter the server operation coordinator, modify its history or acknowledge its durable unknown-outcome interlock.

Footer states distinguish checking, up to date, a newer stable version, not checked and check unavailable. An offline/failed check never creates a success state. Cached results show their last-successful-check timestamp in the tooltip/details. Clicking the footer opens release details; it does not start a download. An equal or older public release is never offered as an upgrade.

## Details and verified download

The refined v1.2.0 dialog has a fixed version/status summary, one independently scrolling release-information panel, and fixed operation/actions below. There is no outer-page scrollbar and no nested release-notes scrollbar. Long titles, notes and paths wrap. Equal and installed-newer releases have distinct compact messages; unavailable checks never masquerade as up to date. Empty progress/destination/download controls are collapsed when no update is offered.

During a transfer the bytes/percentage, verification-pending message and Cancel remain visible even if release notes are scrolled. Cancellation retains the last percentage with an explicit canceled/no-verified-ZIP explanation and Retry Download. Failures show a concise explanation, Retry and expandable/copyable redacted diagnostics. Verified completion exposes the full copyable path and folder/path actions; Download Again reuses the existing safe service behavior (rehash/reuse a matching ZIP, never silently overwrite it). Choose next folder permits another destination. Installation instructions start collapsed under **How to install this update** after verification; help scrolls independently and the primary actions remain fixed.

Details include installed/latest version, release title/date, bounded plaintext notes, exact ZIP name/size, verification availability, last check and destination. Notes are selectable plain text: no HTML rendering, scripts, embedded commands or automatic link execution.

Choose **Download Update** only after reviewing the release. Downloads default to `%LOCALAPPDATA%\ArrakisServerManager\Downloads`; `--data-dir` relocates this under the selected application-data root. An alternative local folder can be chosen. Running application folders, configured Funcom installation paths, backup/log paths, UNC/mapped-network destinations and filesystem reparse-point paths are refused. Application updates are kept separate from server backups and logs.

The downloader streams asynchronously to a unique `.partial` file, reports bytes/total/percentage and supports cancellation. It checks the exact expected asset size and calculates SHA-256 from the actual downloaded file. Only a matching package is atomically renamed to the final ZIP filename. A failed/cancelled transfer never becomes a completed ZIP; its owned partial is removed when possible. A process crash or denied deletion may leave a clearly unfinalized `.partial`; it is never offered as verified and can be removed manually after closing the app. Existing valid ZIPs are rehashed before reuse; existing corrupted files are preserved and require an empty destination or manual renaming before retry.

The preferred digest is GitHub's `sha256:` digest for the exact selected ZIP asset. If absent, the matching `SHA256SUMS.txt` asset may supply one unambiguous exact-filename hash. Its own published digest is also checked when present. Size alone is never verification. Without a reliable hash, in-app download is unavailable; use **View on GitHub** as a safe fallback. Checksums establish correspondence with the GitHub-published asset; they are not an independently authenticated software signature and do not prove publisher/account compromise is impossible.

Success reads **Download complete — SHA-256 verified** and enables **Open Download Folder** / **Copy Download Path**. These actions open only the folder or copy the local path; downloaded content is never launched.

## Installing the portable update

1. Close Arrakis Server Manager yourself.
2. Extract the complete verified ZIP into a new or chosen application folder.
3. Keep the executable, adjacent runtime dependencies, launchers, docs and licenses together.
4. Launch the new executable or administrator launcher as appropriate.
5. Verify the new version appears in the sidebar.

Keep the existing `%LOCALAPPDATA%\ArrakisServerManager` preferences, history, host pins, backups and safety state. Do not delete application data during a portable upgrade. The updater does not extract anything, overwrite a running executable, install a service, request elevation, restart Arrakis or modify Funcom/Steam files, VM state, battlegroups, Kubernetes, game saves, INIs or database backups.

## Network and security policy

Owner/repository identity is fixed in **ApplicationUpdateSource**. Only the anonymous official `/repos/sdfox945/ArrakisServerManager/releases/latest` API is used for discovery; no HTML scraping, GitHub token or sign-in is required. Requests contain an ordinary User-Agent/API headers, not VM/configuration/SSH/account details. Metadata redirects are refused. Asset URLs must bind the selected release tag, exact expected `ArrakisServerManager-v{version}-win-x64.zip` filename and asset API identity to the fixed repository.

Downloads use HTTPS only, no cookies/credentials, and manual redirects capped at five requests. Redirects are restricted to the selected original GitHub URL or the exact HTTPS hosts `release-assets.githubusercontent.com` / `objects.githubusercontent.com`; userinfo, nondefault ports, fragments and foreign hosts are refused. Signed CDN query strings are never logged or used as local paths. The system browser opens only validated HTTPS release pages in the expected repository.

Bounds: metadata 1 MiB, notes 65,536 characters, title 200 characters, at most 100 assets, checksum text 64 KiB, ZIP 512 MiB, cache file 2 MiB. Timeouts: connection 15 seconds, response headers 20 seconds, metadata overall 20 seconds, stalled stream read 30 seconds, download overall 15 minutes. Rate limiting, offline operation, invalid metadata, missing assets, destination/write/disk errors and hash mismatch yield concise update-specific messages and preserve normal server-management operation.

The successful-check cache lives in `application-update-cache.json`, independently of server state/history. Cached remote data is revalidated before use; malformed, future or expired cache entries cannot establish a successful result. Cache-write failure is reported without discarding a valid network result. Cache and partial files are excluded from Git and packages.

## Demo and tests

Demo never constructs a real HTTP handler for updates. Its eight selectable scenarios are UpToDate, UpdateAvailable, ApiUnavailable, InvalidMetadata, MissingZip, DownloadInterrupted, ChecksumMismatch and VerifiedDownload. Its local ZIP is an inert text-only fixture, not an application. Demo has an isolated in-memory cache and `Demo/ApplicationUpdates/Downloads` directory. Browser opening is disabled in Demo; switching modes cancels old work and rejects late results.

The normal full suite and `--application-updates` subset use fake HTTP responses, isolated files and real WPF controls. They do not depend on GitHub availability. `--application-update-probe` is a separate opt-in integration check that uses only the anonymous latest-release API and never downloads a ZIP or creates a server gateway. Physical mixed-monitor DPI/UAC behavior and all previously unaccepted disruptive server operations retain their existing manual acceptance limits.

Official references: [GitHub latest release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release), [release asset API/digests](https://docs.github.com/en/rest/releases/assets#get-a-release-asset), [Semantic Versioning](https://semver.org/).

Native Demo evidence: [separate Tools controls](images/application-update-tools-demo.png), [release details](images/application-update-details-demo.png), [verified completion](images/application-update-verified-demo.png). These use simulated future-version metadata and an inert text-only ZIP, not a real future application release.
