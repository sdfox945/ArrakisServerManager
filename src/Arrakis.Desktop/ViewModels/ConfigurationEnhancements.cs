using System.Collections.ObjectModel;
using System.Globalization;
using Arrakis.Core;

namespace Arrakis.Desktop.ViewModels;

public sealed class CategoryOption(string name) : Bindable
{
    public string Name {get;}=name;
    private int count;
    public int Count {get=>count;internal set {if(Set(ref count,value))OnPropertyChanged(nameof(Label));}}
    public string Label=>Name+" ("+Count+")";
    public override string ToString()=>Label;
}

public sealed partial class ConfigurationViewModel
{
    private readonly Dictionary<string,CategoryOption> categoryIdentities=[];
    public ObservableCollection<CategoryOption> Categories {get;}=[];
    public CategoryOption? SelectedCategory {get=>Categories.FirstOrDefault(c=>c.Name==Category);set{if(value!=null)Category=value.Name;}}
    private bool showAvailableToAdd;
    public bool ShowAvailableToAdd {get=>showAvailableToAdd;set{if(Set(ref showAvailableToAdd,value))RebuildCategories();}}
    private bool MatchesSearch(SettingRow s)=>$"{s.Definition.Name} {s.Definition.Key} {s.Definition.Category} {s.Definition.Description}".Contains(Search,StringComparison.OrdinalIgnoreCase);
    private void RebuildCategories()
    {
        var present=Settings.Where(s=>s.IsPresent&&MatchesSearch(s)).ToArray();
        var names=new[]{"All categories"}.Concat(SettingCatalog.Categories.Where(c=>present.Any(s=>s.Definition.Category==c))).ToArray();
        foreach(var option in Categories.Where(c=>!names.Contains(c.Name)).ToArray())Categories.Remove(option);
        for(var i=0;i<names.Length;i++)
        {
            if(!categoryIdentities.TryGetValue(names[i],out var option)){option=new(names[i]);categoryIdentities.Add(names[i],option);}
            option.Count=names[i]=="All categories"?present.Length:present.Count(s=>s.Definition.Category==names[i]);
            if(!Categories.Contains(option))Categories.Insert(i,option);else if(Categories.IndexOf(option)!=i)Categories.Move(Categories.IndexOf(option),i);
        }
        if(!names.Contains(Category))Category="All categories";
        OnPropertyChanged(nameof(SelectedCategory));SettingsView.Refresh();
    }
    public string FileCaption=>SelectedFile==null?"No document loaded":SelectedFile.Name+" — "+(shell.IsDemo?"Simulated server":"Live server");
    private NumericSettingRange? NumericRange=>NumericSettingRange.For(SelectedSetting?.Definition);
    public bool HasSlider=>NumericRange!=null;
    public double SliderMinimum=>NumericRange?.Minimum??0;
    public double SliderMaximum=>NumericRange?.Maximum??1;
    public double SliderIncrement=>NumericRange?.Increment??0.1;
    public double SliderValue=>NumericRange?.Position(SettingValue)??0;
    public string NumericValidation=>SelectedSetting?.Definition.Validate(SettingValue)??"";
    public string ValueComparison=>SelectedSetting==null?"Select a setting.":$"Saved: {SelectedSetting.Saved}\nDraft: {SelectedSetting.Current}\nProposed: {SettingValue}\nVerified default: {SelectedSetting.Definition.Default??"Unknown"}";
    public bool CanAddSetting=>CanInteract&&document!=null&&SelectedSetting is {} row&&(!row.IsPresent||!SettingValueComparison.Equivalent(row.Definition,row.Current,SettingValue));
    public string SliderMinimumLabel=>SliderMinimum.ToString("G",CultureInfo.InvariantCulture)+" · "+SliderDirection(false);
    public string SliderMaximumLabel=>SliderMaximum.ToString("G",CultureInfo.InvariantCulture)+" · "+SliderDirection(true);
    private string SliderDirection(bool higher)=>SelectedSetting?.Definition.Key switch
    {
        "LootRespawnSpeed"=>higher?"Slower":"Faster",
        "WaterExtractionRate" or "ResourceRespawnSpeed" or "NPCRespawnMultiplier"=>higher?"Faster":"Slower",
        "PlayerStaminaDrain"=>higher?"More drain":"Less drain",
        _=>higher?"Maximum":"Minimum"
    };
    public void ChooseSliderValue(double value)
    {
        if(!CanInteract || NumericRange is not {} range || !double.IsFinite(value))return;
        SettingValue=range.Choose(value);
    }
    private void NotifyNumeric(){foreach(var name in new[]{nameof(HasSlider),nameof(SliderMinimum),nameof(SliderMaximum),nameof(SliderIncrement),nameof(SliderValue),nameof(SliderMinimumLabel),nameof(SliderMaximumLabel),nameof(NumericValidation),nameof(ValueComparison),nameof(CanAddSetting)})OnPropertyChanged(name);System.Windows.Input.CommandManager.InvalidateRequerySuggested();}
}
