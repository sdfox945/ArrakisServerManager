using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static partial class NativeConfigurationRegression
{
    public static void RegisterStabilization(Action<string,Func<Task>> add,string root)
    {
        add("Native v1.1.1: equal typed/slider values disable Add without changing draft bytes",async()=>
        {
            using var c=await Context.Create(root,"v111-no-op");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await SelectSetting(c,"LootRespawnSpeed");var original=c.Vm.Editor.RawText;var id=c.Vm.Editor.DocumentId;var addButton=c.Button("Add to draft");
            System.IO.File.WriteAllText(System.IO.Path.Combine(c.Evidence,"initial-proposal.json"),System.Text.Json.JsonSerializer.Serialize(new{Key=c.Vm.Editor.SelectedSetting?.Definition.Key,Current=c.Vm.Editor.SelectedSetting?.Current,Proposed=c.Vm.Editor.SettingValue,CanAdd=c.Vm.Editor.CanAddSetting,ButtonEnabled=addButton.IsEnabled}));
            await Until(()=>!addButton.IsEnabled);Check(!c.Vm.Editor.CanAddSetting,"The loaded proposed/current value must be a no-op.");Input(c).Focus();EnterSettingInput(c,"0.1");await Until(()=>!addButton.IsEnabled);Check(c.Vm.Editor.RawText==original&&!c.Vm.Editor.IsDirty,"Formatting differences must not become draft edits.");
            ((IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(Slider(c))!.GetPattern(PatternInterface.RangeValue)).SetValue(.1);await Pump();Check(!addButton.IsEnabled&&c.Vm.Editor.RawText==original,"Equivalent slider focus/choice cannot alter the authoritative draft.");
            EnterSettingInput(c,"0.10000000000000001");await Until(()=>addButton.IsEnabled);EnterSettingInput(c,"0.100000");await Until(()=>!addButton.IsEnabled);addButton.BringIntoView();await Pump();await c.Capture("unchanged-disabled.png");
            c.Vm.Editor.ApplySettingCommand.Execute(null);Check(c.Vm.Editor.RawText==original,"The command handler itself must also reject a no-op.");EnterSettingInput(c,"0.200000");await Until(()=>addButton.IsEnabled);Invoke(addButton);await Until(()=>!addButton.IsEnabled);Check(c.Vm.Editor.RawText==original.Replace("LootRespawnSpeed=0.100000","LootRespawnSpeed=0.200000"),"A changed proposal must still create exactly one changed line.");
            EnterSettingInput(c,"0.2");await Until(()=>!addButton.IsEnabled);Check(c.Vm.Editor.IsDirty&&c.Vm.Editor.DocumentId==id,"Comparison must use the current draft, without resetting identity or dirty state.");
            var category=Descendants(c.Window).OfType<ComboBox>().Single(b=>AutomationProperties.GetName(b)=="Configuration category");await SelectCombo(category,c.Vm.Editor.Categories.Single(o=>o.Name=="Respawning"));await c.Mode(1);await c.Mode(0);Check(c.Vm.Editor.DocumentId==id&&c.Vm.Editor.RawText.Contains("LootRespawnSpeed=0.200000")&&c.Gateway.Saves.Count==0&&c.Ui.SwitchPrompts==0,"Filtering, mode changes and no-op state cannot save or detach the draft.");addButton.BringIntoView();await Pump();await c.Capture("draft-no-op-disabled.png");
            EnterSettingInput(c,"0.100000");await Until(()=>addButton.IsEnabled);Invoke(addButton);await Pump();Check(!c.Vm.Editor.IsDirty&&c.Vm.Editor.RawText==original,"An intentional reverse edit must remain possible and restore exact original bytes.");
        });
        add("Native v1.1.1: unchanged integer input stays disabled while absent supported entries remain addable",async()=>
        {
            using var c=await Context.Create(root,"v111-no-op-types");await c.Load();await c.SelectFile("UserEngine.ini");await SelectSetting(c,"Port");EnterSettingInput(c,c.Vm.Editor.SelectedSetting!.Current+".0");await Pump();Check(!c.Vm.Editor.ApplySettingCommand.CanExecute(null),"Whole-number formatting is not a change.");
            await c.SelectFile("UserServerCustomSettings.ini");c.Vm.Editor.ShowAvailableToAdd=true;await SelectSetting(c,"FiefdomLimit");await Until(()=>c.Button("Add to draft").IsEnabled);Invoke(c.Button("Add to draft"));await Until(()=>!c.Button("Add to draft").IsEnabled);EnterSettingInput(c,"3.0");await Pump();Check(!c.Vm.Editor.CanAddSetting,"Integer equality must also use an unsaved draft value.");
            await c.Mode(1);c.Raw.Focus();c.Raw.Text=string.Join('\n',c.Raw.Text.Split('\n').Where(l=>!l.TrimStart().StartsWith("FiefdomLimit=")));await Pump();c.Vm.Editor.ShowAvailableToAdd=true;await c.Mode(0);await SelectSetting(c,"FiefdomLimit");await Until(()=>c.Button("Add to draft").IsEnabled);Check(!c.Vm.Editor.SelectedSetting!.IsPresent&&c.Vm.Editor.CanAddSetting,"An absent entry remains addable regardless of its template value.");Invoke(c.Button("Add to draft"));await Pump();Check(c.Vm.Editor.SelectedSetting.IsPresent,"Explicitly adding an absent supported setting must still work.");
        });
        add("Native v1.1.1: completed utility is healthy Infrastructure with read-only workload details",async()=>
        {
            using var c=await Context.Create(root,"v111-completed-utility");var utility=StabilizationRegression.One(StabilizationRegression.Utility());var unknown=StabilizationRegression.One(StabilizationRegression.Pod());c.Gateway.Status=EnhancementRegression.Snapshot(maps:[utility,unknown]);await c.Vm.RefreshAsync();c.Vm.SelectedPage=0;await Pump();Check(!c.Vm.BattlegroupState.Contains("issue")&&!c.Vm.Notices.Any(n=>n.Current),"Completed workloads must not create current Dashboard errors.");await c.Capture("dashboard-completed.png");
            c.Vm.SelectedPage=1;await Pump();var mode=Descendants(c.Window).OfType<ComboBox>().Single(b=>AutomationProperties.GetName(b)=="Workload view");await SelectCombo(mode,"Infrastructure");Check(c.Vm.MapsView.Cast<MapInstance>().Single().Pod==utility.Pod,"Utility must remain accessible under Infrastructure.");c.Vm.SelectedMap=utility;await Pump();var detail=Descendants(c.Window).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Selected workload details");Check(detail.IsReadOnly&&detail.Text.Contains("Completed successfully")&&detail.Text.Contains("DatabaseDeployment")&&!detail.Text.Contains("Current warning:"),"Completed details must be accurate, copyable and free of empty warnings.");
            Check(!c.Vm.CanEditMapResources&&!c.Button("Review & apply resources").IsEnabled,"Utility workloads cannot expose editable game-map resources.");await c.Capture("infrastructure-completed.png");await SelectCombo(mode,"All");Check(c.Vm.MapsView.Cast<MapInstance>().Count()==2,"Unclassified success remains accessible under All.");
            var failed=StabilizationRegression.One(StabilizationRegression.Pod(owner:"Job"),StabilizationRegression.Jobs("Failed"));c.Gateway.Status=EnhancementRegression.Snapshot(maps:[failed]);await c.Vm.RefreshAsync();Check(c.Vm.Notices.Any(n=>n.Current&&n.Severity=="Error")&&c.Vm.Maps.Single().HasCurrentProblem,"Real Job failures must still surface after a healthy completion refresh.");await c.Capture("failed-job-retained.png");
        });
        add("Native v1.1.1: confirmed idle maps have distinct filtering and unknown minima do not imply demand",async()=>
        {
            using var c=await Context.Create(root,"v111-idle");var idle=KubernetesParser.Maps(StabilizationRegression.Group(0,0,true),StabilizationRegression.List(),null,null,null).Single();var unavailable=KubernetesParser.Maps(StabilizationRegression.Group(null,0,true),StabilizationRegression.List(),null,null,null).Single() with{Map="Unconfirmed map"};c.Gateway.Status=EnhancementRegression.Snapshot(maps:[idle,unavailable]);await c.Vm.RefreshAsync();c.Vm.SelectedPage=1;await Pump();var filter=Descendants(c.Window).OfType<ComboBox>().Single(b=>b.Items.Contains("Idle / On demand"));await SelectCombo(filter,"Idle / On demand");Check(c.Vm.MapsView.Cast<MapInstance>().Single().State=="Idle / On demand"&&unavailable.State!="Idle / On demand"&&unavailable.MinimumDisplay=="Not set","Filtering must distinguish confirmed idle maps from unknown configuration.");await c.Capture("idle-on-demand.png");
        });
    }
    private static void EnterSettingInput(Context c,string value){var input=Input(c);input.Focus();input.SetCurrentValue(TextBox.TextProperty,value);}
}
