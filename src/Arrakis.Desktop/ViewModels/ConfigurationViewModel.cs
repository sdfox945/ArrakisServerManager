using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Desktop.ViewModels;

public sealed class SettingRow(SettingDefinition definition, string? current) : Bindable
{
    public SettingDefinition Definition { get; } = definition;
    public string Source {get;init;}=definition.Source;
    public bool RawOnly {get;init;}
    public string Reason {get;init;}="";
    public string DisplayCurrent=>SettingIdentity.Sensitive(Definition.Key)&&IsPresent?"••••••":Current;
    public string DisplaySaved=>SettingIdentity.Sensitive(Definition.Key)&&Saved!="Not set"?"••••••":Saved;
    private string value = current ?? "Not set";
    public string Current { get => value; internal set { if(Set(ref this.value,value)){OnPropertyChanged(nameof(IsPresent));OnPropertyChanged(nameof(IsModified));OnPropertyChanged(nameof(State));} } }
    private string saved=current??"Not set";
    public string Saved {get=>saved;internal set {if(Set(ref saved,value)){OnPropertyChanged(nameof(IsModified));OnPropertyChanged(nameof(State));}}}
    private bool present=current!=null;
    internal void SetPresence(bool active){present=active;OnPropertyChanged(nameof(IsPresent));OnPropertyChanged(nameof(State));}
    public bool IsPresent => present;
    public bool IsModified => Current != Saved;
    public string State => RawOnly ? "Raw only" : !IsPresent ? "Available to add" : IsModified ? "Modified" : "Saved";
}

