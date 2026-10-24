using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Arrakis.Core;

public sealed class Preferences
{
    public bool AutomaticallyCheckApplicationUpdates { get; set; } = true;
    public string InstallationDirectory { get; set; } = @"C:\Program Files (x86)\Steam\steamapps\common\Dune Awakening Self-Hosted Server";
    public string VmName { get; set; } = "dune-awakening";
    public string SshKeyPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuneAwakeningServer", "sshKey");
    public string ManualIp { get; set; } = "";
    public int RefreshSeconds { get; set; } = 15;
    public double LowHostAvailableGb { get; set; } = 4;
    public double GuestMemoryWarningPercent { get; set; } = 90;
    public double SwapWarningPercent { get; set; } = 65;
    public string BackupDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arrakis", "Backups");
    public string LogDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arrakis", "Diagnostics");
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 860;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool Maximized { get; set; }
    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(VmName) || VmName.Length > 80 || VmName.Any(char.IsControl)) yield return "Enter a VM name (1–80 characters).";
        if (ManualIp.Length > 0 && (!IPAddress.TryParse(ManualIp, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)) yield return "Manual address must be an IPv4 address.";
        if (RefreshSeconds is < 5 or > 300) yield return "Refresh must be 5–300 seconds.";
        if (LowHostAvailableGb is < 0.5 or > 128) yield return "Host warning must be 0.5–128 GB.";
        if (GuestMemoryWarningPercent is < 10 or > 100 || SwapWarningPercent is < 10 or > 100) yield return "Pressure thresholds must be 10–100%.";
        foreach (var path in new[] { InstallationDirectory, SshKeyPath, BackupDirectory, LogDirectory })
            if (!Path.IsPathFullyQualified(path)) yield return "All configured file and directory paths must be absolute.";
    }
}

