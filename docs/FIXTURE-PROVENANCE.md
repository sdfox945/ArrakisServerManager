# Configuration fixture provenance and redistribution review

Review date: 2026-10-23. Applies to `tests/Arrakis.Tests/Fixtures/UserServerCustomSettings.representative.ini`, including its versions in the existing stable history.

## Provenance and purpose

The fixture was created during v1.0.2 development by reading the user's existing UserServerCustomSettings.ini through the already trusted SSH connection. The original development record shows a read-only retrieval, UTF-8 decoding, credential-field screening and writing the representative local test file. It is a live-derived, screened configuration example, **not a wholly invented synthetic document**. No server retrieval or server operation was performed for this review.

Its setting names, schema, ranges, choices and template-derived comments originate in the official Dune: Awakening configuration format. The numerical/boolean selections reflect the user's configured example and must not be represented as verified engine defaults. The review does not independently establish authorship of every original comment or an affirmative Funcom redistribution license. Treat the inherited template material as third-party-origin material rather than claiming it as original project prose.

The file first appears in the v1.0.2 baseline commit `51a0cbb47c99686f0a67606da86811e25771af5f`, and is also present in v1.1.1. The original Git blob is `49d1fb8b1cee73c65b370829e63bc45fc3a41f09`. The only v1.1.2 text change is the water-direction comment, now expressly attributed to user-reported gameplay observations that contradict the supplied template comment and are not independently verified. All configuration values and the file's LF/no-terminal-newline format remain unchanged. The v1.1.2 fixture Git blob is `5f624e77fad4f8412a78df191315931d64c871de`.

It is retained to test loss-preserving editing: comments, blank lines, sections, unknown settings, typed values and exact review/save bytes. It is not shipped in the runnable Windows ZIP and is not a server installer, container, executable, database or game asset. It contains no password, token, private key, player/account data, address or machine identity.

## Targeted content assessment

The current file has 174 lines: 41 active setting assignments, one section header, 83 comment lines and 49 blank lines. Comments include separators and category headings, repeated range/choice lists, short effect-direction statements, cross-references and brief operational descriptions. There is no narrative/lore, dialogue, story, marketing copy, artwork, proprietary program implementation or extended explanatory passage.

The longest inherited explanatory groups were specifically inspected:

| Lines | Content and assessment |
| --- | --- |
| 7–10, 122–129 | References to equivalent settings in UserEngine.ini, key identifiers and allowed choices. Short functional cross-references. |
| 67–68, 83, 89, 118–119 | Single-sentence descriptions of damage, durability, stamina and environmental effects. Functional explanations, without developed creative prose. |
| 142–145 | A three-line explanation of the no-build-zone boolean and its two outcomes. The longest multi-line switch explanation; still a short functional description. |
| 171–173 | Two-line explanation of the sub-fief backup time window and its unit. Short functional description. |
| Remaining inherited comments | Category names, separators, factual ranges/enumerations and brief effect statements. |
| 16 | Project-authored v1.1.2 correction and attribution, distinguished from the inherited template comment. |

**Finding:** no substantial copied expressive material beyond the configuration keys, values, ranges, short functional descriptions and their functional grouping was identified. No particular passage identified in this review justifies automatically deleting the fixture or rewriting the existing stable history. The recommendation is to retain it with this provenance and license-scope notice, subject to the maintainer's final publication decision.

## License boundary and remaining uncertainty

The project's MIT license applies to its original application code and project-authored contributions. It does **not** assert ownership of or grant a new license over any third-party rights in the inherited template wording or arrangement. Attribution documents origin; it is not a substitute for a license where one is required. All rights in third-party-origin material remain with their respective holders to the extent those rights exist. No Funcom endorsement or permission is claimed.

For context, the U.S. Copyright Office distinguishes facts, methods and short phrases from sufficiently original expression, and recognizes that sufficiently creative selection or arrangement may have protection. This supports distinguishing the fixture's functional content from substantial expressive copying; it does not establish that every configuration file is unprotected, resolve other jurisdictions or supersede applicable contract terms. See [Circular 33](https://www.copyright.gov/circs/circ33.pdf) and [compilations guidance](https://www.copyright.gov/register/tx-compilations.html). Funcom's [self-hosting documentation](https://duneawakening.com/self-hosted-servers/) identifies prefilled UserSettings files; availability for editing is not itself evidence of a redistribution grant.

Residual uncertainty is the lack of an affirmative third-party license and the possibility of protection in wording/selection/arrangement or applicable contractual restrictions. No specific restrictive notice or prohibited expressive passage was established by this review. This is a targeted content/provenance assessment, not a guarantee of legal clearance or a claim of fair use.

No fixture replacement or history rewriting is recommended on the evidence found. If a concrete rights-holder objection or restrictive term later emerges, preserve the evidence and first consider permission or clarification. An independently authored future fixture could address future copies but would not remove earlier public history. Removing historical material would require a separate, explicitly approved history decision; stable tags must never be silently moved.
