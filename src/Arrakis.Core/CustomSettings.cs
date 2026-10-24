using System.Globalization;
using System.Text.RegularExpressions;

namespace Arrakis.Core;

public static class SettingIdentity
{
    public static readonly string[] Files=["UserGame.ini","UserEngine.ini","UserServerCustomSettings.ini"];
    public static string Of(string file,string section,string key)=>file.ToUpperInvariant()+"\n"+section.ToUpperInvariant()+"\n"+key.ToUpperInvariant();
    public static bool IsSection(string value)=>value!=null&&value.Length is >0 and <=240&&value==value.Trim()&&!value.Any(c=>char.IsControl(c)||c is '[' or ']' or ';' or '#');
    public static bool IsKey(string value)=>value!=null&&value.Length is >0 and <=240&&Regex.IsMatch(value,@"^[A-Za-z_][A-Za-z0-9_.]*$");
    public static bool Sensitive(string key)=>Regex.IsMatch(key,@"password|passwd|token|secret|credential|private.?key|api.?key",RegexOptions.IgnoreCase);
    public static bool Scalar(string value)
    {
        if(value.Length>8192||value.Any(char.IsControl))return false;
        if(value.StartsWith('"'))return value.EndsWith('"')&&value.Length>=2&&!Regex.IsMatch(value[1..^1],"(?<!\\\\)\"");
        return !value.Contains('"')&&!value.Any(c=>c is '(' or ')' or '[' or ']' or '{' or '}')&&!Regex.IsMatch(value,@"(^|\s)[;#]");
    }
    public static string Friendly(string key)
    {
        var curated=key switch {"IGWPort"=>"Inter-Server Port","m_bShouldForceEnablePvpOnAllPartitions"=>"Force PvP on All Partitions","dw.VehicleDurabilityDamageMultiplier"=>"Vehicle Durability Damage","Vehicle.SandwormInvulnerabilitySecondsOnExit"=>"Sandworm Protection After Exiting Vehicle",_=>null};
        if(curated!=null)return curated;
        var name=Regex.Replace(key,@"^(dw\.|Dune\.|m_b|m_)","");
        name=Regex.Replace(name,@"([A-Z]+)([A-Z][a-z])","$1 $2");name=Regex.Replace(name,@"([a-z0-9])([A-Z])","$1 $2").Replace('.',' ').Replace('_',' ');
        name=Regex.Replace(name,@"\bPvp\b","PvP");name=Regex.Replace(name,@"\b(Npc|Xp|Vm)\b",m=>m.Value.ToUpperInvariant());
        return Regex.Replace(name,@"\s+"," ").Trim().Replace("Battl Eye","BattlEye");
    }
}