public record HostStatus(long? TotalBytes = null, long? AvailableBytes = null, double? CpuPercent = null, bool? GameRunning = null, string? Error = null)
{
    public double UsedPercent => Units.Percent(TotalBytes - AvailableBytes, TotalBytes);
}
public record VmStatus(string Name, string State = "Unavailable", long? ConfiguredBytes = null, long? AssignedBytes = null, long? DemandBytes = null, string Uptime = "Unavailable", string? Ip = null, string Adapter = "Unavailable", string? Error = null);
public record GuestStatus(long? TotalBytes = null, long? AvailableBytes = null, long? SwapTotalBytes = null, long? SwapUsedBytes = null, double? CpuPercent = null, long? DiskTotalBytes = null, long? DiskUsedBytes = null)
{
    public double MemoryPercent => Units.Percent(TotalBytes - AvailableBytes, TotalBytes);
    public double SwapPercent => Units.Percent(SwapUsedBytes, SwapTotalBytes);
}
public enum WorkloadKind { Unknown, GameMap, Infrastructure }
public record WorkloadEvidence(string PodPhase = "", string OwnerKind = "", string OwnerName = "", string JobCondition = "", bool SuccessfulTermination = false, bool CurrentFailure = false, int? DesiredReplicas = null, int UnmetRequiredInstances = 0, string Details = "", bool RecoveredJobAttempt = false, string OwnerUid = "");
public record MapInstance(string Map, string Pod, string Namespace, string State, string Request, string Limit, long? UsedBytes, int Restarts, string Error, bool OomKilled, int? Minimum = null, int SetIndex = -1, string ResourceVersion = "", WorkloadKind Kind = WorkloadKind.Unknown, bool? Ready = null, bool CurrentOom = false, WorkloadEvidence? Evidence = null)
{
    public WorkloadKind Workload => SetIndex >= 0 ? WorkloadKind.GameMap : Kind;
    public bool IsSuccessfullyCompleted => (State=="Succeeded"||(State=="Completed"&&Evidence?.SuccessfulTermination==true)) && (Evidence==null||Evidence.SuccessfulTermination) && Evidence?.CurrentFailure!=true && !CurrentOom;
    public bool HealthIsUnknown => State is "Unknown" or "Unavailable";
    public bool HasCurrentProblem
    {
        get
        {
            if(Evidence?.UnmetRequiredInstances>0)return true;
            if(Evidence?.RecoveredJobAttempt==true)return false;
            if(Evidence?.CurrentFailure==true||CurrentOom)return true;
            if(IsSuccessfullyCompleted)return false;
            return State.Contains("Allocat",StringComparison.OrdinalIgnoreCase)||State.Contains("Pending",StringComparison.OrdinalIgnoreCase)||State.Contains("Crash",StringComparison.OrdinalIgnoreCase)||State.Contains("Failed",StringComparison.OrdinalIgnoreCase)||State.Contains("Error",StringComparison.OrdinalIgnoreCase)||State=="Startup"||HealthIsUnknown||Ready==false||(Pod=="—"&&Minimum>0);
        }
    }
    public string CurrentWarning => !HasCurrentProblem ? "" : string.Join(" ",new[]{
        Evidence?.UnmetRequiredInstances>0?$"{Evidence.UnmetRequiredInstances} required instance(s) not currently available.":"",
        Error.Trim(),
        Ready==false&&!IsSuccessfullyCompleted?"Pod is not ready.":"",
        HealthIsUnknown?"Current workload health is unconfirmed; inspect its status.":""
    }.Where(s=>s.Length>0).DefaultIfEmpty($"{State}: inspect current workload status, logs and events."));
    public string HealthDetail => HasCurrentProblem ? "Current warning: "+CurrentWarning : Evidence?.RecoveredJobAttempt==true ? "Historical failed attempt; owning Job completed successfully." : IsSuccessfullyCompleted ? "Completed successfully; no current warning." : State=="Idle / On demand" ? "Idle / On demand: configured minimum and desired replicas are both zero." : string.IsNullOrWhiteSpace(Error) ? "No current warning." : "Historical detail: "+Error;
    public string Usage => Units.Bytes(UsedBytes);
    public string MinimumDisplay => Minimum?.ToString(CultureInfo.InvariantCulture) ?? "Not set";
}
public record ClusterEvent(string Namespace, string Object, string Reason, string Message, string Type, string Time, string FirstTime = "", int Count = 1, string CurrentStatus = "Unknown", bool? Actionable = null)
{
    public string Relevance => Actionable == true ? "Current" : Actionable == false ? "Historical / recovered" : "Unconfirmed — inspect current object";
    public string Details => $"{Type}: {Reason}\n{Namespace}/{Object}\nFirst observed: {(FirstTime.Length==0?"Unavailable":FirstTime)}\nLast observed: {(Time.Length==0?"Unavailable":Time)}\nOccurrences: {Count}\nCurrent object: {CurrentStatus}\n{Relevance}\n{Message}";
}
public record ServerSnapshot(HostStatus Host, VmStatus Vm, GuestStatus? Guest, bool SshConnected, string Battlegroup, string? Namespace, string? BattlegroupName, IReadOnlyList<MapInstance> Maps, IReadOnlyList<ClusterEvent> Events, IReadOnlyList<string> Issues, string Revision = "Unavailable", int? DirectorPort = null, string CliStatus = "Unavailable", string KubernetesStatus = "Unavailable")
{
    public static ServerSnapshot Empty(string name) => new(new(), new(name), null, false, "Unavailable", null, null, [], [], []);
}
public enum ServerAction { StartVm, ShutdownVm, ForceOffVm, StartBattlegroup, StopBattlegroup, RestartBattlegroup, Update, Backup, Import, ExportBattlegroupLogs, ExportOperatorLogs }
public enum MaintenanceAction { VmNetworking, VmPassword, ExperimentalSwap }
public record ActionResult(string Output, bool Verified, string? Artifact = null);
public record BackupArtifact(string Name, string Path, long Size, DateTimeOffset Modified, string Category = "Database", string Sha256 = "")
{
    public string SizeDisplay => Units.Bytes(Size);
}
public record ConfigFile(string Name, string RemotePath, byte[] Bytes, string Hash)
{
    private (System.Text.Encoding Encoding,int Preamble) Format=>Bytes.AsSpan().StartsWith(new byte[]{239,187,191})?(new System.Text.UTF8Encoding(false,true),3):Bytes.AsSpan().StartsWith(new byte[]{255,254})?(new System.Text.UnicodeEncoding(false,false,true),2):Bytes.AsSpan().StartsWith(new byte[]{254,255})?(new System.Text.UnicodeEncoding(true,false,true),2):(new System.Text.UTF8Encoding(false,true),0);
    public string Text {get{var (encoding,preamble)=Format;return encoding.GetString(Bytes.AsSpan(preamble));}}
    public bool HasBom => Format.Preamble>0;
    public byte[] Encode(string text) {var (encoding,preamble)=Format;return Bytes.Take(preamble).Concat(encoding.GetBytes(text)).ToArray();}
}
public record MapChange(MapInstance Original, string MemoryRequest, string MemoryLimit, int? Minimum)
{
    public void Validate()
    {
        var request = Units.Quantity(MemoryRequest); var limit = Units.Quantity(MemoryLimit);
        if (request is null || limit is null || request <= 0 || limit <= 0 || request > limit) throw new InvalidOperationException("Use positive Kubernetes memory quantities (e.g. 200Mi, 2Gi); request must not exceed limit.");
        if (Minimum is < 0 or > 100) throw new InvalidOperationException("Minimum instances must be 0–100.");
        if (Original.SetIndex < 0 || string.IsNullOrEmpty(Original.ResourceVersion)) throw new InvalidOperationException("Unsupported or stale map schema; reload or use the official editor.");
        if (Original.Workload != WorkloadKind.GameMap) throw new InvalidOperationException("Infrastructure resources are not editable through game-map controls.");
    }
}
public static class Units
{
    public static string Bytes(long? value) => value is null ? "Unavailable" : value >= 1L<<30 ? $"{value / 1073741824.0:0.0} GiB" : value>=1L<<20?$"{value / 1048576.0:0} MiB":value>=1024?$"{value / 1024.0:0.0} KiB":$"{value} B";
    public static string PercentText(double? value) => value is null ? "Unavailable" : $"{value:0.0}%";
    public static double Percent(long? used, long? total) => used is null || total is null || total <= 0 ? 0 : Math.Clamp(100.0 * used.Value / total.Value, 0, 100);
    public static long? Quantity(string? text)
    {
        var m = Regex.Match(text ?? "", @"^([0-9]+(?:\.[0-9]+)?)(Ki|Mi|Gi|Ti|K|M|G|T|k)?$");
        if (!m.Success || !double.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var n)) return null;
        var suffix = m.Groups[2].Value; var power = suffix.Length == 0 ? 0 : "KMGT".IndexOf(char.ToUpperInvariant(suffix[0])) + 1;
        var result = n * Math.Pow(suffix.EndsWith('i') ? 1024 : 1000, power);
        return result >= 0 && result < long.MaxValue ? (long)result : null;
    }
}
