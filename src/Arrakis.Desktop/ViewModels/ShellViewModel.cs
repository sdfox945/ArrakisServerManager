using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Desktop.ViewModels;

public record NavigationItem(string Title,string Icon,string Subtitle);
public sealed partial class ShellViewModel : Bindable,IDisposable
{
    private readonly IUserInteraction ui;private readonly LocalStores stores;private readonly OperationCoordinator operations;private readonly string root;
    private readonly DispatcherTimer timer;private CancellationTokenSource? active;private bool refreshing;
    private readonly Func<bool,IServerGateway>? gatewayFactory;
    private string? preparation;
    public ShellViewModel(IUserInteraction ui,LocalStores stores,bool demo,Func<bool,IServerGateway>? gatewayFactory=null)
    {
        this.ui=ui;this.stores=stores;this.gatewayFactory=gatewayFactory;root=stores.Root;Preferences=stores.Load();operations=new(stores);operations.Changed+=()=>{OnPropertyChanged(nameof(IsBusy));OnPropertyChanged(nameof(CanOperate));OnPropertyChanged(nameof(CanCancel));OnPropertyChanged(nameof(OperationState));CommandManager.InvalidateRequerySuggested();};
        Gateway=CreateGateway(demo);this.demo=demo;Snapshot=ServerSnapshot.Empty(Preferences.VmName);
        MapsView=CollectionViewSource.GetDefaultView(Maps);MapsView.Filter=o=>o is MapInstance m&&($"{m.Map} {m.Pod} {m.State} {m.Error}".Contains(MapSearch,StringComparison.OrdinalIgnoreCase))&&(MapFilter=="All states"||m.State.Contains(MapFilter,StringComparison.OrdinalIgnoreCase))&&(MapMode=="All"||m.Workload==(MapMode=="Game Maps"?WorkloadKind.GameMap:WorkloadKind.Infrastructure));
        BackupsView=CollectionViewSource.GetDefaultView(Backups);BackupsView.Filter=o=>o is BackupArtifact a&&(BackupCategory=="All categories"||a.Category==BackupCategory);
        Editor=new(this,ui);
        PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(CanOperate)){OnPropertyChanged(nameof(CanEditMapResources));OnPropertyChanged(nameof(CanApplyMap));}};
        RefreshCommand=new AsyncCommand(()=>RefreshAsync(),()=>CanOperate);
        CancelCommand=new RelayCommand(()=>{LiveLogs=false;active?.Cancel();},()=>CanCancel);
        foreach(var action in Enum.GetValues<ServerAction>())ActionCommands[action]=new AsyncCommand(()=>ExecuteAction(action),()=>CanTakeAction(action));
        SavePreferencesCommand=new RelayCommand(SavePreferences,()=>CanOperate);
        BrowseInstallationCommand=new RelayCommand(()=>{var p=ui.ChooseFolder("Steam server installation",Preferences.InstallationDirectory);if(p!=null){Preferences.InstallationDirectory=p;OnPropertyChanged(nameof(Preferences));}},()=>CanOperate);
        BrowseKeyCommand=new RelayCommand(()=>{var p=ui.ChooseFile("Select existing SSH private key","All files|*");if(p!=null){Preferences.SshKeyPath=p;OnPropertyChanged(nameof(Preferences));}},()=>CanOperate);
        BrowseBackupCommand=new RelayCommand(()=>{var p=ui.ChooseFolder("Local backup directory",Preferences.BackupDirectory);if(p!=null){Preferences.BackupDirectory=p;OnPropertyChanged(nameof(Preferences));}},()=>CanOperate);
        BrowseLogsCommand=new RelayCommand(()=>{var p=ui.ChooseFolder("Diagnostic export directory",Preferences.LogDirectory);if(p!=null){Preferences.LogDirectory=p;OnPropertyChanged(nameof(Preferences));}},()=>CanOperate);
        OpenBackupsCommand=new RelayCommand(()=>OpenFolder(EffectivePreferences.BackupDirectory));OpenLogsCommand=new RelayCommand(()=>OpenFolder(EffectivePreferences.LogDirectory));
        RefreshBackupsCommand=new AsyncCommand(()=>RunAsync("List backup artifacts",false,async t=>{await PopulateBackups(t);return "Backup list refreshed.";}),()=>CanOperate);
        DownloadBackupCommand=new AsyncCommand(()=>RunAsync("Download database backup",false,async t=>{var output=await Gateway.DownloadBackupAsync(SelectedBackup!,t);await PopulateBackups(t);return output;}),()=>CanOperate&&SelectedBackup?.Category=="Database");
        MapApplyCommand=new AsyncCommand(ApplyMap,()=>CanApplyMap);
        OfficialEditorCommand=new AsyncCommand(OpenEditor,()=>CanOperate);
        MapLogsCommand=new AsyncCommand(async()=>{DiagnosticTab=0;SelectedPage=4;LogSource="Selected map";await LoadLogs();},()=>CanOperate&&SelectedMap!=null);
        MapEventsCommand=new RelayCommand(()=>{var map=SelectedMap;if(map==null)return;DiagnosticTab=1;SelectedPage=4;SelectedEvent=Events.FirstOrDefault(e=>e.Object==map.Pod&&e.Namespace==map.Namespace);},()=>SelectedMap!=null);
        LoadLogsCommand=new AsyncCommand(LoadLogs,()=>CanOperate);
        CopyLogsCommand=new RelayCommand(()=>ui.Copy(FilteredLogs));CopyOutputCommand=new RelayCommand(()=>ui.Copy(string.IsNullOrWhiteSpace(OperationDetails)?OperationOutput:OperationDetails));
        CopyHistoryCommand=new RelayCommand(()=>ui.Copy(SelectedHistoryDetails),()=>SelectedHistory!=null);
        CheckUpdatesCommand=new AsyncCommand(()=>RunAsync("Check available update information",false,async t=>{UpdateInformation=await Gateway.CheckUpdatesAsync(t);return UpdateInformation;}),()=>CanOperate);
        OpenDirectorCommand=new AsyncCommand(()=>OpenTool(true),()=>CanOperate);OpenFileBrowserCommand=new AsyncCommand(()=>OpenTool(false),()=>CanOperate);
        TrustHostCommand=new AsyncCommand(TrustHost,()=>CanOperate&&!IsDemo);
        ElevatedVmReadCommand=new AsyncCommand(()=>RunAsync("Read Hyper-V details with elevation",false,async t=>{if(Gateway is not LiveServerGateway live)return "Unavailable in demo.";var json=await live.Windows.ElevatedAsync(live.Windows.ReadVmScript,t);var vm=live.Windows.ParseVm(json);Preferences.ManualIp=vm.Ip??Preferences.ManualIp;OnPropertyChanged(nameof(Preferences));return JsonSerializer.Serialize(vm,new JsonSerializerOptions{WriteIndented=true});}),()=>CanOperate&&!IsDemo);
        ReconcileCommand=new RelayCommand(()=>{if(ui.Confirm("Acknowledge reviewed server state","Only continue if you have checked the VM, battlegroup and relevant remote operation/file and resolved the previous unknown outcome. Allow new changes?")){operations.AcknowledgeReconciliation();StatusMessage="Unknown outcome acknowledged after manual review.";}},()=>CanOperate);
        ExportDiagnosticsCommand=new AsyncCommand(ExportDiagnostics,()=>CanOperate);
        NetworkConsoleCommand=new AsyncCommand(()=>OpenMaintenance(MaintenanceAction.VmNetworking),()=>CanOperate);
        PasswordConsoleCommand=new AsyncCommand(()=>OpenMaintenance(MaintenanceAction.VmPassword),()=>CanOperate);
        SwapConsoleCommand=new AsyncCommand(()=>OpenMaintenance(MaintenanceAction.ExperimentalSwap),()=>CanOperate);
        timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(Math.Clamp(Preferences.RefreshSeconds,5,300))};timer.Tick+=async(_,_)=>{if(CanOperate&&!refreshing){await RefreshAsync(true);if(LiveLogs&&SelectedPage==4&&CanOperate)await LoadLogs();}};
        LoadHistory();
        FocusNoticeCommand=new RelayCommand(()=>OpenNotice());
    }
    private Preferences activePreferences=new();
    private Preferences EffectivePreferences=>activePreferences;
    private Preferences DemoPreferences()=>new(){BackupDirectory=Path.Combine(root,"Demo","Backups"),LogDirectory=Path.Combine(root,"Demo","Diagnostics")};
    private IServerGateway CreateGateway(bool simulate){activePreferences=simulate?DemoPreferences():stores.Load();return gatewayFactory?.Invoke(simulate)??(simulate?new SimulatedServerGateway(activePreferences,Path.Combine(root,"Demo")):new LiveServerGateway(new ProcessRunner(),activePreferences,root));}
    public IServerGateway Gateway { get; private set; }
    public Preferences Preferences { get; }
    public ConfigurationViewModel Editor { get; }
    public NavigationItem[] Pages { get; }=[new("Dashboard","\uE80F","Your server at a glance"),new("Battlegroup & Maps","\uE8B7","Instances, allocation and resource controls"),new("Server Configuration","\uE713","Review, edit and safely save game settings"),new("Backups","\uE896","Database artifacts and configuration recovery"),new("Logs & Diagnostics","\uE9D9","Find the cause, keep the evidence"),new("Tools & Settings","\uE115","Connections, preferences and advanced tools")];
    private int selectedPage;
    public int SelectedPage { get=>selectedPage;set{if(Set(ref selectedPage,value)){OnPropertyChanged(nameof(PageTitle));OnPropertyChanged(nameof(PageSubtitle));}} }
    public string PageTitle=>Pages[Math.Clamp(SelectedPage,0,5)].Title;
    public string PageSubtitle=>Pages[Math.Clamp(SelectedPage,0,5)].Subtitle;
    private bool demo;
    public bool IsDemo { get=>demo;set { if(!CanOperate||value==demo)return;if(Editor.IsDirty&&!ui.Confirm("Discard configuration draft?","Switching Live/Demo discards the unsaved draft.")){OnPropertyChanged();return;}demo=value;Gateway=CreateGateway(value);ResetMonitoring();Editor.Clear();Maps.Clear();Backups.Clear();LogText="";OnPropertyChanged();OnPropertyChanged(nameof(ModeLabel));_=RefreshAsync(); } }
    public string ModeLabel=>IsDemo?"DEMO · simulated infrastructure":"LIVE · existing server";
    public DemoScenario[] DemoScenarios=>Enum.GetValues<DemoScenario>();
    public DemoScenario DemoScenario { get=>Gateway is SimulatedServerGateway s?s.Scenario:DemoScenario.Running;set{if(CanOperate&&Gateway is SimulatedServerGateway s&&s.Scenario!=value){s.Scenario=value;OnPropertyChanged();_=RefreshAsync();}} }
    public bool IsBusy=>operations.IsBusy;
    public bool CanOperate=>!IsBusy&&preparation==null;
    private bool mutationRunning;
    public bool CanCancel=>IsBusy&&!mutationRunning;
    public string OperationState=>IsBusy?"Operation running":preparation??(operations.NeedsReconciliation?"Review unknown outcome":"Ready");
    public IDisposable PrepareConfiguration(string phase)
    {
        if(!CanOperate)throw new InvalidOperationException("Wait for the current operation or review to finish.");preparation=phase;OnPropertyChanged(nameof(CanOperate));OnPropertyChanged(nameof(OperationState));CommandManager.InvalidateRequerySuggested();
        return new PreparationLease(()=>{preparation=null;OnPropertyChanged(nameof(CanOperate));OnPropertyChanged(nameof(OperationState));CommandManager.InvalidateRequerySuggested();});
    }
    private sealed class PreparationLease(Action release):IDisposable {private Action? action=release;public void Dispose(){Interlocked.Exchange(ref action,null)?.Invoke();}}
    private string statusMessage="Ready to discover your existing server.",operationOutput="",updateInformation="Online update check: not supported by the installed CLI. Check downloaded revision for available local information.";
    public string StatusMessage { get=>statusMessage;set=>Set(ref statusMessage,value); }
    public string OperationOutput { get=>operationOutput;set=>Set(ref operationOutput,value); }
    private string operationDetails="";
    public string OperationDetails { get=>operationDetails;set=>Set(ref operationDetails,value); }
    public string UpdateInformation { get=>updateInformation;set=>Set(ref updateInformation,value); }
    public ServerSnapshot Snapshot { get; private set; }
    public string HostRam=>Units.Bytes(Snapshot.Host.TotalBytes-Snapshot.Host.AvailableBytes)+" used / "+Units.Bytes(Snapshot.Host.TotalBytes);
    public string HostAvailable=>Units.Bytes(Snapshot.Host.AvailableBytes)+" available";
    public string HostCpu=>Units.PercentText(Snapshot.Host.CpuPercent);
    public string ClientState=>Snapshot.Host.GameRunning is true?"Dune client detected":Snapshot.Host.GameRunning is false?"Dune client not detected":"Detection unavailable";
    public string VmName=>Snapshot.Vm.Name;
    public string VmState=>Snapshot.Vm.State;
    public string VmMemory=>"Configured "+Units.Bytes(Snapshot.Vm.ConfiguredBytes)+" · assigned "+Units.Bytes(Snapshot.Vm.AssignedBytes);
    public string VmUptime=>OperationalRules.Duration(Snapshot.Vm.Uptime);
    public string VmAddress=>Snapshot.Vm.Ip??"Unavailable";
    public string VmAdapter=>Snapshot.Vm.Adapter;
    public string GuestRam=>Units.Bytes(Snapshot.Guest?.TotalBytes-Snapshot.Guest?.AvailableBytes)+" used / "+Units.Bytes(Snapshot.Guest?.TotalBytes);
    public string GuestAvailable=>Units.Bytes(Snapshot.Guest?.AvailableBytes)+" available";
    public string GuestSwap=>Units.Bytes(Snapshot.Guest?.SwapUsedBytes)+" / "+Units.Bytes(Snapshot.Guest?.SwapTotalBytes);
    public string GuestDisk=>"Root disk storage: "+Units.Bytes(Snapshot.Guest?.DiskUsedBytes)+" used / "+Units.Bytes(Snapshot.Guest?.DiskTotalBytes);
    public string GuestCpu=>Units.PercentText(Snapshot.Guest?.CpuPercent);
    public string ConnectionState=>Snapshot.SshConnected?"SSH connected":"SSH disconnected";
    public string ConnectionSummary=>$"{ModeLabel} · VM {activePreferences.VmName} · {VmAddress} · Battlegroup {Snapshot.BattlegroupName??"unavailable"}";
    public string BattlegroupState=>OperationalRules.HealthHeadline(Snapshot);
    public string Revision=>Snapshot.Revision;
    public string HealthSummary=>$"Hyper-V: {VmAccess}\nSSH: {ConnectionState}\nGuest memory: {GuestAvailable}\nBattlegroup CLI: {Snapshot.CliStatus}\nKubernetes: {Snapshot.KubernetesStatus}\n{BattlegroupState}\nInstances: {InstanceSummary}";
    public string InstanceSummary=>$"Maps: {Snapshot.Maps.Count(m=>m.Workload==WorkloadKind.GameMap&&m.Pod!="—"&&m.State=="Running"&&!m.HasCurrentProblem)} running · {Snapshot.Maps.Count(m=>m.Workload==WorkloadKind.GameMap&&m.State=="Idle / On demand")} idle/on demand · {Snapshot.Maps.Count(m=>m.Workload==WorkloadKind.GameMap&&(m.State.Contains("Pending")||m.State.Contains("Allocat")||m.State=="Startup"))} pending · {Snapshot.Maps.Count(m=>m.Workload==WorkloadKind.GameMap&&m.HasCurrentProblem&&!m.State.Contains("Pending")&&!m.State.Contains("Allocat")&&m.State!="Startup")} require attention";
    public double HostRamPercent=>Snapshot.Host.UsedPercent;
    public double VmRamPercent=>Units.Percent(Snapshot.Vm.DemandBytes,Snapshot.Vm.AssignedBytes);
    public double GuestRamPercent=>Snapshot.Guest?.MemoryPercent??0;
    public double SwapPercent=>Snapshot.Guest?.SwapPercent??0;
    public ObservableCollection<string> Warnings { get; }=[];
    public ObservableCollection<MapInstance> Maps { get; }=[];
    public ObservableCollection<ClusterEvent> Events { get; }=[];
    public ObservableCollection<OperationRecord> History { get; }=[];
    private OperationRecord? selectedHistory;
    public OperationRecord? SelectedHistory { get=>selectedHistory;set{if(Set(ref selectedHistory,value))OnPropertyChanged(nameof(SelectedHistoryDetails));} }
    public string SelectedHistoryDetails=>SelectedHistory is {} h ? string.IsNullOrWhiteSpace(h.Details)?h.Output:h.Details :"Select an operation to read and copy its full details.";
    public RelayCommand CopyHistoryCommand { get; }
    public ICollectionView MapsView { get; }
    private string mapSearch="",mapFilter="All states";
    public string MapSearch { get=>mapSearch;set{if(Set(ref mapSearch,value))MapsView.Refresh();} }
    public string MapFilter { get=>mapFilter;set{if(Set(ref mapFilter,value))MapsView.Refresh();} }
    public string[] MapFilters=>["All states","Running","Allocating","Pending","Startup","CrashLoopBackOff","Not running","Idle / On demand","Succeeded","Failed","Unknown","Unavailable"];
    private MapInstance? selectedMap;
    public MapInstance? SelectedMap { get=>selectedMap;set { if(value==null&&selectedMap!=null&&Maps.Contains(selectedMap))return;if(Set(ref selectedMap,value)){MapRequest=value?.Request??"";MapLimit=value?.Limit??"";MapMinimum=value?.Minimum?.ToString()??"";OnPropertyChanged(nameof(MapDetails));} } }
    public string MapDetails=>SelectedMap==null?"Select a configured map or pod to inspect its resources and logs.":$"{SelectedMap.Map} · {SelectedMap.Workload}\nPod: {SelectedMap.Pod}\nStatus: {SelectedMap.State} · restarts: {SelectedMap.Restarts}\nRequest: {SelectedMap.Request} · limit: {SelectedMap.Limit} · usage: {SelectedMap.Usage} · minimum: {SelectedMap.MinimumDisplay}\n{SelectedMap.HealthDetail}\n{SelectedMap.Evidence?.Details}";
    public string MapEmptyMessage=>Maps.Count==0?"No maps or pods discovered. Refresh, or check the VM connection in Tools & Settings.":"";
    public string BackupEmptyMessage=>Backups.Count==0?"No backup artifacts discovered. Refresh the list or create an official database backup.":"";
    private string mapRequest="",mapLimit="",mapMinimum="";
    public string MapRequest { get=>mapRequest;set{if(Set(ref mapRequest,value))MapInputChanged();} }public string MapLimit { get=>mapLimit;set{if(Set(ref mapLimit,value))MapInputChanged();} }public string MapMinimum { get=>mapMinimum;set{if(Set(ref mapMinimum,value))MapInputChanged();} }
    public ObservableCollection<BackupArtifact> Backups { get; }=[];
    public ICollectionView BackupsView { get; }
    private BackupArtifact? selectedBackup;
    public BackupArtifact? SelectedBackup { get=>selectedBackup;set=>Set(ref selectedBackup,value); }
    private string backupCategory="All categories";
    public string BackupCategory { get=>backupCategory;set{if(Set(ref backupCategory,value))BackupsView.Refresh();} }
    public string[] BackupCategories=>["All categories","Database","Configuration","Diagnostics"];
    public string[] LogSources=>["Application operations","Battlegroup","Operators","Selected map","Events"];
    private string logSource="Application operations",logText="",logSearch="";private bool liveLogs;
    public string LogSource { get=>logSource;set=>Set(ref logSource,value); }
    public string LogSearch { get=>logSearch;set{if(Set(ref logSearch,value)){OnPropertyChanged(nameof(FilteredLogs));OnPropertyChanged(nameof(LogEmptyMessage));}} }
    public string LogText { get=>logText;set{if(Set(ref logText,Redactor.Clean(value))){OnPropertyChanged(nameof(FilteredLogs));OnPropertyChanged(nameof(LogEmptyMessage));}} }
    public string FilteredLogs=>string.Join(Environment.NewLine,LogText.Split('\n').Where(l=>l.Contains(LogSearch,StringComparison.OrdinalIgnoreCase)&&MatchesLogSeverity(l)));
    public bool LiveLogs { get=>liveLogs;set=>Set(ref liveLogs,value); }
    public Dictionary<ServerAction,AsyncCommand> ActionCommands { get; }=[];
    public AsyncCommand StartVmCommand=>ActionCommands[ServerAction.StartVm];public AsyncCommand ShutdownVmCommand=>ActionCommands[ServerAction.ShutdownVm];public AsyncCommand ForceOffVmCommand=>ActionCommands[ServerAction.ForceOffVm];
    public AsyncCommand StartCommand=>ActionCommands[ServerAction.StartBattlegroup];public AsyncCommand StopCommand=>ActionCommands[ServerAction.StopBattlegroup];public AsyncCommand RestartCommand=>ActionCommands[ServerAction.RestartBattlegroup];public AsyncCommand UpdateCommand=>ActionCommands[ServerAction.Update];
    public AsyncCommand CreateBackupCommand=>ActionCommands[ServerAction.Backup];public AsyncCommand ImportCommand=>ActionCommands[ServerAction.Import];public AsyncCommand ExportBgCommand=>ActionCommands[ServerAction.ExportBattlegroupLogs];public AsyncCommand ExportOperatorCommand=>ActionCommands[ServerAction.ExportOperatorLogs];
    public AsyncCommand RefreshCommand { get; }public RelayCommand CancelCommand { get; }public RelayCommand SavePreferencesCommand { get; }public RelayCommand BrowseInstallationCommand { get; }public RelayCommand BrowseKeyCommand { get; }public RelayCommand BrowseBackupCommand { get; }public RelayCommand BrowseLogsCommand { get; }public RelayCommand OpenBackupsCommand { get; }public RelayCommand OpenLogsCommand { get; }public AsyncCommand RefreshBackupsCommand { get; }public AsyncCommand DownloadBackupCommand { get; }public AsyncCommand MapApplyCommand { get; }public AsyncCommand OfficialEditorCommand { get; }public AsyncCommand MapLogsCommand { get; }public AsyncCommand LoadLogsCommand { get; }public RelayCommand CopyLogsCommand { get; }public RelayCommand CopyOutputCommand { get; }public AsyncCommand CheckUpdatesCommand { get; }public AsyncCommand OpenDirectorCommand { get; }public AsyncCommand OpenFileBrowserCommand { get; }public AsyncCommand TrustHostCommand { get; }public AsyncCommand ElevatedVmReadCommand { get; }public RelayCommand ReconcileCommand { get; }public AsyncCommand ExportDiagnosticsCommand { get; }public AsyncCommand NetworkConsoleCommand { get; }public AsyncCommand PasswordConsoleCommand { get; }public AsyncCommand SwapConsoleCommand { get; }
    public async Task InitializeAsync(){await RefreshAsync();if(!Snapshot.SshConnected)SelectedPage=5;timer.Start();}
    public async Task RunAsync(string name,bool mutation,Func<CancellationToken,Task<string>> work,bool background=false)
    {
        string? errorSummary=null;string? errorDetails=null;
        if(!CanOperate)return;mutationRunning=mutation;active=new();if(!background){OperationOutput="";OperationDetails="";StatusMessage=name+"…";}
        try{var result=await operations.RunAsync(name,mutation,()=>work(active.Token),!background);if(!background){OperationOutput=Redactor.Clean(result);StatusMessage=name+" completed.";}}
        catch(Exception e){var failure=e as IOperationFailure;OperationOutput=Redactor.Clean(failure?.Failure.Summary??name+": "+e.Message);OperationDetails=failure?.Failure.Details??Redactor.Clean(e.ToString());StatusMessage=(e is OutcomeUnknownException?"UNKNOWN OUTCOME — ":e is OperationCanceledException?"Cancelled — ":"Unable to complete — ")+OperationOutput.Split('\n')[0];if(!background&&(failure!=null||name.StartsWith("Save ",StringComparison.Ordinal))){errorSummary=OperationOutput;errorDetails=OperationDetails;}}
        finally{active.Dispose();active=null;LoadHistory();CommandManager.InvalidateRequerySuggested();}
        if(errorSummary!=null)ui.ShowError("Configuration save failed",errorSummary,errorDetails??errorSummary);
    }
    private void LoadHistory(){var selected=SelectedHistory;History.Clear();foreach(var item in ((IHistoryStore)stores).Load().Where(r=>r.Name!="[Safety guard]").Reverse())History.Add(item);SelectedHistory=History.FirstOrDefault(h=>h.Timestamp==selected?.Timestamp&&h.Name==selected.Name)??History.FirstOrDefault();OnPropertyChanged(nameof(BackupSummary));}
    public async Task RefreshAsync(bool background=false)
    {
        if(!CanOperate||refreshing)return;refreshing=true;
        try{await RunAsync("Refresh server status",false,async t=>{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(t);timeout.CancelAfter(TimeSpan.FromSeconds(55));await UpdateSnapshot(timeout.Token);return "Refreshed "+DateTime.Now.ToString("T")+" · "+ConnectionState;},background);}finally{refreshing=false;}
    }
    private async Task UpdateSnapshot(CancellationToken token)
    {
        try { var next=await Gateway.RefreshAsync(token);AcceptSnapshot(next,DateTimeOffset.Now); }
        catch(Exception e) { refreshError=Redactor.Clean(e.Message);OnPropertyChanged(nameof(RefreshState));throw; }
    }
    private async Task ExecuteAction(ServerAction action)
    {
        if(!CanTakeAction(action))return;
        using var review=PrepareConfiguration("Reviewing server operation");
        var message=action switch{ServerAction.ForceOffVm=>"Force power-off can corrupt game data and databases. This is not a graceful shutdown. Only continue for emergency recovery.",ServerAction.ShutdownVm=>"Gracefully shut down the Linux VM? Players will disconnect. Stop the battlegroup first to protect active saves.",ServerAction.Import=>$"Restore {SelectedBackup?.Name}? This OVERWRITES the selected battlegroup database and can permanently lose progress. The battlegroup must already be Stopped. Verify you have a recent backup.",ServerAction.Update=>"Download and apply Funcom server updates? This can change the installed CLI and running server version and interrupt players. Take a database backup first. A future CLI hash will require compatibility review.",ServerAction.Backup=>"Create an official database backup? This can significantly affect VM performance. Funcom recommends stopping the battlegroup first.",_=>$"Run {action} on {(IsDemo?"the simulated server":activePreferences.VmName)}? This may interrupt connected players or create server-side diagnostic files."};
        if(action==ServerAction.ShutdownVm)message="Gracefully shut down the entire VM? Connected players may disconnect, all game-server instances will stop and battlegroup services will become unavailable. Shutdown may take time. Stop the battlegroup first when appropriate; no additional shutdown action is performed automatically.";
        if(!ui.Confirm(action.ToString(),ConnectionSummary+"\n\n"+message))return;
        review.Dispose();
        await RunAsync(action.ToString(),true,async t=>{var progress=new Progress<string>(s=>OperationOutput=Redactor.Clean(OperationOutput+s+Environment.NewLine));var result=await Gateway.ExecuteAsync(action,action==ServerAction.Import?SelectedBackup?.Name:null,progress,t);if(!result.Verified)throw new OutcomeUnknownException("Operation returned without verified completion.");await UpdateSnapshot(t);if(action==ServerAction.Backup)await PopulateBackups(t);return result.Output;});
    }
    private async Task ApplyMap()
    {
        if(!CanApplyMap||SelectedMap==null)return;
        using var review=PrepareConfiguration("Reviewing map resources");
        try{var change=ProposedMapChange();var diff=$"{SelectedMap.Map}\nMemory request: {SelectedMap.Request} → {MapRequest}\nMemory limit: {SelectedMap.Limit} → {MapLimit}\nMinimum running: {SelectedMap.MinimumDisplay} → {MapMinimum}\n\nA rollback copy is saved before applying. Kubernetes validates the authoritative Battlegroup patch and checks its resource version. Reconciliation/restart may interrupt this map. No immediate pod limit change is assumed.";if(!ui.Review("Apply map resource changes",ConnectionSummary,diff))return;review.Dispose();await RunAsync("Apply map resources",true,async t=>{var result=await Gateway.ChangeMapAsync(change,t);await UpdateSnapshot(t);return result;});}catch(Exception e){StatusMessage=e.Message;}
    }
    private async Task OpenEditor(){if(!ui.Confirm("Open Funcom editor","Open the official interactive map editor in a console? Changes made there affect the live Battlegroup. Use its backup and preview controls before saving. The app will wait for the console to close."))return;await RunAsync("Official Battlegroup editor",true,async t=>{await Gateway.LaunchOfficialEditorAsync(t);await UpdateSnapshot(t);return "Official editor closed; state refreshed. Changes in the editor require review there.";});}
    private async Task OpenMaintenance(MaintenanceAction action)
    {
        var warning=action switch{MaintenanceAction.VmNetworking=>"Open Funcom's VM IP configuration console? This can stop battlegroups, change networking and reboot the VM. Players will disconnect and SSH may drop. Have the Hyper-V console available for recovery. No changes occur until you answer the official console prompts.",MaintenanceAction.VmPassword=>"Open the Linux password-change console? You will type passwords directly into native SSH. The app does not capture or store them. Keep your existing SSH key and a recovery method available.",_=>"Open Funcom's EXPERIMENTAL swap console? Funcom warns that this can leave the VM requiring manual recovery or reinstall. Take an independent backup first. Enabling swap changes kernel and map resource behavior; the official console requires its own confirmation."};
        if(!ui.Confirm("Advanced maintenance — "+action,ConnectionSummary+"\n\n"+warning))return;
        await RunAsync("Interactive maintenance: "+action,true,async t=>{var result=await Gateway.LaunchMaintenanceAsync(action,t);await UpdateSnapshot(t);return result;});
    }
    private Task LoadLogs()=>ReadLogsWithState();
    private async Task PopulateBackups(CancellationToken token)
    {
        var remote=await Gateway.ListBackupsAsync(token);Backups.Clear();foreach(var a in remote)Backups.Add(a);
        foreach(var (directory,category,pattern) in new[]{(Path.Combine(EffectivePreferences.BackupDirectory,"Configuration"),"Configuration","*.asmconfig"),(EffectivePreferences.LogDirectory,"Diagnostics","*")})if(Directory.Exists(directory))foreach(var f in new DirectoryInfo(directory).EnumerateFiles(pattern).OrderByDescending(f=>f.LastWriteTimeUtc).Take(200))Backups.Add(new(f.Name,f.FullName,f.Length,f.LastWriteTimeUtc,category));OnPropertyChanged(nameof(BackupEmptyMessage));await UpdateBackupSummary(token);
    }
    private async Task OpenTool(bool director)=>await RunAsync(director?"Open Director":"Open File Browser",false,async t=>{var uri=await Gateway.GetToolUriAsync(director,t);ui.Open(uri.AbsoluteUri);return "Service availability verified: "+uri;});
    private async Task TrustHost()=>await RunAsync("Review SSH host identity",false,async t=>{if(Gateway is not LiveServerGateway live)return "Unavailable in demo.";if(live.Ssh.Ip==null)live.Ssh.Ip=Preferences.ManualIp;var scan=await live.Ssh.ScanAsync(t);if(!ui.Confirm("Verify SSH fingerprint",$"Host: {live.Ssh.Ip}\n{scan.Fingerprints}\n\nCompare this fingerprint independently with the VM console (ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub). Keyscan alone does not prove identity. Trust this NEW host? Existing pins cannot be replaced here."))return "Host was not trusted.";await live.Ssh.TrustAsync(scan.Keys,t);return "New SSH host identity pinned. Refresh to reconnect.";});
    private void SavePreferences()
    {
        try{if(Editor.IsDirty&&!ui.Confirm("Discard draft?","Saving connection settings reloads the adapter. Discard the unsaved configuration draft?"))return;stores.Save(Preferences);Editor.Clear();Gateway=CreateGateway(IsDemo);ResetMonitoring();timer.Interval=TimeSpan.FromSeconds(Preferences.RefreshSeconds);OnPropertyChanged(nameof(CloudBackupWarning));StatusMessage="Preferences saved. Refresh to rediscover the server.";}catch(Exception e){StatusMessage=e.Message;}
    }
    private async Task ExportDiagnostics()
    {
        if(!ui.Confirm("Export diagnostic bundle","Create local application diagnostics and invoke both official log exporters? Bundles can contain server details and logs. Inspect before sharing. No repair will be performed."))return;
        await RunAsync("Export diagnostic bundle",true,async t=>{Directory.CreateDirectory(EffectivePreferences.LogDirectory);var stage=Path.Combine(EffectivePreferences.LogDirectory,"bundle-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(stage);await File.WriteAllTextAsync(Path.Combine(stage,"status.json"),Redactor.Clean(JsonSerializer.Serialize(Snapshot,new JsonSerializerOptions{WriteIndented=true})),t);await File.WriteAllTextAsync(Path.Combine(stage,"operations.txt"),Redactor.Clean(string.Join("\n",History.Select(h=>$"{h.Timestamp:u} {h.Name} [{h.Outcome}] {h.Output}"))),t);foreach(var action in new[]{ServerAction.ExportBattlegroupLogs,ServerAction.ExportOperatorLogs}){var result=await Gateway.ExecuteAsync(action,null,new Progress<string>(s=>OperationOutput=Redactor.Clean(OperationOutput+s)),t);if(result.Artifact!=null&&File.Exists(result.Artifact))File.Copy(result.Artifact,Path.Combine(stage,Path.GetFileName(result.Artifact)));}var zip=stage+".zip";ZipFile.CreateFromDirectory(stage,zip);return "Verified local diagnostic bundle: "+zip+"\nOfficial archives are retained intact and can contain sensitive logs.";});
    }
    private void OpenFolder(string path){try{Directory.CreateDirectory(path);ui.Open(path);}catch(Exception e){StatusMessage=e.Message;}}
    public void Dispose(){timer.Stop();active?.Cancel();}
}