// No active INI value is part of this persistence/export model. Example/default are explicit user annotations.
public sealed record CustomSettingDefinition
{
    public string File {get;init;}="UserEngine.ini";
    public string Section {get;init;}="ConsoleVariables";
    public string Key {get;init;}="";
    public string FriendlyName {get;init;}="";
    public string Category {get;init;}="Uncategorized";
    public string Description {get;init;}="";
    public string Kind {get;init;}="string";
    public double? Minimum {get;init;}
    public double? Maximum {get;init;}
    public double? Step {get;init;}
    public string[] Choices {get;init;}=[];
    public string Direction {get;init;}="";
    public string? Example {get;init;}
    public string? Default {get;init;}
    public string Restart {get;init;}="Unknown; verify before applying";
    public bool ClientRequired {get;init;}
    public string Notes {get;init;}="";
    public string Identity=>SettingIdentity.Of(File,Section,Key);
    public void Validate()
    {
        if(!SettingIdentity.Files.Contains(File)||!SettingIdentity.IsSection(Section)||!SettingIdentity.IsKey(Key))throw new InvalidOperationException("Choose a supported file, valid section and scalar key (letters, digits, dots and underscores; no operator prefixes).");
        if(new[]{FriendlyName,Category,Description,Direction,Restart,Notes}.Any(s=>s==null||s.Length>4000||s.Contains('\0'))||string.IsNullOrWhiteSpace(FriendlyName)||FriendlyName.Length>160||string.IsNullOrWhiteSpace(Category)||Category.Length>120||FriendlyName.Any(char.IsControl)||Category.Any(char.IsControl))throw new InvalidOperationException("Provide a short friendly name/category and bounded plain-text descriptions.");
        if(Kind is not ("string" or "bool" or "integer" or "number" or "choice"))throw new InvalidOperationException("Unsupported custom value type.");
        if(Minimum is {} minimum&&!double.IsFinite(minimum))throw new InvalidOperationException("Minimum must be a finite number.");
        if(Maximum is {} maximum&&!double.IsFinite(maximum))throw new InvalidOperationException("Maximum must be a finite number.");
        if(Minimum>Maximum)throw new InvalidOperationException("Minimum must not exceed Maximum.");
        if(Minimum is {} low&&Maximum is {} high&&!double.IsFinite(high-low))throw new InvalidOperationException("The numeric range is too large.");
        if(Step is {} increment&&(!double.IsFinite(increment)||increment<=0))throw new InvalidOperationException("Slider Step must be greater than zero and finite.");
        if(Step>1e12||Step<1e-12)throw new InvalidOperationException("Slider Step must be between 1e-12 and 1e12.");
        if((Minimum!=null||Maximum!=null||Step!=null)&&Kind is not ("number" or "integer"))throw new InvalidOperationException("Numeric bounds/step require a numeric type.");
        if(Kind=="integer"&&new[]{Minimum,Maximum,Step}.Any(n=>n.HasValue&&n!=Math.Truncate(n.Value)))throw new InvalidOperationException("Integer bounds/step must be whole numbers.");
        if(Choices==null||Choices.Length>100)throw new InvalidOperationException("Allowed Choices must contain at most 100 values.");
        if(Kind=="choice"&&(Choices.Length==0||Choices.Any(v=>string.IsNullOrWhiteSpace(v))))throw new InvalidOperationException("Allowed Choices cannot be empty or contain empty entries. Separate values with |.");
        if(Choices.Any(v=>v==null||!SettingIdentity.Scalar(v)))throw new InvalidOperationException("Allowed Choices must be simple single-line values.");
        if(Choices.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Choices.Length)throw new InvalidOperationException("Allowed Choices cannot contain duplicates (case-insensitive).");
        if(Kind!="choice"&&Choices.Length>0)throw new InvalidOperationException("Allowed Choices apply only to Enumerated Choice.");
        foreach(var (field,value) in new[]{("Example Value",Example),("User Default",Default)})if(AnnotationError(field,value) is {} annotationError)throw new InvalidOperationException(annotationError);
        if(SettingIdentity.Sensitive(Key)&&(!string.IsNullOrWhiteSpace(Example)||!string.IsNullOrWhiteSpace(Default)))throw new InvalidOperationException("Do not store secret examples/defaults in metadata.");
        if(Regex.IsMatch(string.Join("\n",FriendlyName,Description,Direction,Notes,Example,Default),@"-----BEGIN .*PRIVATE KEY|(?:ghp_|github_pat_)[A-Za-z0-9_]{20,}|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.",RegexOptions.IgnoreCase))throw new InvalidOperationException("Credential-like material cannot be stored in custom metadata.");
    }
    public string? AnnotationError(string field,string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        if(!SettingIdentity.Scalar(value))return field+" must be a simple single-line value.";
        if(Kind=="bool"&&!new[]{"True","False"}.Contains(value,StringComparer.OrdinalIgnoreCase))return field+" must be a valid Boolean (True or False).";
        if(Kind=="choice"&&!Choices.Contains(value,StringComparer.OrdinalIgnoreCase))return field+" must be one of the configured choices.";
        if(Kind is "integer" or "number")
        {
            if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number))return field+" must be a finite number using a decimal point.";
            if(Kind=="integer"&&number!=Math.Truncate(number))return field+" must be a whole number.";
            if(number<Minimum||number>Maximum)return field+" is outside the configured numeric range.";
        }
        return null;
    }
    public SettingDefinition ToDefinition(SettingDefinition? builtIn=null)=>new(File,Section,Key,Category,Description,string.IsNullOrWhiteSpace(Example)?"":Example,Kind,Minimum,Maximum,Choices.Length>0?Choices.ToArray():null,Direction.Length>0?Direction:"Behavior not verified; user/community supplied.",ClientRequired,"Local user metadata; not official or independently verified",string.IsNullOrWhiteSpace(Default)?null:Default,
        FriendlyName,"Custom",Restart,Step,Notes,builtIn?.Details??"",builtIn?.Default);
}

