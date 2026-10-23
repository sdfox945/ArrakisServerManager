using System.Text.RegularExpressions;

namespace Arrakis.Core;

public sealed class OperationCoordinator(IHistoryStore history)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool IsBusy { get; private set; }
    public bool NeedsReconciliation { get; private set; }=history.Load().LastOrDefault(r=>r.Name=="[Safety guard]")?.Outcome=="Locked";
    public event Action? Changed;
    public void AcknowledgeReconciliation() { history.Append(new(DateTimeOffset.Now,"[Safety guard]","Cleared","Server state manually reviewed."));NeedsReconciliation = false; Changed?.Invoke(); }
    public async Task<T> RunAsync<T>(string name, bool mutation, Func<Task<T>> work,bool retainHistory=true)
    {
        if (!await gate.WaitAsync(0)) throw new InvalidOperationException("Another operation is already running.");
        try
        {
            if (mutation && NeedsReconciliation) throw new InvalidOperationException("A previous operation has an unknown outcome. Check current server state and acknowledge it in Tools before making another change.");
            IsBusy = true; Changed?.Invoke();
            if(mutation)history.Append(new(DateTimeOffset.Now,"[Safety guard]","Locked","Mutation started. An interrupted application must reconcile server state."));
            try
            {
                var result = await work();
                var detail = result is ActionResult action ? action.Output : result is string s ? s : "Completed";
                if(retainHistory)history.Append(new(DateTimeOffset.Now, name, "Succeeded", Redactor.Clean(detail)));if(mutation)history.Append(new(DateTimeOffset.Now,"[Safety guard]","Cleared","Completion verified.")); return result;
            }
            catch (Exception e)
            {
                if (e is OutcomeUnknownException) NeedsReconciliation = true;
                else if(mutation)history.Append(new(DateTimeOffset.Now,"[Safety guard]","Cleared","Operation reported a known failure."));
                var failure=e as IOperationFailure;
                history.Append(new(DateTimeOffset.Now, name, e is OutcomeUnknownException ? "Unknown outcome" : e is OperationCanceledException ? "Cancelled" : "Failed", Redactor.Clean(failure?.Failure.Summary??e.Message), failure?.Failure.Details??""));
                throw;
            }
        }
        finally { IsBusy = false; gate.Release(); Changed?.Invoke(); }
    }
}
public static partial class Redactor
{
    public static string Clean(string text)
    {
        text = Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
        text = Regex.Replace(text, @"(?i)((?:password|passwd|token|secret|ServiceAuthToken|ServerLoginPassword)[\w]*[\s\""']*[:=][\s\""']*)[^\s,\""';]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[REDACTED JWT]");
        text = Regex.Replace(text, @"-----BEGIN [^-]*PRIVATE KEY-----[\s\S]*?-----END [^-]*PRIVATE KEY-----", "[REDACTED PRIVATE KEY]");
        return text.Length > 60000 ? text[^60000..] : text;
    }
}
public static class HealthRules
{
    public static IReadOnlyList<string> Evaluate(ServerSnapshot s, Preferences p) => HealthAssessment.Notices(s,p,DateTimeOffset.Now).Select(n=>n.ToString()).Distinct().ToArray();
}