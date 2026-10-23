using System.Globalization;
using System.Text.Json.Nodes;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public static class J
{
    public static JsonNode? At(this JsonNode? node,params string[] path) { foreach(var p in path)node=node is JsonObject o?o[p]:null;return node; }
    public static string S(this JsonNode? node,string fallback="")=>node?.ToString()??fallback;
    public static int? I(this JsonNode? node)=>int.TryParse(node.S(),out var n)?n:null;
    public static long? L(this JsonNode? node)=>long.TryParse(node.S(),out var n)?n:null;
    public static IEnumerable<JsonNode> Items(this JsonNode? node)=>node is JsonArray a?a.OfType<JsonNode>():[];
    public static JsonNode Parse(string text)=>JsonNode.Parse(text)??throw new InvalidOperationException("Empty JSON response.");
}
public static class KubernetesParser
{
    public const string SetsPath="/spec/serverGroup/template/spec/sets";
    public const string DirectorPath="/spec/utilities/director/spec/configFiles/files/director.ini";
    public static IReadOnlyList<MapInstance> Maps(JsonNode? bg,JsonNode pods,JsonNode? stats,JsonNode? metrics,JsonNode? events,JsonNode? jobs=null)
    {
        var results=new List<MapInstance>();var ns=bg.At("metadata","namespace").S();var rv=bg.At("metadata","resourceVersion").S();
        var config=new IniDocument(bg.At("spec","utilities","director","spec","configFiles","files","director.ini").S());
        var sets=bg.At("spec","serverGroup","template","spec","sets").Items().ToArray();
        var statList=stats?["items"].Items().ToArray()??[];var metricList=metrics?["items"].Items().ToArray()??[];
        foreach(var pod in pods["items"].Items().Where(p=>p.At("metadata","namespace").S()==ns))
        {
            var name=pod.At("metadata","name").S();var stat=statList.FirstOrDefault(s=>s.At("metadata","name").S()==name);
            var map=stat.At("spec","area","map").S();var index=Array.FindIndex(sets,s=>s["map"].S()==map);
            if(map.Length==0){var label=pod.At("metadata","labels","role").S();map=string.IsNullOrWhiteSpace(label)?name:label;}
            var containers=pod.At("spec","containers").Items().ToArray();var statuses=pod.At("status","containerStatuses").Items().Concat(pod.At("status","initContainerStatuses").Items()).ToArray();
            var usage=metricList.FirstOrDefault(m=>m.At("metadata","name").S()==name&&m.At("metadata","namespace").S()==ns);
            var uses=usage?["containers"].Items().Select(c=>Units.Quantity(c.At("usage","memory").S())).ToArray();
            var mem=index>=0?sets[index].At("resources","requests","memory").S("Unavailable"):containers.Select(c=>c.At("resources","requests","memory").S()).FirstOrDefault(s=>s.Length>0)??"Unavailable";
            var limit=index>=0?sets[index].At("resources","limits","memory").S("Unavailable"):containers.Select(c=>c.At("resources","limits","memory").S()).FirstOrDefault(s=>s.Length>0)??"Unavailable";
            var minimum=int.TryParse(config.Get(map,"MinServers"),out var min)?min:(int?)null;
            var inspected=WorkloadParser.Inspect(pod,stat,index,index>=0?sets[index]["replicas"].I():null,jobs);
            var ready=pod.At("status","conditions").Items().FirstOrDefault(c=>c["type"].S()=="Ready")?["status"].S();
            results.Add(new(map,name,ns,inspected.State,mem,limit,uses?.Length>0&&uses.All(v=>v!=null)?uses.Sum(v=>v!.Value):null,statuses.Sum(c=>c["restartCount"].I()??0),inspected.Error,inspected.Oom,minimum,index,rv,inspected.Kind,ready=="True"?true:ready=="False"?false:null,inspected.CurrentOom,inspected.Evidence));
        }
        for(var i=0;i<sets.Length;i++)
        {
            var set=sets[i];var map=set["map"].S();if(results.Any(r=>r.Map==map))continue;
            var minimum=int.TryParse(config.Get(map,"MinServers"),out var min)?min:(int?)null;var desired=set["replicas"].I();
            var idle=minimum==0&&desired==0&&bg.At("status","phase").S()=="Healthy"&&bg.At("spec","stop").S()!="true";
            results.Add(new(map,"—",ns,idle?"Idle / On demand":"Not running",set.At("resources","requests","memory").S("Unavailable"),set.At("resources","limits","memory").S("Unavailable"),null,0,"",false,minimum,i,rv,Evidence:new(DesiredReplicas:desired,Details:$"No pod observed. Configured minimum: {minimum?.ToString(CultureInfo.InvariantCulture)??"unavailable"}; desired replicas: {desired?.ToString(CultureInfo.InvariantCulture)??"unavailable"}.")));
        }
        WorkloadParser.IncludeJobsWithoutPods(results,jobs,ns);
        return WorkloadParser.RequiredAvailability(results);
    }
    public static IReadOnlyList<ClusterEvent> Events(JsonNode node,IReadOnlyList<MapInstance>? maps=null)
    {
        return node["items"].Items().Select(e=>
        {
            var ns=e.At("metadata","namespace").S();var name=e.At("involvedObject","name").S(e.At("regarding","name").S());
            var map=maps?.FirstOrDefault(m=>m.Namespace==ns&&(m.Pod==name||(m.Evidence?.OwnerKind=="Job"&&m.Evidence.OwnerName==name)));
            var first=e["firstTimestamp"].S(e["eventTime"].S(e.At("metadata","creationTimestamp").S()));
            var last=e.At("series","lastObservedTime").S(e["lastTimestamp"].S(e["eventTime"].S(first)));
            return new ClusterEvent(ns,name,e["reason"].S(),Redactor.Clean(e["message"].S(e["note"].S())),e["type"].S(),last,first,e.At("series","count").I()??e["count"].I()??1,map?.State??"Unknown",map==null?null:map.HasCurrentProblem);
        }).GroupBy(e=>(e.Namespace,e.Object,e.Reason,e.Message,e.Type)).Select(g=>
        {
            var last=g.OrderByDescending(e=>DateTimeOffset.TryParse(e.Time,out var t)?t:DateTimeOffset.MinValue).First();
            return last with {Count=g.Sum(e=>Math.Max(1,e.Count)),FirstTime=g.OrderBy(e=>e.FirstTime,StringComparer.Ordinal).First().FirstTime};
        }).OrderByDescending(e=>e.Time,StringComparer.Ordinal).Take(150).ToArray();
    }
    public static int? DirectorPort(JsonNode services,string ns)
    {
        var ports=services["items"].Items().Where(s=>s.At("metadata","namespace").S()==ns).SelectMany(s=>s.At("spec","ports").Items()).Where(p=>p["port"].I()==11717).Select(p=>p["nodePort"].I()).Where(p=>p is >0 and <=65535).Distinct().ToArray();return ports.Length==1?ports[0]:null;
    }
    public static JsonArray MapPatch(JsonNode bg,MapChange change)
    {
        change.Validate();if(bg["apiVersion"].S()!="igw.funcom.com/v1" || bg["kind"].S()!="BattleGroup")throw new InvalidOperationException("Unknown Battlegroup schema. Use the official editor.");
        if(bg.At("metadata","resourceVersion").S()!=change.Original.ResourceVersion)throw new InvalidOperationException("Battlegroup changed since loading. Refresh and review the change again.");
        var sets=bg.At("spec","serverGroup","template","spec","sets") as JsonArray;var i=change.Original.SetIndex;
        if(sets is null || i>=sets.Count || sets[i]?["map"].S()!=change.Original.Map)throw new InvalidOperationException("Map identity changed. Reload before editing.");
        var patch=new JsonArray();
        void Add(string op,string path,JsonNode? value)=>patch.Add(new JsonObject{{"op",op},{"path",path},{"value",value?.DeepClone()}});
        Add("test","/metadata/resourceVersion",JsonValue.Create(change.Original.ResourceVersion));Add("test",SetsPath+$"/{i}/map",JsonValue.Create(change.Original.Map));
        var resources=sets[i]?["resources"] as JsonObject??throw new InvalidOperationException("Unsupported map resources. Use the official editor.");
        var next=(JsonObject)resources.DeepClone();next["requests"]??=new JsonObject();next["limits"]??=new JsonObject();next["requests"]!["memory"]=change.MemoryRequest;next["limits"]!["memory"]=change.MemoryLimit;
        Add("replace",SetsPath+$"/{i}/resources",next);
        if(change.Minimum!=change.Original.Minimum)
        {
            var text=bg.At("spec","utilities","director","spec","configFiles","files","director.ini");if(text is null)throw new InvalidOperationException("Director configuration not found; use the official editor for minimum instances.");
            var doc=new IniDocument(text.S());doc.Set(change.Original.Map,"MinServers",change.Minimum?.ToString(CultureInfo.InvariantCulture)??throw new InvalidOperationException("Enter a minimum instance count."));Add("replace",DirectorPath,JsonValue.Create(doc.ToString()));
        }
        return patch;
    }
}
