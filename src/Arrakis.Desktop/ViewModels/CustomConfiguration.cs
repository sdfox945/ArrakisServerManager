using System.Globalization;
using System.IO;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Desktop.ViewModels;

public sealed partial class ConfigurationViewModel
{
    private readonly LocalStores stores;
    private ICustomSettingStore metadataStore;
    private CustomSettingDefinition[] customDefinitions=[];
    public string MetadataStatus=>metadataStore.Status;
    public string[] SectionNames=>new IniDocument(RawText).Sections().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public string[] CategoryNames=>Settings.Select(s=>s.Definition.Category).Append("Uncategorized").Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
    public bool CanManageMetadata=>CanInteract&&document!=null&&!metadataStore.ReadOnly;
    public bool IsDemoCatalog=>shell.IsDemo;
    internal bool ContainsVariable(string section,string key)=>new IniDocument(RawText).Entries().Any(e=>e.Section.Equals(section,StringComparison.OrdinalIgnoreCase)&&e.Key.Equals(key,StringComparison.OrdinalIgnoreCase));
    public void LoadDemoDefinitions()
    {
        if(!shell.IsDemo||!CanManageMetadata)return;
        using var lease=BeginMetadataReview();
        try
        {
            var examples=new[]{
                new CustomSettingDefinition{Key="dw.VehiclePowerConsumptionMultiplier",FriendlyName="Vehicle Power Consumption",Kind="number",Minimum=0,Maximum=2,Step=.01,Category="Community examples",Description="Demo-only user annotation. Bounds are illustrative; actual game range/behavior is not verified."},
                new CustomSettingDefinition{Key="Community.ExperimentalFlag",FriendlyName="Community Experimental Flag",Kind="bool",Category="Community examples",Description="Demo scalar typed by a user, not official guidance."},
                new CustomSettingDefinition{Key="Dune.GlobalMiningOutputMultiplier",FriendlyName="My mining label",Kind="number",Category="Community examples",Description="Demonstrates local annotations coexisting with built-in documentation after an upgrade."}};
            if(!ui.Review("Demo metadata examples", "Merge local DEMO annotations only. No INI values change; no Live metadata is read or written. Existing matching Demo annotations will be replaced.",System.Text.Json.JsonSerializer.Serialize(examples,new System.Text.Json.JsonSerializerOptions{WriteIndented=true})))return;
            var identities=examples.Select(d=>d.Identity).ToHashSet();var next=customDefinitions.Where(d=>!identities.Contains(d.Identity)).Concat(examples).ToArray();metadataStore.Save(next);customDefinitions=next;document?.RegisterDefinitions(next);UpdateValues();Validation="Demo metadata examples loaded locally; draft unchanged.";OnPropertyChanged(nameof(MetadataStatus));
        }
        catch(Exception e){Validation=e.Message;}
    }
    public double DetailsPaneWidth=>double.IsFinite(shell.Preferences.ConfigurationDetailsWidth)?Math.Clamp(shell.Preferences.ConfigurationDetailsWidth,300,720):440;
    public void SavePaneWidth(double width)
    {
        if(!double.IsFinite(width))return;
        shell.Preferences.ConfigurationDetailsWidth=Math.Clamp(width,300,720);
        try{stores.SaveConfigurationDetailsWidth(shell.Preferences.ConfigurationDetailsWidth);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException){Validation="Could not save pane preference: "+e.Message;}
    }
    private void LoadMetadata(){customDefinitions=metadataStore.Load().ToArray();OnPropertyChanged(nameof(MetadataStatus));OnPropertyChanged(nameof(CanManageMetadata));}
    public DefinitionEditorViewModel CreateDefinitionEditor(bool edit)
    {
        if(!CanManageMetadata||document==null)throw new InvalidOperationException("Load a document and resolve metadata errors first.");
        var row=edit?SelectedSetting:null;
        if(edit&&row==null)throw new InvalidOperationException("Select a setting first.");
        if(row?.RawOnly==true)throw new InvalidOperationException(row.Reason);
        var existing=row==null?null:customDefinitions.FirstOrDefault(d=>d.Identity==SettingIdentity.Of(row.Definition.File,row.Definition.Section,row.Definition.Key));
        return new(this,document.Id,document.Revision,edit,existing,row?.Definition);
    }
    public IDisposable BeginMetadataReview()
    {
        if(!CanManageMetadata)throw new InvalidOperationException("Configuration is busy or metadata is read-only.");
        var lease=shell.PrepareConfiguration("Editing local setting metadata");Transition(true);
        return new MetadataLease(()=>{Transition(false);lease.Dispose();OnPropertyChanged(nameof(CanManageMetadata));});
    }
    private sealed class MetadataLease(Action close):IDisposable {public void Dispose()=>close();}
    internal void AcceptDefinition(DefinitionEditorViewModel editor,CustomSettingDefinition definition,string initialValue)
    {
        if(document==null||document.Id!=editor.DocumentId||document.Revision!=editor.DocumentRevision||definition.File!=selectedFile?.Name)throw new InvalidOperationException("The document changed while the definition was open. Nothing was applied.");
        definition.Validate();
        var identity=definition.Identity;
        if(!editor.MetadataOnly&&new IniDocument(RawText).Entries().Any(e=>SettingIdentity.Of(definition.File,e.Section,e.Key)==identity))throw new InvalidOperationException("This variable already exists. Cancel and select it, then use Manage definition instead of inserting a duplicate.");
        var next=customDefinitions.Where(d=>d.Identity!=identity).Append(definition).ToArray();
        if(!editor.MetadataOnly)
        {
            var trial=new ConfigurationDraft(document.Original);trial.RegisterDefinitions(next);trial.EditRaw(RawText);trial.ApplyVisual(definition.ToDefinition(),initialValue);
        }
        metadataStore.Save(next);customDefinitions=next;document.RegisterDefinitions(next);
        if(!editor.MetadataOnly)document.ApplyVisual(definition.ToDefinition(),initialValue);
        UpdateValues();SelectedSetting=Settings.FirstOrDefault(s=>SettingIdentity.Of(s.Definition.File,s.Definition.Section,s.Definition.Key)==identity);Changed();
        Validation=editor.MetadataOnly?"Metadata saved locally. INI draft unchanged.":"Variable added to draft only. Review & save is required before any server change.";
        OnPropertyChanged(nameof(MetadataStatus));
    }
    public void RemoveDefinition()
    {
        if(!CanManageMetadata||SelectedSetting==null)return;
        var identity=SettingIdentity.Of(SelectedSetting.Definition.File,SelectedSetting.Definition.Section,SelectedSetting.Definition.Key);
        if(!customDefinitions.Any(d=>d.Identity==identity)){Validation="No local definition exists for this setting.";return;}
        using var lease=BeginMetadataReview();
        try
        {
            if(!ui.Confirm("Remove Definition", "Remove only the local metadata for "+SelectedSetting.Definition.Key+"? The server variable and draft bytes will remain unchanged."))return;
            var next=customDefinitions.Where(d=>d.Identity!=identity).ToArray();metadataStore.Save(next);customDefinitions=next;document?.RegisterDefinitions(next);UpdateValues();Validation="Definition removed; INI content unchanged.";OnPropertyChanged(nameof(MetadataStatus));
        }
        catch(Exception e){Validation=e.Message;}
    }
    public void ReloadMetadata()
    {
        if(!CanInteract)return;
        LoadMetadata();document?.RegisterDefinitions(customDefinitions);UpdateValues();Validation=MetadataStatus;
    }
    public void ExportDefinitions()
    {
        if(!CanInteract)return;
        try
        {
            var directory=ui.ChooseFolder("Export local metadata only — never include passwords in annotations",stores.Root);if(directory==null)return;
            var path=Path.Combine(directory,"Arrakis-custom-definitions-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".json");
            CustomSettingStore.Export(path,customDefinitions);Validation="Metadata exported (no active INI values): "+path;
        }
        catch(Exception e){Validation="Export failed: "+e.Message;}
    }
    public void ImportDefinitions()
    {
        if(!CanManageMetadata)return;
        using var lease=BeginMetadataReview();
        try
        {
            var path=ui.ChooseFile("Import custom definitions — review annotations for secrets", "Arrakis metadata (*.json)|*.json");if(path==null)return;
            var incoming=CustomSettingStore.Import(path).ToArray();
            var review=string.Join("\n",incoming.Select(d=>(customDefinitions.Any(c=>c.Identity==d.Identity)?"REPLACE LOCAL METADATA: ":"ADD LOCAL METADATA: ")+d.File+" ["+d.Section+"] "+d.Key+"\n"+System.Text.Json.JsonSerializer.Serialize(d)));
            if(!ui.Review("Merge custom definitions", "Review all imported annotations. Matching local identities are replaced only on confirmation; unmatched local definitions are retained. No INI values change. Keep secrets out of metadata.",review))return;
            var identities=incoming.Select(d=>d.Identity).ToHashSet();var next=customDefinitions.Where(d=>!identities.Contains(d.Identity)).Concat(incoming).ToArray();
            metadataStore.Save(next);customDefinitions=next;document?.RegisterDefinitions(next);UpdateValues();Validation="Metadata merged locally; draft unchanged.";OnPropertyChanged(nameof(MetadataStatus));
        }
        catch(Exception e){Validation="Import refused: "+e.Message;}
    }
    public void CopyKey(){if(SelectedSetting!=null)ui.Copy(SelectedSetting.Definition.Key);}
}

