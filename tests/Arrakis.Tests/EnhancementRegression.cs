using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static class EnhancementRegression
{
    private static void Check(bool value,string message)=>ConfigurationIntegrityRegression.Check(value,message);
    internal static ServerSnapshot Snapshot(string vm="Running",string group="Healthy",bool ssh=true,IReadOnlyList<MapInstance>? maps=null)=>new(new(32L<<30,16L<<30),new("isolated VM",vm,Uptime:"2.00:28:48.4020000"),ssh?new(8L<<30,4L<<30,4L<<30,1L<<30):null,ssh,group,"fixture","fixture",maps??[Map()],[],[],CliStatus:"Available",KubernetesStatus:"Nodes Ready");
    internal static MapInstance Map(string state="Running")=>new("Fixture map","fixture-pod","fixture",state,"200Mi","2Gi",128L<<20,0,"",false,0,0,"1",Ready:state=="Running");
    public static void Register(Action<string,Func<Task>> add,string root)
    {
        void Test(string name,Action work)=>add("v1.1: "+name,()=>{work();return Task.CompletedTask;});
        Test("slider increments derive from range and integer type",()=>
        {
            foreach(var (max,step) in new[]{(1d,.01),(5d,.05),(10d,.1),(100d,1d)}){var d=new SettingDefinition("F","S","K","C","D","1",Min:0,Max:max);Check(Math.Abs(NumericSettingRange.For(d)!.Increment-step)<1e-10,"Unexpected derived increment.");}
            Check(NumericSettingRange.For(new("F","S","K","C","D","1","integer",0,65535))!.Increment==1,"Integer interaction step must be one.");
        });
        Test("sliders refuse unverified ranges and nonnumeric settings",()=>
        {
            foreach(var d in SettingCatalog.All.Where(d=>d.Kind is not ("number" or "integer")||d.Min==null||d.Max==null))Check(NumericSettingRange.For(d)==null,"Unsupported metadata cannot create a slider.");
            Check(NumericSettingRange.For(new("F","S","K","C","D","1",Min:0,Max:double.PositiveInfinity))==null,"Infinite bounds are invalid.");
        });
        Test("numeric position preserves a manually entered intermediate value",()=>{var r=new NumericSettingRange(.1,10,.1,false);Check(r.Position("0.375")==.375,"Reading slider position must not snap precise input.");Check(r.Choose(.375)=="0.4","An explicit slider choice uses the interaction increment.");});
        Test("slider boundary and documented zero values remain valid",()=>
        {
            var d=SettingCatalog.All.Single(d=>d.Key=="CraftingTimeMultiplier");var r=NumericSettingRange.For(d)!;Check(r.Choose(0)=="0"&&d.Validate("0")==null,"Documented instant/disabled zero must be available.");Check(r.Choose(-100)=="0"&&r.Choose(100)=="5","Explicit interaction must stay bounded.");
        });
        Test("invariant numeric parsing rejects locale commas, NaN and invalid integers",()=>
        {
            var original=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");var d=SettingCatalog.All.Single(d=>d.Key=="LootRespawnSpeed");Check(d.Validate("0.375")==null&&d.Validate("0,375")!=null&&d.Validate("NaN")!=null&&d.Validate("0.01")!=null,"Decimal parsing and bounds must be culture independent.");Check(SettingCatalog.All.Single(d=>d.Key=="FiefdomLimit").Validate("1.5")!=null,"Whole-number setting cannot accept fractions.");}finally{CultureInfo.CurrentCulture=original;}
        });
        foreach(var state in new[]{"Off","Running","Starting","Stopping","Paused","Unavailable","Unknown"})
        {
            Test("VM lifecycle availability for verified "+state,()=>{var s=Snapshot(state);Check(OperationalRules.Allows(s,ServerAction.StartVm)==(state=="Off"),"Start must depend on verified Off state.");Check(OperationalRules.Allows(s,ServerAction.ShutdownVm)==(state=="Running"),"Shutdown must depend on verified Running state.");});
        }
        Test("Hyper-V access failure cannot become stopped even when SSH is connected",()=>{var s=Snapshot() with {Vm=new("fixture","Unavailable",Error:"Access denied")};Check(!OperationalRules.Allows(s,ServerAction.StartVm)&&!OperationalRules.Allows(s,ServerAction.ShutdownVm)&&s.SshConnected,"SSH reachability does not grant verified VM state.");});
        Test("battlegroup lifecycle availability handles stopped, transitional and degraded states",()=>
        {
            foreach(var state in new[]{"Healthy","Running","Stopped","Starting","Stopping","Unavailable","Unknown","Failed","Degraded"}){var s=Snapshot(group:state);Check(OperationalRules.Allows(s,ServerAction.StartBattlegroup)==(state=="Stopped"),"Already-running/transitional/unknown groups cannot start.");Check(OperationalRules.Allows(s,ServerAction.StopBattlegroup)==new[]{"Healthy","Running","Failed","Degraded"}.Contains(state),"Stop requires an observed actionable phase.");}
            Check(!OperationalRules.Allows(Snapshot(ssh:false),ServerAction.StopBattlegroup),"Disconnected groups cannot mutate.");
        });
        Test("workload classification uses configured relationships and known role metadata",()=>
        {
            var bg=J.Parse("""{"metadata":{"namespace":"fixture","resourceVersion":"1"},"spec":{"serverGroup":{"template":{"spec":{"sets":[{"map":"Configured","resources":{"requests":{"memory":"1Gi"},"limits":{"memory":"2Gi"}}}]}}}}} """);
            var pods=J.Parse("""{"items":[{"metadata":{"name":"db","namespace":"fixture","labels":{"role":"igw-database"}},"status":{"phase":"Running"}},{"metadata":{"name":"igw-unknown","namespace":"fixture","labels":{"role":"igw-new-component"}},"status":{"phase":"Running"}}]}""");
            var maps=KubernetesParser.Maps(bg,pods,null,null,null);Check(maps.Single(m=>m.Map=="Configured").Workload==WorkloadKind.GameMap,"A configured set is a game map even without a pod.");Check(maps.Single(m=>m.Pod=="db").Workload==WorkloadKind.Infrastructure,"Verified database role classifies infrastructure.");Check(maps.Single(m=>m.Pod=="igw-unknown").Workload==WorkloadKind.Unknown,"Unknown prefixes alone must not classify a resource.");
        });
        Test("current map failures qualify otherwise healthy core services",()=>{var s=Snapshot(maps:[Map("Allocating")]);Check(OperationalRules.HealthHeadline(s).Contains("1 map"),"Healthy core must expose allocation problems.");Check(HealthAssessment.Notices(s,new(),DateTimeOffset.Now).Any(n=>n.Current&&n.Target=="Fixture map"),"Current map problem must be actionable.");});
        Test("a recovered OOM is historical rather than an active workload failure",()=>{var m=Map() with{OomKilled=true,Restarts=2};var notices=HealthAssessment.Notices(Snapshot(maps:[m]),new(),DateTimeOffset.Now);Check(!m.HasCurrentProblem&&notices.Any(n=>!n.Current&&n.Explanation.Contains("OOMKilled")),"A last termination cannot override healthy current state.");});
        Test("configured memory limit above VM RAM alone is not memory exhaustion",()=>{var s=Snapshot(maps:[Map() with{Limit="128Gi"}]);Check(HealthAssessment.Notices(s,new(),DateTimeOffset.Now).Count==0,"Configured limits are not measured usage.");});
        Test("event recurrence is grouped and recovered readiness errors remain historical",()=>
        {
            var json=J.Parse("""{"items":[{"metadata":{"namespace":"fixture","creationTimestamp":"2026-10-20T01:00:00Z"},"involvedObject":{"name":"fixture-pod"},"reason":"Unhealthy","message":"Readiness probe failed","type":"Warning","lastTimestamp":"2026-10-20T01:05:00Z","count":2},{"metadata":{"namespace":"fixture","creationTimestamp":"2026-10-20T01:00:00Z"},"involvedObject":{"name":"fixture-pod"},"reason":"Unhealthy","message":"Readiness probe failed","type":"Warning","lastTimestamp":"2026-10-20T01:06:00Z","count":3}]}""");
            var events=KubernetesParser.Events(json,[Map()]);Check(events.Count==1&&events[0].Count==5&&events[0].Actionable==false&&events[0].Time.EndsWith("06:00Z"),"Duplicates, counts, last time and recovered relevance must be retained.");
            var pending=KubernetesParser.Events(json,[Map("Pending")]);Check(pending[0].Actionable==true,"An unresolved current readiness failure stays actionable.");Check(KubernetesParser.Events(json,[])[0].Actionable==null,"Missing object state is unconfirmed, never invented as recovered.");
        });
        Test("modern Kubernetes event fields preserve last observation and current state",()=>{var e=KubernetesParser.Events(J.Parse("""{"items":[{"metadata":{"namespace":"fixture"},"regarding":{"name":"fixture-pod"},"reason":"FailedScheduling","note":"Allocation pending","type":"Warning","eventTime":"2026-10-20T01:00:00Z","series":{"lastObservedTime":"2026-10-20T01:08:00Z","count":7}}]}"""),[Map("Pending")]).Single();Check(e.Count==7&&e.Actionable==true&&e.Details.Contains("01:08")&&e.Message=="Allocation pending","Modern event evidence cannot be dropped.");});
        Test("resource history is time/count bounded with explicit unavailable gaps",()=>
        {
            var history=new ResourceHistory();var start=DateTimeOffset.UtcNow;for(var i=0;i<1000;i++)history.Add(Snapshot(),start.AddSeconds(i*3));Check(history.Samples.Count<=361&&history.Samples[0].Time>=history.Samples[^1].Time.AddMinutes(-30),"History must be bounded by time and count.");history.Add(Snapshot(ssh:false) with{Host=new()},start.AddSeconds(3001));Check(history.Samples[^1].GuestRam==null&&history.Samples[^1].HostRam==null,"Unavailable measurements must be gaps.");var count=history.Samples.Count;history.Add(Snapshot(),start);Check(history.Samples.Count==count,"Late samples cannot reorder history.");
        });
        Test("duration and storage formatting use readable units",()=>{Check(OperationalRules.Duration("2.00:28:48.4020000")=="2 days, 28 minutes","Days/hours/minutes must be readable.");Check(Units.Bytes(1L<<30)=="1.0 GiB"&&Units.Bytes(null)=="Unavailable","Binary units cannot be labeled GB.");Check(Units.Bytes(50)=="50 B"&&Units.Bytes(16384)=="16.0 KiB","Small backups must not round down to zero MiB.");});
        Test("cloud backup destinations are warned without changing the path",()=>{Check(OperationalRules.CloudDestination(@"C:\Users\fixture\OneDrive - Work\Backups")&&OperationalRules.CloudDestination(@"D:\Dropbox\Archives")&&!OperationalRules.CloudDestination(@"D:\ServerBackups"),"Detect common synchronized destinations without mutating them.");});
        add("v1.1: downloaded database copy verification rejects tampering and wrong remote identity",async()=>
        {
            var directory=Path.Combine(root,"verified-download");Directory.CreateDirectory(Path.Combine(directory,"Database"));var bytes=Encoding.UTF8.GetBytes("Isolated fake database bytes");var artifact=new BackupArtifact("fixture.backup","/fixture/fixture.backup",bytes.Length,DateTimeOffset.UtcNow);var path=Path.Combine(directory,"Database",artifact.Name);await File.WriteAllBytesAsync(path,bytes);await BackupVerification.RecordAsync(path,artifact,Convert.ToHexStringLower(SHA256.HashData(bytes)),default);Check(await BackupVerification.VerifyAsync(artifact,directory,default),"Verified downloaded copy must be recognized.");Check(!await BackupVerification.VerifyAsync(artifact with{Path="/other/fixture.backup"},directory,default),"Receipt binds to the remote identity.");bytes[0]++;await File.WriteAllBytesAsync(path,bytes);Check(!await BackupVerification.VerifyAsync(artifact,directory,default),"Same-size tampering must fail SHA verification.");
        });
        add("v1.1: confirmed-off live adapter performs no SSH request after shutdown",async()=>
        {
            var runner=new OffVmRunner();var gateway=new LiveServerGateway(runner,new(){InstallationDirectory=root},Path.Combine(root,"off-adapter"));var s=await gateway.RefreshAsync(default);Check(s.Vm.State=="Off"&&!s.SshConnected&&s.Issues.All(i=>!i.StartsWith("Connection/discovery"))&&runner.SshCalls==0,"Expected SSH loss after a verified shutdown must not become an operation failure.");
        });
    }
    private sealed class OffVmRunner:IProcessRunner
    {
        public int SshCalls;
        public Task<ProcessResult> RunAsync(ProcessSpec spec,IProgress<string>? progress,CancellationToken token)
        {
            if(spec.FileName.Contains("ssh",StringComparison.OrdinalIgnoreCase)){SshCalls++;throw new InvalidOperationException("No SSH is permitted for a verified-off VM.");}
            return Task.FromResult(new ProcessResult(0,"{\"Name\":\"fixture\",\"State\":\"Off\",\"Configured\":8589934592,\"Assigned\":0,\"Demand\":0,\"Uptime\":\"00:00:00\"}",""));
        }
    }
}
