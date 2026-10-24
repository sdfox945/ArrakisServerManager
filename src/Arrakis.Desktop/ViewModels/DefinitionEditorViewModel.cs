using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Arrakis.Core;

namespace Arrakis.Desktop.ViewModels;

public sealed class DefinitionEditorViewModel : Bindable
{
    private readonly ConfigurationViewModel owner;
    private readonly SettingDefinition? builtIn;
    private Dictionary<string,string> errors=[];
    private string operationError="";
    public Guid DocumentId {get;}
    public long DocumentRevision {get;}
    public bool MetadataOnly {get;}
    public bool IsAdding=>!MetadataOnly;
    public string AcceptCaption=>MetadataOnly?"Save local definition":"Add variable to draft";
    public string Title=>MetadataOnly?"Manage Setting Definition":"Add Variable";
    public string Purpose=>MetadataOnly?"Editing this definition does not change the server's INI.":"Add an explicit draft change. Nothing is written to the server until normal review and save.";
    public string IdentityCaption=>$"{File} · [{Section}] · {Key}";
    public string PreviewCaption=>MetadataOnly?"Local metadata only":"Intended draft representation";
    public string File {get;}
    public string[] Sections=>owner.SectionNames;
    public string[] Categories=>owner.CategoryNames;
    public string[] Types {get;}=["Boolean","Integer","Decimal number","String","Enumerated choice"];
    // Include the exact user-selected casing without changing any INI representation.
    public string[] BooleanValues=>new[]{Value.Equals("false",StringComparison.OrdinalIgnoreCase)?Value:"false",Value.Equals("true",StringComparison.OrdinalIgnoreCase)?Value:"true"};
    public bool IdentityEditable=>!MetadataOnly;
    private string section="",key="",friendly="",category="Uncategorized",description="",kind="String",value="",min="",max="",step="",choices="",direction="",example="",defaultValue="",restart="Unknown; verify before applying",notes="";
    private bool client,allowNewSection;
    public DefinitionEditorViewModel(ConfigurationViewModel owner,Guid id,long revision,bool metadataOnly,CustomSettingDefinition? custom,SettingDefinition? definition)
    {
        this.owner=owner;DocumentId=id;DocumentRevision=revision;MetadataOnly=metadataOnly;File=owner.SelectedFile!.Name;
        builtIn=definition==null?null:SettingCatalog.All.FirstOrDefault(d=>SettingIdentity.Of(d.File,d.Section,d.Key)==SettingIdentity.Of(definition.File,definition.Section,definition.Key));
        var source=custom??(definition==null?null:new CustomSettingDefinition{File=File,Section=definition.Section,Key=definition.Key,FriendlyName=definition.Name,Category=definition.Category,Description=definition.Description,Kind=definition.Kind=="quotedString"?"string":definition.Kind,Minimum=definition.Min,Maximum=definition.Max,Choices=definition.Choices??[],Direction=definition.Direction,Restart=definition.Restart,ClientRequired=definition.ClientRequired});
        if(source!=null)Populate(source);else section=Sections.FirstOrDefault()??"ConsoleVariables";
        AcceptCommand=new RelayCommand(Accept,()=>CanAccept);SuggestNameCommand=new RelayCommand(()=>FriendlyName=SettingIdentity.Friendly(Key));
        ResetBuiltInCommand=new RelayCommand(ResetBuiltIn,()=>builtIn!=null);
        ClearIncompatibleCommand=new RelayCommand(ClearIncompatible,()=>CanClearIncompatible);
        FocusIssueCommand=new RelayCommand(()=>FocusRequested?.Invoke(errors.Keys.First()),()=>errors.Count>0);
        RefreshValidation();
    }
    public event Action? Accepted;
    public event Action<string>? FocusRequested;
    public RelayCommand AcceptCommand {get;}
    public RelayCommand SuggestNameCommand {get;}
    public RelayCommand ResetBuiltInCommand {get;}
    public RelayCommand ClearIncompatibleCommand {get;}
    public RelayCommand FocusIssueCommand {get;}
    private void Edit(ref string field,string? next,[CallerMemberName]string? property=null){if(next!=null&&Set(ref field,next,property))Changed();}
    private void Changed()
    {
        operationError="";RefreshValidation();
        foreach(var name in new[]{nameof(Preview),nameof(IdentityCaption),nameof(IsBoolean),nameof(IsTextValue),nameof(IsChoice),nameof(IsEnumeration),nameof(IsNumeric),nameof(ChoiceValues),nameof(BooleanValues),nameof(SectionExplanation),nameof(TypeHelp),nameof(InactiveMetadataNotice)})OnPropertyChanged(name);
    }
    public bool AllowNewSection {get=>allowNewSection;set{if(Set(ref allowNewSection,value))Changed();}}
    public string Section {get=>section;set{if(!MetadataOnly)Edit(ref section,value);}}
    public string Key {get=>key;set{if(MetadataOnly)return;var auto=friendly==SettingIdentity.Friendly(key)||friendly.Length==0;if(value!=null&&Set(ref key,value)){if(auto){friendly=SettingIdentity.Friendly(key);OnPropertyChanged(nameof(FriendlyName));}Changed();}}}
    public string FriendlyName {get=>friendly;set=>Edit(ref friendly,value);}
    public string Category {get=>category;set=>Edit(ref category,value);}
    public string Description {get=>description;set=>Edit(ref description,value);}
    public string Kind {get=>kind;set{if(value!=null&&Types.Contains(value)&&Set(ref kind,value))Changed();}}
    public string Value {get=>value;set=>Edit(ref this.value,value);}
    public string Minimum {get=>min;set=>Edit(ref min,value);}
    public string Maximum {get=>max;set=>Edit(ref max,value);}
    public string Step {get=>step;set=>Edit(ref step,value);}
    public string Choices {get=>choices;set=>Edit(ref choices,value);}
    public string Direction {get=>direction;set=>Edit(ref direction,value);}
    public string Example {get=>example;set=>Edit(ref example,value);}
    public string Default {get=>defaultValue;set=>Edit(ref defaultValue,value);}
    public string Restart {get=>restart;set=>Edit(ref restart,value);}
    public string Notes {get=>notes;set=>Edit(ref notes,value);}
    public bool ClientRequired {get=>client;set{if(Set(ref client,value))Changed();}}
    public string Error=>operationError.Length>0?operationError:errors.Values.FirstOrDefault()??"";
    public bool CanAccept=>errors.Count==0;
    public bool HasIssues=>errors.Count>0;
    public bool CanClearIncompatible=>InactiveMetadataNotice.Length>0||new[]{nameof(Example),nameof(Default),nameof(Minimum),nameof(Maximum),nameof(Step)}.Any(errors.ContainsKey);
    public bool IsNumeric=>Kind is "Integer" or "Decimal number";
    public bool IsEnumeration=>Kind=="Enumerated choice";
    public bool IsBoolean=>IsAdding&&Kind=="Boolean";
    public bool IsChoice=>IsAdding&&IsEnumeration;
    public bool IsTextValue=>IsAdding&&!IsBoolean&&!IsChoice;
    public string[] ChoiceValues=>Choices.Split('|',StringSplitOptions.TrimEntries);
    public string TypeHelp=>Kind switch
    {
        "Boolean"=>"Two states: True or False. No manual choices list is needed. Letter case is accepted without rewriting existing INI text.",
        "Integer"=>"Whole numbers. Optional minimum, maximum and whole-number slider step are user-defined; ranges are not guessed.",
        "Decimal number"=>"Numbers with decimal precision. Use a decimal point. Optional bounds and slider step do not round typed INI values.",
        "Enumerated choice"=>"A finite list of values. Separate choices with |; empty or duplicate choices are not allowed.",
        _=>"Single-line text. Existing spelling, case and quotation are preserved. No numeric range or Boolean type is inferred."
    };
    public string InactiveMetadataNotice=>(!IsNumeric&&new[]{Minimum,Maximum,Step}.Any(s=>!string.IsNullOrWhiteSpace(s))||!IsEnumeration&&!string.IsNullOrWhiteSpace(Choices))?"Type-specific fields from another type are retained in this window but excluded from the saved definition. Switch back to recover them, or explicitly clear incompatible metadata. Example Value and User Default still need to match the chosen type.":"";
    public string SectionExplanation=>Sections.Contains(Section,StringComparer.OrdinalIgnoreCase)?"Existing INI section. The game reads variables from this section.":"A new section will be created only in the draft. Confirm the game expects this section.";
    public string Preview=>MetadataOnly?"Metadata only — no INI value changes.":"["+Section+"]\n"+Key+"="+Value;
    public string ExampleError=>Issue(nameof(Example));
    public string DefaultError=>Issue(nameof(Default));
    public string MinimumError=>Issue(nameof(Minimum));
    public string MaximumError=>Issue(nameof(Maximum));
    public string StepError=>Issue(nameof(Step));
    public string ChoicesError=>Issue(nameof(Choices));
    public string InitialValueError=>Issue(nameof(Value));
    private string Issue(string name)=>errors.GetValueOrDefault(name)??"";
    private static string? Optional(string text)=>string.IsNullOrWhiteSpace(text)?null:text;
    private CustomSettingDefinition BuildDefinition(out Dictionary<string,string> validation)
    {
        var fields=new Dictionary<string,string>();validation=fields;
        double? Number(string text,string field)
        {
            if(string.IsNullOrWhiteSpace(text)||!IsNumeric)return null;
            if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)){fields[field]=field+" must be a finite number using a decimal point.";return null;}
            return n;
        }
        var definition=new CustomSettingDefinition{File=File,Section=Section,Key=Key,FriendlyName=FriendlyName,Category=Category,Description=Description,Kind=Kind switch{"Boolean"=>"bool","Integer"=>"integer","Decimal number"=>"number","Enumerated choice"=>"choice",_=>"string"},Minimum=Number(Minimum,nameof(Minimum)),Maximum=Number(Maximum,nameof(Maximum)),Step=Number(Step,nameof(Step)),Choices=IsEnumeration?ChoiceValues:[],Direction=Direction,Example=Optional(Example),Default=Optional(Default),Restart=Restart,ClientRequired=ClientRequired,Notes=Notes};
        if(!SettingIdentity.IsSection(Section))fields[nameof(Section)]="INI Section must be a valid section name.";
        else if(!MetadataOnly&&!Sections.Contains(Section,StringComparer.OrdinalIgnoreCase)&&!AllowNewSection)fields[nameof(Section)]="Choose an existing section or explicitly allow a new section under Advanced.";
        if(!SettingIdentity.IsKey(Key))fields[nameof(Key)]="Variable Key must use letters, digits, dots or underscores; no operators or line breaks.";
        else if(!MetadataOnly&&owner.ContainsVariable(Section,Key))fields[nameof(Key)]="This variable already exists. Select it and use Manage definition instead of inserting a duplicate.";
        if(IsNumeric&&Kind=="Integer")foreach(var pair in new[]{(nameof(Minimum),definition.Minimum),(nameof(Maximum),definition.Maximum),(nameof(Step),definition.Step)})if(pair.Item2 is {} n&&n!=Math.Truncate(n))fields[pair.Item1]=(pair.Item1==nameof(Step)?"Slider Step":pair.Item1)+" must be a whole number.";
        if(definition.Minimum>definition.Maximum)fields[nameof(Minimum)]="Minimum must not exceed Maximum.";
        if(definition.Step<=0)fields[nameof(Step)]="Slider Step must be greater than zero.";
        if(definition.AnnotationError("Example Value",definition.Example) is {} sampleError)fields[nameof(Example)]=sampleError;
        if(definition.AnnotationError("User Default",definition.Default) is {} defaultError)fields[nameof(Default)]=defaultError;
        try{definition.Validate();}catch(Exception e)when(e is InvalidOperationException or ArgumentException)
        {
            var field=e.Message.StartsWith("Allowed Choices")?nameof(Choices):e.Message.StartsWith("Slider Step")?nameof(Step):e.Message.StartsWith("Minimum")?nameof(Minimum):e.Message.StartsWith("Maximum")?nameof(Maximum):e.Message.StartsWith("Example Value")?nameof(Example):e.Message.StartsWith("User Default")?nameof(Default):string.IsNullOrWhiteSpace(FriendlyName)?nameof(FriendlyName):string.IsNullOrWhiteSpace(Category)?nameof(Category):"Definition";
            fields.TryAdd(field,e.Message);
        }
        if(!MetadataOnly)
        {
            var initial=definition.AnnotationError("Initial Value",Value);
            if(!SettingIdentity.Scalar(Value))initial="Initial Value must be a simple single-line value.";
            if(string.IsNullOrWhiteSpace(Value)&&Kind!="String")initial="Initial Value is required for this type.";
            if(initial!=null)fields[nameof(Value)]=initial;
        }
        return definition;
    }
    private void RefreshValidation()
    {
        BuildDefinition(out errors);
        foreach(var name in new[]{nameof(Error),nameof(CanAccept),nameof(HasIssues),nameof(CanClearIncompatible),nameof(ExampleError),nameof(DefaultError),nameof(MinimumError),nameof(MaximumError),nameof(StepError),nameof(ChoicesError),nameof(InitialValueError)})OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
    private void Populate(CustomSettingDefinition d){section=d.Section;key=d.Key;friendly=d.FriendlyName;category=d.Category;description=d.Description;kind=d.Kind switch{"bool"=>"Boolean","integer"=>"Integer","number"=>"Decimal number","choice"=>"Enumerated choice",_=>"String"};min=d.Minimum?.ToString(CultureInfo.InvariantCulture)??"";max=d.Maximum?.ToString(CultureInfo.InvariantCulture)??"";step=d.Step?.ToString(CultureInfo.InvariantCulture)??"";choices=string.Join('|',d.Choices);direction=d.Direction;example=d.Example??"";defaultValue=d.Default??"";restart=d.Restart;notes=d.Notes;client=d.ClientRequired;}
    private void ClearIncompatible()
    {
        var clear=errors.Keys.ToHashSet();
        if(!IsNumeric||clear.Contains(nameof(Minimum)))Minimum="";
        if(!IsNumeric||clear.Contains(nameof(Maximum)))Maximum="";
        if(!IsNumeric||clear.Contains(nameof(Step)))Step="";
        if(!IsEnumeration)Choices="";
        if(clear.Contains(nameof(Example)))Example="";
        if(clear.Contains(nameof(Default)))Default="";
    }
    private void ResetBuiltIn()
    {
        if(builtIn==null)return;
        FriendlyName=builtIn.Name;Category=builtIn.Category;Description=builtIn.Description;Kind=builtIn.Kind switch{"bool"=>"Boolean","integer"=>"Integer","number"=>"Decimal number","choice"=>"Enumerated choice",_=>"String"};Minimum=builtIn.Min?.ToString(CultureInfo.InvariantCulture)??"";Maximum=builtIn.Max?.ToString(CultureInfo.InvariantCulture)??"";Step="";Choices=string.Join('|',builtIn.Choices??[]);Direction=builtIn.Direction;Example="";Default="";Restart=builtIn.Restart;ClientRequired=builtIn.ClientRequired;Notes="";
    }
    private void Accept()
    {
        var definition=BuildDefinition(out errors);
        if(errors.Count>0){RefreshValidation();FocusRequested?.Invoke(errors.Keys.First());return;}
        try{owner.AcceptDefinition(this,definition,Value);Accepted?.Invoke();}
        catch(Exception e){operationError=e.Message;OnPropertyChanged(nameof(Error));}
    }
}
