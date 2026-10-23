using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static class ConfigurationSaveRegression
{
    private const string Target = "/work/settings/UserServerCustomSettings.ini";
    private const string Image = "busybox@sha256:bdf57e528e45e4433820e045b29b4597825a1c9e38353532d90a01445013f82e";
    private static readonly byte[] Original = Encoding.UTF8.GetBytes("; Preserve comment and CRLF\r\n[/Script/DuneSandbox.UserServerCustomSettings]\r\nDifficultyLevel=Custom\r\nLootRespawnSpeed=0.100000\r\nUnknownKey=keep-me\r\n");
    private static readonly byte[] Proposed = Encoding.UTF8.GetBytes(new IniDocument(Encoding.UTF8.GetString(Original)).AlsoSet().ToString());
    private static IniDocument AlsoSet(this IniDocument doc) { doc.Set("/Script/DuneSandbox.UserServerCustomSettings", "LootRespawnSpeed", "0.200000"); return doc; }
    private static ConfigFile File => new("UserServerCustomSettings.ini", Target, Original, Convert.ToHexStringLower(SHA256.HashData(Original)));
    private sealed record Invocation(ProcessResult Result, byte[] TargetBytes, int Backups, int Temporaries, string Metadata);

    public static void Register(Action<string, Func<Task>> add, string evidenceRoot)
    {
        add("BusyBox save: actual unsupported -w regression is a confirmed unchanged failure", async () =>
        {
            var plan = ConfigurationSaveScript.Build(File, Proposed, 0);
            var legacy = plan with { Script = "set -eu\np=" + CommandBuilder.ShellQuote(Target) + "\nexec 9>\"$p.arrakis-lock\"\nflock -w 10 9\nbase64 -d > \"$p\"\n" };
            var run = await Invoke(legacy, "", "", evidenceRoot);
            Check(run.Result.ExitCode == 1 && Regex.IsMatch(run.Result.Error, @"flock: (invalid|unrecognized) option.*w"), "BusyBox must reject the real v1 command's -w option.");
            Check(run.TargetBytes.SequenceEqual(Original) && run.Backups == 0, "Unsupported locking must leave the target and remote backups untouched.");
            var failure = await Fail<ConfigurationSaveException>(run);
            Check(failure.Failure.FileState == ConfigurationFileState.VerifiedUnchanged, "Fresh unchanged read must classify as normal failure.");
        });
        add("BusyBox save: compatible kernel lock, atomic replacement, metadata and byte preservation", async () =>
        {
            var run = await Invoke(ConfigurationSaveScript.Build(File, Proposed), "", "", evidenceRoot);
            Check(run.Result.ExitCode == 0, run.Result.Error);
            Check(run.TargetBytes.SequenceEqual(Proposed), "All exact bytes, including comments/unknown keys/line endings, must match.");
            Check(run.Backups == 1 && run.Temporaries == 0 && run.Metadata == "755:1000:65534", "Backup, cleanup and ownership/mode must be retained.");
            var saved = await Reconcile(run); Check(saved.Bytes.SequenceEqual(Proposed), "Host readback must verify the final bytes.");
        });
        add("BusyBox save: real lock contention times out without replacement", async () =>
        {
            var plan = ConfigurationSaveScript.Build(File, Proposed, 1);
            var run = await Invoke(plan, "", "exec 8>\"$p.arrakis-lock\"\nflock -x 8", evidenceRoot);
            Check(run.Result.ExitCode == 75 && run.Backups == 0 && run.TargetBytes.SequenceEqual(Original), "Contended lock must fail closed before backup/write.");
            await Fail<ConfigurationSaveException>(run);
        });
        add("BusyBox save: failed temporary write retains original and rollback copy", async () =>
        {
            var run = await Invoke(ConfigurationSaveScript.Build(File, Proposed), "base64() { echo 'fixture disk full during temporary write' >&2; return 1; }", "", evidenceRoot);
            Check(run.TargetBytes.SequenceEqual(Original) && run.Backups == 1 && run.Temporaries == 0, "Temp failure cannot damage original or remove rollback copies.");
            var failure = await Fail<ConfigurationSaveException>(run); Check(failure.Failure.Step == "temporary-write", "Failed step must be recorded.");
        });
        add("BusyBox save: failed final rename is verified unchanged", async () =>
        {
            var run = await Invoke(ConfigurationSaveScript.Build(File, Proposed), "mv() { echo 'fixture rename denied' >&2; return 1; }", "", evidenceRoot);
            Check(run.TargetBytes.SequenceEqual(Original) && run.Backups == 1, "Rename failure must retain original/backup.");
            var failure = await Fail<ConfigurationSaveException>(run); Check(failure.Failure.Step == "replacement", "Replacement error must be explicit.");
        });
        add("BusyBox save: failed final verification atomically rolls back the original", async () =>
        {
            var hooks = """
                sha256sum() {
                    if [ "$1" = "$p" ] && [ "$stage" = verification ] && [ ! -f /work/verification-failed ]; then
                        : > /work/verification-failed
                        printf 'bad-digest  %s\n' "$1"
                    else /bin/busybox sha256sum "$@"; fi
                }
                """;
            var run = await Invoke(ConfigurationSaveScript.Build(File, Proposed), hooks, "", evidenceRoot);
            Check(run.TargetBytes.SequenceEqual(Original) && run.Backups == 1 && run.Metadata == "755:1000:65534" && run.Result.Output.Contains(":restored"), "Rollback must restore exact original and metadata under the same lock and retain its copy.");
            var failure = await Fail<ConfigurationSaveException>(run); Check(failure.Failure.Explanation.Contains("restored"), "History must report verified rollback.");
        });
        add("BusyBox save: external edit before replacement is preserved and reported as conflict", async () =>
        {
            var hooks = """
                chmod() {
                    /bin/busybox chmod "$@" || return $?
                    if [ "$stage" = metadata ]; then printf 'external writer content\n' > "$p"; fi
                }
                """;
            var run = await Invoke(ConfigurationSaveScript.Build(File, Proposed), hooks, "", evidenceRoot);
            Check(Encoding.UTF8.GetString(run.TargetBytes) == "external writer content\n" && run.Result.ExitCode == 73, "External data must never be replaced by the draft or rollback.");
            var failure = await Fail<ConfigurationSaveException>(run); Check(failure.Failure.FileState == ConfigurationFileState.VerifiedExternalChange, "Pre-replacement conflict is a known failure, not an unresolved manager write.");
        });
        add("BusyBox save: external edit after replacement is not overwritten by rollback", async () =>
        {
            var hooks = """
                mv() {
                    /bin/busybox mv "$@" || return $?
                    if [ "$stage" = replacement ]; then printf 'external edit after replacement\n' > "$p"; fi
                }
                """;
            var run = await Invoke(ConfigurationSaveScript.Build(File, Proposed), hooks, "", evidenceRoot);
            Check(Encoding.UTF8.GetString(run.TargetBytes) == "external edit after replacement\n" && run.Result.Output.Contains("refused-external-change"), "Rollback must refuse an unrecognized current digest.");
            await Fail<ConfigurationSaveUnknownException>(run);
        });
        add("Configuration save: failed fresh readback retains the unknown-outcome guard", async () =>
        {
            await ExpectUnknown(() => ConfigurationSaveTransaction.ExecuteAsync(File, Proposed, () => { }, (plan, _, _) => Task.FromResult(new ProcessResult(0,$"ARRAKIS_CONFIG_RESULT={plan.ReceiptId}:0:complete:1:not-needed\n","")), _ => throw new IOException("fixture readback unavailable"), default, 0));
        });
        add("Configuration save: lost transport with unchanged readback remains genuinely uncertain", async () =>
        {
            var history = new MemoryHistory(); var gate = new OperationCoordinator(history);
            await ExpectUnknown(() => gate.RunAsync("Save UserServerCustomSettings.ini", true, () => ConfigurationSaveTransaction.ExecuteAsync(File, Proposed, () => { }, (_, _, _) => Task.FromResult(new ProcessResult(255, "", "Connection lost")), _ => Task.FromResult(Original), default, 0)));
            Check(gate.NeedsReconciliation && new OperationCoordinator(history).NeedsReconciliation, "Unchanged instant readback cannot clear a potentially running remote writer, including across restart.");
        });
        add("Configuration save: confirmed pre-write failure does not lock later operations", async () =>
        {
            var history = new MemoryHistory(); var gate = new OperationCoordinator(history);
            try { await gate.RunAsync("Save UserServerCustomSettings.ini", true, () => ConfigurationSaveTransaction.ExecuteAsync(File, Proposed, () => { }, (_, _, _) => Task.FromResult(new ProcessResult(1, "", "flock: unrecognized option: w")), _ => Task.FromResult(Original), default, 0)); throw new Exception("Expected known failure."); }
            catch (ConfigurationSaveException) { }
            Check(!gate.NeedsReconciliation && !new OperationCoordinator(history).NeedsReconciliation, "Verified failure should not leave a new unknown guard.");
            Check(await gate.RunAsync("next", true, () => Task.FromResult("allowed")) == "allowed", "Subsequent authorized mutations must be possible after a known failure.");
            var failure = history.Load().Single(h => h.Name.StartsWith("Save "));
            Check(failure.Outcome == "Failed" && failure.Output.Contains("Verified unchanged") && failure.Details.Contains("unrecognized option"), "History must retain concise and technical error information.");
        });
        add("Configuration save: interrupted remote exits remain uncertain despite an unchanged instant read", async () =>
        {
            foreach(var signalExit in new[]{129,130,137,143})
                await ExpectUnknown(()=>ConfigurationSaveTransaction.ExecuteAsync(File,Proposed,()=>{},(plan,_,_)=>Task.FromResult(new ProcessResult(signalExit,$"ARRAKIS_CONFIG_RESULT={plan.ReceiptId}:{signalExit}:replacement:1:original-present\n","fixture interrupted writer")),_=>Task.FromResult(Original),default,0));
        });
        add("Configuration save: verified updated failure is distinguished from unknown transport", async () =>
        {
            try { await ConfigurationSaveTransaction.ExecuteAsync(File, Proposed, () => { }, (_, _, _) => Task.FromResult(new ProcessResult(1, "", "fixture nonzero after replacement")), _ => Task.FromResult(Proposed), default, 0); throw new Exception("Expected failure."); }
            catch (ConfigurationSaveException e) { Check(e.Failure.FileState == ConfigurationFileState.VerifiedUpdated && e.Failure.Recovery.Contains("do not blindly retry"), "A known updated file must be identified without blind resubmission."); }
        });
        add("Configuration save: pre-existing unknown guard is never auto-cleared by this patch", async () =>
        {
            var history = new MemoryHistory(); history.Append(new(DateTimeOffset.Now, "[Safety guard]", "Locked", "v1 unresolved save"));
            var gate = new OperationCoordinator(history); var called = false;
            try { await gate.RunAsync("Save UserServerCustomSettings.ini", true, () => { called = true; return Task.FromResult("bad"); }); throw new Exception("Expected safety rejection."); }
            catch (InvalidOperationException) { }
            Check(!called && gate.NeedsReconciliation && history.Load().Count == 1, "Legacy unknown state must remain untouched until explicit review.");
        });
        add("Configuration save: old history JSON preserves its guard and diagnostic compatibility", async () =>
        {
            var directory=Path.Combine(evidenceRoot,"legacy-history");Directory.CreateDirectory(directory);
            await System.IO.File.WriteAllTextAsync(Path.Combine(directory,"history.json"),"[{\"Timestamp\":\"2026-10-22T19:20:15-04:00\",\"Name\":\"[Safety guard]\",\"Outcome\":\"Locked\",\"Output\":\"v1 unresolved save\"}]");
            var stores=new LocalStores(directory);var before=await System.IO.File.ReadAllBytesAsync(Path.Combine(directory,"history.json"));
            Check(new OperationCoordinator(stores).NeedsReconciliation,"A v1 guard must still block after loading v1.0.1 records.");
            Check(((IHistoryStore)stores).Load().Single().Details=="","Missing v1 diagnostic field must deserialize without dropping history.");
            var after=await System.IO.File.ReadAllBytesAsync(Path.Combine(directory,"history.json"));Check(before.SequenceEqual(after),"Reading legacy history must not migrate/clear it automatically.");
        });
    }

    private static async Task<Invocation> Invoke(ConfigurationSavePlan plan, string hooks, string arrange, string evidenceRoot)
    {
        var script = $$"""
            set -eu
            mkdir -p /work/settings
            p={{CommandBuilder.ShellQuote(Target)}}
            printf '%s' '{{Convert.ToBase64String(Original)}}' | base64 -d > "$p"
            chmod 755 "$p"
            chown 1000:65534 "$p"
            {{arrange}}
            cat > /work/save.sh <<'ARRAKIS_TEST_SCRIPT_{{plan.ReceiptId}}'
            {{hooks}}
            {{plan.Script}}
            ARRAKIS_TEST_SCRIPT_{{plan.ReceiptId}}
            set +e
            printf '%s' '{{Convert.ToBase64String(Proposed)}}' | sh /work/save.sh > /work/output 2> /work/error
            rc=$?
            set -e
            printf 'HARNESS_EXIT=%s\n' "$rc"
            printf 'HARNESS_BYTES='; base64 "$p" | tr -d '\n'; printf '\n'
            printf 'HARNESS_METADATA='; stat -c '%a:%u:%g' "$p"
            printf 'HARNESS_BACKUPS='; find /work/settings -name '*.bak' | wc -l
            printf 'HARNESS_TEMPS='; find /work/settings -type f -name '*.arrakis-*' ! -name '*.arrakis-lock' | wc -l
            cat /work/output
            printf 'HARNESS_ERROR_BEGIN\n'; cat /work/error; printf 'HARNESS_ERROR_END\n'
            """;
        var run = await new ProcessRunner().RunAsync(new("docker.exe", ["run", "--rm", "-i", "--read-only", "--network", "none", "--tmpfs", "/work:rw,nosuid,nodev,size=8m", Image, "sh", "-s"], TimeSpan.FromSeconds(25), script.Replace("\r\n", "\n") + "\n"), null, default);
        run.EnsureSuccess();
        string Field(string key) => Regex.Match(run.Output, "(?m)^" + key + "=(.*)$").Groups[1].Value.Trim();
        var error = Regex.Match(run.Output, "HARNESS_ERROR_BEGIN\\n([\\s\\S]*?)HARNESS_ERROR_END").Groups[1].Value;
        var result = new ProcessResult(int.Parse(Field("HARNESS_EXIT")), run.Output, error);
        var observed = Convert.FromBase64String(Field("HARNESS_BYTES"));
        await System.IO.File.WriteAllTextAsync(Path.Combine(evidenceRoot, "busybox-" + plan.ReceiptId + ".txt"), run.Output + run.Error);
        // Associate the executed script's random receipt with this captured test response.
        plans[result] = plan;
        return new(result, observed, int.Parse(Field("HARNESS_BACKUPS")), int.Parse(Field("HARNESS_TEMPS")), Field("HARNESS_METADATA"));
    }
    private static readonly Dictionary<ProcessResult,ConfigurationSavePlan> plans = [];
    private static Task<ConfigFile> Reconcile(Invocation run) => ConfigurationSaveTransaction.ExecuteAsync(File, Proposed, () => { }, (generated, _, _) =>
    {
        var executed = plans[run.Result];
        return Task.FromResult(run.Result with { Output = run.Result.Output.Replace(executed.ReceiptId, generated.ReceiptId, StringComparison.Ordinal) });
    }, _ => Task.FromResult(run.TargetBytes), default, 0);
    private static async Task<T> Fail<T>(Invocation run) where T : Exception
    {
        try { await Reconcile(run); } catch (T e) { return e; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task ExpectUnknown(Func<Task> action) { try { await action(); } catch (ConfigurationSaveUnknownException) { return; } throw new Exception("Expected unknown outcome."); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class MemoryHistory : IHistoryStore { private readonly List<OperationRecord> entries=[];public IReadOnlyList<OperationRecord> Load()=>entries;public void Append(OperationRecord r)=>entries.Add(r); }
}
