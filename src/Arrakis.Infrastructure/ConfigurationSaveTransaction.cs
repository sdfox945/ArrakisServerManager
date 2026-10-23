using System.Security.Cryptography;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public static class ConfigurationSaveTransaction
{
    public static async Task<ConfigFile> ExecuteAsync(
        ConfigFile original, byte[] proposed, Action saveLocalBackup,
        Func<ConfigurationSavePlan, string, CancellationToken, Task<ProcessResult>> execute,
        Func<CancellationToken, Task<byte[]>> readback, CancellationToken token, int lockWaitSeconds = 10)
    {
        ConfigurationIntegrity.ValidateProposal(original, (original with { Bytes = proposed }).Text);
        var plan = ConfigurationSaveScript.Build(original, proposed, lockWaitSeconds);
        ProcessResult? result = null;
        Exception? failure = null;
        var submitted = false;
        try
        {
            saveLocalBackup();
            token.ThrowIfCancellationRequested();
            submitted = true;
            result = await execute(plan, Convert.ToBase64String(proposed), token);
        }
        catch (Exception e) { failure = e; }

        // Reconciliation is a separate bounded read, even if the mutation token/transport failed.
        byte[]? observed = null;
        Exception? readFailure = null;
        using (var reconciliation = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            try { observed = await readback(reconciliation.Token); }
            catch (Exception e) { readFailure = e; }
        }
        var receipt = ConfigurationSaveScript.Receipt(plan, result?.Output ?? "");
        // Signal termination can leave a foreground utility alive with an inherited lock FD.
        // A trap receipt for HUP/INT/TERM therefore does not prove the writer is quiescent.
        var terminal = !submitted || (receipt is not null
            ? receipt.ExitCode is >= 0 and < 128
            : result is { ExitCode: >= 0 and < 128 });
        var unchanged = observed is not null && observed.SequenceEqual(original.Bytes);
        var updated = observed is not null && observed.SequenceEqual(proposed);
        if (result?.ExitCode == 0 && receipt is { ExitCode: 0, Stage: "complete" } && updated)
            return new(original.Name, original.RemotePath, observed!, plan.ProposedHash);

        var state = unchanged ? ConfigurationFileState.VerifiedUnchanged : updated ? ConfigurationFileState.VerifiedUpdated : ConfigurationFileState.Uncertain;
        var knownFailure = terminal && (unchanged || updated || receipt is { ReplacementAttempted: false } && observed is not null);
        if (knownFailure && !unchanged && !updated) state = ConfigurationFileState.VerifiedExternalChange;
        var stage = !submitted ? "local backup / preparation" : receipt?.Stage ?? "remote configuration command";
        var underlying = result is { ExitCode: not 0 }
            ? $"Remote save exited {result.ExitCode}: {Redactor.Clean(result.Error).Trim()}"
            : failure?.Message ?? readFailure?.Message ?? "The final file or completion receipt did not match the reviewed save.";
        var explanation = receipt?.Rollback == "restored"
            ? "The remote verification failed and the original was atomically restored from its rollback copy."
            : !terminal ? "The SSH operation may still be running; a readback alone cannot prove it has finished."
            : unchanged ? "A fresh readback matched the loaded original; the proposed edit is not present."
            : updated ? "A fresh readback matched the proposal, but the command reported a failure. Do not retry the write."
            : receipt is { ReplacementAttempted: false } ? "An external change was detected before replacement. This save did not replace the target."
            : "The final state could not be established; automatic rollback would risk overwriting another change.";
        var recovery = knownFailure
            ? updated ? "Reload configuration and verify the reviewed values; do not blindly retry."
            : "Reload the current configuration, review the failure details, and retry only after resolving the cause."
            : "Inspect the current file, remote process and rollback copies; then explicitly acknowledge review in Tools. Do not retry or clear the safety guard automatically.";
        var details = $"File: {original.RemotePath}\nReceipt: {plan.ReceiptId}\nStage: {stage}\nTransport exit: {result?.ExitCode.ToString() ?? "not returned"}\nRemote terminal receipt: {receipt?.ToString() ?? "not received"}\nOriginal SHA-256: {plan.OriginalHash}\nProposed SHA-256: {plan.ProposedHash}\nObserved SHA-256: {(observed is null ? "unavailable" : Convert.ToHexStringLower(SHA256.HashData(observed)))}\n\nRemote stderr:\n{result?.Error}\nRemote protocol output:\n{result?.Output}\nTransport error:\n{failure}\nReadback error:\n{readFailure}";
        var diagnostic = new OperationFailure(original.Name, stage, explanation, underlying, state, recovery, Redactor.Clean(details));
        if (knownFailure) throw new ConfigurationSaveException(diagnostic);
        throw new ConfigurationSaveUnknownException(diagnostic);
    }
}
