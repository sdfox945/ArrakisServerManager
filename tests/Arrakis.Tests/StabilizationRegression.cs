using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static class StabilizationRegression
{
    internal const string Ns="funcom-seabass-demo";
    private static void Check(bool value,string message)=>ConfigurationIntegrityRegression.Check(value,message);
    internal static JsonNode Utility()=>J.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","completed-database-utility.json")));
    internal static JsonNode Pod(string phase="Succeeded",string role="",string owner="",int? exit=0,bool? ready=false,string? waiting=null)
    {
        var pod=Utility();pod["metadata"]!["name"]="fixture-pod";pod["metadata"]!["labels"]=new JsonObject{{"role",role}};
        pod["metadata"]!["ownerReferences"]=owner.Length==0?new JsonArray():new JsonArray(new JsonObject{{"apiVersion",owner=="Job"?"batch/v1":"igw.funcom.com/v1"},{"kind",owner},{"name","fixture-job"},{"uid","22222222-2222-2222-2222-222222222222"},{"controller",true}});
        pod["status"]!["phase"]=phase;
        pod["status"]!["conditions"]=ready==null?new JsonArray():new JsonArray(new JsonObject{{"type","Ready"},{"status",ready==true?"True":"False"},{"reason",phase=="Succeeded"?"PodCompleted":"ContainersNotReady"}});
        pod["status"]!["containerStatuses"]![0]!["state"]=waiting!=null?new JsonObject{{"waiting",new JsonObject{{"reason",waiting}}}}:exit is {} code?new JsonObject{{"terminated",new JsonObject{{"exitCode",code},{"reason",code==0?"Completed":"Error"}}}}:new JsonObject{{"running",new JsonObject()}};
        return pod;
    }
    internal static JsonNode List(params JsonNode[] nodes)=>new JsonObject{{"items",new JsonArray(nodes.Select(n=>n.DeepClone()).ToArray())}};
    internal static JsonNode Jobs(string condition="Complete",string uid="22222222-2222-2222-2222-222222222222")=>List(new JsonObject{{"metadata",new JsonObject{{"namespace",Ns},{"name","fixture-job"},{"uid",uid}}},{"status",new JsonObject{{"conditions",new JsonArray(new JsonObject{{"type",condition},{"status","True"}})}}}});
    internal static JsonNode Group(int? minimum=null,int? replicas=null,bool withMap=false,string phase="Healthy")=>new JsonObject
    {
        {"metadata",new JsonObject{{"namespace",Ns},{"resourceVersion","1"}}},
        {"status",new JsonObject{{"phase",phase}}},
        {"spec",new JsonObject{{"serverGroup",new JsonObject{{"template",new JsonObject{{"spec",new JsonObject{{"sets",withMap?new JsonArray(new JsonObject{{"map","Fixture map"},{"replicas",replicas},{"resources",new JsonObject{{"requests",new JsonObject{{"memory","200Mi"}}},{"limits",new JsonObject{{"memory","2Gi"}}}}}}):new JsonArray()}}}}}}},{"utilities",new JsonObject{{"director",new JsonObject{{"spec",new JsonObject{{"configFiles",new JsonObject{{"files",new JsonObject{{"director.ini",minimum is {} min?$"[Fixture map]\nMinServers={min}\n":""}}}}}}}}}}}}}
    };
    internal static MapInstance One(JsonNode pod,JsonNode? jobs=null)=>KubernetesParser.Maps(Group(),List(pod),null,null,null,jobs).Single(r=>r.Pod==pod.At("metadata","name").S());
    internal static JsonNode Stats(string pod="fixture-pod")=>List(new JsonObject{{"metadata",new JsonObject{{"name",pod}}},{"spec",new JsonObject{{"area",new JsonObject{{"map","Fixture map"}}}}}});
    public static void Register(Action<string,Func<Task>> add)
    {
        void Test(string name,Action work)=>add("v1.1.1: "+name,()=>{work();return Task.CompletedTask;});
        Test("actual completed utility shape overrides Ready false with successful terminal evidence",()=>
        {
            var row=One(Utility());Check(row.State=="Succeeded"&&row.Ready==false&&row.Workload==WorkloadKind.Infrastructure&&!row.HasCurrentProblem,"Completed utility must be non-error Infrastructure despite PodCompleted readiness.");Check(row.Evidence?.OwnerKind=="DatabaseDeployment"&&row.Evidence.Details.Contains("exit code: 0"),"Ownership and current exit evidence must survive parsing.");var s=EnhancementRegression.Snapshot(maps:[row]);Check(!OperationalRules.HealthHeadline(s).Contains("issue")&&HealthAssessment.Notices(s,new(),DateTimeOffset.Now).All(n=>!n.Current),"Utility completion cannot create an active Dashboard issue.");
        });
        Test("unclassified successful pod stays visible without an active error",()=>{var row=One(Pod());Check(row.Workload==WorkloadKind.Unknown&&row.IsSuccessfullyCompleted&&!row.HasCurrentProblem,"Classification uncertainty alone cannot invalidate confirmed completion.");Check(!OperationalRules.HealthHeadline(EnhancementRegression.Snapshot(maps:[row])).Contains("unclassified workload"),"Unclassified issue count must exclude success.");});
        Test("infrastructure derives from verified role or controller rather than resource-name prefixes",()=>
        {
            Check(One(Pod(role:"igw-database-utility")).Workload==WorkloadKind.Infrastructure,"Verified utility role must classify infrastructure.");Check(One(Pod(owner:"DatabaseDeployment")).Workload==WorkloadKind.Infrastructure,"Verified database controller must classify infrastructure.");var unknown=Pod();unknown["metadata"]!["name"]="igw-database-utility-lookalike";Check(One(unknown).Workload==WorkloadKind.Unknown,"A name prefix must not grant a known resource relationship.");
        });
        Test("successful UID-bound Job completion is non-error",()=>{var row=One(Pod(owner:"Job"),Jobs());Check(row.Workload==WorkloadKind.Infrastructure&&row.Evidence?.JobCondition=="Complete"&&!row.HasCurrentProblem,"A successful Job must not inherit a false readiness failure.");});
        Test("failed Job condition wins over a pod's successful phase",()=>{var row=One(Pod(owner:"Job"),Jobs("Failed"));Check(row.State=="Failed"&&row.HasCurrentProblem&&row.CurrentWarning.Contains("Job reports Failed"),"Failed Job context cannot be suppressed by one successful pod.");});
        Test("Job conditions remain visible even when no pod is retained",()=>
        {
            var good=KubernetesParser.Maps(Group(),List(),null,null,null,Jobs()).Single();var failed=KubernetesParser.Maps(Group(),List(),null,null,null,Jobs("Failed")).Single();
            Check(good.Workload==WorkloadKind.Infrastructure&&good.State=="Succeeded"&&!good.HasCurrentProblem&&failed.State=="Failed"&&failed.HasCurrentProblem,"Deleting an attempt pod must neither invent a completion failure nor hide a failed Job.");
        });
        Test("Job success criteria without final Complete is not terminal success",()=>{var row=One(Pod("Running",owner:"Job",exit:null),Jobs("SuccessCriteriaMet"));Check(!row.IsSuccessfullyCompleted&&row.HasCurrentProblem,"An active unready pod must stay actionable until terminal success is established.");});
        Test("stale same-name Job UID and missing Job discovery cannot clear a pod failure",()=>
        {
            var pod=Pod("Failed",owner:"Job",exit:1);Check(One(pod,Jobs(uid:"different-uid")).HasCurrentProblem&&One(pod).HasCurrentProblem,"Job completion must bind name/namespace/UID before affecting health.");Check(!One(Pod(owner:"Job")).HasCurrentProblem,"Pod success remains sufficient if Job readback is unavailable.");
        });
        Test("active crash loops remain errors even if an owning Job reports Complete",()=>{var row=One(Pod("Running",owner:"Job",exit:null,waiting:"CrashLoopBackOff"),Jobs());Check(row.State=="CrashLoopBackOff"&&row.HasCurrentProblem&&row.CurrentWarning.Length>0,"Active crashes cannot be hidden by terminal context on a different attempt.");foreach(var state in new[]{"Succeeded","Completed"})Check((row with{State=state,Evidence=new(PodPhase:"Running")}).HasCurrentProblem,"A runtime display label alone cannot override active readiness without verified terminal evidence.");});
        Test("explicit nonzero container exit contradicts Succeeded and stays failed",()=>{var row=One(Pod(exit:7));Check(row.State=="Failed"&&row.HasCurrentProblem&&row.Error.Contains("exited 7"),"Contradictory failure evidence must win.");});
        Test("init-container failures are retained",()=>{var pod=Pod("Pending",exit:null);pod["status"]!["initContainerStatuses"]=new JsonArray(new JsonObject{{"name","prepare"},{"restartCount",2},{"state",new JsonObject{{"terminated",new JsonObject{{"exitCode",2},{"reason","Error"}}}}}});var row=One(pod);Check(row.HasCurrentProblem&&row.Restarts==2&&row.Error.Contains("prepare"),"Init failure and restart evidence must not be dropped.");});
        Test("running readiness failures and genuinely unknown status remain actionable",()=>
        {
            foreach(var phase in new[]{"Running","Pending","Unknown"}){var row=One(Pod(phase,exit:null));Check(row.State==phase&&row.HasCurrentProblem&&!string.IsNullOrWhiteSpace(row.CurrentWarning),"Live unready/unknown status must not be broadly suppressed.");}
            Check(!One(Pod("Running",exit:null,ready:true)).HasCurrentProblem,"A ready running pod is healthy.");
        });
        Test("historical failed attempts on a completed Job remain visible without current failure",()=>{var row=One(Pod("Failed",owner:"Job",exit:1),Jobs());Check(row.State=="Failed"&&row.Evidence?.RecoveredJobAttempt==true&&!row.HasCurrentProblem,"A terminal attempt recovered by exact Job completion is historical.");Check(HealthAssessment.Notices(EnhancementRegression.Snapshot(maps:[row]),new(),DateTimeOffset.Now).Any(n=>!n.Current&&n.Explanation.Contains("attempt")),"Historical attempt evidence must remain accessible.");});
        Test("events on completed utilities and Jobs remain accessible and historical",()=>
        {
            var row=One(Pod(owner:"Job"),Jobs());var events=List(new JsonObject{{"metadata",new JsonObject{{"namespace",Ns}}},{"involvedObject",new JsonObject{{"name","fixture-job"}}},{"type","Warning"},{"reason","Unhealthy"},{"message","Past readiness failure"},{"count",3}});var e=KubernetesParser.Events(events,[row]).Single();Check(e.Actionable==false&&e.Count==3&&e.Message.Contains("Past"),"Historical warnings must not be removed or promoted to active errors.");
        });
        Test("idle on-demand requires explicit zero minimum and zero desired replicas",()=>
        {
            MapInstance Missing(int? min,int? desired,string phase="Healthy")=>KubernetesParser.Maps(Group(min,desired,true,phase),List(),null,null,null).Single();
            Check(Missing(0,0).State=="Idle / On demand"&&!Missing(0,0).HasCurrentProblem,"Confirmed inactive map should use idle terminology.");
            foreach(var row in new[]{Missing(null,0),Missing(0,null),Missing(null,null),Missing(0,0,"Stopped")})Check(row.State!="Idle / On demand","Unknown configuration or stopped core cannot imply on-demand availability.");
            Check(Missing(null,0).MinimumDisplay=="Not set"&&Missing(null,0).Evidence!.Details.Contains("unavailable"),"Missing minimum information must stay explicitly unavailable.");
        });
        Test("required replicas and required minimum instances still produce current warnings",()=>
        {
            foreach(var (min,desired) in new[]{(0,1),(1,0)}){var row=KubernetesParser.Maps(Group(min,desired,true),List(),null,null,null).Single();Check(row.HasCurrentProblem&&row.CurrentWarning.Contains("required instance"),"Required absent maps must remain unhealthy.");}
            var rowWithOneOfTwo=KubernetesParser.Maps(Group(0,2,true),List(Pod("Running",exit:null,ready:true)),Stats(),null,null).Single();Check(rowWithOneOfTwo.HasCurrentProblem&&rowWithOneOfTwo.Evidence!.UnmetRequiredInstances==1,"Partial required replica availability must not be mistaken for healthy capacity.");
            Check(KubernetesParser.Maps(Group(1,1,true),List(Pod()),Stats(),null,null).Single().HasCurrentProblem,"A completed game-map pod cannot satisfy an ongoing required replica.");
        });
        Test("empty error fields cannot generate empty current warnings",()=>{var row=new MapInstance("fixture","pod",Ns,"Running","1Gi","2Gi",null,0,"",false,Ready:false);Check(row.HasCurrentProblem&&row.CurrentWarning.Length>0&&!row.HealthDetail.EndsWith("Current warning: "),"Derived readiness warnings need a concise explanation.");Check(!One(Utility()).HealthDetail.Contains("Current warning:"),"Healthy completion must not emit any Current warning entry.");});
        Test("numeric no-op comparison normalizes formatting and exponents without losing precision",()=>
        {
            var d=new SettingDefinition("F","S","K","C","D","0.1","number");foreach(var equal in new[]{"0.1",".1000","1e-1","+0.100000"," 0.100 "})Check(SettingValueComparison.Equivalent(d,"0.100000",equal),"Equivalent numeric formatting must be a no-op.");Check(!SettingValueComparison.Equivalent(d,"0.1","0.10000000000000001"),"Distinct typed precision must not be rounded away.");
            var culture=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Check(SettingValueComparison.Equivalent(d,"0.100000","1e-1")&&!SettingValueComparison.Equivalent(d,"0.1","0,1"),"No-op comparison must remain culture independent.");}finally{CultureInfo.CurrentCulture=culture;}
        });
        Test("integer no-op comparison preserves large values and signed zero",()=>{var d=new SettingDefinition("F","S","K","C","D","1","integer");Check(SettingValueComparison.Equivalent(d,"3","03.0")&&SettingValueComparison.Equivalent(d,"3","3e0")&&SettingValueComparison.Equivalent(d,"0","-0.000"),"Whole-number formatting and signed zero should normalize.");Check(!SettingValueComparison.Equivalent(d,"9007199254740992","9007199254740993"),"Integer comparison must not use lossy binary double equality.");});
        Test("nonnumeric comparisons follow validated setting semantics",()=>{var flag=new SettingDefinition("F","S","K","C","D","True","bool");Check(SettingValueComparison.Equivalent(flag,"True","true"),"Boolean casing is not a change.");var text=flag with{Kind="quotedString"};Check(!SettingValueComparison.Equivalent(text,"\"Case\"","\"case\""),"Quoted strings retain meaningful case.");});
    }
}
