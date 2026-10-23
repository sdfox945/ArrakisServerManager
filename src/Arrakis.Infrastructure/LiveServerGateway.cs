using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed partial class LiveServerGateway(IProcessRunner runner,Preferences preferences,string dataRoot) : IServerGateway
{
    private const string Cli="/home/dune/.dune/bin/battlegroup";
    private const string ReviewedCliHash="4e4b94aec678e4ce7f25f5d53aaa0af20697f97b886ba1dacfbf64426d8c6171";
    public bool IsDemo=>false;
    public SshTransport Ssh { get; }=new(runner,preferences,dataRoot);
    public WindowsHost Windows { get; }=new(runner,preferences,dataRoot);
    private ServerSnapshot snapshot=ServerSnapshot.Empty(preferences.VmName);
    private JsonNode? battlegroup;
    private static string Q(string s)=>CommandBuilder.ShellQuote(s);
    private const string BgQuery="sudo -n kubectl get battlegroups -A -o json | jq '{items:[.items[] | {apiVersion,kind,metadata:(.metadata|{name,namespace,resourceVersion}),spec:{stop:.spec.stop,serverGroup:{template:{spec:{sets:[.spec.serverGroup.template.spec.sets[]|{map,resources,replicas,image}]}}},utilities:{director:{spec:{configFiles:{files:{\"director.ini\":.spec.utilities.director.spec.configFiles.files[\"director.ini\"]}}}}}},status:(.status|{phase,serverGroupPhase})}]} '";
    public async Task<ServerSnapshot> RefreshAsync(CancellationToken token)
    {
        var host=Windows.ReadHost();var vm=await Windows.ReadVmAsync(token);Ssh.Ip=vm.Ip;var issues=new List<string>();
        if(vm.Error!=null)issues.Add("Hyper-V unavailable: "+vm.Error+" Use the Hyper-V Administrators group or enter the VM IPv4 address in Tools.");
        if(!Directory.Exists(preferences.InstallationDirectory))issues.Add("Steam server installation not found. Set its directory in Tools.");
        if(vm.State=="Off"&&vm.Error==null){battlegroup=null;snapshot=new(host,vm,null,false,"Stopped",null,null,[],[],issues);return snapshot;}
        GuestStatus? guest=null;bool connected=false;JsonNode? pods=null,stats=null,metrics=null,events=null,services=null,jobs=null;var cliStatus="Unavailable";var kubeStatus="Unavailable";
        try
        {
            var raw=await Ssh.ReadAsync(GuestScript,token);var g=J.Parse(raw);guest=new(g["total"].L(),g["available"].L(),g["swapTotal"].L(),g["swapUsed"].L(),double.TryParse(g["cpu"].S(),System.Globalization.CultureInfo.InvariantCulture,out var cpu)?cpu:null,g["diskTotal"].L(),g["diskUsed"].L());connected=true;
            try{var hash=(await Ssh.ReadAsync("test -x "+Cli+" && sha256sum "+Cli,token)).Split(' ')[0];cliStatus=hash==ReviewedCliHash?"Available · reviewed CLI":"Available · compatibility review required";if(hash!=ReviewedCliHash)issues.Add("Funcom CLI version changed; automatic CLI mutations are disabled until reviewed.");}catch(Exception e)when(e is not OperationCanceledException){issues.Add("Battlegroup CLI unavailable: "+Redactor.Clean(e.Message));}
            var nodes=await OptionalJson("sudo -n kubectl get nodes -o json | jq '{items:[.items[]|{status:{conditions:.status.conditions}}]}'",token,issues,"Kubernetes nodes");
            if(nodes!=null){var all=nodes["items"].Items().ToArray();kubeStatus=all.Length>0&&all.All(n=>n.At("status","conditions").Items().Any(c=>c["type"].S()=="Ready"&&c["status"].S()=="True"))?"Nodes Ready":"Node readiness degraded";if(kubeStatus!="Nodes Ready")issues.Add("Kubernetes node is not Ready. Inspect cluster events before starting maps.");foreach(var c in all.SelectMany(n=>n.At("status","conditions").Items()).Where(c=>c["type"].S().EndsWith("Pressure",StringComparison.Ordinal)&&c["status"].S()=="True"))issues.Add("Kubernetes node condition: "+c["type"].S()+". "+c["message"].S());}
            var bg=J.Parse(await Ssh.ReadAsync("set -o pipefail; "+BgQuery,token));var groups=bg["items"].Items().ToArray();
            battlegroup=groups.Length==1?groups[0]:null;
            if(groups.Length>1)issues.Add("Multiple battlegroups found. Mutations are disabled; use Funcom's interactive editor to select a battlegroup.");
            if(groups.Length==0)issues.Add("No Battlegroup resource found. No setup or repair will be performed automatically.");
            var ns=battlegroup.At("metadata","namespace").S();
            if(ns.Length>0)
            {
                CommandBuilder.ResourceName(ns);
                pods=J.Parse(await Ssh.ReadAsync("set -o pipefail; sudo -n kubectl get pods -n "+Q(ns)+" -o json | jq '{items:[.items[]|{metadata:(.metadata|{name,namespace,uid,labels,ownerReferences}),spec:{restartPolicy,containers:[.spec.containers[]|{name,resources}]},status}]} '",token));
                jobs=await OptionalJson("sudo -n kubectl get jobs -n "+Q(ns)+" -o json | jq '{items:[.items[]|{metadata:(.metadata|{name,namespace,uid,ownerReferences}),status:(.status|{active,succeeded,failed,conditions})}]}'",token,issues,"Kubernetes Jobs");
                stats=await OptionalJson("sudo -n kubectl get serverstats -n "+Q(ns)+" -o json | jq '{items:[.items[]|{metadata:(.metadata|{name,namespace}),spec:{area:.spec.area},status:{runtime:.status.runtime}}]}'",token,issues,"ServerStats");
                metrics=await OptionalJson("sudo -n kubectl get --raw "+Q("/apis/metrics.k8s.io/v1beta1/namespaces/"+ns+"/pods"),token,issues,"Pod usage metrics");
                events=await OptionalJson("sudo -n kubectl get events -n "+Q(ns)+" -o json",token,issues,"Kubernetes events");
                services=await OptionalJson("sudo -n kubectl get svc -n "+Q(ns)+" -o json",token,issues,"Director discovery");
            }
        }
        catch(Exception e)when(e is not OperationCanceledException) { issues.Add("Connection/discovery: "+Redactor.Clean(e.Message));battlegroup=null; }
        var maps=pods==null?[]:KubernetesParser.Maps(battlegroup,pods,stats,metrics,events,jobs);
        var revision=battlegroup.At("spec","serverGroup","template","spec","sets").Items().Select(s=>s["image"].S()).FirstOrDefault(s=>s.Length>0)?.Split(':').Last()??"Unavailable";
        snapshot=new(host,vm,guest,connected,battlegroup.At("status","phase").S("Unavailable"),battlegroup.At("metadata","namespace")?.ToString(),battlegroup.At("metadata","name")?.ToString(),maps,events==null?[]:KubernetesParser.Events(events,maps),issues,revision,services==null?null:KubernetesParser.DirectorPort(services,battlegroup.At("metadata","namespace").S()),cliStatus,kubeStatus);return snapshot;
    }
    private async Task<JsonNode?> OptionalJson(string command,CancellationToken token,List<string> issues,string label) { try { return J.Parse(await Ssh.ReadAsync("set -o pipefail; "+command,token)); }catch(Exception e)when(e is not OperationCanceledException){issues.Add(label+": unavailable ("+Redactor.Clean(e.Message)+")");return null;} }
    private const string GuestScript="""
        set -e
        awk '/^cpu /{a=$2+$3+$4+$5+$6+$7+$8+$9;b=$5+$6;exit} END{print a,b}' /proc/stat > /dev/null
        cpu=$(awk 'BEGIN{getline s < "/proc/stat";split(s,a," ");close("/proc/stat");system("sleep 1");getline s < "/proc/stat";split(s,b," ");x=0;y=0;for(i=2;i<=9;i++){x+=a[i];y+=b[i]}if(y>x)printf "%.2f",100*(1-((b[5]+b[6])-(a[5]+a[6]))/(y-x));else print "null"}')
        mem=$(awk '/^MemTotal:/{t=$2}/^MemAvailable:/{a=$2}/^SwapTotal:/{s=$2}/^SwapFree:/{f=$2}END{printf "\"total\":%.0f,\"available\":%.0f,\"swapTotal\":%.0f,\"swapUsed\":%.0f",t*1024,a*1024,s*1024,(s-f)*1024}' /proc/meminfo)
        disk=$(df -Pk / | awk 'NR==2{printf "\"diskTotal\":%.0f,\"diskUsed\":%.0f",$2*1024,$3*1024}')
        printf '{%s,%s,"cpu":%s}\n' "$mem" "$disk" "$cpu"
        """;
    private async Task EnsureCliAsync(CancellationToken token)
    {
        if(battlegroup==null)throw new InvalidOperationException("Refresh and connect to exactly one battlegroup before running this operation.");
        var hash=(await Ssh.ReadAsync("sha256sum "+Cli,token)).Split(' ',StringSplitOptions.RemoveEmptyEntries)[0];
        if(hash!=ReviewedCliHash)throw new InvalidOperationException("Funcom CLI has changed since this release was reviewed. Automatic CLI mutations are disabled. Use the official interactive workflow and request a compatibility review.");
        var namespaces=J.Parse(await Ssh.ReadAsync("sudo -n kubectl get ns -o json",token))["items"].Items().Select(n=>n.At("metadata","name").S()).Where(n=>n.StartsWith("funcom-seabass-",StringComparison.Ordinal)).ToArray();
        if(namespaces.Length!=1||namespaces[0]!=snapshot.Namespace)throw new InvalidOperationException("The CLI would require an ambiguous battlegroup selection. Use the official interactive editor; automatic operations are disabled.");
        var current=J.Parse(await Ssh.ReadAsync("set -o pipefail; "+BgQuery,token))["items"].Items().ToArray();
        if(current.Length!=1 || current[0].At("metadata","name").S()!=snapshot.BattlegroupName)throw new InvalidOperationException("Battlegroup selection changed. Refresh and review again.");
    }
    public async Task<ActionResult> ExecuteAsync(ServerAction action,string? argument,IProgress<string>? progress,CancellationToken token)
    {
        if(action is ServerAction.StartVm or ServerAction.ShutdownVm or ServerAction.ForceOffVm) { var result=await Windows.VmActionAsync(action,token);return new(result,true); }
        await EnsureCliAsync(token);
        var command=action switch { ServerAction.StartBattlegroup=>"start",ServerAction.StopBattlegroup=>"stop",ServerAction.RestartBattlegroup=>"restart",ServerAction.Update=>"update",ServerAction.Backup=>"backup",ServerAction.Import=>"import",ServerAction.ExportBattlegroupLogs=>"logs-export",ServerAction.ExportOperatorLogs=>"operator-logs-export",_=>throw new ArgumentOutOfRangeException(nameof(action)) };
        if(action==ServerAction.Import)
        {
            if(argument==null)throw new InvalidOperationException("Select a discovered database backup.");CommandBuilder.ArtifactName(argument);
            var fresh=await RefreshAsync(token);if(fresh.Battlegroup!="Stopped")throw new InvalidOperationException("Stop the battlegroup and wait until Stopped before importing.");
            if(!(await ListBackupsAsync(token)).Any(a=>a.Name==argument && a.Size>0))throw new InvalidOperationException("Selected backup no longer exists or is empty.");
        }
        if(action==ServerAction.Backup)argument="arrakis-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+".backup";
        var beforeRevision=snapshot.Revision;
        try
        {
            var result=await Ssh.RunAsync(Cli+" "+command+(argument==null?"":" "+Q(argument)),token,TimeSpan.FromMinutes(action==ServerAction.Update?30:12),action==ServerAction.Import?"yes\n":null,progress);if(result.ExitCode!=0)throw new OutcomeUnknownException($"CLI exited {result.ExitCode}; remote state may have partially changed. "+Redactor.Clean(result.Error+result.Output));
            if(action==ServerAction.Backup)
            {
                var found=(await ListBackupsAsync(token)).FirstOrDefault(b=>b.Name==argument&&b.Size>0);if(found==null||!result.Output.Contains("Database dump succeeded.",StringComparison.Ordinal))throw new OutcomeUnknownException("CLI completed but a completed non-empty database backup was not verified. Inspect databaseoperations before retrying.");
                return new(Redactor.Clean(result.Output)+"\nVerified backup: "+found.Name+" ("+found.SizeDisplay+")",true,found.Path);
            }
            if(action is ServerAction.ExportBattlegroupLogs or ServerAction.ExportOperatorLogs)
            {
                Directory.CreateDirectory(preferences.LogDirectory);var path=Path.Combine(preferences.LogDirectory,command+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".tar.gz");var remote=action==ServerAction.ExportBattlegroupLogs?"/tmp/dune-bg-logs":"/tmp/dune-operator-logs";
                var download=await Ssh.RunAsync("tar -czf - -C "+remote+" .",token,TimeSpan.FromMinutes(3),binary:path);download.EnsureSuccess();await ArchiveVerifier.VerifyLogArchiveAsync(path,token);var output=Redactor.Clean(result.Output+result.Error);if(System.Text.RegularExpressions.Regex.IsMatch(output,@"(?im)^\s*error:"))throw new InvalidOperationException("The official exporter reported errors. A partial archive is retained for inspection: "+path+"\n"+output);return new("Official log archive downloaded and structurally verified. Archives may contain sensitive server logs; share carefully.\n"+path,true,path);
            }
            if(action==ServerAction.Import)
            {
                if(!result.Output.Contains("Database import succeeded.",StringComparison.Ordinal)||!result.Output.Contains("Imported "+argument,StringComparison.Ordinal))throw new OutcomeUnknownException("Import returned without its verified completion marker. Inspect databaseoperations before proceeding.");return new(Redactor.Clean(result.Output),true);
            }
            for(var i=0;i<30;i++)
            {
                var state=await RefreshAsync(token);var ok=action==ServerAction.StopBattlegroup?state.Battlegroup=="Stopped":state.Battlegroup=="Healthy";
                if(ok && (action!=ServerAction.Update || state.Revision!="Unavailable"))return new(Redactor.Clean(result.Output)+"\nVerified battlegroup: "+state.Battlegroup+(action==ServerAction.Update?$"; revision {beforeRevision} → {state.Revision}":""),true);
                progress?.Report("Waiting for battlegroup state: "+state.Battlegroup);await Task.Delay(3000,token);
            }
            throw new OutcomeUnknownException("Command returned but the requested healthy/stopped state was not observed. Inspect current status and logs before retrying.");
        }
        catch(InvalidDataException e)when(action is ServerAction.ExportBattlegroupLogs or ServerAction.ExportOperatorLogs){throw new InvalidOperationException("Downloaded log archive failed integrity validation. "+e.Message,e);}
        catch(Exception e)when(e is TimeoutException or OperationCanceledException or IOException) { throw new OutcomeUnknownException("Connection ended during a server operation; it may still be running remotely. Refresh state and inspect operation logs before making another change.",e); }
    }
    public async Task<string> ChangeMapAsync(MapChange change,CancellationToken token)
    {
        await EnsureCliAsync(token);
        var current=J.Parse(await Ssh.ReadAsync("set -o pipefail; "+BgQuery,token))["items"].Items().SingleOrDefault()??throw new InvalidOperationException("Exactly one battlegroup is required.");
        var patch=KubernetesParser.MapPatch(current,change);var json=patch.ToJsonString();var ns=current.At("metadata","namespace").S();var name=current.At("metadata","name").S();CommandBuilder.ResourceName(ns);CommandBuilder.ResourceName(name);
        var command="sudo -n kubectl patch battlegroup "+Q(name)+" -n "+Q(ns)+" --type=json -p "+Q(json);
        var validate=await Ssh.RunAsync(command+" --dry-run=server -o name",token);validate.EnsureSuccess();
        var rollback=new JsonObject{{"namespace",ns},{"name",name},{"resourceVersion",change.Original.ResourceVersion},{"setIndex",change.Original.SetIndex},{"map",change.Original.Map},{"resources",current.At("spec","serverGroup","template","spec","sets")?[change.Original.SetIndex]?["resources"]?.DeepClone()},{"directorIni",current.At("spec","utilities","director","spec","configFiles","files","director.ini")?.DeepClone()}};
        var backup="/home/dune/.arrakis-map-backups/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json";
        var encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(rollback.ToJsonString()));
        ProcessResult result;try{result=await Ssh.RunAsync("set -e; umask 077; mkdir -p /home/dune/.arrakis-map-backups; printf %s "+Q(encoded)+" | base64 -d > "+Q(backup)+"; "+command+" -o name",token);}catch(Exception e)when(e is TimeoutException or OperationCanceledException or IOException){throw new OutcomeUnknownException("Map change lost its acknowledgment. Inspect the authoritative spec before retrying.",e);}if(result.ExitCode!=0)throw new OutcomeUnknownException("Map patch did not return verified success. Inspect current spec and rollback record. "+Redactor.Clean(result.Error));
        await RefreshAsync(token);
        // Pod allocations may lag reconciliation; verify authoritative spec instead of old pods.
        var updated=battlegroup.At("spec","serverGroup","template","spec","sets")?[change.Original.SetIndex];
        var verifiedMinimum=new IniDocument(battlegroup.At("spec","utilities","director","spec","configFiles","files","director.ini").S()).Get(change.Original.Map,"MinServers");
        if(updated.At("resources","requests","memory").S()!=change.MemoryRequest || updated.At("resources","limits","memory").S()!=change.MemoryLimit || (change.Minimum!=change.Original.Minimum&&verifiedMinimum!=change.Minimum?.ToString(System.Globalization.CultureInfo.InvariantCulture)))throw new OutcomeUnknownException("Map patch was submitted but could not be verified. Refresh and inspect the authoritative configuration.");
        return "Verified authoritative map settings. Existing pods may retain their previous resources until operator reconciliation or a restart. Rollback values: "+backup;
    }
    public async Task<string> ReadLogsAsync(string source,MapInstance? pod,CancellationToken token)
    {
        if(source=="Events")return string.Join("\n",snapshot.Events.Select(e=>$"{e.Time} {e.Type} {e.Object}: {e.Reason} — {e.Message}"));
        if(pod!=null && source=="Selected map") { if(pod.Pod=="—")return "This map has no running pod. Inspect recent Kubernetes events.";CommandBuilder.ResourceName(pod.Pod);CommandBuilder.ResourceName(pod.Namespace);return Redactor.Clean(await Ssh.ReadAsync("sudo -n kubectl logs -n "+Q(pod.Namespace)+" "+Q(pod.Pod)+" --all-containers=true --tail=350 --timestamps=true --limit-bytes=80000",token)); }
        var ns=source=="Operators"?"funcom-operators":snapshot.Namespace??throw new InvalidOperationException("Connect to a battlegroup first.");CommandBuilder.ResourceName(ns);
        return Redactor.Clean(await Ssh.ReadAsync("set -o pipefail; pods=$(sudo -n kubectl get pods -n "+Q(ns)+" -o name | head -n 8); for pod in $pods; do printf '\\n--- %s ---\\n' \"$pod\"; sudo -n kubectl logs -n "+Q(ns)+" \"$pod\" --all-containers=true --tail=100 --timestamps=true --limit-bytes=10000 2>&1; done",token));
    }
    public async Task<string> CheckUpdatesAsync(CancellationToken token)
    {
        var downloaded=(await Ssh.ReadAsync("cat /home/dune/.dune/download/images/battlegroup/version.txt",token)).Trim();
        return "Running image: "+snapshot.Revision+"\nDownloaded image: "+downloaded+"\nThe installed CLI has no read-only online update check. Its Update operation downloads and applies updates. Use the separately confirmed Apply Update action when ready.";
    }
    public async Task<Uri> GetToolUriAsync(bool director,CancellationToken token)
    {
        await RefreshAsync(token);if(Ssh.Ip==null)throw new InvalidOperationException("No VM address available.");var port=director?snapshot.DirectorPort:18888;if(port==null)throw new InvalidOperationException("Director NodePort was not found in service port 11717.");
        var uri=new Uri($"http://{Ssh.Ip}:{port}/");using var client=new HttpClient { Timeout=TimeSpan.FromSeconds(5) };using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);if((int)response.StatusCode>=500)throw new InvalidOperationException("The service is reachable but unhealthy: "+response.StatusCode);return uri;
    }
    public async Task LaunchOfficialEditorAsync(CancellationToken token)
    {
        if(snapshot.Namespace==null)throw new InvalidOperationException("Connect to the VM first.");
        var spec=Ssh.Build(Cli+" edit",TimeSpan.FromHours(1),terminal:true);var start=new ProcessStartInfo(spec.FileName){UseShellExecute=true};foreach(var a in spec.Arguments)start.ArgumentList.Add(a);
        using var process=Process.Start(start)??throw new InvalidOperationException("Could not open the official editor.");using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromHours(1));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException e){throw new OutcomeUnknownException("The official editor console is unresolved after its deadline. Inspect the console and current Battlegroup before making another change.",e);}if(process.ExitCode!=0)throw new OutcomeUnknownException("Official editor exited "+process.ExitCode+". It may have saved changes before disconnecting; inspect its output and current spec.");
    }
    public async Task<string> LaunchMaintenanceAsync(MaintenanceAction action,CancellationToken token)
    {
        if(!snapshot.SshConnected)throw new InvalidOperationException("Connect to the VM first.");
        if(action!=MaintenanceAction.VmPassword)await EnsureCliAsync(token);
        var command=action switch{MaintenanceAction.VmNetworking=>Cli+" change-vm-ip",MaintenanceAction.VmPassword=>"passwd",MaintenanceAction.ExperimentalSwap=>Cli+" enable-experimental-swap",_=>throw new ArgumentOutOfRangeException(nameof(action))};
        var spec=Ssh.Build(command,terminal:true);var start=new ProcessStartInfo(spec.FileName){UseShellExecute=true};foreach(var a in spec.Arguments)start.ArgumentList.Add(a);
        using var process=Process.Start(start)??throw new InvalidOperationException("Could not open the secure maintenance console.");using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromHours(1));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException e){throw new OutcomeUnknownException("Maintenance console outcome is unresolved. Inspect the VM console before any further changes.",e);}
        if(process.ExitCode!=0)throw new OutcomeUnknownException("Maintenance SSH console exited "+process.ExitCode+". Networking changes may disconnect SSH. Check the VM console and rediscover its address before retrying.");
        return "Maintenance console completed. Review its messages; refresh connection/status before proceeding. Passwords were entered only into the native SSH console and were not captured by this app.";
    }
}
