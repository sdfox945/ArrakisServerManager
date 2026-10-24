using System.Text;
using System.IO;
using System.Text.Json;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static class CustomConfigurationRegression
{
    internal static CustomSettingDefinition Definition(string key="BattlEye.Enabled")=>new(){File="UserEngine.ini",Section="ConsoleVariables",Key=key,FriendlyName=SettingIdentity.Friendly(key),Kind="bool",Category="Security / Anti-Cheat",Description="User/community supplied example; not independently verified."};
    internal static ConfigFile File(string text,Encoding? encoding=null)
    {
        encoding??=new UTF8Encoding(false);var bytes=encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();return new("UserEngine.ini","/isolated/UserSettings/UserEngine.ini",bytes,ConfigurationIntegrity.Hash(bytes));
    }
    private static void Check(bool condition,string message="Custom configuration assertion failed"){if(!condition)throw new InvalidOperationException(message);}
    private static void Reject(Action work){try{work();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or JsonException or NotSupportedException or IOException){return;}throw new Exception("Expected refusal.");}
    public static void Register(Action<string,Func<Task>> add,string root)
    {
        void Test(string name,Action action)=>add("Custom config: "+name,()=>{action();return Task.CompletedTask;});
        foreach(var file in SettingIdentity.Files)Test("discovery combines built-ins and active entries in "+file,()=>
        {
            var original=ConfigurationIntegrityRegression.Fixture(file);var rows=SettingResolver.Resolve(file,original.Text);Check(rows.Any(r=>r.Source=="Built-in")&&rows.Any(r=>r.Source=="Discovered"));Check(original.Hash==ConfigurationIntegrity.Hash(original.Bytes));
        });
        Test("comments never become active variables and unknown values stay text",()=>
        {
            var rows=SettingResolver.Resolve("UserEngine.ini","[ConsoleVariables]\n;Disabled=1\n# Example=2\ndw.VehiclePowerConsumptionMultiplier=0.500000\nFlag=1\nOther=false\n");
            Check(rows.Count(r=>r.IsPresent)==3);foreach(var row in rows.Where(r=>r.IsPresent))Check(row.Definition.Kind=="string"&&row.Definition.Min==null&&row.Definition.Default==null&&row.Definition.TemplateValue=="");
        });
        var ambiguous=new Dictionary<string,string>{{"duplicate scalar","[S]\nK=1\nk=2\n"},{"collection directives","[S]\n+K=1\n+K=2\nK=3\n"},{"complex value","[S]\nK=(A=1,B=2)\n"},{"repeated section","[S]\nK=1\n[S]\nOther=2\n"},{"unclosed quote","[S]\nK=\"unclosed\n"},{"malformed section","[Bad\nK=1\n"}};
        foreach(var (name,text) in ambiguous)Test("raw-only: "+name,()=>{var rows=SettingResolver.Resolve("UserEngine.ini",text).Where(r=>r.IsPresent).ToArray();Check(rows.Length>0&&rows.All(r=>r.RawOnly));});
        Test("stable case-insensitive identity preserves exact spelling",()=>
        {
            var d=Definition() with{Key="battleye.enabled",Section="consolevariables"};var rows=SettingResolver.Resolve("UserEngine.ini","[ConsoleVariables]\nBattlEye.Enabled=False\n",[d]);Check(rows.Count(r=>r.IsPresent)==1&&rows.Single(r=>r.IsPresent).Source=="Custom");var draft=new ConfigurationDraft(File("[ConsoleVariables]\nBattlEye.Enabled=False\n"));draft.RegisterDefinitions([d]);draft.ApplyVisual(d.ToDefinition(),"true");Check(draft.Text=="[ConsoleVariables]\nBattlEye.Enabled=true\n");
        });
        Test("friendly names handle namespaces prefixes acronyms and curated labels",()=>
        {
            foreach(var pair in new Dictionary<string,string>{{"BattlEye.Enabled","BattlEye Enabled"},{"Sandstorm.Treasure.Enabled","Sandstorm Treasure Enabled"},{"Dune.GlobalMiningOutputMultiplier","Global Mining Output Multiplier"},{"dw.VehicleDurabilityDamageMultiplier","Vehicle Durability Damage"},{"Vehicle.SandwormInvulnerabilitySecondsOnExit","Sandworm Protection After Exiting Vehicle"},{"IGWPort","Inter-Server Port"},{"m_bShouldForceEnablePvpOnAllPartitions","Force PvP on All Partitions"},{"m_bNpc_XpVm","NPC XP VM"}})Check(SettingIdentity.Friendly(pair.Key)==pair.Value,pair.Key+" name was "+SettingIdentity.Friendly(pair.Key));
        });
        foreach(var type in new[]{"bool","integer","number","string","choice"})Test("registered "+type+" definition validates and edits",()=>
        {
            var value=type switch{"bool"=>"false","integer"=>"12","number"=>"0.123456789012345","choice"=>"B",_=>"Keep.Case"};var d=Definition() with{Kind=type,Choices=type=="choice"?["A","B"]:[]};d.Validate();var draft=new ConfigurationDraft(File("[ConsoleVariables]\nUnknown=keep\n"));draft.RegisterDefinitions([d]);draft.ApplyVisual(d.ToDefinition(),value);var review=draft.PrepareReview(new(d.File,draft.Original.RemotePath));review.Validate();Check(new IniDocument(draft.Text).Get(d.Section,d.Key)==value&&Encoding.UTF8.GetString(review.Payload)==draft.Text);
        });
        Test("numeric bounds step choices and initial values fail closed",()=>
        {
            foreach(var d in new[]{Definition() with{Kind="number",Minimum=2,Maximum=1},Definition() with{Kind="number",Step=0},Definition() with{Kind="integer",Step=.1},Definition() with{Kind="choice",Choices=["A","a"]},Definition() with{Kind="choice"},Definition() with{Kind="bool",Minimum=0},Definition() with{Default="neither"},Definition() with{Kind="number",Maximum=double.PositiveInfinity}})Reject(d.Validate);
            var definition=Definition() with{Kind="number",Minimum=0,Maximum=1,Step=.01};var draft=new ConfigurationDraft(File("[S]\nX=1\n"));draft.RegisterDefinitions([definition]);Reject(()=>draft.ApplyVisual(definition.ToDefinition(),"2"));Check(!draft.IsDirty);
        });
        Test("custom insertion preserves BOM encoding lines comments and no-final-newline",()=>
        {
            foreach(var encoding in new Encoding[]{new UTF8Encoding(false),new UTF8Encoding(true),new UnicodeEncoding(false,true),new UnicodeEncoding(true,true)})foreach(var ending in new[]{"\n","\r\n"})
            {
                var text="; keep"+ending+"[ConsoleVariables]"+ending+"Unknown = keep ; note";var original=File(text,encoding);var d=Definition();var draft=new ConfigurationDraft(original);draft.RegisterDefinitions([d]);draft.ApplyVisual(d.ToDefinition(),"false");var review=draft.PrepareReview(new(original.Name,original.RemotePath));review.Validate();Check(review.Payload.SequenceEqual(original.Encode(text+ending+"BattlEye.Enabled=false"+ending)));
            }
        });
        Test("editing a discovered scalar produces exactly one line and preserves quotes",()=>
        {
            var original=File("[ConsoleVariables]\nCustom.Value=\"Keep Case\" ; original\nUnknown=stay\n");var draft=new ConfigurationDraft(original);var row=SettingResolver.Resolve(original.Name,draft.Text).Single(r=>r.Definition.Key=="Custom.Value");draft.ApplyVisual(row.Definition,"\"New Case\"");var review=draft.PrepareReview(new(original.Name,original.RemotePath));review.Validate();Check(review.Diff.Split(Environment.NewLine).Length==2&&draft.Text==original.Text.Replace("Keep Case","New Case"));
        });
        Test("new sections require registered definitions and replay only the intended insertion",()=>
        {
            var original=File("[S]\nX=1\n");var draft=new ConfigurationDraft(original);var d=Definition() with{Section="New.Section"};Reject(()=>draft.ApplyVisual(d.ToDefinition(),"false"));draft.RegisterDefinitions([d]);draft.ApplyVisual(d.ToDefinition(),"false");draft.PrepareReview(new(original.Name,original.RemotePath)).Validate();Check(draft.Text==original.Text+"\n[New.Section]\nBattlEye.Enabled=false\n");
        });
        Test("key section operator and value injection cannot enter visual mutations",()=>
        {
            foreach(var key in new[]{"", "+Array","-Array","A=B","A\nOther","A;B","A B"})Reject(()=>(Definition() with{Key=key}).Validate());
            foreach(var section in new[]{"", "S]\n[X", "[Section]"})Reject(()=>(Definition() with{Section=section}).Validate());
            var d=Definition() with{Kind="string"};var draft=new ConfigurationDraft(File("[S]\nX=1\n"));draft.RegisterDefinitions([d]);foreach(var value in new[]{"1\nOther=2","1 ; hidden comment","(array)","x\0"})Reject(()=>draft.ApplyVisual(d.ToDefinition(),value));Check(!draft.IsDirty);
        });
        Test("registered annotations cannot loosen built-in validation or protected entries",()=>
        {
            var original=ConfigurationIntegrityRegression.Fixture();var d=new CustomSettingDefinition{File=original.Name,Section=ConfigurationIntegrity.CustomSection,Key="DifficultyLevel",FriendlyName="Local name",Kind="string"};var draft=new ConfigurationDraft(original);draft.RegisterDefinitions([d]);Reject(()=>draft.ApplyVisual(d.ToDefinition(),"Other"));Check(!draft.IsDirty);
        });
        Test("metadata changes do not alter bytes or invalidate a frozen payload",()=>
        {
            var original=File("[ConsoleVariables]\nX=1\n");var draft=new ConfigurationDraft(original);var d=Definition();draft.RegisterDefinitions([d]);Check(!draft.IsDirty&&draft.Revision==0);draft.ApplyVisual(d.ToDefinition(),"false");var review=draft.PrepareReview(new(original.Name,original.RemotePath));draft.RegisterDefinitions([]);Check(draft.Matches(review));review.Validate();
        });
        Test("new raw entries are immediately rediscovered without normalization",()=>
        {
            var original=File("[S]\nX=1\n");var draft=new ConfigurationDraft(original);draft.EditRaw(original.Text+"RawNumber=001.2300\n");var row=SettingResolver.Resolve(original.Name,draft.Text).Single(r=>r.Definition.Key=="RawNumber");Check(row.Definition.Kind=="string"&&!row.RawOnly&&new IniDocument(draft.Text).Get("S","RawNumber")=="001.2300");draft.ApplyVisual(row.Definition,"001.2400");draft.PrepareReview(new(original.Name,original.RemotePath)).Validate();
        });
        Test("built-in upgrade reconciles custom identity and preserves user annotations",()=>
        {
            var known=SettingCatalog.All.First(d=>d.Key=="Dune.GlobalMiningOutputMultiplier");var custom=Definition(known.Key) with{File=known.File,Section=known.Section,FriendlyName="My mining",Kind="number",Category="My group"};var rows=SettingResolver.Resolve(known.File,$"[{known.Section}]\n{known.Key}=1\n",[custom]);var row=rows.Single(r=>r.Definition.Key==known.Key);Check(row.Source=="Custom"&&row.Definition.Name=="My mining"&&row.Definition.BuiltInDetails.Contains(known.Description)&&rows.Count(r=>r.Definition.Key==known.Key)==1);
        });
        Test("atomic catalog restart recovery and damaged-file retention",()=>
        {
            var path=Path.Combine(root,"custom-store");var a=new CustomSettingStore(path,true);a.Load();a.Save([Definition()]);a.Save([Definition() with{FriendlyName="Changed"}]);var current=Path.Combine(path,"CustomSettings/Demo/definitions.json");System.IO.File.WriteAllText(current,"{broken");var b=new CustomSettingStore(path,true);Check(b.Load().Single().FriendlyName=="BattlEye Enabled"&&!b.ReadOnly&&b.Status.Contains("Recovered"));b.Save([Definition()]);Check(Directory.GetFiles(Path.GetDirectoryName(current)!,"*.damaged-*").Length==1&&new CustomSettingStore(path,true).Load().Count==1);
        });
        Test("malformed catalog without backup and future schema are preserved read-only",()=>
        {
            foreach(var json in new[]{"{bad","{\"SchemaVersion\":99,\"Definitions\":[]}"}){var path=Path.Combine(root,"bad-"+Guid.NewGuid());var folder=Path.Combine(path,"CustomSettings/Live");Directory.CreateDirectory(folder);System.IO.File.WriteAllText(Path.Combine(folder,"definitions.json"),json);var store=new CustomSettingStore(path,false);Check(store.Load().Count==0&&store.ReadOnly);Reject(()=>store.Save([]));Check(System.IO.File.ReadAllText(Path.Combine(folder,"definitions.json"))==json);}
        });
        Test("schema zero migrates deterministically without dropping definitions",()=>
        {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new CustomCatalogDocument(0,[Definition()]));var parsed=CustomSettingStore.Parse(bytes);Check(parsed.SchemaVersion==1&&parsed.Definitions.Single().Key=="BattlEye.Enabled");
        });
        Test("external metadata edits are detected before replacement",()=>
        {
            var path=Path.Combine(root,"catalog-conflict");var a=new CustomSettingStore(path,false);a.Load();a.Save([Definition()]);var b=new CustomSettingStore(path,false);b.Load();a.Save([Definition() with{FriendlyName="External"}]);Reject(()=>b.Save([]));Check(new CustomSettingStore(path,false).Load().Single().FriendlyName=="External");
        });
        Test("Demo Live isolation and export contain metadata not current values",()=>
        {
            var path=Path.Combine(root,"isolated-catalog");var demo=new CustomSettingStore(path,true);demo.Load();demo.Save([Definition()]);Check(new CustomSettingStore(path,false).Load().Count==0);var file=Path.Combine(root,"custom-export.json");CustomSettingStore.Export(file,demo.Load());Check(CustomSettingStore.Import(file).Count==1&&!System.IO.File.ReadAllText(file).Contains("InitialValue")&&!System.IO.File.ReadAllText(file).Contains("\"Value\""));
        });
        Test("import rejects duplicate identities unsupported targets and oversized files",()=>
        {
            Reject(()=>CustomSettingStore.Serialize([Definition(),Definition() with{Key="battleye.enabled"}]));Reject(()=>CustomSettingStore.Serialize([Definition() with{File="/etc/passwd"}]));Reject(()=>CustomSettingStore.Parse(new byte[CustomSettingStore.Limit+1]));Reject(()=>CustomSettingStore.Parse(Encoding.UTF8.GetBytes("{}")));
        });
        Test("sensitive entries are masked raw-only and secret examples are forbidden",()=>
        {
            var row=SettingResolver.Resolve("UserEngine.ini","[S]\nApiToken=fixture-value\n").Single(r=>r.IsPresent);Check(row.RawOnly&&row.Reason.Contains("masked"));Reject(()=>(Definition("ApiToken") with{Kind="string",Example="fixture-value"}).Validate());
        });
        Test("sensitive dotted INI values redact full quoted lines and API credentials",()=>
        {
            var cleaned=Redactor.Clean("+ Community.ApiKey=\"two word fixture secret\"\nPassword=\"space separated fixture\"\nNormal=keep\n");Check(!cleaned.Contains("two word")&&!cleaned.Contains("space separated")&&cleaned.Contains("Normal=keep"));
        });
        Test("duplicate schema fields cannot hide a future schema",()=>Reject(()=>CustomSettingStore.Parse(Encoding.UTF8.GetBytes("{\"SchemaVersion\":1,\"schemaVersion\":99,\"Definitions\":[]}"))));
        Test("multiple inserted keys retain replay order after reediting and metadata case changes",()=>
        {
            var original=File("[ConsoleVariables]\nX=keep\n");var draft=new ConfigurationDraft(original);var first=Definition("Community.First") with{Kind="string"};var second=Definition("Community.Second") with{Kind="string"};draft.RegisterDefinitions([first,second]);draft.ApplyVisual(first.ToDefinition(),"a");draft.ApplyVisual(second.ToDefinition(),"b");var recased=first with{Key="community.first"};draft.RegisterDefinitions([recased,second]);draft.ApplyVisual(recased.ToDefinition(),"edited");var review=draft.PrepareReview(new(original.Name,original.RemotePath));review.Validate();Check(draft.Text==original.Text+"Community.First=edited\nCommunity.Second=b\n");
        });
    }
}
