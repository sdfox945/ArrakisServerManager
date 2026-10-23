using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Desktop.ViewModels;

public sealed partial class ShellViewModel
{
    private readonly ResourceHistory resourceHistory=new();
    private DateTimeOffset? lastSuccessfulRefresh;
    private string? refreshError;
    public string RefreshState=>refreshError!=null?"Stale data — refresh failed: "+refreshError:lastSuccessfulRefresh is {} t?$"Last refresh: {t:HH:mm:ss} · "+(Snapshot.SshConnected?"connected":Snapshot.Vm.State=="Off"?"VM confirmed off":"partial — guest unavailable"):"No successful refresh yet";
    public string VmAccess=>Snapshot.Vm.Error==null?"Verified "+Snapshot.Vm.State:(new[]{"denied","authorization","permission","privilege"}.Any(word=>Snapshot.Vm.Error.Contains(word,StringComparison.OrdinalIgnoreCase))?"Hyper-V access denied":"Hyper-V unavailable")+(Snapshot.SshConnected?" · guest reachable through SSH":"");
    public string VmTechnicalDetails=>Snapshot.Vm.Error??Snapshot.Vm.Adapter;
    public string CorePhase=>Snapshot.Battlegroup;
    public string AdminStatus=>WindowsHost.IsAdministrator?"Administrator · direct Hyper-V access permitted by Windows policy":"Standard user · use Launch as Administrator.cmd for direct Hyper-V access; Demo requires no elevation.";
    public bool CanTakeAction(ServerAction action)=>CanOperate&&!operations.NeedsReconciliation&&OperationalRules.Allows(Snapshot,action)&&(action!=ServerAction.Import||SelectedBackup?.Category=="Database");
    public string[] MapModes=>["Game Maps","Infrastructure","All"];
    private string mapMode="Game Maps";
    public string MapMode {get=>mapMode;set{if(value!=null&&Set(ref mapMode,value))MapsView.Refresh();}}
    public string MapValidation {get{try{ProposedMapChange();return "Review required. Reconciliation/restart may be needed; actual pod usage is separate from configured limits.";}catch(Exception e){return e.Message;}}}
    public bool CanEditMapResources=>CanOperate&&SelectedMap is {Workload:WorkloadKind.GameMap,SetIndex:>=0} map&&Units.Quantity(map.Request)!=null&&Units.Quantity(map.Limit)!=null;
    public bool CanApplyMap
    {
        get
        {
            if(!CanOperate||operations.NeedsReconciliation)return false;
            try{var c=ProposedMapChange();return Units.Quantity(c.MemoryRequest)!=Units.Quantity(c.Original.Request)||Units.Quantity(c.MemoryLimit)!=Units.Quantity(c.Original.Limit)||c.Minimum!=c.Original.Minimum;}catch{return false;}
        }
    }
    private MapChange ProposedMapChange()
    {
        var map=SelectedMap??throw new InvalidOperationException("Select a game map to edit supported resources.");
        if(map.Workload!=WorkloadKind.GameMap||map.SetIndex<0)throw new InvalidOperationException("This resource has no verified editable game-map relationship. Use the official editor.");
        if(Units.Quantity(map.Request)==null||Units.Quantity(map.Limit)==null)throw new InvalidOperationException("Original resources are unavailable. Refresh or use the official editor.");
        int? min=null;
        if(MapMinimum.Length>0){if(!int.TryParse(MapMinimum,NumberStyles.None,CultureInfo.InvariantCulture,out var parsed))throw new InvalidOperationException("Minimum instances must be a whole number from 0 to 100.");min=parsed;}
        else if(map.Minimum!=null)throw new InvalidOperationException("Enter the minimum instance count; unavailable text is not a number.");
        var change=new MapChange(map,MapRequest,MapLimit,min);change.Validate();return change;
    }
    private void MapInputChanged(){OnPropertyChanged(nameof(MapValidation));OnPropertyChanged(nameof(CanApplyMap));OnPropertyChanged(nameof(CanEditMapResources));CommandManager.InvalidateRequerySuggested();}
    public ObservableCollection<HealthNotice> Notices {get;}=[];
    public string NoticeSummary=>Notices.Count==0?"No current warnings detected in available measurements.":$"{Notices.Count(n=>n.Current)} current notice(s) · {Notices.Count(n=>!n.Current)} historical notice(s). Select a notice to investigate.";
    public HealthNotice? SelectedNotice {get;set;}
    public RelayCommand FocusNoticeCommand {get;}
    public RelayCommand MapEventsCommand {get;}
    private int diagnosticTab;
    public int DiagnosticTab {get=>diagnosticTab;set=>Set(ref diagnosticTab,value);}
    public void OpenNotice(HealthNotice? notice=null)
    {
        notice??=SelectedNotice;
        var map=Maps.FirstOrDefault(m=>m.Map==notice?.Target||m.Pod==notice?.Target);
        if(map!=null){MapMode=map.Workload==WorkloadKind.GameMap?"Game Maps":map.Workload==WorkloadKind.Infrastructure?"Infrastructure":"All";MapSearch="";MapFilter="All states";SelectedMap=map;SelectedPage=1;}
        else {DiagnosticTab=1;SelectedPage=4;SelectedEvent=Events.FirstOrDefault(e=>e.Object==notice?.Target)??Events.FirstOrDefault();}
    }
    private ClusterEvent? selectedEvent;
    public ClusterEvent? SelectedEvent {get=>selectedEvent;set{if(Set(ref selectedEvent,value))OnPropertyChanged(nameof(SelectedEventDetails));}}
    public string SelectedEventDetails=>SelectedEvent?.Details??"Select an event to inspect and copy its timestamps, recurrence and current relevance.";
    public int[] HistoryWindows=>[15,30];
    private int historyMinutes=15;
    public int HistoryMinutes {get=>historyMinutes;set{if(value is 15 or 30&&Set(ref historyMinutes,value)){OnPropertyChanged(nameof(HistorySamples));OnPropertyChanged(nameof(HistoryCaption));}}}
    public IReadOnlyList<ResourceSample> HistorySamples=>resourceHistory.Samples.Where(s=>s.Time>=(resourceHistory.Samples.LastOrDefault()?.Time??DateTimeOffset.Now).AddMinutes(-HistoryMinutes)).ToArray();
    public string HistoryCaption=>$"Recent {HistoryMinutes} minutes · {HistorySamples.Count} samples · 0–100% · gaps mean unavailable · refresh interval {Preferences.RefreshSeconds}s";
    private void ResetMonitoring(){resourceHistory.Clear();lastSuccessfulRefresh=null;refreshError=null;backupChecked=false;latestLocalVerified=false;OnPropertyChanged(nameof(HistorySamples));OnPropertyChanged(nameof(RefreshState));OnPropertyChanged(nameof(BackupSummary));}
    private void AcceptSnapshot(ServerSnapshot snapshot,DateTimeOffset now)
    {
        var previous=SelectedMap;var request=MapRequest;var limit=MapLimit;var minimum=MapMinimum;
        var draft=previous!=null&&(request!=previous.Request||limit!=previous.Limit||minimum!=(previous.Minimum?.ToString(CultureInfo.InvariantCulture)??""));
        Snapshot=snapshot;lastSuccessfulRefresh=now;refreshError=null;resourceHistory.Add(snapshot,now);
        Maps.Clear();foreach(var map in snapshot.Maps)Maps.Add(map);
        SelectedMap=Maps.FirstOrDefault(m=>m.Pod==previous?.Pod&&m.Map==previous.Map&&m.Namespace==previous.Namespace)??Maps.FirstOrDefault(m=>m.Workload==WorkloadKind.GameMap)??Maps.FirstOrDefault();
        if(draft&&SelectedMap?.Map==previous?.Map&&SelectedMap?.Namespace==previous?.Namespace){MapRequest=request;MapLimit=limit;MapMinimum=minimum;}
        Events.Clear();foreach(var e in snapshot.Events)Events.Add(e);SelectedEvent=Events.FirstOrDefault();
        Notices.Clear();foreach(var n in HealthAssessment.Notices(snapshot,activePreferences,now))Notices.Add(n);
        Warnings.Clear();foreach(var n in Notices)Warnings.Add(n.ToString());if(Warnings.Count==0)Warnings.Add("No current resource warnings detected in available measurements.");
        foreach(var name in new[]{nameof(HostRam),nameof(HostAvailable),nameof(HostCpu),nameof(ClientState),nameof(VmName),nameof(VmState),nameof(VmMemory),nameof(VmUptime),nameof(VmAddress),nameof(VmAdapter),nameof(VmAccess),nameof(GuestRam),nameof(GuestAvailable),nameof(GuestSwap),nameof(GuestCpu),nameof(GuestDisk),nameof(ConnectionState),nameof(BattlegroupState),nameof(Revision),nameof(InstanceSummary),nameof(HostRamPercent),nameof(VmRamPercent),nameof(GuestRamPercent),nameof(SwapPercent),nameof(HealthSummary),nameof(MapEmptyMessage),nameof(ConnectionSummary),nameof(RefreshState),nameof(HistorySamples),nameof(HistoryCaption),nameof(MapDetails)})OnPropertyChanged(name);
        OnPropertyChanged(nameof(CorePhase));OnPropertyChanged(nameof(VmTechnicalDetails));OnPropertyChanged(nameof(NoticeSummary));MapInputChanged();CommandManager.InvalidateRequerySuggested();
    }
    public string[] LogSeverities=>["All severities","Error","Warning","Info"];
    private string logSeverity="All severities",logStatus="Choose a source and Read logs to retrieve a bounded tail.";
    public string LogSeverity {get=>logSeverity;set{if(value!=null&&Set(ref logSeverity,value)){OnPropertyChanged(nameof(FilteredLogs));OnPropertyChanged(nameof(LogEmptyMessage));}}}
    public string LogStatus {get=>logStatus;private set=>Set(ref logStatus,value);}
    public bool AutoScroll {get;set;}=true;
    public string LogEmptyMessage=>string.IsNullOrWhiteSpace(FilteredLogs)?"No matching log lines. Select a source, read logs, or clear search/severity filters.":"";
    private bool MatchesLogSeverity(string line)
    {
        if(LogSeverity=="All severities")return true;
        var match=System.Text.RegularExpressions.Regex.Match(line,@"(?i)(?:\[(?<level>ERROR|FATAL|CRITICAL|WARN(?:ING)?|INFO(?:RMATION)?)\]|^\s*(?<level>ERROR|FATAL|CRITICAL|WARN(?:ING)?|INFO(?:RMATION)?)\b[:\s]|:\s*(?<level>ERROR|WARNING|FATAL)\s*:)");
        var level=match.Groups["level"].Value.ToUpperInvariant();
        return LogSeverity=="Error"?level is "ERROR" or "FATAL" or "CRITICAL":LogSeverity=="Warning"?level is "WARN" or "WARNING":level is "INFO" or "INFORMATION";
    }
    private Task ReadLogsWithState()=>RunAsync("Read "+LogSource+" logs",false,async token=>
    {
        LogStatus="Reading "+LogSource+"…";
        try{LogText=LogSource=="Application operations"?string.Join("\n\n",History.Select(h=>$"{h.Timestamp:u} {h.Name} [{h.Outcome}]\n{h.Output}")):await Gateway.ReadLogsAsync(LogSource,SelectedMap,token);LogStatus=$"{LogSource} · retrieved {DateTimeOffset.Now:HH:mm:ss} · bounded to 60 KB; source timestamps retained";return LogStatus;}
        catch(OperationCanceledException){LiveLogs=false;LogStatus="Log read cancelled; live tail stopped.";throw;}
        catch(Exception e){LogStatus="Log retrieval failed: "+Redactor.Clean(e.Message)+". Previously displayed lines retained.";throw;}
    });
    private bool backupChecked,latestLocalVerified;
    public string CloudBackupWarning=>OperationalRules.CloudDestination(Preferences.BackupDirectory)?"Possible cloud-synchronized backup destination: archives may be large and contain sensitive data. Choose deliberately; the destination has not been changed.":"";
    public string BackupSummary
    {
        get
        {
            var last=Backups.Where(b=>b.Category=="Database"&&b.Size>0).OrderByDescending(b=>b.Modified).FirstOrDefault();
            var op=History.FirstOrDefault(h=>h.Name==ServerAction.Backup.ToString()||h.Name=="Download database backup");
            return (!backupChecked?"Database backup inventory has not been checked. Refresh list.":last==null?"No nonempty database backup was found. Configuration copies do not protect database progress.":$"Latest database artifact: {last.Name}\n{last.Modified:yyyy-MM-dd HH:mm:ss zzz} · {last.SizeDisplay}\nRemote: {last.Path}\nLocal copy: {(latestLocalVerified?"present; size and SHA-256 verified":"no verified matching copy found")}")+"\nLatest backup operation: "+(op==null?"None recorded":$"{op.Outcome} · {op.Timestamp:yyyy-MM-dd HH:mm:ss}");
        }
    }
    private async Task UpdateBackupSummary(CancellationToken token)
    {
        backupChecked=true;var latest=Backups.Where(b=>b.Category=="Database"&&b.Size>0).OrderByDescending(b=>b.Modified).FirstOrDefault();
        latestLocalVerified=latest!=null&&await BackupVerification.VerifyAsync(latest,EffectivePreferences.BackupDirectory,token);
        OnPropertyChanged(nameof(BackupSummary));OnPropertyChanged(nameof(CloudBackupWarning));
    }
}
