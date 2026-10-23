using System.Text.Json.Nodes;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

internal static class WorkloadParser
{
    private static readonly string[] InfrastructureRoles=["igw-database","igw-database-utility","igw-message-queue","igw-battlegroup-director","igw-director","igw-operator"];
    public static (string State,WorkloadKind Kind,string Error,bool Oom,bool CurrentOom,WorkloadEvidence Evidence) Inspect(JsonNode pod,JsonNode? stat,int setIndex,int? desired,JsonNode? jobs)
    {
        var phase=pod.At("status","phase").S("Unknown");
        var owner=pod.At("metadata","ownerReferences").Items().FirstOrDefault(o=>o["controller"].S()=="true");
        var ownerKind=owner?["kind"].S()??"";var ownerName=owner?["name"].S()??"";var ownerApi=owner?["apiVersion"].S()??"";
        var batchOwner=ownerKind=="Job"&&ownerApi=="batch/v1";
        var databaseOwner=ownerKind=="DatabaseDeployment"&&ownerApi=="igw.funcom.com/v1";
        var role=pod.At("metadata","labels","role").S();
        var kind=setIndex>=0?WorkloadKind.GameMap:InfrastructureRoles.Contains(role)||databaseOwner||batchOwner?WorkloadKind.Infrastructure:WorkloadKind.Unknown;
        var uid=owner?["uid"].S()??"";
        var job=batchOwner&&uid.Length>0?jobs?["items"].Items().FirstOrDefault(j=>j.At("metadata","uid").S()==uid&&j.At("metadata","name").S()==ownerName&&j.At("metadata","namespace").S()==pod.At("metadata","namespace").S()):null;
        var conditions=job.At("status","conditions").Items().Where(c=>c["status"].S()=="True").Select(c=>c["type"].S()).ToArray();
        var jobFailed=conditions.Contains("Failed")||conditions.Contains("FailureTarget");var jobComplete=conditions.Contains("Complete");
        var statuses=pod.At("status","initContainerStatuses").Items().Concat(pod.At("status","containerStatuses").Items()).ToArray();
        var terminated=statuses.Select(c=>c.At("state","terminated")).Where(t=>t!=null).ToArray();
        var badExit=terminated.Any(t=>t.At("exitCode").I() is {} code&&code!=0);
        var currentOom=terminated.Any(t=>t.At("reason").S()=="OOMKilled");
        var oom=currentOom||statuses.Any(c=>c.At("lastState","terminated","reason").S()=="OOMKilled");
        var waiting=statuses.Select(c=>c.At("state","waiting","reason").S()).FirstOrDefault(s=>s.Length>0);
        var recovered=jobComplete&&!jobFailed&&phase=="Failed"&&statuses.Length>0&&terminated.Length==statuses.Length&&waiting==null;
        var failure=!recovered&&(phase=="Failed"||badExit||currentOom||jobFailed);
        var allExitedZero=statuses.Length>0&&terminated.Length==statuses.Length&&terminated.All(t=>t.At("exitCode").I()==0);
        // Pod phase is a terminal Kubernetes assertion; partial status lists cannot turn Ready=false into an error.
        // Explicit contradictory failures still win. A Job condition alone never hides a currently active/unready pod.
        var success=!failure&&waiting==null&&(phase=="Succeeded"||(jobComplete&&allExitedZero));
        var state=failure||recovered?"Failed":success?"Succeeded":waiting??stat.At("status","runtime","gamePhase").S(phase);
        var errors=statuses.SelectMany(c=>new[]{
            c.At("state","waiting","message").S(c.At("state","waiting","reason").S()),
            c.At("state","terminated","exitCode").I() is {} code&&code!=0?$"Container {c["name"].S()} exited {code} ({c.At("state","terminated","reason").S("no reason supplied")}).":""
        }).Where(s=>!string.IsNullOrWhiteSpace(s)).ToList();
        if(jobFailed)errors.Add("Owning Job reports "+string.Join(", ",conditions)+".");
        var detail=$"Pod phase: {phase}\nOwner: {(ownerKind.Length==0?"Unavailable":ownerKind+"/"+ownerName)}\nRestart policy: {pod.At("spec","restartPolicy").S("Unavailable")}\nJob conditions: {(job==null?"Unavailable":conditions.Length==0?"No terminal condition":string.Join(", ",conditions))}\n"+
            string.Join("\n",statuses.Select(c=>$"Container {c["name"].S()}: {c.At("state","terminated","reason").S(c.At("state","waiting","reason").S("active / status unavailable"))}; current exit code: {c.At("state","terminated","exitCode").S("unavailable")}"));
        return(state,kind,Redactor.Clean(string.Join("; ",errors.Distinct())),oom,currentOom&&!recovered,new(phase,ownerKind,ownerName,string.Join(", ",conditions),success,failure,desired,Details:detail,RecoveredJobAttempt:recovered,OwnerUid:uid));
    }
    public static void IncludeJobsWithoutPods(List<MapInstance> rows,JsonNode? jobs,string ns)
    {
        foreach(var job in jobs?["items"].Items()??[])
        {
            var name=job.At("metadata","name").S();var uid=job.At("metadata","uid").S();
            if(name.Length==0||uid.Length==0||job.At("metadata","namespace").S()!=ns||rows.Any(r=>r.Namespace==ns&&r.Evidence?.OwnerKind=="Job"&&r.Evidence.OwnerName==name&&r.Evidence.OwnerUid==uid))continue;
            var conditions=job.At("status","conditions").Items().Where(c=>c["status"].S()=="True").Select(c=>c["type"].S()).ToArray();
            var failed=conditions.Contains("Failed")||conditions.Contains("FailureTarget");var complete=conditions.Contains("Complete")&&!failed;
            var phase=failed?"Failed":complete?"Succeeded":job.At("status","active").I()>0?"Running":"Unknown";
            var evidence=new WorkloadEvidence(OwnerKind:"Job",OwnerName:name,JobCondition:string.Join(", ",conditions),SuccessfulTermination:complete,CurrentFailure:failed,Details:$"Job/{name}\nNo pod currently observed.\nConditions: {string.Join(", ",conditions)}\nActive: {job.At("status","active").S("unavailable")}; succeeded: {job.At("status","succeeded").S("unavailable")}; failed attempts: {job.At("status","failed").S("unavailable")}",OwnerUid:uid);
            rows.Add(new("Job/"+name,"—",ns,phase,"Unavailable","Unavailable",null,0,failed?"Job reports "+string.Join(", ",conditions)+".":"",false,Kind:WorkloadKind.Infrastructure,Evidence:evidence));
        }
    }
    public static IReadOnlyList<MapInstance> RequiredAvailability(List<MapInstance> rows)
    {
        foreach(var group in rows.Where(m=>m.Workload==WorkloadKind.GameMap).GroupBy(m=>(m.Namespace,m.Map)).ToArray())
        {
            var desired=group.Max(m=>Math.Max(m.Minimum??0,m.Evidence?.DesiredReplicas??0));
            var available=group.Count(m=>m.Pod!="—"&&!m.IsSuccessfullyCompleted&&m.State=="Running"&&m.Ready!=false);
            var missing=Math.Max(0,desired-available);
            if(missing==0)continue;
            var representative=group.FirstOrDefault(m=>!m.IsSuccessfullyCompleted)??group.First();
            var index=rows.IndexOf(representative);rows[index]=representative with{Evidence=(representative.Evidence??new()) with{UnmetRequiredInstances=missing}};
        }
        return rows.OrderByDescending(r=>r.HasCurrentProblem).ThenBy(r=>r.Map).ToArray();
    }
}
