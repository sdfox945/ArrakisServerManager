# Arrakis Server Manager v1.1.2 — water extraction guidance correction

This is a narrow settings-description correction on top of the accepted v1.1.1 implementation, not a change to server behavior. No production server or Funcom installation is modified during development. The existing v1.0.2/v1.1.1 tags, stable commits and prior executable packages remain intact.

## Cause and corrected behavior

The catalog inherited Funcom's supplied template statement that increasing `WaterExtractionRate` makes extraction take longer. The slider separately grouped that key with LootRespawnSpeed's lower/faster, higher/slower labels. Both therefore presented the opposite of the user's reported gameplay observation.

The corrected description is **water extraction speed multiplier**, with **higher = faster water extraction** and **lower = slower**. The slider displays **0.1 · Slower** at the lower end and **10 · Faster** at the higher end. Setting details explicitly say this follows **user-reported gameplay observations contradicting Funcom's supplied template comment, not independently verified**. No independent gameplay test is claimed.

The section, INI key, numerical type, 0.1–10 bounds, 1.000000 template example, unknown engine default and existing user-entered values are unchanged. This patch changes presentation metadata, not multiplier interpretation or stored numbers. No setting is migrated, inverted, reset or auto-applied.

The representative test fixture's water comment now carries the same attribution. Its WaterExtractionRate remains 5.000000 and every other setting value is unchanged. Existing server-file comments, including the incorrect upstream comment, remain byte-preserved when editing a value. That is separately tested with intentional legacy input; the editor must not silently change a user's comments.

## Scope and other effect directions

The catalog's description/direction/evidence drives the visible details panel; no separate hardcoded water tooltip exists. File/search tooltips contain no effect-direction claim. The only water-specific slider mapping was corrected.

Reviewed the remaining explicit directions: chest timing, resource/NPC respawn, stamina drain, inventory capacity, shield duration, mining output and sandworm threat. Their catalog wording does not obviously contradict their effect labels or the local fixture evidence. No independent gameplay verification was performed, and no unrelated effect direction was changed. Undocumented effects remain neutral rather than guessed.

Application/manifest/sidebar versions and local release/Demo/smoke paths advance to v1.1.2. README, changelog, current testing guidance, third-party inventory and GitHub release notes describe the correction. Historical implementation reports remain historical evidence; their original release packages are not overwritten.

## Regression coverage and validation

Five new cases are added to the existing 154, for **159 tests**, including **29 native WPF workflows**:

1. Correct catalog wording, both directions, evidence attribution, identity, unchanged range/example/default and validation.
2. Corrected fixture comment with unchanged numerical value.
3. A real visual-document edit preserves an existing contradictory upstream comment, CRLF, unknown keys and the exact reviewed bytes.
4. Native WPF selection renders corrected details and both slider labels, preserves loaded/no-op state and draft bytes through endpoint interactions/mode changes, and retains unrelated labels.
5. Native WPF typed edit produces exactly one changed value, with cancellation/no-write, identical approved payload and consistent reopen behavior through an isolated gateway.

The focused subset is `dotnet run --project tests/Arrakis.Tests -c Release -- --correction`. The full suite also retains BusyBox locking/rollback/conflicts, configuration integrity, unknown outcomes, lifecycle gating and workload-health regressions. All mutation tests use isolated fixtures/adapters or private containers. Full build, native-test and packaged-executable results are recorded in the local v1.1.2 publication-readiness report; do not confuse these checks with live gameplay verification.

## Acceptance and publication

The previously accepted v1.1.1 UI/configuration preview and earlier v1.0.2 save evidence remain the live acceptance baseline. This correction adds no live operational claims. VM/battlegroup lifecycle, map changes/reconciliation, database restore, server updates, physical multi-monitor DPI/UAC and other unexercised disruptive actions still need separate live acceptance.

Safe manual check: launch the new package's Demo mode, load UserServerCustomSettings.ini, select WaterExtractionRate and read the corrected wording/evidence. Confirm 0.1 is Slower and 10 is Faster. Type a new in-range value, add to draft, verify the preview contains only that value change, and cancel. Repeat only with simulation if testing save/reopen. Production mutation is unnecessary for this check.

Proposed package: `ArrakisServerManager-v1.1.2-win-x64.zip` plus SHA256SUMS.txt. No v1.1.2 commit/tag, remote repository, push or release upload is created without further authorization. A final targeted review recommends retaining the template-derived fixture and stable history: no substantial copied expressive passage beyond configuration facts and short functional descriptions was identified. See FIXTURE-PROVENANCE.md for provenance, specific passage review, license exclusions and residual uncertainty; no affirmative third-party permission or guaranteed legal clearance is claimed. The user-selected MIT license is Copyright (c) 2026 Steven Fox for original project contributions; Microsoft runtime terms remain separate. Explicit final publication approval is still required.