public sealed partial class ConfigurationViewModel : Bindable
{
    private readonly ShellViewModel shell;
    private readonly IUserInteraction ui;
    private Dictionary<string, ConfigFile> snapshots = [];
    private ConfigurationDraft? document;
    private ConfigurationFileChoice? selectedFile;
    private bool transitioning;
    public ConfigurationViewModel(ShellViewModel shell, IUserInteraction ui,LocalStores stores)
    {
        this.shell = shell; this.ui = ui;this.stores=stores;metadataStore=new CustomSettingStore(stores.Root,shell.IsDemo);LoadMetadata();
        SettingsView = CollectionViewSource.GetDefaultView(Settings);
        SettingsView.Filter = o => o is SettingRow s && (s.IsPresent || ShowAvailableToAdd) && (Category == "All categories" || s.Definition.Category == Category) && MatchesSearch(s);
        shell.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(shell.CanOperate)) {OnPropertyChanged(nameof(CanInteract));OnPropertyChanged(nameof(CanManageMetadata));} };
        LoadCommand = new AsyncCommand(Reload, () => CanInteract);
        ApplySettingCommand = new RelayCommand(ApplySetting, () => CanAddSetting);
        ResetCommand = new RelayCommand(() => {if(CanInteract&&HasVerifiedDefault)SettingValue=SelectedSetting!.Definition.VerifiedEngineDefault!;}, () => CanInteract && HasVerifiedDefault);
        ResetUserDefaultCommand=new RelayCommand(()=>{if(CanInteract&&HasUserDefault)SettingValue=SelectedSetting!.Definition.Default!;},()=>CanInteract&&HasUserDefault);
        UndoCommand = new RelayCommand(Undo, () => CanInteract && IsDirty);
        PreviewCommand = new RelayCommand(Preview, () => CanInteract && document != null);
        SaveCommand = new AsyncCommand(Save, () => CanInteract && IsDirty);
        RestoreCommand = new RelayCommand(Restore, () => CanInteract && document != null);
        ReviewRemovalsCommand = new RelayCommand(ReviewRemovals, () => CanInteract && document?.HasRawEdits == true);
        CopyPathCommand = new RelayCommand(()=>ui.Copy(FilePath),()=>selectedFile!=null);
    }
    public ObservableCollection<ConfigurationFileChoice> Files { get; } = [];
    public ObservableCollection<SettingRow> Settings { get; } = [];
    public ICollectionView SettingsView { get; }
    public ConfigurationFileChoice? SelectedFile => selectedFile;
    public ConfigFile? CurrentOriginal => document?.Original;
    public Guid? DocumentId => document?.Id;
    public bool CanInteract => !transitioning && shell.CanOperate;
    public string RawText => document?.Text ?? "";
    public bool IsDirty => document?.IsDirty == true;
    public string DraftState => document == null ? "Load the current server configuration to begin." : IsDirty ? "Unsaved draft • restart required after saving" : "Loaded from server • no unsaved changes";
    public string FilePath => selectedFile?.RemotePath ?? "No verified path loaded";
    private string Summary(ConfigurationReview review) => shell.ConnectionSummary + "\nTarget: " + review.Original.Name + "\n" + review.Original.RemotePath + "\n" + (review.IsRaw ? "Advanced raw edits. " : "Only explicit visual setting edits are permitted. ") + "Encrypted local and remote rollback copies will be created. Restart may be required.\nOriginal SHA-256: " + review.OriginalHash + "\nProposed SHA-256: " + review.ProposedHash;
    private string search = "", category = "All categories", settingValue = "", validation = "";
    private string[] choices = [];
    private SettingRow? selectedSetting;
    public string Search { get => search; set { if (Set(ref search, value ?? "")) RebuildCategories(); } }
    public string Category { get => category; set { if (value != null && Set(ref category, value)) {SettingsView.Refresh();OnPropertyChanged(nameof(SelectedCategory));} } }
    public SettingRow? SelectedSetting
    {
        get => selectedSetting;
        set
        {
            if(value==null&&selectedSetting!=null&&Settings.Contains(selectedSetting))return;
            if (!Set(ref selectedSetting, value)) return;
            choices = value?.Definition.Kind == "bool" ? ["True", "False"] : value?.Definition.Choices?.ToArray() ?? [];
            SettingValue = value == null||SettingIdentity.Sensitive(value.Definition.Key) ? "" : !value.IsPresent ? value.Definition.TemplateValue : value.Current;
            foreach (var p in new[] { nameof(SettingDetails), nameof(Choices), nameof(HasChoices), nameof(HasNumericValue), nameof(HasVerifiedDefault),nameof(HasUserDefault) }) OnPropertyChanged(p);
            NotifyNumeric();
        }
    }
    public string SettingDetails => SelectedSetting is {} row?row.Definition.Description+"\n"+row.Definition.Direction+"\n"+row.Definition.Evidence+(row.RawOnly?"\nRAW ONLY: "+row.Reason:""):"Select a setting to read its source, accepted values and restart requirements.";
    public string[] Choices => choices;
    public bool HasChoices => choices.Length > 0;
    public bool HasNumericValue => !HasChoices;
    public bool HasVerifiedDefault => SelectedSetting is {RawOnly:false} row&&!string.IsNullOrWhiteSpace(row.Definition.VerifiedEngineDefault);
    public bool HasUserDefault=>SelectedSetting is {RawOnly:false} row&&row.Definition.Source=="Custom"&&!string.IsNullOrWhiteSpace(row.Definition.Default);
    // An inactive choice ComboBox may clear its selection while its ItemsSource changes.
    // Null is not editing intent; an actual empty TextBox value remains a valid validation request.
    public string SettingValue { get => settingValue; set {if(value!=null&&Set(ref settingValue, value))NotifyNumeric();} }
    public string Validation { get => validation; set => Set(ref validation, value); }
    public RelayCommand ApplySettingCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ResetUserDefaultCommand {get;}
    public RelayCommand UndoCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand ReviewRemovalsCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public AsyncCommand LoadCommand { get; }
    public AsyncCommand SaveCommand { get; }

    // Called only by an explicit file-picker commit, never by a TwoWay binding setter.
    public bool RequestFileSelection(ConfigurationFileChoice? choice)
    {
        if (choice == null || !CanInteract || !Files.Contains(choice)) return false;
        if (selectedFile == choice) return true;
        using var lease = shell.PrepareConfiguration("Reviewing file change");
        Transition(true);
        try
        {
            if (IsDirty && !ui.Confirm("Unsaved changes", "Discard the current configuration draft and switch files?")) return false;
            Adopt(choice, snapshots[choice.RemotePath]); return true;
        }
        finally { Transition(false); }
    }
    public void EditRawText(string text)
    {
        if (!CanInteract || document == null) return;
        document.EditRaw(text); Changed(); UpdateValues();
        if (SelectedSetting != null) SettingValue = SettingIdentity.Sensitive(SelectedSetting.Definition.Key)?"":!SelectedSetting.IsPresent ? SelectedSetting.Definition.TemplateValue : SelectedSetting.Current;
    }
    private async Task Reload()
    {
        if (!CanInteract) return;
        using var lease = shell.PrepareConfiguration("Reviewing configuration reload"); Transition(true);
        try
        {
            if (IsDirty && !ui.Confirm("Reload configuration", "Discard this unsaved draft and reload the current server files?")) return;
            var gateway = shell.Gateway; var previous = selectedFile;
            lease.Dispose();
            await shell.RunAsync("Load configuration", false, async token =>
            {
                var loaded = await gateway.LoadConfigurationAsync(token);
                if (!ReferenceEquals(shell.Gateway, gateway)) throw new InvalidOperationException("Connection changed during reload.");
                if (loaded.Count != 3 || loaded.Select(f => f.Name).Distinct().Count() != 3 || loaded.Select(f => f.RemotePath).Distinct().Count() != 3) throw new InvalidOperationException("Expected three distinct supported configuration files.");
                foreach (var file in loaded) ConfigurationIntegrity.ValidateOriginal(file);
                snapshots = loaded.ToDictionary(f => f.RemotePath, f => f with { Bytes = f.Bytes.ToArray() });
                Files.Clear(); foreach (var file in loaded) Files.Add(new(file.Name, file.RemotePath));
                var choice = Files.FirstOrDefault(f => f.Name == previous?.Name && f.RemotePath == previous.RemotePath) ?? Files[0];
                Adopt(choice, snapshots[choice.RemotePath]); Validation = "Loaded verified current files.";
                return "Loaded verified UserSettings files.";
            });
        }
        finally { Transition(false); }
    }
    private void Adopt(ConfigurationFileChoice choice, ConfigFile snapshot)
    {
        document = new(snapshot); selectedFile = choice;
        document.RegisterDefinitions(customDefinitions);
        UpdateValues(); SelectedSetting = Settings.FirstOrDefault(); Validation = "";
        OnPropertyChanged(nameof(SelectedFile)); OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(FileCaption)); Changed();
    }
    private void UpdateValues()
    {
        if(document==null)return;
        var previous=selectedSetting==null?null:SettingIdentity.Of(selectedSetting.Definition.File,selectedSetting.Definition.Section,selectedSetting.Definition.Key);
        var resolved=SettingResolver.Resolve(document.Original.Name,RawText,customDefinitions);
        var entries = new IniDocument(RawText).Entries().GroupBy(e => SettingIdentity.Of(document.Original.Name,e.Section,e.Key)).ToDictionary(g => g.Key, g => g.Count()==1?g.Single().Value:"Multiple directives — Raw only");
        var saved = new IniDocument(document.Original.Text).Entries().GroupBy(e=>SettingIdentity.Of(document.Original.Name,e.Section,e.Key)).ToDictionary(g=>g.Key,g=>g.Count()==1?g.Single().Value:"Multiple directives — Raw only");
        Settings.Clear();foreach(var item in resolved){var row=new SettingRow(item.Definition,null){Source=item.Source,RawOnly=item.RawOnly,Reason=item.Reason};var identity=SettingIdentity.Of(row.Definition.File,row.Definition.Section,row.Definition.Key);row.Saved=saved.GetValueOrDefault(identity)??"Not set";row.Current=entries.GetValueOrDefault(identity)??"Not set";row.SetPresence(item.IsPresent);Settings.Add(row);}
        SelectedSetting=Settings.FirstOrDefault(s=>SettingIdentity.Of(s.Definition.File,s.Definition.Section,s.Definition.Key)==previous)??Settings.FirstOrDefault();
        RebuildCategories(); NotifyNumeric();
    }
    private void ApplySetting()
    {
        if (!CanAddSetting || document == null || SelectedSetting == null) return;
        try { document.ApplyVisual(SelectedSetting.Definition, SettingValue); UpdateValues(); Changed(); Validation = "Added to draft. Review and save to apply."; }
        catch (Exception e) { Validation = e.Message; }
    }
    private void Undo()
    {
        if (!CanInteract || document == null) return;
        using var lease = shell.PrepareConfiguration("Reviewing draft discard"); Transition(true);
        try { if (ui.Confirm("Discard draft?", "Discard unsaved changes for " + selectedFile?.Name + "?")) { document.Undo(); UpdateValues(); Changed(); Validation = "Draft discarded."; } }
        finally { Transition(false); }
    }
    private void Preview()
    {
        if (!CanInteract || document == null || selectedFile == null) return;
        using var lease = shell.PrepareConfiguration("Reviewing configuration"); Transition(true);
        try { var review = document.PrepareReview(selectedFile, true); ui.Preview("Configuration preview", Summary(review), review.Diff); }
        catch (Exception e) { Validation = e.Message; }
        finally { Transition(false); }
    }
    private void ReviewRemovals()
    {
        if (!CanInteract || document == null || selectedFile == null) return;
        using var lease = shell.PrepareConfiguration("Reviewing raw removals"); Transition(true);
        try { var review = document.PrepareReview(selectedFile, true); if (ui.ReviewRawRemovals("Authorize intentional raw removals", Summary(review), review.Diff, selectedFile.Name)) { document.AuthorizeRemovals(review); Validation = "These exact raw removals are authorized. Review & save is still required."; } }
        catch (Exception e) { Validation = e.Message; }
        finally { Transition(false); }
    }
    private async Task Save()
    {
        if (!CanInteract || document == null || selectedFile == null) return;
        using var lease = shell.PrepareConfiguration("Reviewing configuration"); Transition(true);
        try
        {
            var active = document; var choice = selectedFile; var gateway = shell.Gateway;
            var review = active.PrepareReview(choice); review.Validate();
            if (!ui.Review("Save server configuration", Summary(review), review.Diff)) { Validation = "Save review cancelled; draft preserved."; return; }
            if (!ReferenceEquals(active, document) || choice != selectedFile || !ReferenceEquals(gateway, shell.Gateway) || !active.Matches(review)) throw new InvalidOperationException("The document or connection changed during review. Nothing was saved; review again.");
            review.Validate(); lease.Dispose();
            await shell.RunAsync("Save " + choice.Name, true, async token =>
            {
                if (!ReferenceEquals(active, document) || choice != selectedFile || !ReferenceEquals(gateway, shell.Gateway) || !active.Matches(review)) throw new InvalidOperationException("The approved document changed before save; nothing was sent.");
                var saved = await gateway.SaveReviewedConfigurationAsync(review, token);
                if (!review.Matches(review.Original, saved.Bytes) || saved.Name != choice.Name || saved.RemotePath != choice.RemotePath || saved.Hash != ConfigurationIntegrity.Hash(saved.Bytes)) throw new OutcomeUnknownException("Save service returned a different document or payload. Reload and reconcile before retrying.");
                // Stable picker choices are never replaced on save; only the document snapshot advances.
                snapshots[choice.RemotePath] = saved with { Bytes = saved.Bytes.ToArray() };
                document = new(saved);document.RegisterDefinitions(customDefinitions); UpdateValues(); Changed(); Validation = "Saved and verified. Restart the battlegroup when ready.";
                return Validation;
            });
        }
        catch (Exception e) { Validation = e.Message; }
        finally { Transition(false); }
    }
    private void Restore()
    {
        if (!CanInteract || document == null || selectedFile == null) return;
        using var lease = shell.PrepareConfiguration("Reviewing configuration backup"); Transition(true);
        try
        {
            var path = ui.ChooseFile("Select encrypted configuration backup", "Arrakis configuration (*.asmconfig)|*.asmconfig"); if (path == null) return;
            var backup = new ConfigBackupStore("").Read(path);
            if (backup.Name != selectedFile.Name || backup.RemotePath != selectedFile.RemotePath) throw new InvalidOperationException("Backup targets a different file or server volume.");
            ConfigurationIntegrity.ValidateProposal(document.Original, backup.Text);
            if (ui.Review("Restore configuration into draft", "Load the reviewed backup into this draft only. No server write occurs. Raw removals need separate authorization before saving.", ConfigurationIntegrity.Diff(RawText, backup.Text))) { document.EditRaw(backup.Text); UpdateValues(); Changed(); }
        }
        catch (Exception e) { Validation = e.Message; }
        finally { Transition(false); }
    }
    public void Clear()
    {
        if (transitioning) return;
        document = null; selectedFile = null; snapshots.Clear(); Files.Clear(); Settings.Clear(); SelectedSetting = null; Validation = "";
        metadataStore=new CustomSettingStore(stores.Root,shell.IsDemo);LoadMetadata();
        RebuildCategories();OnPropertyChanged(nameof(SelectedFile)); OnPropertyChanged(nameof(FilePath));OnPropertyChanged(nameof(FileCaption)); Changed();
    }
    private void Changed() { foreach (var p in new[] { nameof(RawText), nameof(CurrentOriginal), nameof(DocumentId), nameof(IsDirty), nameof(DraftState),nameof(CanManageMetadata),nameof(SectionNames),nameof(CategoryNames),nameof(IsDemoCatalog) }) OnPropertyChanged(p); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    private void Transition(bool active) { transitioning = active; OnPropertyChanged(nameof(CanInteract));OnPropertyChanged(nameof(CanManageMetadata)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
}
