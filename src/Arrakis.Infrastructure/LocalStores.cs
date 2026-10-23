using System.Text.Json;
using System.Text.Json.Serialization;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed class LocalStores(string root) : IPreferencesStore,IHistoryStore
{
    private readonly object sync=new();
    public string Root { get; }=root;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
    public Preferences Load() { try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Root,"preferences.json")),Json)??new(); }catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return new();} }
    public void Save(Preferences p) { var errors=p.Validate().ToArray();if(errors.Length>0)throw new InvalidOperationException(string.Join("\n",errors));Directory.CreateDirectory(Root);var path=Path.Combine(Root,"preferences.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(p,Json));File.Move(path+".tmp",path,true); }
    IReadOnlyList<OperationRecord> IHistoryStore.Load() { try{return JsonSerializer.Deserialize<List<OperationRecord>>(File.ReadAllText(Path.Combine(Root,"history.json")),Json)??[];}catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return [];} }
    public void Append(OperationRecord record)
    {
        lock(sync) { Directory.CreateDirectory(Root);var existing=((IHistoryStore)this).Load();var guard=record.Name=="[Safety guard]"?record:existing.LastOrDefault(r=>r.Name=="[Safety guard]");var records=existing.Where(r=>r.Name!="[Safety guard]").TakeLast(198).ToList();if(guard!=null)records.Add(guard);if(record.Name!="[Safety guard]")records.Add(record with { Output=Redactor.Clean(record.Output), Details=Redactor.Clean(record.Details??"") });var path=Path.Combine(Root,"history.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(records,Json));File.Move(path+".tmp",path,true); }
    }
}
