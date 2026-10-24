using System.Globalization;

namespace Arrakis.Core;

public sealed record NumericSettingRange(double Minimum, double Maximum, double Increment, bool Integer)
{
    public static NumericSettingRange? For(SettingDefinition? setting)
    {
        if (setting?.Kind is not ("number" or "integer") || setting.Min is not {} min || setting.Max is not {} max || !double.IsFinite(min) || !double.IsFinite(max) || max <= min) return null;
        var desired = (max - min) / 100;
        var power = Math.Pow(10, Math.Floor(Math.Log10(desired)));
        var ratio = desired / power;
        var step = (ratio <= 1 ? 1 : ratio <= 2 ? 2 : ratio <= 5 ? 5 : 10) * power;
        if (setting.Kind == "integer") step = 1;
        if(setting.Step is {} preferred&&double.IsFinite(preferred)&&preferred>0)step=preferred;
        return new(min, max, step, setting.Kind == "integer");
    }
    public double Position(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : Minimum;
    public string Choose(double value) => Math.Clamp(Math.Round(value / Increment, MidpointRounding.AwayFromZero) * Increment, Minimum, Maximum).ToString(Integer ? "0" : "0.###############", CultureInfo.InvariantCulture);
}

public static class OperationalRules
{
    public static bool Allows(ServerSnapshot snapshot, ServerAction action) => action switch
    {
        ServerAction.StartVm => snapshot.Vm.State == "Off" && snapshot.Vm.Error == null,
        ServerAction.ShutdownVm or ServerAction.ForceOffVm => snapshot.Vm.State == "Running" && snapshot.Vm.Error == null,
        ServerAction.StartBattlegroup => snapshot.SshConnected && snapshot.Battlegroup == "Stopped",
        ServerAction.StopBattlegroup or ServerAction.RestartBattlegroup => snapshot.SshConnected && snapshot.Battlegroup is "Healthy" or "Running" or "Degraded" or "Failed" or "Error",
        ServerAction.Import => snapshot.SshConnected && snapshot.Battlegroup == "Stopped",
        _ => snapshot.SshConnected && snapshot.Namespace != null
    };
    public static string Duration(string text)
    {
        if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span)) return text;
        var parts = new List<string>();
        if (span.Days > 0) parts.Add($"{span.Days} day{(span.Days == 1 ? "" : "s")}");
        if (span.Hours > 0) parts.Add($"{span.Hours} hour{(span.Hours == 1 ? "" : "s")}");
        if (span.Minutes > 0) parts.Add($"{span.Minutes} minute{(span.Minutes == 1 ? "" : "s")}");
        return parts.Count == 0 ? "Less than a minute" : string.Join(", ", parts);
    }
    public static string HealthHeadline(ServerSnapshot snapshot)
    {
        if(!snapshot.SshConnected)return snapshot.Vm.State=="Off"?"Core services stopped — VM confirmed off":"Core services unavailable — SSH disconnected";
        var maps = snapshot.Maps.Count(m => m.Workload == WorkloadKind.GameMap && m.HasCurrentProblem);
        var services = snapshot.Maps.Count(m => m.Workload == WorkloadKind.Infrastructure && m.HasCurrentProblem);
        var unknown=snapshot.Maps.Count(m=>m.Workload==WorkloadKind.Unknown&&m.HasCurrentProblem);
        return (snapshot.Battlegroup == "Healthy" ? "Core services healthy" : "Core services: " + snapshot.Battlegroup) +
            (maps > 0 ? $" — {maps} map{(maps == 1 ? " requires" : "s require")} attention" : " — no current map failures observed") +
            (services > 0 ? $" · {services} infrastructure issue(s)" : "")+(unknown>0?$" · {unknown} unclassified workload issue(s); inspect All":"");
    }
    public static bool CloudDestination(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(p => p.StartsWith("OneDrive",StringComparison.OrdinalIgnoreCase) || p.Equals("Dropbox",StringComparison.OrdinalIgnoreCase) || p.Equals("Google Drive",StringComparison.OrdinalIgnoreCase) || p.Equals("iCloudDrive",StringComparison.OrdinalIgnoreCase));
}

public sealed record HealthNotice(string Severity, string Target, string Explanation, string Investigation, DateTimeOffset Observed, bool Current = true)
{
    public string Heading => $"{Severity} · {Target} · {(Current ? "Current" : "Historical")} · observed {Observed:HH:mm:ss}";
    public override string ToString() => Heading + " — " + Explanation + " " + Investigation;
}

public static class HealthAssessment
{
    public static IReadOnlyList<HealthNotice> Notices(ServerSnapshot s, Preferences p, DateTimeOffset now)
    {
        var notices = new List<HealthNotice>();
        void Add(string target,string detail,string action,string severity="Warning",bool current=true) => notices.Add(new(severity,target,detail,action,now,current));
        foreach (var issue in s.Issues.Where(i=>!string.IsNullOrWhiteSpace(i)).Distinct()) Add("Infrastructure", issue, "Inspect connection and diagnostics.");
        if (s.Host.AvailableBytes / 1073741824.0 < p.LowHostAvailableGb) Add("Windows host", "Available RAM is below your threshold.", "Inspect host processes and actual usage.");
        if (s.Guest?.MemoryPercent >= p.GuestMemoryWarningPercent) Add("Linux guest", "Memory usage is above your threshold.", "Inspect actual pod usage and available memory.");
        if (s.Guest?.SwapPercent >= p.SwapWarningPercent) Add("Linux swap", "Swap usage is above your threshold.", "Inspect memory pressure and map startup latency.");
        if (s.Vm.DemandBytes > s.Vm.AssignedBytes) Add("Hyper-V", "Reported demand exceeds assigned RAM.", "Inspect VM memory and host availability.");
        foreach (var map in s.Maps)
        {
            if (map.HasCurrentProblem) Add(map.Map, map.State + (map.CurrentOom || map.OomKilled ? " · OOMKilled recorded" : "") + ": " + map.CurrentWarning, "Open this instance's logs and events.", map.HealthIsUnknown?"Warning":"Error");
            else if(map.Evidence?.RecoveredJobAttempt==true) Add(map.Map,"An earlier Job attempt failed; its owning Job completed successfully.","Inspect retained attempt details and historical events.","Information",false);
            else if (map.OomKilled) Add(map.Map, "An earlier container terminated with OOMKilled; current status: "+map.State+".", "Review historical events and restart count.", "Information", false);
        }
        foreach (var e in s.Events.Where(e => e.Type == "Warning" && e.Actionable == true).Take(10)) Add(e.Object, e.Reason + ": " + e.Message, "Inspect current object state and event details.");
        return notices;
    }
}

public sealed record ResourceSample(DateTimeOffset Time, double? HostRam, double? GuestRam, double? Swap);
public sealed class ResourceHistory
{
    private readonly List<ResourceSample> samples = [];
    public IReadOnlyList<ResourceSample> Samples => samples;
    public void Add(ServerSnapshot s, DateTimeOffset time)
    {
        if (samples.Count > 0 && time <= samples[^1].Time) return;
        samples.Add(new(time, s.Host.TotalBytes > 0 && s.Host.AvailableBytes != null ? s.Host.UsedPercent : null,
            s.Guest?.TotalBytes > 0 && s.Guest.AvailableBytes != null ? s.Guest.MemoryPercent : null,
            s.Guest?.SwapTotalBytes > 0 ? s.Guest.SwapPercent : null));
        samples.RemoveAll(x => x.Time < time.AddMinutes(-30));
        if (samples.Count > 361) samples.RemoveRange(0, samples.Count - 361);
    }
    public void Clear() => samples.Clear();
}