public sealed record ResolvedSetting(SettingDefinition Definition,string Source,bool IsPresent,bool RawOnly,string Reason);
public static class SettingResolver
{
    public static IReadOnlyList<ResolvedSetting> Resolve(string file,string text,IEnumerable<CustomSettingDefinition>? custom=null)
    {
        if(!SettingIdentity.Files.Contains(file))throw new InvalidOperationException("Unsupported configuration file.");
        var metadata=(custom??[]).ToArray();foreach(var item in metadata)item.Validate();
        var overrides=metadata.ToDictionary(d=>d.Identity,StringComparer.Ordinal);
        var entries=new IniDocument(text).Entries();
        var groups=entries.GroupBy(e=>SettingIdentity.Of(file,e.Section,e.Key)).ToDictionary(g=>g.Key,g=>g.ToArray());
        var definitions=SettingCatalog.All.Where(d=>d.File==file).ToDictionary(d=>SettingIdentity.Of(d.File,d.Section,d.Key));
        foreach(var item in metadata.Where(d=>d.File==file))definitions.TryAdd(item.Identity,item.ToDefinition());
        foreach(var entry in entries)definitions.TryAdd(SettingIdentity.Of(file,entry.Section,entry.Key),new(file,entry.Section,entry.Key,"Uncategorized","Detected from configuration. Confirm type and behavior before changing.","","string",Evidence:"Detected from configuration; behavior, range and default not verified",FriendlyName:SettingIdentity.Friendly(entry.Key),Source:"Discovered",Restart:"Unknown; verify before applying"));
        var sectionNames=new IniDocument(text).Sections();
        var malformed=text.Replace("\r\n","\n").Split('\n').Any(l=>l.TrimStart().StartsWith('[')&&!Regex.IsMatch(l.Trim(),@"^\[[^\]\r\n]+\]\s*(?:[;#].*)?$"));
        return definitions.Select(pair=>
        {
            var definition=pair.Value;var builtIn=SettingCatalog.All.FirstOrDefault(d=>SettingIdentity.Of(d.File,d.Section,d.Key)==pair.Key);
            var source=builtIn!=null?"Built-in":"Discovered";
            if(overrides.TryGetValue(pair.Key,out var local)){definition=local.ToDefinition(builtIn);source="Custom";}
            var present=groups.TryGetValue(pair.Key,out var found);
            var reason=malformed?"Malformed section in draft; resolve it in Raw INI editing first.":!SettingIdentity.IsSection(definition.Section)||!SettingIdentity.IsKey(definition.Key)?"Unsupported section/key or collection operator; use Raw INI editing.":
                present&&found!.Length!=1?"Repeated directive; precedence is ambiguous. Use Raw INI editing.":
                entries.Any(e=>e.Section.Equals(definition.Section,StringComparison.OrdinalIgnoreCase)&&e.Key.Length>1&&"+-!.".Contains(e.Key[0])&&e.Key[1..].Equals(definition.Key,StringComparison.OrdinalIgnoreCase))?"Related collection operators make this key ambiguous. Use Raw INI editing.":
                sectionNames.Count(s=>s.Equals(definition.Section,StringComparison.OrdinalIgnoreCase))>1?"Repeated section; insertion/precedence needs Raw INI review.":
                present&&!SettingIdentity.Scalar(found![0].Value)?"Complex or unparseable value; use Raw INI editing.":
                SettingIdentity.Sensitive(definition.Key)?"Sensitive value masked. Use the explicit Raw INI workflow; never export secrets.":"";
            return new ResolvedSetting(definition,reason.Length>0?"Raw-only":source,present,reason.Length>0,reason);
        }).ToArray();
    }
}

public sealed record CustomCatalogDocument(int SchemaVersion,CustomSettingDefinition[] Definitions);
public interface ICustomSettingStore
{
    string Status {get;}
    bool ReadOnly {get;}
    IReadOnlyList<CustomSettingDefinition> Load();
    void Save(IReadOnlyList<CustomSettingDefinition> definitions);
}
