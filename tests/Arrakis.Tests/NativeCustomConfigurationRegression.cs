using System.IO;
using System.Text;
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
    private static void CustomCheck(bool condition,string message="Native custom configuration assertion failed")=>Check(condition,message);
    private static TextBox Field(Window window,string name)=>Descendants(window).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==name);
    private static ComboBox Combo(Window window,string name)=>Descendants(window).OfType<ComboBox>().Single(t=>AutomationProperties.GetName(t)==name);
    private static async Task EnterCombo(Window window,string name,string text)
    {
        var combo=Combo(window,name);combo.ApplyTemplate();var input=(TextBox?)combo.Template.FindName("PART_EditableTextBox",combo);CustomCheck(input!=null,"New section/category entry needs a real editable field.");input!.Focus();((IValueProvider)new TextBoxAutomationPeer(input).GetPattern(PatternInterface.Value)).SetValue(text);await Pump();combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
    }
    private static async Task Enter(Window window,string name,string text){if(!Descendants(window).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)==name)){var advanced=Descendants(window).OfType<Expander>().Single(e=>(e.Header as string)=="Advanced metadata · optional");advanced.IsExpanded=true;await Pump();}var control=Field(window,name);control.Focus();control.SetCurrentValue(TextBox.TextProperty,text);control.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();await Pump();}
    private static async Task DefinitionDialog(Context c,bool manage,Func<SettingDefinitionWindow,DefinitionEditorViewModel,Task> action,bool cancel=false)
    {
        var done=new TaskCompletionSource();
        _ = c.Window.Dispatcher.BeginInvoke(async()=>
        {
            SettingDefinitionWindow? window=null;
            try
            {
                await Until(()=>Application.Current.Windows.OfType<SettingDefinitionWindow>().Any(w=>w.IsVisible));window=Application.Current.Windows.OfType<SettingDefinitionWindow>().Single(w=>w.IsVisible);
                await action(window,(DefinitionEditorViewModel)window.DataContext);
                if(window.IsVisible){var button=Descendants(window).OfType<Button>().Single(b=>cancel?(b.Content as string)=="Cancel":AutomationProperties.GetName(b)=="Confirm variable definition");Invoke(button);await Pump();}
                CustomCheck(!window.IsVisible,"Wizard did not close: "+((DefinitionEditorViewModel)window.DataContext).Error);done.SetResult();
            }
            catch(Exception e){window?.Close();done.SetException(e);}
        });
        await Until(()=>c.Button(manage?"Manage definition":"Add Variable").IsEnabled);
        Invoke(c.Button(manage?"Manage definition":"Add Variable"));await done.Task;await Until(()=>c.Vm.Editor.CanInteract);await Pump();
    }
    private static async Task FillVariable(SettingDefinitionWindow window,string key="BattlEye.Enabled",string type="Boolean",string value="false",string? section=null)
    {
        await EnterCombo(window,"Variable INI section",section??"ConsoleVariables");
        await Enter(window,"Variable exact key",key);Combo(window,"Variable value type").SetCurrentValue(Selector.SelectedItemProperty,type);await Pump();
        if(type=="Boolean")Combo(window,"Variable Boolean value").SetCurrentValue(Selector.SelectedItemProperty,value);
        else if(type=="Enumerated choice"){await Enter(window,"Variable allowed choices","A|B");await Pump();Combo(window,"Variable choice value").SetCurrentValue(Selector.SelectedItemProperty,value);}
        else await Enter(window,"Variable initial value",value);
        await EnterCombo(window,"Variable visual category","Security / Anti-Cheat");
        await Enter(window,"Variable description","User/community supplied behavior; not independently verified.");
    }
    public static void RegisterCustomConfiguration(Action<string,Func<Task>> add,string root)
    {
        add("Native custom config: BattlEye add preview cancel exact save and reopen",async()=>
        {
            using var c=await Context.Create(root,"custom-battleye");await c.Load();await c.SelectFile("UserEngine.ini");var original=c.Vm.Editor.RawText;
            await DefinitionDialog(c,false,async(w,m)=>{await FillVariable(w);CustomCheck(m.FriendlyName=="BattlEye Enabled"&&m.Preview=="[ConsoleVariables]\nBattlEye.Enabled=false"&&m.SectionExplanation.StartsWith("Existing"),"Exact identity preview and generated name required.");CustomCheck(!c.Vm.Editor.CanInteract&&!c.Vm.IsBusy,"Modal metadata preparation cannot report a remote operation.");CaptureWindow(w,Path.Combine(c.Evidence,"add-variable.png"));});
            CustomCheck(c.Vm.Editor.IsDirty&&c.Gateway.Saves.Count==0,"Wizard only creates a draft.");var proposed=new IniDocument(original);proposed.Set("ConsoleVariables","BattlEye.Enabled","false");CustomCheck(c.Vm.Editor.RawText==proposed.ToString());var row=c.Vm.Editor.Settings.Single(s=>s.Definition.Key=="BattlEye.Enabled");CustomCheck(row.Source=="Custom"&&row.Definition.Category=="Security / Anti-Cheat");await c.Capture("battleye-draft.png");
            c.Ui.ReviewAnswers.Enqueue(false);await c.Save();CustomCheck(c.Gateway.Saves.Count==0&&c.Vm.Editor.IsDirty);await c.Save();CustomCheck(c.Gateway.Saves.Single().Payload.SequenceEqual(Encoding.UTF8.GetBytes(proposed.ToString()))&&!c.Vm.Editor.IsDirty);await c.Load();CustomCheck(c.Vm.Editor.Settings.Single(s=>s.Definition.Key=="BattlEye.Enabled").Source=="Custom"&&!c.Vm.Editor.IsDirty);
        });
        add("Native custom config: wizard cancel preserves document identity and all bytes",async()=>
        {
            using var c=await Context.Create(root,"custom-cancel");await c.Load();var text=c.Vm.Editor.RawText;var id=c.Vm.Editor.DocumentId;
            await DefinitionDialog(c,false,async(w,_)=>await FillVariable(w),true);CustomCheck(c.Vm.Editor.RawText==text&&c.Vm.Editor.DocumentId==id&&!c.Vm.Editor.IsDirty&&c.Gateway.Saves.Count==0);
        });
        add("Native custom config: duplicate wizard directs user to existing metadata",async()=>
        {
            using var c=await Context.Create(root,"custom-duplicate");await c.Load();await c.SelectFile("UserEngine.ini");var text=c.Vm.Editor.RawText;
            await DefinitionDialog(c,false,async(w,m)=>{await FillVariable(w,"FixtureUnknown","String","new");CustomCheck(!m.CanAccept&&!Descendants(w).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Confirm variable definition").IsEnabled);CustomCheck(m.Error.Contains("already exists")&&c.Vm.Editor.RawText==text);},true);CustomCheck(!c.Vm.Editor.IsDirty);
        });
        add("Native custom config: new section requires explicit choice and precise insertion",async()=>
        {
            using var c=await Context.Create(root,"custom-section");await c.Load();await c.SelectFile("UserEngine.ini");var before=c.Vm.Editor.RawText;
            await DefinitionDialog(c,false,async(w,m)=>{await FillVariable(w,"Custom.Value","String","Exact.Case","Community.NewSection");CustomCheck(!m.CanAccept&&m.Error.Contains("explicitly allow"));var box=Descendants(w).OfType<CheckBox>().Single(b=>(b.Content as string)=="Advanced: allow creating a new INI section");((IToggleProvider)new CheckBoxAutomationPeer(box).GetPattern(PatternInterface.Toggle)).Toggle();await Pump();});
            var expected=new IniDocument(before);expected.Set("Community.NewSection","Custom.Value","Exact.Case");CustomCheck(c.Vm.Editor.RawText==expected.ToString()&&c.Gateway.Saves.Count==0);
        });
        add("Native custom config: raw discovery mode switching exact proposal and category search",async()=>
        {
            using var c=await Context.Create(root,"custom-discovery");await c.Load();await c.SelectFile("UserEngine.ini");await c.Mode(1);var text=c.Vm.Editor.RawText+"\n[Community]\nRaw.Thing=001.2300\n";c.Raw.Focus();c.Raw.Text=text;await Pump();await c.Mode(0);var row=c.Vm.Editor.Settings.Single(s=>s.Definition.Key=="Raw.Thing");CustomCheck(row.Source=="Discovered"&&row.Current=="001.2300");c.Vm.Editor.SelectedSetting=row;await Pump();CustomCheck(c.Vm.Editor.SettingValue=="001.2300"&&!c.Vm.Editor.HasSlider);c.Vm.Editor.Search="Community";await Pump();CustomCheck(c.Vm.Editor.SettingsView.Cast<SettingRow>().Any(s=>s.Definition.Key=="Raw.Thing")&&c.Vm.Editor.RawText==text);c.Vm.Editor.Search="";
        });
        add("Native custom config: metadata-only name category bounds and removal preserve bytes",async()=>
        {
            using var c=await Context.Create(root,"custom-metadata");await c.Load();await c.SelectFile("UserEngine.ini");c.Vm.Editor.SelectedSetting=c.Vm.Editor.Settings.Single(s=>s.Definition.Key=="FixtureUnknown");var before=c.Vm.Editor.RawText;var id=c.Vm.Editor.DocumentId;
            await DefinitionDialog(c,true,async(w,_)=>{await Enter(w,"Variable friendly name","My readable name");await EnterCombo(w,"Variable visual category","My category");});
            CustomCheck(c.Vm.Editor.RawText==before&&c.Vm.Editor.DocumentId==id&&!c.Vm.Editor.IsDirty&&c.Vm.Editor.SelectedSetting?.Definition.Name=="My readable name"&&c.Vm.Editor.Categories.Any(o=>o.Name=="My category"),"Metadata: "+c.Vm.Editor.SelectedSetting?.Definition.Name+" category="+c.Vm.Editor.SelectedSetting?.Definition.Category+" dirty="+c.Vm.Editor.IsDirty+" same="+(c.Vm.Editor.RawText==before));
            Invoke(c.Button("Remove Definition"));await Until(()=>c.Vm.Editor.CanInteract&&c.Vm.Editor.SelectedSetting?.Source=="Discovered");CustomCheck(c.Vm.Editor.RawText==before&&!c.Vm.Editor.IsDirty&&c.Vm.Editor.SelectedSetting?.Source=="Discovered","Removal: "+c.Vm.Editor.Validation+" source="+c.Vm.Editor.SelectedSetting?.Source+" dirty="+c.Vm.Editor.IsDirty);
        });
        add("Native custom config: user numeric metadata enables slider without rounding exact values",async()=>
        {
            using var c=await Context.Create(root,"custom-numeric");await c.Load();await c.SelectFile("UserEngine.ini");await DefinitionDialog(c,false,async(w,_)=>{await FillVariable(w,"Community.Number","Decimal number","0.123456789012345");await Enter(w,"Variable minimum","0");await Enter(w,"Variable maximum","1");await Enter(w,"Variable step","0.01");});CustomCheck(c.Vm.Editor.HasSlider&&c.Vm.Editor.SettingValue=="0.123456789012345"&&c.Vm.Editor.SliderIncrement==.01);var text=c.Vm.Editor.RawText;await c.Mode(1);await c.Mode(0);CustomCheck(c.Vm.Editor.RawText==text);
        });
        add("Native custom config: raw-only duplicate collection and secrets cannot enable Add",async()=>
        {
            using var c=await Context.Create(root,"custom-raw-only");await c.Load();await c.SelectFile("UserEngine.ini");c.Vm.Editor.EditRawText(c.Vm.Editor.RawText+"\n[Complex]\n+Array=One\n+Array=Two\nDuplicate=1\nDuplicate=2\nSecretToken=fixture-only\n");
            foreach(var key in new[]{"+Array","Duplicate","SecretToken"}){c.Vm.Editor.SelectedSetting=c.Vm.Editor.Settings.Single(s=>s.Definition.Key==key);await Pump();CustomCheck(c.Vm.Editor.SelectedSetting.RawOnly&&!c.Vm.Editor.CanAddSetting&&c.Vm.Editor.SettingDetails.Contains("RAW ONLY"));}
            CustomCheck(c.Vm.Editor.SelectedSetting!.DisplayCurrent=="••••••"&&!c.Vm.Editor.ValueComparison.Contains("fixture-only")&&c.Vm.Editor.SettingValue=="");await c.Capture("raw-only.png");
        });
        add("Native custom config: local definitions and categories survive editor restart",async()=>
        {
            using var c=await Context.Create(root,"custom-restart");await c.Load();await c.SelectFile("UserEngine.ini");c.Vm.Editor.SelectedSetting=c.Vm.Editor.Settings.Single(s=>s.Definition.Key=="FixtureUnknown");await DefinitionDialog(c,true,async(w,_)=>await Enter(w,"Variable friendly name","Restart label"));
            var stores=new LocalStores(Path.Combine(c.Evidence,"data"));using var shell=new ShellViewModel(c.Ui,stores,true,_=>c.Gateway);shell.Editor.LoadCommand.Execute(null);await Until(()=>shell.Editor.Files.Count==3&&shell.Editor.CanInteract);shell.Editor.RequestFileSelection(shell.Editor.Files.Single(f=>f.Name=="UserEngine.ini"));CustomCheck(shell.Editor.Settings.Single(s=>s.Definition.Key=="FixtureUnknown").Definition.Name=="Restart label"&&!shell.Editor.IsDirty);
        });
        add("Native custom config: splitter resizing persists width without dirtying draft",async()=>
        {
            using var c=await Context.Create(root,"custom-splitter");await c.Load();var view=Descendants(c.Window).OfType<ConfigurationView>().Single();var grid=(Grid)view.FindName("VisualSplit");var splitter=Descendants(view).OfType<GridSplitter>().Single();grid.ColumnDefinitions[2].Width=new GridLength(380);await Pump();splitter.RaiseEvent(new DragCompletedEventArgs(60,0,false){RoutedEvent=Thumb.DragCompletedEvent});await Pump();CustomCheck(new LocalStores(Path.Combine(c.Evidence,"data")).Load().ConfigurationDetailsWidth==380&&!c.Vm.Editor.IsDirty);await c.Capture("splitter.png");
        });
        foreach(var scale in new[]{1d,1.25,1.5})add("Native custom config: minimum editor and wizard rendering "+scale,async()=>
        {
            using var c=await Context.Create(root,"custom-scale-"+scale);c.Window.WindowState=WindowState.Normal;c.Window.Width=1040;c.Window.Height=700;await c.Load();await c.SelectFile("UserEngine.ini");await Pump();var divider=Descendants(c.Window).OfType<GridSplitter>().Single();CustomCheck(divider.ActualWidth>0&&divider.IsVisible);var bitmap=new RenderTargetBitmap((int)(c.Window.ActualWidth*scale),(int)(c.Window.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(c.Window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=System.IO.File.Create(Path.Combine(c.Evidence,"scaled-editor.png")))encoder.Save(file);
            var view=Descendants(c.Window).OfType<ConfigurationView>().Single();var leftScroll=(ScrollViewer)view.FindName("SettingsPaneScroll");leftScroll.ScrollToBottom();await Pump();var table=Descendants(view).OfType<DataGrid>().Single();var tableBounds=table.TransformToAncestor(leftScroll).TransformBounds(new Rect(table.RenderSize));CustomCheck(table.ActualHeight>=120&&tableBounds.Top<leftScroll.ActualHeight&&tableBounds.Bottom>0,"The table must remain reachable by scrolling at minimum height.");await c.Capture("minimum-scrolled-table.png");
            await DefinitionDialog(c,false,async(w,_)=>{w.Width=640;w.Height=600;await FillVariable(w);w.UpdateLayout();CustomCheck(Descendants(w).OfType<Button>().Where(b=>AutomationProperties.GetName(b)=="Confirm variable definition").All(b=>b.IsVisible));Field(w,"Variable exact key").Focus();CustomCheck(Field(w,"Variable exact key").IsKeyboardFocusWithin);Field(w,"Variable exact key").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));CustomCheck(Keyboard.FocusedElement!=null);CaptureWindow(w,Path.Combine(c.Evidence,"wizard-minimum.png"));},true);
        });
        add("Native custom config: metadata modal freezes refresh switching and raw edits",async()=>
        {
            using var c=await Context.Create(root,"custom-races");await c.Load();await c.SelectFile("UserEngine.ini");var text=c.Vm.Editor.RawText;var id=c.Vm.Editor.DocumentId;
            await DefinitionDialog(c,false,async(_,_)=>{var count=c.Gateway.RefreshCalls;await c.Vm.RefreshAsync(true);c.Vm.Editor.EditRawText("");CustomCheck(!c.Vm.Editor.RequestFileSelection(c.Vm.Editor.Files.First())&&c.Vm.Editor.RawText==text&&c.Vm.Editor.DocumentId==id&&count==c.Gateway.RefreshCalls);},true);
        });
        add("Native custom config: friendly metadata and section search cannot edit INI",async()=>
        {
            using var c=await Context.Create(root,"custom-search");await c.Load();await c.SelectFile("UserEngine.ini");var text=c.Vm.Editor.RawText;foreach(var query in new[]{"ConsoleVariables","FixtureUnknown","Fixture","Uncategorized"}){c.Vm.Editor.Search=query;await Pump();CustomCheck(c.Vm.Editor.SettingsView.Cast<SettingRow>().Any()&&c.Vm.Editor.RawText==text&&!c.Vm.Editor.IsDirty);}c.Vm.Editor.Search="";
        });
        add("Native custom config: metadata import merge cancellation export and malformed rejection",async()=>
        {
            using var c=await Context.Create(root,"custom-import");await c.Load();await c.SelectFile("UserEngine.ini");var before=c.Vm.Editor.RawText;var file=Path.Combine(c.Evidence,"incoming.json");var definition=CustomConfigurationRegression.Definition("FixtureUnknown") with{Kind="string",FriendlyName="Imported name"};CustomSettingStore.Export(file,[definition]);c.Ui.ChosenFile=file;
            c.Ui.ReviewAnswers.Enqueue(false);Invoke(c.Button("Import definitions"));await Until(()=>c.Ui.Events.Any(e=>e.Title=="Merge custom definitions")&&c.Vm.Editor.CanInteract);CustomCheck(c.Vm.Editor.RawText==before&&c.Vm.Editor.Settings.Single(r=>r.Definition.Key=="FixtureUnknown").Source=="Discovered");
            Invoke(c.Button("Import definitions"));await Until(()=>c.Vm.Editor.CanInteract&&c.Vm.Editor.Settings.Single(r=>r.Definition.Key=="FixtureUnknown").Source=="Custom");CustomCheck(c.Vm.Editor.Settings.Single(r=>r.Definition.Key=="FixtureUnknown").Definition.Name=="Imported name"&&!c.Vm.Editor.IsDirty);c.Ui.ChosenFolder=c.Evidence;Invoke(c.Button("Export definitions"));await Pump();var export=Directory.GetFiles(c.Evidence,"Arrakis-custom-definitions-*.json").Single();CustomCheck(CustomSettingStore.Import(export).Count==1);
            System.IO.File.WriteAllText(file,"{malformed");Invoke(c.Button("Import definitions"));await Pump();CustomCheck(c.Vm.Editor.Validation.Contains("Import refused")&&c.Vm.Editor.RawText==before&&c.Vm.Editor.Settings.Single(r=>r.Definition.Key=="FixtureUnknown").Definition.Name=="Imported name");
        });
        add("Native custom config: custom draft dirty switch cancel and discard preserve metadata",async()=>
        {
            using var c=await Context.Create(root,"custom-dirty-switch");await c.Load();await c.SelectFile("UserEngine.ini");await DefinitionDialog(c,false,async(w,_)=>await FillVariable(w));var draft=c.Vm.Editor.RawText;c.Ui.ConfirmAnswers.Enqueue(false);await c.SelectFile("UserGame.ini");CustomCheck(c.Vm.Editor.RawText==draft&&c.Vm.Editor.IsDirty);c.Ui.ConfirmAnswers.Enqueue(true);await c.SelectFile("UserGame.ini");await c.SelectFile("UserEngine.ini");CustomCheck(!c.Vm.Editor.IsDirty&&c.Vm.Editor.Settings.Single(s=>s.Definition.Key=="BattlEye.Enabled") is {IsPresent:false,Source:"Custom"}&&c.Gateway.Saves.Count==0);
        });
        foreach(var (type,value) in new[]{("Integer","12"),("Enumerated choice","B")})add("Native custom config: typed wizard "+type,async()=>
        {
            using var c=await Context.Create(root,"custom-type-"+type);await c.Load();await c.SelectFile("UserEngine.ini");await DefinitionDialog(c,false,async(w,_)=>await FillVariable(w,"Community.Typed",type,value));CustomCheck(new IniDocument(c.Vm.Editor.RawText).Get("ConsoleVariables","Community.Typed")==value&&c.Gateway.Saves.Count==0);
        });
        add("Native custom config: Demo examples reconcile built-in metadata without INI edits",async()=>
        {
            using var c=await Context.Create(root,"custom-demo-examples");await c.Load();await c.SelectFile("UserEngine.ini");var text=c.Vm.Editor.RawText;Invoke(c.Button("Demo examples"));await Until(()=>c.Vm.Editor.CanInteract&&c.Vm.Editor.Validation.Contains("Demo metadata examples"));var row=c.Vm.Editor.Settings.Single(r=>r.Definition.Key=="Dune.GlobalMiningOutputMultiplier");CustomCheck(row.Source=="Custom"&&row.Definition.BuiltInDetails.Length>0&&c.Vm.Editor.RawText==text&&!c.Vm.Editor.IsDirty);var live=new CustomSettingStore(Path.Combine(c.Evidence,"data"),false);CustomCheck(live.Load().Count==0);
        });
    }
}
