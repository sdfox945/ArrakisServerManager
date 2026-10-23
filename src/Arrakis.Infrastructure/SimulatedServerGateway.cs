using System.Security.Cryptography;
using System.Text;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public enum DemoScenario { Running, OfflineVm, AllocatingMap, OomKilled, SshFailure, ConfigurationSaveFailure }
public sealed class SimulatedServerGateway(Preferences preferences,string root) : IServerGateway
{
    public bool IsDemo=>true;
    public DemoScenario Scenario { get; set; }=DemoScenario.AllocatingMap;
    private bool vmRunning=true,bgRunning=true;
    private int revision=1;
    private readonly List<BackupArtifact> backups=[];
    private readonly Dictionary<string,ConfigFile> files=new[]{"UserGame.ini","UserEngine.ini","UserServerCustomSettings.ini"}.ToDictionary(n=>n,n=>
    {
        var builder=new StringBuilder("; Arrakis simulated configuration — no live server data\r\n");
        foreach(var group in SettingCatalog.All.Where(d=>d.File==n).GroupBy(d=>d.Section)) { builder.AppendLine("["+group.Key+"]");foreach(var d in group)builder.AppendLine(d.Key+"="+d.TemplateValue); }
        builder.AppendLine("\r\n[Custom]\r\n; unknown entries are retained\r\nExperimentalValue=keep-me");var text=builder.ToString();if(n=="UserEngine.ini")text=text.Replace("\r\n","\n");var bytes=Encoding.UTF8.GetBytes(text);return new ConfigFile(n,"/demo/UserSettings/"+n,bytes,Convert.ToHexStringLower(SHA256.HashData(bytes)));
    });
    private string request="200Mi",limit="1Gi";private int? minimum=0;
    public async Task<ServerSnapshot> RefreshAsync(CancellationToken token)
    {
        await Task.Delay(100,token);const long gb=1L<<30;var offline=!vmRunning||Scenario==DemoScenario.OfflineVm;var connected=!offline&&Scenario!=DemoScenario.SshFailure;
        var maps=new List<MapInstance>();
        if(connected&&bgRunning)
        {
            maps.Add(new("Survival_1","survival-1-pod-1","funcom-seabass-demo","Running","5Gi","12Gi",3*gb,0,"",false,1,0,revision.ToString()));
            maps.Add(new("Overmap","overmap-pod-2","funcom-seabass-demo","Running","200Mi","1Gi",390*1024*1024,0,"",false,1,1,revision.ToString()));
            maps.Add(new("DLC_Story_LostHarvest_EcolabA","ecolab-pod-3","funcom-seabass-demo",Scenario==DemoScenario.AllocatingMap?"Allocating":Scenario==DemoScenario.OomKilled?"CrashLoopBackOff":"Running",request,limit,800*1024*1024,Scenario==DemoScenario.OomKilled?4:0,Scenario==DemoScenario.OomKilled?"Last termination: OOMKilled":Scenario==DemoScenario.AllocatingMap?"Waiting for map allocation. Inspect events and resource limits.":"",Scenario==DemoScenario.OomKilled,minimum,2,revision.ToString()));
            maps.Add(new("Demo_Unstarted_Map","—","funcom-seabass-demo","Not running","200Mi","2Gi",null,0,"",false,0,3,revision.ToString()));
        }
        IReadOnlyList<ClusterEvent> events=Scenario==DemoScenario.AllocatingMap&&connected?[new("funcom-seabass-demo","ecolab-pod-3","FailedScheduling","Insufficient memory for requested allocation.","Warning",DateTimeOffset.Now.ToString("u"))]:[];
        return new(new(32*gb,6*gb,24,true),new("dune-awakening",offline?"Off":"Running",10*gb,offline?0:10*gb,offline?0:9*gb,offline?"00:00:00":"02:43:18",offline?null:"192.0.2.10","Demo adapter / simulated switch"),connected?new(10*gb,2*gb,8*gb,3*gb,36,100*gb,37*gb):null,connected,connected?(bgRunning?"Healthy":"Stopped"):"Unavailable",connected?"funcom-seabass-demo":null,connected?"demo":null,maps,events,Scenario==DemoScenario.SshFailure?["SSH connection failed (simulated). Verify the VM address and existing SSH key."]:[],"demo-build-1",connected?30117:null);
    }
    public async Task<ActionResult> ExecuteAsync(ServerAction action,string? argument,IProgress<string>? progress,CancellationToken token)
    {
        progress?.Report("SIMULATED: "+action);await Task.Delay(350,token);
        switch(action)
        {
            case ServerAction.StartVm:vmRunning=true;Scenario=DemoScenario.Running;break;
            case ServerAction.ShutdownVm:case ServerAction.ForceOffVm:vmRunning=false;break;
            case ServerAction.StartBattlegroup:case ServerAction.RestartBattlegroup:bgRunning=true;break;
            case ServerAction.StopBattlegroup:bgRunning=false;break;
            case ServerAction.Backup:
                var name="demo-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".backup";backups.Add(new(name,"/demo/backups/"+name,16384,DateTimeOffset.Now));return new("SIMULATED: verified database backup artifact "+name,true,name);
            case ServerAction.Import:if(bgRunning)throw new InvalidOperationException("Stop the demo battlegroup before importing.");if(!backups.Any(b=>b.Name==argument))throw new InvalidOperationException("Select a backup.");break;
            case ServerAction.ExportBattlegroupLogs:case ServerAction.ExportOperatorLogs:Directory.CreateDirectory(preferences.LogDirectory);var path=Path.Combine(preferences.LogDirectory,"demo-"+action+"-"+Guid.NewGuid().ToString("N")+".txt");await File.WriteAllTextAsync(path,"Simulated diagnostic export. No live data.",token);return new("SIMULATED export: "+path,true,path);
        }
        return new("SIMULATED: completed and verified "+action,true);
    }
    public Task<IReadOnlyList<ConfigFile>> LoadConfigurationAsync(CancellationToken token)=>Task.FromResult<IReadOnlyList<ConfigFile>>(files.Values.ToArray());
    public Task<ConfigFile> SaveConfigurationAsync(ConfigFile original,byte[] proposed,CancellationToken token)
    {
        if(Scenario==DemoScenario.ConfigurationSaveFailure)throw new IOException("SIMULATED: disk full; original configuration retained.");
        if(files[original.Name].Hash!=original.Hash)throw new InvalidOperationException("SIMULATED: configuration changed after loading. Reload before saving.");
        new ConfigBackupStore(Path.Combine(preferences.BackupDirectory,"Configuration")).Save(original);var updated=new ConfigFile(original.Name,original.RemotePath,proposed,Convert.ToHexStringLower(SHA256.HashData(proposed)));files[original.Name]=updated;return Task.FromResult(updated);
    }
    public Task<IReadOnlyList<BackupArtifact>> ListBackupsAsync(CancellationToken token)=>Task.FromResult<IReadOnlyList<BackupArtifact>>(backups.ToArray());
    public async Task<string> DownloadBackupAsync(BackupArtifact artifact,CancellationToken token) { Directory.CreateDirectory(Path.Combine(preferences.BackupDirectory,"Database"));var path=Path.Combine(preferences.BackupDirectory,"Database",artifact.Name);await File.WriteAllTextAsync(path,"SIMULATED BACKUP. Not an actual game database.",token);return "SIMULATED backup copied to "+path; }
    public Task<string> ReadLogsAsync(string source,MapInstance? pod,CancellationToken token)=>Task.FromResult($"{DateTimeOffset.Now:u} [SIMULATED] {source}\n"+(pod?.Error??"Server ready. 2 simulated players. No live data has been read."));
    public Task<string> ChangeMapAsync(MapChange change,CancellationToken token) { change.Validate();if(change.Original.ResourceVersion!=revision.ToString())throw new InvalidOperationException("SIMULATED: map revision conflict.");Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"map-rollback-"+revision+".json"),System.Text.Json.JsonSerializer.Serialize(new{request,limit,minimum}));request=change.MemoryRequest;limit=change.MemoryLimit;minimum=change.Minimum;revision++;return Task.FromResult("SIMULATED: map resource change verified; rollback copy created."); }
    public Task<string> CheckUpdatesAsync(CancellationToken token)=>Task.FromResult("SIMULATED: running demo-build-1. Online update checking is not supported by the installed official CLI.");
    public Task<Uri> GetToolUriAsync(bool director,CancellationToken token)=>throw new InvalidOperationException("Demo mode has no Director or File Browser service. Switch to Live to open actual tools.");
    public Task LaunchOfficialEditorAsync(CancellationToken token)=>throw new InvalidOperationException("The official editor is available only in Live mode. Demo map resources can be edited directly here.");
    public Task<string> LaunchMaintenanceAsync(MaintenanceAction action,CancellationToken token)=>Task.FromResult("SIMULATED maintenance completed: "+action+". No credentials, networking or guest settings changed.");
}
