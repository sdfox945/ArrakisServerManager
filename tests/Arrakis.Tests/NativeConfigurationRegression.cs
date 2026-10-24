using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Arrakis.Core;
using Arrakis.Desktop;
using Arrakis.Desktop.ViewModels;
using Arrakis.Desktop.Views;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static partial class NativeConfigurationRegression
{
    public static void Register(Action<string,Func<Task>> add,string root)
    {
        add("Native WPF: exact one-line visual review/save, stable picker and reopening saved document",async()=>
        {
            using var c=await Context.Create(root,"one-line");await c.Load();
            foreach(var name in new[]{"UserGame.ini","UserEngine.ini","UserServerCustomSettings.ini"}) {await c.SelectFile(name);Check(c.Vm.Editor.RawText==c.Gateway.Current(name).Text&&!c.Vm.Editor.IsDirty,"Native loading must preserve each file.");}
            var choice=c.Vm.Editor.SelectedFile;var before=c.Gateway.Current("UserServerCustomSettings.ini");await c.EditLoot("0.200000");await c.Capture("draft.png");
            c.Ui.DuringDialog=async(kind,_)=>{if(kind=="review"){Check(!c.Vm.IsBusy&&c.Vm.OperationState=="Reviewing configuration","Review is not a running remote save.");Check(c.Gateway.Saves.Count==0,"No service write is allowed before approval.");await c.Capture("owner-during-review.png");}};
            await c.Save();Check(c.Gateway.Saves.Count==1,"Exactly one reviewed save must be sent.");
            var diff=c.Ui.Events.Single(e=>e.Kind=="review").Diff;
            Check(diff.Replace("\r\n","\n")=="- LootRespawnSpeed=0.100000\n+ LootRespawnSpeed=0.200000","The complete preview must contain only one replacement.");
            Check(c.Gateway.Saves[0].Payload.SequenceEqual(before.Encode(before.Text.Replace("LootRespawnSpeed=0.100000","LootRespawnSpeed=0.200000"))),"Actual saved payload must equal the exact reviewed full file.");
            Check(ReferenceEquals(choice,c.Vm.Editor.SelectedFile)&&ReferenceEquals(choice,c.Picker.SelectedItem),"A save must not replace or null the file-picker identity.");
            Check(!c.Vm.Editor.IsDirty&&c.Vm.Editor.RawText.Length==before.Text.Length,"Successful save must advance the baseline without losing the draft.");
            Check(c.Ui.SwitchPrompts==0,"Review/save must never ask to switch files.");await c.Capture("saved.png");
            c.Ui.DuringDialog=null;await c.SelectFile("UserEngine.ini");await c.SelectFile("UserServerCustomSettings.ini");Check(c.Vm.Editor.RawText==c.Gateway.Current("UserServerCustomSettings.ini").Text,"Reopening must use the saved snapshot.");
            await c.EditLoot("0.300000");await c.Save();Check(c.Ui.SwitchPrompts==0&&c.Ui.Events.Last(e=>e.Kind=="review").Diff.Contains("- LootRespawnSpeed=0.200000"),"A second review must use the latest successful baseline without an empty document.");
        });
        add("Native WPF: genuine dirty file switch cancels and confirms consistently",async()=>
        {
            using var c=await Context.Create(root,"file-switch");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.EditLoot("0.200000");var draft=c.Vm.Editor.RawText;var id=c.Vm.Editor.DocumentId;
            c.Ui.ConfirmAnswers.Enqueue(false);await c.SelectFile("UserEngine.ini");Check(c.Vm.Editor.SelectedFile?.Name=="UserServerCustomSettings.ini"&&ReferenceEquals(c.Picker.SelectedItem,c.Vm.Editor.SelectedFile)&&c.Vm.Editor.RawText==draft&&c.Vm.Editor.DocumentId==id,"No/Cancel must retain both selection and every draft byte: "+JsonSerializer.Serialize(new{File=c.Vm.Editor.SelectedFile?.Name,Picker=(c.Picker.SelectedItem as ConfigurationFileChoice)?.Name,SameDraft=c.Vm.Editor.RawText==draft,SameId=c.Vm.Editor.DocumentId==id,Prompts=c.Ui.SwitchPrompts}));
            c.Ui.ConfirmAnswers.Enqueue(true);await c.SelectFile("UserEngine.ini");Check(c.Vm.Editor.SelectedFile?.Name=="UserEngine.ini"&&!c.Vm.Editor.IsDirty&&c.Vm.Editor.RawText==c.Gateway.Current("UserEngine.ini").Text,"Yes must atomically load the requested file and reset dirty state.");
            await c.SelectFile("UserServerCustomSettings.ini");Check(c.Vm.Editor.RawText==c.Gateway.Current("UserServerCustomSettings.ini").Text&&c.Gateway.Saves.Count==0,"Discarded edits must not enter server snapshots.");Check(c.Ui.SwitchPrompts==2,"Only actual dirty switch requests should prompt.");
        });
        add("Native WPF: programmatic picker null/reselection and Escape never discard a draft",async()=>
        {
            using var c=await Context.Create(root,"programmatic-selection");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.EditLoot("0.200000");var draft=c.Vm.Editor.RawText;
            c.Picker.SetCurrentValue(Selector.SelectedItemProperty,null);await Pump();c.Picker.SetCurrentValue(Selector.SelectedItemProperty,c.Vm.Editor.Files.First());await Pump();
            Check(c.Ui.SwitchPrompts==0&&c.Vm.Editor.RawText==draft&&ReferenceEquals(c.Picker.SelectedItem,c.Vm.Editor.SelectedFile),"Binding/programmatic updates cannot own a file switch.");
            await c.SelectFile("UserEngine.ini",true);Check(c.Ui.SwitchPrompts==0&&c.Vm.Editor.RawText==draft&&c.Vm.Editor.SelectedFile?.Name=="UserServerCustomSettings.ini","Escaping a picker must not commit a different file.");
        });
        add("Native WPF: visual/raw mode switches preserve identity, comments, blank lines and line endings",async()=>
        {
            using var c=await Context.Create(root,"modes");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.EditLoot("0.200000");var draft=c.Vm.Editor.RawText;var id=c.Vm.Editor.DocumentId;
            for(var i=0;i<3;i++){await c.Mode(1);Check(c.Raw.Text==draft,"Raw control must render the authoritative visual draft.");await c.Mode(0);Check(c.Vm.Editor.RawText==draft&&c.Vm.Editor.DocumentId==id,"Mode changes must not change text, endings or identity.");}
            await c.Mode(1);c.Raw.Focus();c.Raw.SetCurrentValue(TextBox.TextProperty,draft+"\n; user raw comment\n");await Pump();Check(c.Vm.Editor.RawText==draft+"\n; user raw comment\n","Native raw input must update the same document.");await c.Mode(0);Check(c.Vm.Editor.Settings.Single(s=>s.Definition.Key=="LootRespawnSpeed").Current=="0.200000","Visual values must stay synchronized with raw text.");
        });
        add("Native WPF: reload cancellation retains draft and confirmed reload adopts current original",async()=>
        {
            using var c=await Context.Create(root,"reload");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.EditLoot("0.200000");var draft=c.Vm.Editor.RawText;var calls=c.Gateway.LoadCalls;
            c.Ui.ConfirmAnswers.Enqueue(false);await c.Load();Check(c.Vm.Editor.RawText==draft&&c.Vm.Editor.IsDirty&&c.Gateway.LoadCalls==calls,"Cancelled reload must not clear or fetch over the draft.");
            c.Ui.ConfirmAnswers.Enqueue(true);await c.Load();Check(!c.Vm.Editor.IsDirty&&c.Vm.Editor.RawText==c.Gateway.Current("UserServerCustomSettings.ini").Text&&c.Vm.Editor.SelectedFile?.Name=="UserServerCustomSettings.ini","Confirmed reload must retain file identity and load a clean current snapshot.");Check(c.Ui.SwitchPrompts==0,"Reload uses its own confirmation, not a binding-driven switch dialog.");
        });
        add("Native WPF: cancelling exact final review leaves simulated server and draft unchanged",async()=>
        {
            using var c=await Context.Create(root,"cancel-save");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");var original=c.Gateway.Current("UserServerCustomSettings.ini");await c.EditLoot("0.200000");var draft=c.Vm.Editor.RawText;
            c.Ui.ReviewAnswers.Enqueue(false);await c.Save();Check(c.Gateway.Saves.Count==0&&c.Gateway.Current(original.Name).Bytes.SequenceEqual(original.Bytes)&&c.Vm.Editor.RawText==draft&&c.Vm.Editor.IsDirty,"Cancel must not perform a write or lose a draft.");Check(c.Ui.SwitchPrompts==0,"Cancel/save must not request a file switch.");
        });
        add("Native WPF: refresh, selection, raw edits and connection changes cannot race a review or save",async()=>
        {
            using var c=await Context.Create(root,"races");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.EditLoot("0.200000");var draft=c.Vm.Editor.RawText;var original=c.Vm.Editor.CurrentOriginal!;var approved=original.Encode(draft);var gateway=c.Vm.Gateway;
            c.Ui.DuringDialog=async(kind,_)=>{if(kind!="review")return;var reads=c.Gateway.RefreshCalls;await c.Vm.RefreshAsync(true);c.Vm.Editor.EditRawText("");Check(!c.Vm.Editor.RequestFileSelection(c.Vm.Editor.Files.First()),"Selection must be frozen during review.");c.Vm.IsDemo=false;Check(c.Gateway.RefreshCalls==reads&&c.Vm.Editor.RawText==draft&&ReferenceEquals(gateway,c.Vm.Gateway)&&!c.Vm.IsBusy,"Background work/connection changes must not start during review.");};
            c.Gateway.HoldSave=new TaskCompletionSource();await Until(()=>c.Button("Review & save").IsEnabled);Invoke(c.Button("Review & save"));await Until(()=>c.Gateway.Saves.Count==1);
            Check(c.Vm.IsBusy&&!c.Vm.Editor.CanInteract&&c.Vm.OperationState=="Operation running","Only submitted save work should report Operation running.");c.Vm.Editor.EditRawText("");c.Vm.Editor.Clear();Check(!c.Vm.Editor.RequestFileSelection(c.Vm.Editor.Files.First()),"Selection is frozen during save.");await c.Vm.RefreshAsync(true);c.Vm.IsDemo=false;Check(c.Vm.Editor.RawText==draft&&c.Gateway.Saves[0].Payload.SequenceEqual(approved),"In-flight payload must stay exactly approved.");
            c.Gateway.HoldSave.SetResult();await Until(()=>!c.Vm.IsBusy&&c.Vm.Editor.CanInteract);Check(!c.Vm.Editor.IsDirty&&c.Vm.Editor.RawText==draft&&c.Ui.SwitchPrompts==0,"Completion cannot clear or detach the saved document.");
        });
        add("Native WPF: empty/truncated raw edits are rejected before any final review or write",async()=>
        {
            using var c=await Context.Create(root,"invalid-raw");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.Mode(1);c.Raw.Focus();c.Raw.SetCurrentValue(TextBox.TextProperty,"");await Pump();await c.Save();Check(c.Gateway.Saves.Count==0&&c.Ui.Events.All(e=>e.Kind!="review")&&c.Vm.Editor.Validation.Contains("Empty"),"Empty proposals must fail before review/service.");
            c.Raw.SetCurrentValue(TextBox.TextProperty,"["+ConfigurationIntegrity.CustomSection+"]\nDifficultyLevel=Custom\n");await Pump();await c.Save();Check(c.Gateway.Saves.Count==0&&c.Vm.Editor.Validation.Contains("truncated"),"Structured-but-truncated proposals must also be rejected.");
        });
        add("Native WPF: intentional raw removal needs typed filename authorization and final exact review",async()=>
        {
            using var c=await Context.Create(root,"raw-removals");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.Mode(1);var raw=c.Vm.Editor.RawText.Replace("FixtureUnknownKey=keep-exactly\n","");c.Raw.Focus();c.Raw.SetCurrentValue(TextBox.TextProperty,raw);await Pump();await c.Save();Check(c.Gateway.Saves.Count==0&&c.Vm.Editor.Validation.Contains("Review raw removals"),"Save alone cannot authorize removed content.");
            Invoke(c.Button("Review raw removals"));await Until(()=>c.Vm.Editor.CanInteract);Check(c.Ui.Events.Any(e=>e.Kind=="raw"),"A real typed-confirmation dialog must be used.");await c.Save();Check(c.Gateway.Saves.Count==1&&Encoding.UTF8.GetString(c.Gateway.Saves[0].Payload)==raw,"Only the explicitly authorized exact raw payload may be saved.");
        });
    }

    private static void Check(bool condition,string message)=>ConfigurationIntegrityRegression.Check(condition,message);
    private static Task Pump()=>Task.Delay(70);
    private static async Task Until(Func<bool> predicate){for(var i=0;i<200;i++){await Task.Delay(20);if(predicate())return;}throw new TimeoutException("Native WPF command timed out.");}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject p){for(var i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var child=VisualTreeHelper.GetChild(p,i);yield return child;foreach(var node in Descendants(child))yield return node;}}
    private static void Invoke(Button b){Check(b.IsEnabled,"Button disabled: "+b.Content);b.Focus();Keyboard.Focus(b);((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();}

    private sealed class Context : IDisposable
    {
        public required MainWindow Window;public required ShellViewModel Vm;public required ModalUi Ui;public required FixtureGateway Gateway;public required string Evidence;
        public ComboBox Picker=>Descendants(Window).OfType<ComboBox>().Single(c=>ReferenceEquals(c.ItemsSource,Vm.Editor.Files));
        private ConfigurationView View=>Descendants(Window).OfType<ConfigurationView>().Single();
        public TextBox Raw=>(TextBox)View.FindName("RawEditor");
        public Button Button(string content)=>Descendants(Window).OfType<Button>().Single(b=>(b.Content as string)==content);
        public static async Task<Context> Create(string root,string name)
        {
            if(Application.Current==null){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/ArrakisServerManager;component/Theme.xaml",UriKind.Relative)});}
            var evidence=Path.Combine(root,"native-editor",name);Directory.CreateDirectory(evidence);var gateway=new FixtureGateway();var ui=new ModalUi(evidence);var vm=new ShellViewModel(ui,new LocalStores(Path.Combine(evidence,"data")),true,_=>gateway);var window=new MainWindow{DataContext=vm};ui.Owner=window;window.Show();window.Activate();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);var nav=Descendants(window).OfType<ListBox>().Single(l=>ReferenceEquals(l.ItemsSource,vm.Pages));((ISelectionItemProvider)new ListBoxItemAutomationPeer(vm.Pages[2],new ListBoxAutomationPeer(nav)).GetPattern(PatternInterface.SelectionItem)).Select();await Until(()=>Descendants(window).OfType<Button>().Any(b=>(b.Content as string)=="Load current configuration"&&b.IsVisible));return new(){Window=window,Vm=vm,Ui=ui,Gateway=gateway,Evidence=evidence};
        }
        public async Task Load(){await Until(()=>Button("Load current configuration").IsEnabled&&Vm.Editor.CanInteract);Invoke(Button("Load current configuration"));await Until(()=>!Vm.IsBusy&&Vm.Editor.CanInteract&&Vm.Editor.Files.Count==3);await Pump();}
        public async Task SelectFile(string name,bool escape=false)
        {
            var picker=Picker;picker.Focus();var peer=new ComboBoxAutomationPeer(picker);var expand=(IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);expand.Expand();await Pump();
            var choice=Vm.Editor.Files.Single(f=>f.Name==name);Check(picker.ItemContainerGenerator.ContainerFromItem(choice) is ComboBoxItem,"Expanded picker must realize the file item.");((ISelectionItemProvider)new ListBoxItemAutomationPeer(choice,peer).GetPattern(PatternInterface.SelectionItem)).Select();
            if(escape){var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(picker),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};picker.RaiseEvent(key);}
            expand.Collapse();await Until(()=>Vm.Editor.CanInteract&&!picker.IsDropDownOpen&&ReferenceEquals(picker.SelectedItem,Vm.Editor.SelectedFile));await Pump();
        }
        public async Task EditLoot(string value)
        {
            await Mode(0);var grid=Descendants(Window).OfType<DataGrid>().Single(g=>g.ItemsSource==Vm.Editor.SettingsView);grid.SetCurrentValue(Selector.SelectedItemProperty,Vm.Editor.Settings.Single(s=>s.Definition.Key=="LootRespawnSpeed"));await Pump();var input=Descendants(Window).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Proposed setting value");input.Focus();input.SetCurrentValue(TextBox.TextProperty,value);await Until(()=>Button("Add to draft").IsEnabled);Invoke(Button("Add to draft"));await Pump();
        }
        public async Task Mode(int index){var tabs=(TabControl)View.FindName("EditorModes");var item=(TabItem)tabs.Items[index];((ISelectionItemProvider)new TabItemAutomationPeer(item,new TabControlAutomationPeer(tabs)).GetPattern(PatternInterface.SelectionItem)).Select();await Pump();}
        public async Task Save(){await Until(()=>!Vm.IsBusy&&Vm.Editor.CanInteract);await Window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Check(Vm.Editor.IsDirty,"Native save requires a preserved dirty draft.");await Until(()=>Button("Review & save").IsEnabled);Invoke(Button("Review & save"));await Pump();await Until(()=>!Vm.IsBusy&&Vm.Editor.CanInteract);}
        public Task Capture(string name){CaptureWindow(Window,Path.Combine(Evidence,name));return Task.CompletedTask;}
        public void Dispose(){Gateway.HoldSave?.TrySetResult();Vm.Dispose();Window.Close();File.WriteAllText(Path.Combine(Evidence,"dialogs.json"),JsonSerializer.Serialize(Ui.Events,new JsonSerializerOptions{WriteIndented=true}));}
    }
    private sealed record DialogEvent(string Kind,string Title,string Summary,string Diff);
    private sealed class ModalUi(string evidence):IUserInteraction
    {
        public Window Owner=null!;public List<DialogEvent> Events=[];public Queue<bool> ConfirmAnswers=[];public Queue<bool> ReviewAnswers=[];
        public string? ChosenFile,ChosenFolder;
        public Func<string,Window,Task>? DuringDialog;
        public int SwitchPrompts=>Events.Count(e=>e.Title=="Unsaved changes");
        public bool Confirm(string title,string message)=>Show("confirm",title,message,"",ConfirmAnswers.Count==0||ConfirmAnswers.Dequeue());
        public bool Review(string title,string summary,string diff)=>Show("review",title,summary,diff,ReviewAnswers.Count==0||ReviewAnswers.Dequeue());
        public bool ReviewRawRemovals(string title,string summary,string diff,string fileName)=>Show("raw",title,summary,diff,true,fileName);
        private bool Show(string kind,string title,string summary,string diff,bool accept,string? fileName=null)
        {
            Events.Add(new(kind,title,summary,diff));var window=new ReviewWindow(title,summary,diff,true,fileName){Owner=Owner};Exception? error=null;
            window.ContentRendered+=async(_,_)=>{try{if(DuringDialog!=null)await DuringDialog(kind,window);if(fileName!=null){var input=Descendants(window).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Confirm configuration filename");input.Focus();input.Text=fileName;}await Pump();CaptureWindow(window,Path.Combine(evidence,$"dialog-{Events.Count}-{kind}.png"));var button=Descendants(window).OfType<Button>().Single(b=>(b.Content as string)==(accept?"Confirm reviewed change":"Cancel"));Invoke(button);}catch(Exception e){error=e;window.Close();}};
            var result=window.ShowDialog()==true;if(error!=null)throw new InvalidOperationException("Native dialog assertion failed.",error);return result;
        }
        public void Preview(string title,string summary,string diff){}public void ShowError(string title,string summary,string details)=>throw new InvalidOperationException(summary+"\n"+details);
        public string? ChooseFile(string title,string filter)=>ChosenFile;public string? ChooseFolder(string title,string initial)=>ChosenFolder;public void Copy(string text){}public void Open(string path){}
    }
    private static void CaptureWindow(Window window,string path){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);}

    private sealed class FixtureGateway : IServerGateway
    {
        private readonly Dictionary<string,ConfigFile> files=new[]{"UserGame.ini","UserEngine.ini","UserServerCustomSettings.ini"}.Select(n=>ConfigurationIntegrityRegression.Fixture(n)).ToDictionary(f=>f.Name);
        public List<(ConfigFile Original,byte[] Payload)> Saves=[];public int LoadCalls,RefreshCalls;public TaskCompletionSource? HoldSave;
        public ServerSnapshot Status=ServerSnapshot.Empty("isolated fixture");public bool AllowActions,FailRefresh,FailAction,FailLogs;public TaskCompletionSource? HoldRefresh,HoldLogs;public List<ServerAction> Actions=[];public List<MapChange> MapWrites=[];public string LogLines="2026-10-23T01:00:00Z [INFO] fixture ready\n2026-10-23T01:01:00Z [WARNING] fixture warning\n2026-10-23T01:02:00Z [ERROR] fixture failure\n2026-10-23T01:03:00Z [INFO] no error occurred";
        public bool IsDemo=>true;
        public ConfigFile Current(string name){var f=files[name];return f with{Bytes=f.Bytes.ToArray()};}
        public void SeedConfiguration(string name,string text){var current=files[name];var bytes=current.Encode(text);files[name]=current with{Bytes=bytes,Hash=ConfigurationIntegrity.Hash(bytes)};}
        public async Task<ServerSnapshot> RefreshAsync(CancellationToken token){RefreshCalls++;if(HoldRefresh!=null)await HoldRefresh.Task.WaitAsync(token);if(FailRefresh)throw new IOException("Isolated refresh failure");return Status;}
        public Task<IReadOnlyList<ConfigFile>> LoadConfigurationAsync(CancellationToken token){LoadCalls++;return Task.FromResult<IReadOnlyList<ConfigFile>>(files.Values.Select(f=>Current(f.Name)).ToArray());}
        public async Task<ConfigFile> SaveConfigurationAsync(ConfigFile original,byte[] proposed,CancellationToken token){ConfigurationIntegrity.ValidateProposal(original,(original with{Bytes=proposed}).Text);if(original.Hash!=files[original.Name].Hash)throw new InvalidOperationException("Fixture conflict.");Saves.Add((original,proposed.ToArray()));if(HoldSave!=null)await HoldSave.Task;var saved=new ConfigFile(original.Name,original.RemotePath,proposed.ToArray(),ConfigurationIntegrity.Hash(proposed));files[original.Name]=saved;return Current(original.Name);}
        public async Task<ActionResult> ExecuteAsync(ServerAction a,string? argument,IProgress<string>? p,CancellationToken t){if(!AllowActions)throw new InvalidOperationException("Native editor test must not call lifecycle operations.");Actions.Add(a);await Task.Delay(35,t);if(FailAction)throw new InvalidOperationException("Isolated operation failure");Status=a switch{ServerAction.ShutdownVm=>Status with{Vm=Status.Vm with{State="Off"},SshConnected=false,Guest=null,Battlegroup="Stopped",Maps=[]},ServerAction.StartVm=>Status with{Vm=Status.Vm with{State="Running"},SshConnected=true,Battlegroup="Healthy"},ServerAction.StopBattlegroup=>Status with{Battlegroup="Stopped"},ServerAction.StartBattlegroup=>Status with{Battlegroup="Healthy"},_=>Status};return new("Isolated verified "+a,true);}
        public Task<IReadOnlyList<BackupArtifact>> ListBackupsAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<BackupArtifact>>([]);
        public async Task<string> ReadLogsAsync(string s,MapInstance? p,CancellationToken t){if(HoldLogs!=null)await HoldLogs.Task.WaitAsync(t);if(FailLogs)throw new IOException("Isolated log failure");return LogLines;}
        public Task<string> ChangeMapAsync(MapChange c,CancellationToken t){if(!AllowActions)throw new InvalidOperationException("Map mutation not enabled for this isolated case.");c.Validate();MapWrites.Add(c);Status=Status with{Maps=Status.Maps.Select(m=>m.Map==c.Original.Map?m with{Request=c.MemoryRequest,Limit=c.MemoryLimit,Minimum=c.Minimum,ResourceVersion="2"}:m).ToArray()};return Task.FromResult("Isolated map change verified");}
        public Task<string> DownloadBackupAsync(BackupArtifact a,CancellationToken t)=>throw new NotSupportedException();public Task<string> CheckUpdatesAsync(CancellationToken t)=>throw new NotSupportedException();public Task<Uri> GetToolUriAsync(bool d,CancellationToken t)=>throw new NotSupportedException();public Task LaunchOfficialEditorAsync(CancellationToken t)=>throw new NotSupportedException();public Task<string> LaunchMaintenanceAsync(MaintenanceAction a,CancellationToken t)=>throw new NotSupportedException();
    }
}
