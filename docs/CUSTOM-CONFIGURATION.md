# Custom configuration management (v1.3.0)

This feature manages the three existing UserSettings targets only: UserGame.ini, UserEngine.ini and UserServerCustomSettings.ini. Loading or organizing settings never writes to the server. Save still requires the complete diff, concurrency check, encrypted local backup, remote rollback copy, atomic replacement and readback. Unknown-outcome interlocks remain unchanged.

## Find and edit a setting

Load current configuration, select a file, then search by friendly name, exact/partial key, section, category or description. The visual list combines built-in definitions, local custom definitions and active entries detected from the authoritative draft. Raw edits and reloads reconcile immediately. Commented-out examples are not active settings. Include supported settings available to add also exposes absent built-in/custom definitions; no entry is inserted merely by selecting it.

Discovered scalars use conservative text editing: `0`, `1`, `false`, numbers and quoted strings retain their representation rather than gaining an inferred type or range. No default, game effect or accepted range is guessed. Duplicate keys, related collection operators, repeated/malformed sections, complex values and password/token-like values remain clearly labeled Raw-only. General lists mask sensitive values; the Raw editor is an explicit advanced view of the real file. Never share raw screenshots or exports containing secrets.

Saved, draft and proposed values remain separate. Only **Add to draft** changes document bytes. Semantically unchanged validated numeric/Boolean proposals remain no-ops. Custom numeric sliders require finite user-provided minimum and maximum; optional step affects slider selection, not exact typed values. Bounds are labeled as user annotations. Built-in validation still applies even if a local definition supplies different bounds/type; local annotations cannot bypass protected required entries.

## Add Variable

1. Select the target file first, then choose **Add Variable**. The window displays and locks that supported file; it cannot choose arbitrary files or switch the active document underneath the draft.
2. Choose an existing section or type a new section and explicitly allow its creation. Sections control where the game reads a key; verify the correct location yourself.
3. Enter the exact key. Letters, digits, underscores and dotted names are supported; collection operators, line breaks and malformed keys are rejected. Review/edit the generated friendly name.
4. Choose or type a visual category. This is only Arrakis organization, not an INI section. Add an optional description without secrets.
5. Confirm Boolean, Integer, Decimal number, String or Enumerated choice and provide a valid initial value. Expand Advanced metadata for numeric bounds/slider step, `|`-separated choices, direction, example/default, restart/client dependencies and personal notes. These annotations are user/community supplied, not official or independently verified documentation.
6. Inspect the literal section/key/value preview. Confirm **Add variable to draft**. A valid metadata definition is saved locally, and only the intended insertion enters the current draft. Existing keys are refused: select the existing row and use **Manage definition** instead.
7. Review the normal complete diff, then save only when you intend a server change. Canceling the wizard writes neither metadata nor INI values. Canceling save preserves the draft. If you later discard the draft, explicitly confirmed metadata remains locally available; it does not imply the variable exists on the server.

Optional explanatory comments are not inserted by this version. Add any intentional comment through Raw mode and review it explicitly. Adding a key preserves surrounding comments/formatting, original encoding/BOM and line endings. A missing terminal newline is added only as needed to separate a new line. A new section is an explicit insertion rather than a file rewrite.

### BattlEye example — simulation first

In Demo select UserEngine.ini → Add Variable → ConsoleVariables → `BattlEye.Enabled` → friendly name **BattlEye Enabled** → Boolean → false → category **Security / Anti-Cheat**. Describe it as community/user supplied. The preview is:

```ini
[ConsoleVariables]
BattlEye.Enabled=false
```

This demonstrates configuration management, not independently verified game/anti-cheat behavior. Arrakis does not automatically disable BattlEye. The comment `; Disable BattlEye for this self-hosted server` is optional Raw content, never silently inserted. Never use production to exercise this example during development acceptance.

## Manage local definitions

### Examples and defaults are metadata

Advanced metadata is collapsed initially. **Example Value (Optional)** is a reference sample, not a setting to apply. **User-Defined Default (Optional)** is your preferred default; it does not establish the game's actual default. Blank or whitespace-only optional inputs mean unspecified metadata, not an empty-string INI value. Existing Example and Default fields retain their values and schema 1; no migration is required.

Saved and Draft continue to show the INI values independently of either annotation. **Reset User Default** copies only your annotation to Proposed Value. You must still choose **Add to draft**, review the exact diff and explicitly confirm a save. The action is unavailable without a user default. A genuine built-in **Verified Engine Default**, when present, has a separate label and reset action; the application does not silently select between the two or promote a template example into an engine default. This refinement adds no newly verified game defaults.

Choose Boolean for True/False; a manual choices list is unnecessary and case-insensitive validation preserves existing INI spelling until an explicit change. Enumerated Choice exposes its `|`-separated choices editor, rejects empty/duplicate entries, and validates annotations against the list. Integer/Decimal expose optional minimum/maximum/step fields, with whole-number validation for Integer. String hides numeric/enum controls and preserves text. Unknown values still start as text until you explicitly confirm a type.

