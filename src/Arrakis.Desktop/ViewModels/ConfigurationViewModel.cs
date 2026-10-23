using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Desktop.ViewModels;

public sealed class SettingRow(SettingDefinition definition, string? current) : Bindable
{
    public SettingDefinition Definition { get; } = definition;
    private string value = current ?? "Not set";
    public string Current { get => value; internal set => Set(ref this.value, value); }
}

public sealed class ConfigurationViewModel : Bindable
{
    private readonly ShellViewModel shell;
    private readonly IUserInteraction ui;
    private Dictionary<string, ConfigFile> snapshots = [];
    private ConfigurationDraft? document;
    private ConfigurationFileChoice? selectedFile;
    private bool transitioning;
    public ConfigurationViewModel(ShellViewModel shell, IUserInteraction ui)
    {
        this.shell = shell; this.ui = ui;
        SettingsView = CollectionViewSource.GetDefaultView(Settings);
        SettingsView.Filter = o => o is SettingRow s && (Category == "All categories" || s.Definition.Category == Category) && $"{s.Definition.Name} {s.Definition.Key} {s.Definition.Description}".Contains(Search, StringComparison.OrdinalIgnoreCase);
        shell.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(shell.CanOperate)) OnPropertyChanged(nameof(CanInteract)); };
        LoadCommand = new AsyncCommand(Reload, () => CanInteract);
        ApplySettingCommand = new RelayCommand(ApplySetting, () => CanInteract && SelectedSetting != null && document != null);
        ResetCommand = new RelayCommand(() => SettingValue = SelectedSetting!.Definition.Default!, () => CanInteract && HasVerifiedDefault);
        UndoCommand = new RelayCommand(Undo, () => CanInteract && IsDirty);
        PreviewCommand = new RelayCommand(Preview, () => CanInteract && document != null);
        SaveCommand = new AsyncCommand(Save, () => CanInteract && IsDirty);
        RestoreCommand = new RelayCommand(Restore, () => CanInteract && document != null);
        ReviewRemovalsCommand = new RelayCommand(ReviewRemovals, () => CanInteract && document?.HasRawEdits == true);
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
    public string[] Categories => ["All categories", ..SettingCatalog.Categories];
    private string search = "", category = "All categories", settingValue = "", validation = "";
    private string[] choices = [];
    private SettingRow? selectedSetting;
    public string Search { get => search; set { if (Set(ref search, value)) SettingsView.Refresh(); } }
    public string Category { get => category; set { if (Set(ref category, value)) SettingsView.Refresh(); } }
    public SettingRow? SelectedSetting
    {
        get => selectedSetting;
        set
        {
            if (!Set(ref selectedSetting, value)) return;
            choices = value?.Definition.Kind == "bool" ? ["True", "False"] : value?.Definition.Choices?.ToArray() ?? [];
            SettingValue = value == null ? "" : value.Current == "Not set" ? value.Definition.TemplateValue : value.Current;
            foreach (var p in new[] { nameof(SettingDetails), nameof(Choices), nameof(HasChoices), nameof(HasNumericValue), nameof(HasVerifiedDefault) }) OnPropertyChanged(p);
        }
    }
    public string SettingDetails => SelectedSetting?.Definition.Details ?? "Select a setting to read its source, accepted values and restart requirements. Unknown keys remain available in the raw editor.";
    public string[] Choices => choices;
    public bool HasChoices => choices.Length > 0;
    public bool HasNumericValue => !HasChoices;
    public bool HasVerifiedDefault => SelectedSetting?.Definition.Default != null;
    public string SettingValue { get => settingValue; set => Set(ref settingValue, value ?? ""); }
    public string Validation { get => validation; set => Set(ref validation, value); }
    public RelayCommand ApplySettingCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand ReviewRemovalsCommand { get; }
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
        if (SelectedSetting != null) SettingValue = SelectedSetting.Current == "Not set" ? SelectedSetting.Definition.TemplateValue : SelectedSetting.Current;
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
        Settings.Clear(); foreach (var d in SettingCatalog.All.Where(d => d.File == choice.Name)) Settings.Add(new(d, null));
        UpdateValues(); SelectedSetting = Settings.FirstOrDefault(); Validation = "";
        OnPropertyChanged(nameof(SelectedFile)); OnPropertyChanged(nameof(FilePath)); Changed();
    }
    private void UpdateValues()
    {
        var entries = new IniDocument(RawText).Entries().GroupBy(e => (e.Section.ToUpperInvariant(), e.Key.ToUpperInvariant())).ToDictionary(g => g.Key, g => g.Last().Value);
        foreach (var row in Settings) row.Current = entries.GetValueOrDefault((row.Definition.Section.ToUpperInvariant(), row.Definition.Key.ToUpperInvariant())) ?? "Not set";
    }
    private void ApplySetting()
    {
        if (!CanInteract || document == null || SelectedSetting == null) return;
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
                document = new(saved); UpdateValues(); Changed(); Validation = "Saved and verified. Restart the battlegroup when ready.";
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
        OnPropertyChanged(nameof(SelectedFile)); OnPropertyChanged(nameof(FilePath)); Changed();
    }
    private void Changed() { foreach (var p in new[] { nameof(RawText), nameof(CurrentOriginal), nameof(DocumentId), nameof(IsDirty), nameof(DraftState) }) OnPropertyChanged(p); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    private void Transition(bool active) { transitioning = active; OnPropertyChanged(nameof(CanInteract)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
}
