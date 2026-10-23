using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arrakis.Core;
using Arrakis.Desktop.Controls;

namespace Arrakis.Tests;

internal static partial class NativeConfigurationRegression
{
    public static void RegisterEnhancements(Action<string,Func<Task>> add,string root)
    {
        add("Native v1.1: slider input intent preserves exact typing and only Add changes the draft",async()=>
        {
            using var c=await Context.Create(root,"v11-slider-precision");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await SelectSetting(c,"LootRespawnSpeed");
            var input=Input(c);input.Focus();input.SetCurrentValue(TextBox.TextProperty,"0.375");await Pump();var original=c.Vm.Editor.RawText;var slider=Slider(c);
            Check(c.Vm.Editor.SettingValue=="0.375"&&Math.Abs(slider.Value-.375)<1e-10&&!c.Vm.Editor.IsDirty,"Exact typed precision must initialize slider position without adding a draft edit.");
            slider.Focus();await c.Mode(1);await c.Mode(0);await Pump();Check(c.Vm.Editor.SettingValue=="0.375"&&c.Vm.Editor.RawText==original,"Focus and editor modes must not quantize input or mutate bytes.");
            var peer=UIElementAutomationPeer.CreatePeerForElement(slider)!;((IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)).SetValue(.8);await Pump();Check(c.Vm.Editor.SettingValue=="0.8"&&Input(c).Text=="0.8"&&!c.Vm.Editor.IsDirty,"A real UIA slider choice updates the proposed input only.");
            Invoke(c.Button("Add to draft"));await Pump();Check(c.Vm.Editor.RawText==original.Replace("LootRespawnSpeed=0.100000","LootRespawnSpeed=0.8"),"Slider draft must be an exact one-line replacement.");
            await c.Capture("slider-draft.png");c.Ui.ReviewAnswers.Enqueue(false);await c.Save();Check(c.Gateway.Saves.Count==0&&c.Vm.Editor.IsDirty,"Cancel cannot send slider bytes.");await c.Save();Check(c.Gateway.Saves.Count==1&&c.Ui.SwitchPrompts==0,"Reviewed slider save must retain baseline protections.");await c.Load();Check(!c.Vm.Editor.IsDirty&&c.Vm.Editor.RawText==c.Gateway.Current("UserServerCustomSettings.ini").Text,"Simulated save/reload must share the saved authoritative bytes.");
        });
        add("Native v1.1: keyboard integer/zero sliders and unsupported inputs follow metadata",async()=>
        {
            using var c=await Context.Create(root,"v11-slider-types");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await SelectSetting(c,"FiefdomLimit");var slider=Slider(c);
            ((IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(slider)!.GetPattern(PatternInterface.RangeValue)).SetValue(0);await Pump();Check(c.Vm.Editor.SettingValue=="0"&&slider.SmallChange==1,"Integer-only and zero boundaries must be supported.");
            slider.Focus();var source=PresentationSource.FromVisual(slider);slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Right){RoutedEvent=Keyboard.KeyDownEvent});await Pump();Check(c.Vm.Editor.SettingValue=="1"&&!c.Vm.Editor.IsDirty,"Keyboard slider interaction must choose a whole-number proposal only.");
            await SelectSetting(c,"bBuildingInfiniteStability");Check(!c.Vm.Editor.HasSlider&&!slider.IsVisible,"Boolean controls must have no slider.");await SelectSetting(c,"BaseBackupToolTimeRestriction");Check(!c.Vm.Editor.HasSlider,"An unverified upper bound cannot enable a slider.");
            Input(c).Focus();Input(c).Text="NaN";await Until(()=>c.Button("Add to draft").IsEnabled);Invoke(c.Button("Add to draft"));await Pump();Check(!c.Vm.Editor.IsDirty&&c.Vm.Editor.Validation.Contains("finite"),"Invalid input cannot change the document.");
        });
        add("Native v1.1: dynamic category identities/counts/search do not modify or detach a draft",async()=>
        {
            using var c=await Context.Create(root,"v11-categories");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.EditLoot("0.200000");var draft=c.Vm.Editor.RawText;var id=c.Vm.Editor.DocumentId;var choice=c.Vm.Editor.SelectedFile;
            var category=Descendants(c.Window).OfType<ComboBox>().Single(b=>AutomationProperties.GetName(b)=="Configuration category");var water=c.Vm.Editor.Categories.Single(o=>o.Name=="Water");await SelectCombo(category,water);
            Check(c.Vm.Editor.Category=="Water"&&water.Count==c.Vm.Editor.Settings.Count(s=>s.IsPresent&&s.Definition.Category=="Water"),"Categories and counts must reflect actual entries.");await c.Vm.RefreshAsync(true);
            Check(c.Vm.Editor.Categories.Contains(water)&&c.Vm.Editor.RawText==draft&&c.Vm.Editor.DocumentId==id,"A background status refresh cannot rebuild the active document or category identity.");
            c.Vm.Editor.Search="__no_match__";await Pump();Check(c.Vm.Editor.Categories.Count==1&&c.Vm.Editor.SettingsView.IsEmpty&&c.Vm.Editor.Category=="All categories","Empty categories must disappear and fall back to All.");c.Vm.Editor.Search="Water";await Pump();Check(c.Vm.Editor.Categories.Any(o=>o.Name=="Water"),"Category searches must match metadata.");c.Vm.Editor.Search="";
            Check(c.Vm.Editor.RawText==draft&&ReferenceEquals(choice,c.Vm.Editor.SelectedFile)&&c.Ui.SwitchPrompts==0,"Filtering must have no document/file/save side effects.");await c.Capture("categories.png");
            await c.Mode(1);Check(c.Raw.Text==draft,"Raw mode must show the complete document regardless of visual filters.");
        });
        add("Native v1.1: raw removals update applicable categories while absent supported entries remain addable",async()=>
        {
            using var c=await Context.Create(root,"v11-category-removal");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");var before=c.Vm.Editor.RawText;
            await c.Mode(1);var lines=before.Split('\n').Where(l=>!l.TrimStart().StartsWith("WaterExtractionRate=")&&!l.TrimStart().StartsWith("ThirstMultiplier="));c.Raw.Focus();c.Raw.Text=string.Join('\n',lines);await Pump();
            Check(c.Vm.Editor.Categories.All(o=>o.Name!="Water"),"A category with no configured entries must disappear after raw changes.");c.Vm.Editor.ShowAvailableToAdd=true;c.Vm.Editor.Category="All categories";await c.Mode(0);Check(c.Vm.Editor.SettingsView.Cast<Arrakis.Desktop.ViewModels.SettingRow>().Any(s=>s.Definition.Key=="ThirstMultiplier"&&!s.IsPresent),"Recognized absent entries must remain explicitly available to add.");
            c.Ui.ConfirmAnswers.Enqueue(false);await c.SelectFile("UserEngine.ini");Check(c.Vm.Editor.SelectedFile?.Name=="UserServerCustomSettings.ini"&&c.Vm.Editor.IsDirty,"Dynamic categories must preserve genuine dirty-switch cancellation.");
        });
        add("Native v1.1: Dashboard VM actions share state gating, confirmation and post-shutdown refresh",async()=>
        {
            using var c=await Context.Create(root,"v11-vm-controls");c.Gateway.Status=EnhancementRegression.Snapshot();c.Gateway.AllowActions=true;await c.Vm.RefreshAsync();c.Vm.SelectedPage=0;await Pump();
            var start=c.Button("Start VM");var shutdown=c.Button("Graceful shutdown…");await Until(()=>shutdown.IsEnabled);Check(!start.IsEnabled,"Running VM must disable Start.");c.Ui.ConfirmAnswers.Enqueue(false);Invoke(shutdown);await Pump();Check(c.Gateway.Actions.Count==0,"Cancel must not dispatch VM work.");
            await Until(()=>shutdown.IsEnabled&&c.Window.OwnedWindows.Count==0);Invoke(shutdown);await Until(()=>c.Gateway.Actions.Count==1&&!c.Vm.IsBusy&&c.Vm.History.Any(h=>h.Name=="ShutdownVm"));await Until(()=>start.IsEnabled);Check(c.Vm.Snapshot.Vm.State=="Off"&&!shutdown.IsEnabled&&!c.Vm.Snapshot.SshConnected&&c.Vm.History.First().Outcome=="Succeeded","A verified Off state and expected SSH loss must complete normally.");
            Check(c.Ui.Events.Any(e=>e.Summary.Contains("all game-server instances")&&e.Summary.Contains("Shutdown may take time")),"Shutdown confirmation must explain impact.");Invoke(start);await Until(()=>c.Gateway.Actions.Count==2&&!c.Vm.IsBusy);await c.Capture("dashboard.png");var dashboardScroll=Descendants(c.Window).OfType<ScrollViewer>().First(v=>v.ScrollableHeight>200);dashboardScroll.ScrollToEnd();await Pump();await c.Capture("history-and-health.png");Check(c.Vm.Snapshot.Vm.State=="Running"&&c.Vm.BattlegroupState.Contains("Core services"),"Start must refresh the same shared state model.");
        });
        add("Native v1.1: access denied, transitions and failed lifecycle operations stay explicit",async()=>
        {
            using var c=await Context.Create(root,"v11-vm-failures");c.Vm.SelectedPage=0;
            c.Gateway.Status=EnhancementRegression.Snapshot() with{Vm=new("fixture","Unavailable",Error:"Access denied")};await c.Vm.RefreshAsync();await Pump();Check(c.Vm.VmAccess.Contains("access denied")&&c.Vm.VmAccess.Contains("reachable")&&!c.Vm.StartVmCommand.CanExecute(null)&&!c.Vm.ShutdownVmCommand.CanExecute(null),"Access denial is distinct from Off, with independent SSH state.");
            c.Gateway.Status=EnhancementRegression.Snapshot() with{Vm=new("fixture","Unavailable",Error:"You do not have the required permission to complete this task.")};await c.Vm.RefreshAsync();Check(c.Vm.VmAccess.Contains("access denied"),"Hyper-V's required-permission wording must also be recognized as access failure.");
            foreach(var state in new[]{"Starting","Stopping"}){c.Gateway.Status=EnhancementRegression.Snapshot(state);await c.Vm.RefreshAsync();Check(!c.Vm.StartVmCommand.CanExecute(null)&&!c.Vm.ShutdownVmCommand.CanExecute(null),"Transitional states must disable both controls.");}
            c.Gateway.Status=EnhancementRegression.Snapshot();c.Gateway.AllowActions=true;c.Gateway.FailAction=true;await c.Vm.RefreshAsync();await Until(()=>c.Button("Graceful shutdown…").IsEnabled);Invoke(c.Button("Graceful shutdown…"));await Until(()=>c.Gateway.Actions.Count==1&&!c.Vm.IsBusy&&c.Vm.History.Any(h=>h.Name=="ShutdownVm"));Check(c.Vm.History.First().Outcome=="Failed"&&c.Vm.Snapshot.Vm.State=="Running","A known failed simulated operation must leave the observed state intact.");
        });
        add("Native v1.1: map modes preserve search/selection drafts and reject invalid resource edits",async()=>
        {
            using var c=await Context.Create(root,"v11-map-modes");var map=EnhancementRegression.Map();var service=map with{Map="Database",Pod="db",SetIndex=-1,Kind=WorkloadKind.Infrastructure};var unknown=service with{Map="Unclassified",Pod="unknown",Kind=WorkloadKind.Unknown};c.Gateway.Status=EnhancementRegression.Snapshot(maps:[map,service,unknown]);await c.Vm.RefreshAsync();c.Vm.SelectedPage=1;await Pump();
            Check(c.Vm.MapsView.Cast<MapInstance>().Count()==1,"Game Maps must be the default view.");c.Vm.SelectedMap=map;c.Vm.MapLimit="3Gi";Check(c.Vm.CanApplyMap,"A real valid game-map change must enable Apply.");var mode=Descendants(c.Window).OfType<ComboBox>().Single(b=>AutomationProperties.GetName(b)=="Workload view");await SelectCombo(mode,"Infrastructure");Check(c.Vm.MapsView.Cast<MapInstance>().Single().Pod=="db"&&c.Vm.MapLimit=="3Gi","Filtering must not erase the selected resource draft.");await SelectCombo(mode,"All");Check(c.Vm.MapsView.Cast<MapInstance>().Count()==3,"Unknown resources remain visible under All.");
            c.Vm.MapSearch="Unclassified";await Pump();Check(c.Vm.MapsView.Cast<MapInstance>().Count()==1,"Search works across modes.");c.Vm.MapSearch="";c.Vm.SelectedMap=unknown;Check(!c.Vm.CanApplyMap,"Unknown/infrastructure resources cannot use game-map editing.");c.Vm.SelectedMap=map;c.Vm.MapRequest="Unavailable";Check(!c.Vm.CanApplyMap,"Unavailable cannot be treated as zero.");c.Vm.MapRequest="4Gi";Check(!c.Vm.CanApplyMap,"Request cannot exceed limit.");c.Vm.MapRequest="200Mi";c.Vm.MapMinimum="Not set";Check(!c.Vm.CanApplyMap,"Minimum must be a number.");c.Vm.MapMinimum="0";Check(!c.Vm.CanApplyMap,"Unchanged resources cannot apply.");await Pump();await c.Capture("maps.png");
        });
        add("Native v1.1: overlapping/failed/cancelled refreshes preserve prior data and configuration",async()=>
        {
            using var c=await Context.Create(root,"v11-refresh");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await c.EditLoot("0.200000");var draft=c.Vm.Editor.RawText;c.Gateway.Status=EnhancementRegression.Snapshot();await c.Vm.RefreshAsync();var calls=c.Gateway.RefreshCalls;c.Gateway.HoldRefresh=new();var first=c.Vm.RefreshAsync(true);await Until(()=>c.Vm.IsBusy);await c.Vm.RefreshAsync(true);Check(c.Gateway.RefreshCalls==calls+1,"Overlapping requests must not accumulate.");c.Vm.CancelCommand.Execute(null);await first;Check(!c.Vm.IsBusy&&c.Vm.RefreshState.StartsWith("Stale")&&c.Vm.Editor.RawText==draft,"Cancelled refresh must retain snapshot/draft and report stale data.");c.Gateway.HoldRefresh=null;c.Gateway.FailRefresh=true;await c.Vm.RefreshAsync();Check(c.Vm.Snapshot.Vm.State=="Running"&&c.Vm.RefreshState.Contains("failed"),"Failed refresh must not erase last known state.");c.Gateway.FailRefresh=false;await c.Vm.RefreshAsync();Check(!c.Vm.RefreshState.StartsWith("Stale")&&c.Vm.HistorySamples.Count>0,"Recovery refresh must resume bounded samples.");
        });
        add("Native v1.1: log source/search/severity and copyable event details retain historical evidence",async()=>
        {
            using var c=await Context.Create(root,"v11-diagnostics");var evt=new ClusterEvent("fixture","fixture-pod","Unhealthy","Recovered readiness failure","Warning","2026-10-22T01:00:00Z",Count:4,CurrentStatus:"Running",Actionable:false);c.Gateway.Status=EnhancementRegression.Snapshot() with{Events=[evt]};await c.Vm.RefreshAsync();c.Vm.SelectedPage=4;await Pump();c.Vm.LogSource="Battlegroup";Invoke(c.Button("Read logs"));await Until(()=>!c.Vm.IsBusy&&c.Vm.LogText.Length>0);c.Vm.LogSeverity="Error";await Pump();Check(c.Vm.FilteredLogs.Contains("[ERROR]")&&!c.Vm.FilteredLogs.Contains("[INFO]"),"Severity filtering must honor explicit log markers.");c.Vm.LogSearch="__none__";Check(c.Vm.LogEmptyMessage.Length>0,"Empty searches need instructions.");c.Vm.LogSearch="";c.Vm.SelectedEvent=evt;Check(c.Vm.SelectedEventDetails.Contains("Historical")&&c.Vm.SelectedEventDetails.Contains("Occurrences: 4"),"Historical events and counts must remain available.");
            var tabs=Descendants(c.Window).OfType<TabControl>().Single(t=>t.Name=="DiagnosticTabs");((ISelectionItemProvider)new TabItemAutomationPeer((TabItem)tabs.Items[1],new TabControlAutomationPeer(tabs)).GetPattern(PatternInterface.SelectionItem)).Select();await Pump();await c.Capture("events.png");
        });
        add("Native v1.1: Dashboard and Maps share state-aware battlegroup handlers",async()=>
        {
            using var c=await Context.Create(root,"v11-battlegroup");c.Gateway.Status=EnhancementRegression.Snapshot();c.Gateway.AllowActions=true;await c.Vm.RefreshAsync();c.Vm.SelectedPage=0;await Pump();Check(!c.Vm.StartCommand.CanExecute(null)&&c.Vm.StopCommand.CanExecute(null),"Healthy group must disable redundant Start.");Invoke(c.Button("Stop"));await Until(()=>c.Vm.History.Any(h=>h.Name=="StopBattlegroup")&&!c.Vm.IsBusy);Check(c.Vm.StartCommand.CanExecute(null)&&!c.Vm.StopCommand.CanExecute(null),"Stopped group must disable redundant Stop.");c.Vm.SelectedPage=1;await Pump();var start=Descendants(c.Window).OfType<Button>().Single(b=>ReferenceEquals(b.Command,c.Vm.StartCommand));await Until(()=>start.IsEnabled);Invoke(start);await Until(()=>c.Vm.History.Any(h=>h.Name=="StartBattlegroup")&&!c.Vm.IsBusy);Check(c.Gateway.Actions.SequenceEqual(new[]{ServerAction.StopBattlegroup,ServerAction.StartBattlegroup}),"Both pages must dispatch the same serialized gateway handlers.");
        });
        add("Native v1.1: map-resource review cancellation and approval bind the actual immutable change",async()=>
        {
            using var c=await Context.Create(root,"v11-map-review");c.Gateway.Status=EnhancementRegression.Snapshot();c.Gateway.AllowActions=true;await c.Vm.RefreshAsync();c.Vm.SelectedPage=1;await Pump();c.Vm.MapLimit="3Gi";var button=c.Button("Review & apply resources");await Until(()=>button.IsEnabled);c.Ui.ReviewAnswers.Enqueue(false);Invoke(button);await Until(()=>c.Ui.Events.Count==1&&c.Vm.CanOperate);Check(c.Gateway.MapWrites.Count==0,"Cancellation must not send resource changes.");
            c.Ui.DuringDialog=async(kind,_)=>{if(kind=="review"){Check(c.Gateway.MapWrites.Count==0&&!c.Vm.IsBusy,"Review is not a remote operation.");await c.Vm.RefreshAsync(true);}};await Until(()=>button.IsEnabled&&c.Window.OwnedWindows.Count==0);Invoke(button);await Until(()=>c.Gateway.MapWrites.Count==1&&c.Vm.History.Any(h=>h.Name=="Apply map resources"));Check(c.Gateway.MapWrites[0].MemoryLimit=="3Gi"&&c.Ui.Events.Last().Diff.Contains("2Gi → 3Gi"),"Actual resource payload must equal the approved change.");
        });
        add("Native v1.1: backup summary never treats configuration copies as database protection",async()=>
        {
            using var c=await Context.Create(root,"v11-backups");c.Gateway.Status=EnhancementRegression.Snapshot();await c.Vm.RefreshAsync();c.Vm.SelectedPage=3;await Pump();Invoke(c.Button("Refresh list"));await Until(()=>c.Vm.History.Any(h=>h.Name=="List backup artifacts"));c.Vm.Backups.Add(new("fixture.asmconfig","/isolated/config-copy",50,DateTimeOffset.Now,"Configuration"));Check(c.Vm.BackupSummary.Contains("No nonempty database backup"),"A config copy cannot create healthy database-backup status.");c.Vm.Preferences.BackupDirectory=@"C:\Users\fixture\OneDrive\Backups";Check(c.Vm.CloudBackupWarning.Contains("sensitive data"),"Cloud destinations require an explicit warning.");await c.Capture("backups.png");
        });
        add("Native v1.1: log cancellation stops live tail and failures preserve bounded prior text",async()=>
        {
            using var c=await Context.Create(root,"v11-log-cancel");c.Gateway.Status=EnhancementRegression.Snapshot();await c.Vm.RefreshAsync();c.Vm.SelectedPage=4;await Pump();c.Vm.LogSource="Battlegroup";c.Vm.LogText="prior evidence";c.Gateway.HoldLogs=new();c.Vm.LiveLogs=true;Invoke(c.Button("Read logs"));await Until(()=>c.Vm.IsBusy);c.Vm.CancelCommand.Execute(null);await Until(()=>!c.Vm.IsBusy&&c.Vm.LogStatus.Contains("cancelled"));Check(!c.Vm.LiveLogs&&c.Vm.LogText=="prior evidence","Cancelling a tail cannot discard previously displayed evidence.");c.Gateway.HoldLogs=null;c.Gateway.FailLogs=true;await Until(()=>c.Button("Read logs").IsEnabled);Invoke(c.Button("Read logs"));await Until(()=>c.Vm.LogStatus.Contains("failed")&&!c.Vm.IsBusy);Check(c.Vm.LogText=="prior evidence","Failed reads must retain and label prior text.");c.Vm.LogText=new string('x',70000);Check(c.Vm.LogText.Length==60000,"The displayed log buffer must stay bounded.");
        });
        add("Native v1.1: minimum window and 125/150 percent render scaling retain native controls",async()=>
        {
            using var c=await Context.Create(root,"v11-layout");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await SelectSetting(c,"LootRespawnSpeed");c.Window.Width=1040;c.Window.Height=700;await Pump();Check(c.Window.ActualWidth>=1040&&c.Window.ActualHeight>=700&&c.Button("Review & save").ActualWidth>0,"Minimum layout must retain the save action.");
            foreach(var scale in new[]{1d,1.25,1.5}){c.Window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)(c.Window.ActualWidth*scale),(int)(c.Window.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(c.Window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(c.Evidence,$"minimum-{scale:0.00}.png"));encoder.Save(file);}
            File.WriteAllText(Path.Combine(c.Evidence,"scaling.txt"),$"Native logical layout 1040x700; render DPI 96/120/144. Current system DPI: {VisualTreeHelper.GetDpi(c.Window).PixelsPerInchX}. OS display scaling was not changed.");
            var input=Input(c);input.Focus();Check(input.IsKeyboardFocusWithin&&input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),"Keyboard focus can traverse numerical controls.");
        });
    }
    private static TextBox Input(Context c)=>Descendants(c.Window).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Proposed setting value");
    private static ProposalSlider Slider(Context c)=>Descendants(c.Window).OfType<ProposalSlider>().Single();
    private static async Task SelectSetting(Context c,string key){await c.Mode(0);var grid=Descendants(c.Window).OfType<DataGrid>().Single(g=>ReferenceEquals(g.ItemsSource,c.Vm.Editor.SettingsView));var row=c.Vm.Editor.Settings.Single(s=>s.Definition.Key==key);grid.ScrollIntoView(row);grid.SetCurrentValue(Selector.SelectedItemProperty,row);await Pump();}
    private static async Task SelectCombo(ComboBox combo,object item){combo.Focus();var peer=new ComboBoxAutomationPeer(combo);var expand=(IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);expand.Expand();await Pump();((ISelectionItemProvider)new ListBoxItemAutomationPeer(item,peer).GetPattern(PatternInterface.SelectionItem)).Select();expand.Collapse();await Pump();}
}