Switching types never changes INI bytes. Inactive numeric/choice fields are retained in the open window and excluded from the saved definition, with an explanation; switch back to recover them before saving. Examples/defaults remain visible and must match the newly chosen type. Incompatible input is not silently deleted: correct it, or use **Clear incompatible metadata** explicitly. Field-specific errors appear beside annotations/bounds/choices, invalid definitions cannot be saved, and **Go to first issue** expands/focuses the relevant input. Valid values remain in the form while another field is corrected.

Manage Setting Definition emphasizes that editing a definition does not change the server INI, keeps the immutable file/section/key in its fixed header, and omits initial-value/new-section controls. The final metadata-only preview and actions remain fixed while the form scrolls.

Select a row and choose **Manage definition** to change names/categories/descriptions, confirm a type, adjust optional bounds or maintain notes. Existing identity is locked; metadata-only confirmation does not alter the INI, dirty state or document identity. Built-in fields can be restored in the dialog, and **Remove Definition** removes all local overrides after confirmation. It does **not** remove a server variable. There is no automatic Remove Server Variable action; intentional raw removals retain their separate typed authorization and final review.

Identity is case-insensitive `(filename, section, exact key)` with spelling preserved in metadata and existing INI lines. There is one visual row per identity, never per friendly label, IP or temporary PVC path. Local metadata takes presentation precedence; matching built-in documentation remains available under Technical identity, bounds and documentation. An app upgrade cannot silently delete/replace a user definition or modify server values. The user can manually reset fields or remove the local definition after comparing documentation.

The split divider starts at 440 logical pixels for details on a wide window, adapts to smaller windows and can be dragged. Its width preference persists independently of connection form edits. Both panes have minimum widths; details scroll vertically. Exact technical keys remain available through details/tooltips, search and **Copy key**.

## Persistence, recovery and privacy

Normal storage is under `%LOCALAPPDATA%\ArrakisServerManager\CustomSettings\Live\definitions.json`. Demo uses `CustomSettings\Demo` beneath its own selected data root; Launch Demo uses a separate versioned Demo root. A portable application upgrade preserves the normal application-data root. Do not copy private catalogs into the executable folder or public release.

Catalog schema 1 contains an explicit SchemaVersion and Definitions array. Schema 0 with the same definition fields migrates in memory without losing definitions; the next explicit save writes schema 1. Unknown future schemas are preserved read-only and require a compatible application. Documents/imports are limited to 1 MiB and 1,000 definitions, validate identities/types/bounds/choices and reject duplicate identities/schema fields. No scripts, shell expansion or network synchronization is performed. Active server values are never copied into the metadata model. Examples/defaults are explicit annotations; secret-like keys cannot store them. Keep secrets out of all descriptions and personal notes, and review annotations before exporting or importing them.

Writes use a sidecar exclusive kernel handle, optimistic catalog hash, unique temporary file, flush and atomic local replacement, retaining `.previous`. External edits are not overwritten. Malformed current data can be recovered from a valid previous copy in memory; on the next explicit metadata save the damaged file is retained separately. If neither copy is valid, files remain untouched and metadata management becomes read-only while discovery/INI editing remains available. Review the local files and restore a known good copy or move damaged files aside yourself, then choose **Reload metadata**. No automatic deletion or safety-guard clearing occurs. Linked metadata paths are refused.

**Export definitions** chooses a folder and creates a uniquely named versioned JSON file without overwriting an existing export. It contains metadata, not current INI values, SSH keys, passwords or server history. This is not a server backup. **Import definitions** validates first and shows all incoming annotations plus an add/replace conflict review. Confirming replaces matching local metadata identities and retains unmatched local definitions; cancel preserves both metadata and draft. Export includes all local definitions in the active Demo/Live catalog, not merely the selected INI. Review your own annotation text for private information before sharing.

## Demo and acceptance

Demo INIs contain unknown numeric/text/Boolean-style scalars and collection examples. **Demo examples** offers an explicit metadata-only merge for a custom category, typed unknowns and an existing built-in key with a local name; built-in documentation remains available. Create raw variables/new sections, try malformed imports, and use the BattlEye workflow entirely in simulation. No real GitHub, SSH, Hyper-V or production operation is required.

Automated evidence uses actual WPF controls and simulated gateways. It checks exact reviewed/save bytes, encoding preservation, metadata restart/recovery/import/export, races, layout/keyboard behavior and 100/125/150-percent rendering. Game recognition/effects of custom variables, physical monitor transitions and disruptive operations remain manual acceptance responsibilities. Unknown complex Unreal structures are intentionally read-only in Visual mode rather than interpreted heuristically.
