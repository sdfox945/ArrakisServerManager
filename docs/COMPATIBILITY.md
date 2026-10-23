# Verified integration contracts

Discovery date: 2026-10-22. Source is the user's installed Windows scripts and Linux VM, plus Funcom's self-hosting documentation: https://duneawakening.com/zh-hant/self-hosted-servers/ . No proprietary server code is distributed with this project.

| Interface | Inspected contract | Application behavior |
|---|---|---|
| Windows entry point | battlegroup.bat launches elevated battlegroup-management/battlegroup.ps1 | Read for discovery; never modified or used to run setup |
| Windows SSH | dune user, existing `%LOCALAPPDATA%/DuneAwakeningServer/sshKey` | Reuse key; strict identity checking; no passwords saved |
| VM | Hyper-V VM dune-awakening | Get-VM / Get-VMNetworkAdapter; narrow UAC for actions |
| Graceful shutdown | Installed Stop-VM exposes no `-Shutdown` parameter | Default Stop-VM without Force/TurnOff; separate emergency action |
| CLI path | /home/dune/.dune/bin/battlegroup → download/scripts/battlegroup.sh | Allowlisted commands; script hash compatibility gate |
| Reviewed CLI digest | `4e4b94aec678e4ce7f25f5d53aaa0af20697f97b886ba1dacfbf64426d8c6171` | Unknown digest disables automatic CLI/map/config mutations |
| CLI target selection | Prefix funcom-seabass-; one namespace is selected automatically; multiple prompt | Require exactly one matching resource and namespace; otherwise official editor |
| Lifecycle | start, stop, restart | Official CLI plus observed Healthy/Stopped status |
| Update | update downloads, reloads scripts/operators/maps and applies | Confirmed apply only; no fabricated online check-only flag |
| Version | download/images/battlegroup/version.txt and set image tag | Read already-downloaded and running revision |
| Battlegroup schema | igw.funcom.com/v1, kind BattleGroup | Exact inspected layout only |
| Map resource authority | spec.serverGroup.template.spec.sets[].map/resources | JSON Patch with resourceVersion/map tests, dry-run and rollback copy |
| Minimum running authority | spec.utilities.director.spec.configFiles.files[director.ini], map section MinServers | Preserve Director INI; add/edit only selected map's MinServers |
| Director | Kubernetes service port 11717, dynamic nodePort | Namespace-scoped discovery, ambiguity rejected |
| File Browser | VM IPv4:18888 | Availability check before opening |
| Active game configuration | role=igw-server PVC → bound local-path PV → Saved/UserSettings | Validate claim identity/path; no assumed /home/dune/server tree |
| Database backup | CLI backup [name], .backup under /funcom/artifacts/database-dumps/<bg> | Unique app name, completed nonempty artifact required |
| Database import | CLI import [name], explicit yes prompt, completed DatabaseOperation | Existing discovered artifact, stopped battlegroup, destructive review, success markers |
| Log exports | logs-export → /tmp/dune-bg-logs; operator-logs-export → /tmp/dune-operator-logs | Official command plus verified downloaded tar.gz |
| Native maintenance | change-vm-ip, enable-experimental-swap, Linux passwd | Explicitly confirmed secure native SSH consoles; recovery messages retained |

The Linux CLI on this installation contains locally adapted backup/import and startup handling. Compatibility is deliberately bound to the inspected implementation; this is not a claim of universal support for all Funcom releases. A server update can change that implementation. Monitoring, raw reading and official editor access remain available, while automatic writes fail safely until a developer reviews the new contracts and tests.

Kubernetes resource quantities are accepted in a conservative supported subset: positive decimal numbers with Ki/Mi/Gi/Ti or K/M/G/T/k suffixes, or bytes. Other valid Kubernetes quantity forms remain readable but require the official editor for changes. Request must not exceed limit. Minimum-instance GUI input is guarded to 0–100; larger or additional scaling options use the official editor.

The visual settings catalog records supplied template values, descriptions, documented ranges and client dependencies. It does not equate template examples with verified engine defaults. Undocumented semantics remain labeled unknown; existing unknown values are preserved. Unreal multi-valued array directives and custom/experimental keys are retained in the raw editor, rather than reinterpreted as unique scalar controls.

Unsupported automatic functions: online update availability without applying; SSH key rotation; arbitrary map fields; unknown volume providers/schemas; direct client changes; reinstall/setup; automatic repair; direct character or progression database manipulation. Native maintenance consoles require user supervision and manual acceptance of their own prompts. There is no unattended restore, credential rotation, network reconfiguration or experimental swap enablement.
