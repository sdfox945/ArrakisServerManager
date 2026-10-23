using System.Security.Cryptography;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed record ConfigurationSavePlan(string Script, string ReceiptId, string OriginalHash, string ProposedHash);
public sealed record ConfigurationSaveReceipt(int ExitCode, string Stage, bool ReplacementAttempted, string Rollback);

public static class ConfigurationSaveScript
{
    public static ConfigurationSavePlan Build(ConfigFile original, byte[] proposed, int lockWaitSeconds = 10)
    {
        if (lockWaitSeconds is < 0 or > 60) throw new ArgumentOutOfRangeException(nameof(lockWaitSeconds));
        var id = Guid.NewGuid().ToString("N");
        var before = Convert.ToHexStringLower(SHA256.HashData(original.Bytes));
        var after = Convert.ToHexStringLower(SHA256.HashData(proposed));
        var script = $$"""
            set -eu
            umask 077
            p={{CommandBuilder.ShellQuote(original.RemotePath)}}
            receipt={{id}}
            original_hash={{before}}
            proposed_hash={{after}}
            stage=lock
            tmp=
            restore_tmp=
            backup_file=
            replaced=0
            rollback=not-needed
            digest() { sha256sum "$1" | cut -d ' ' -f1; }
            fail() { printf 'Configuration save: %s\n' "$1" >&2; exit "$2"; }
            finish() {
                rc=$?
                trap - EXIT HUP INT TERM
                set +e
                if [ "$rc" -ne 0 ] && [ "$replaced" -eq 1 ]; then
                    current=$(digest "$p")
                    if [ "$current" = "$original_hash" ]; then
                        rollback=original-present
                    elif [ "$current" = "$proposed_hash" ] && [ -n "$backup_file" ]; then
                        rollback=failed
                        restore_tmp=$(mktemp "$p.arrakis-rollback-XXXXXX")
                        if [ -n "$restore_tmp" ] && cp -p "$backup_file" "$restore_tmp" &&
                           [ "$(digest "$restore_tmp")" = "$original_hash" ] &&
                           [ "$(digest "$p")" = "$proposed_hash" ] &&
                           mv -f "$restore_tmp" "$p" && [ "$(digest "$p")" = "$original_hash" ]; then
                            rollback=restored
                            restore_tmp=
                        fi
                    else
                        rollback=refused-external-change
                    fi
                fi
                [ -z "$tmp" ] || rm -f "$tmp"
                [ -z "$restore_tmp" ] || rm -f "$restore_tmp"
                printf 'ARRAKIS_CONFIG_RESULT=%s:%s:%s:%s:%s\n' "$receipt" "$rc" "$stage" "$replaced" "$rollback"
                exit "$rc"
            }
            trap finish EXIT
            trap 'exit 129' HUP
            trap 'exit 130' INT
            trap 'exit 143' TERM
            test ! -L "$p.arrakis-lock" || fail 'Lock path must not be a symlink.' 73
            exec 9>"$p.arrakis-lock"
            waited=0
            until flock -x -n 9; do
                [ "$waited" -lt {{lockWaitSeconds}} ] || fail 'Timed out waiting for the configuration lock; no replacement attempted.' 75
                sleep 1
                waited=$((waited + 1))
            done
            stage=original-check
            test ! -L "$p" && test -f "$p" || fail 'Target must be a regular, non-symlink file.' 73
            [ "$(digest "$p")" = "$original_hash" ] || fail 'Configuration changed since it was loaded; reload before saving.' 73
            mode=$(stat -c '%a' "$p")
            owner=$(stat -c '%u:%g' "$p")
            stage=backup
            backup_dir="$(dirname "$p")/.arrakis-backups"
            mkdir -p "$backup_dir"
            chmod 700 "$backup_dir"
            backup_file="$backup_dir/$(basename "$p").$(date -u +%Y%m%d-%H%M%S).$receipt.bak"
            cp -p "$p" "$backup_file"
            [ "$(digest "$backup_file")" = "$original_hash" ] || fail 'Rollback copy did not match the loaded original.' 74
            stage=temporary-write
            tmp=$(mktemp "$p.arrakis-XXXXXX")
            base64 -d > "$tmp"
            [ "$(digest "$tmp")" = "$proposed_hash" ] || fail 'Temporary file verification failed.' 74
            stage=metadata
            chown "$owner" "$tmp"
            chmod "$mode" "$tmp"
            [ "$(stat -c '%a' "$tmp")" = "$mode" ] && [ "$(stat -c '%u:%g' "$tmp")" = "$owner" ] || fail 'Temporary file metadata did not match the original.' 74
            stage=conflict-check
            [ "$(digest "$p")" = "$original_hash" ] || fail 'Configuration changed while preparing the save; no replacement attempted.' 73
            stage=replacement
            replaced=1
            mv -f "$tmp" "$p"
            tmp=
            stage=verification
            [ "$(digest "$p")" = "$proposed_hash" ] || fail 'Final file verification failed.' 74
            stage=complete
            """;
        return new(script.Replace("\r\n", "\n"), id, before, after);
    }

    public static ConfigurationSaveReceipt? Receipt(ConfigurationSavePlan plan, string output)
    {
        var prefix = "ARRAKIS_CONFIG_RESULT=" + plan.ReceiptId + ":";
        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        if (line is null) return null;
        var fields = line[prefix.Length..].Split(':');
        return fields.Length == 4 && int.TryParse(fields[0], out var code) && fields[2] is "0" or "1"
            ? new(code, fields[1], fields[2] == "1", fields[3]) : null;
    }
}
