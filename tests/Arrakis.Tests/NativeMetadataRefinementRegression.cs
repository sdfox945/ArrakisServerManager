using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Arrakis.Core;
using Arrakis.Desktop;
using Arrakis.Desktop.ViewModels;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static partial class NativeConfigurationRegression
{
    private static async Task LoadBattlEye(Context c)
    {
        var file=c.Gateway.Current("UserEngine.ini");var ini=new IniDocument(file.Text);ini.Set("ConsoleVariables","BattlEye.Enabled","false");c.Gateway.SeedConfiguration(file.Name,ini.ToString());await c.Load();await c.SelectFile(file.Name);await SelectSetting(c,"BattlEye.Enabled");
    }
    public static void RegisterMetadataRefinement(Action<string,Func<Task>> add,string root)
    {
        add("Native metadata refinement: discovered BattlEye User Default is independent through exact review cancel and reopen",async()=>
        {
            using var c=await Context.Create(root,"metadata-battleye");await LoadBattlEye(c);var original=c.Vm.Editor.RawText;var id=c.Vm.Editor.DocumentId;var saved=c.Vm.Editor.CurrentOriginal!.Bytes;
            await DefinitionDialog(c,true,async(w,m)=>
            {
                CustomCheck(m.Kind=="String"&&m.MetadataOnly,"Discovery must not guess Boolean.");await Enter(w,"Variable friendly name","BattlEye Enabled");await EnterCombo(w,"Variable visual category","Security / Anti-Cheat");Combo(w,"Variable value type").SetCurrentValue(Selector.SelectedItemProperty,"Boolean");await Enter(w,"Variable example","");await Enter(w,"Variable default","True");
                CustomCheck(m.CanAccept&&m.ExampleError==""&&m.DefaultError==""&&m.Purpose=="Editing this definition does not change the server's INI.");CustomCheck(!Descendants(w).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)=="Variable initial value"&&t.IsVisible));CaptureWindow(w,Path.Combine(c.Evidence,"manage-user-default.png"));
            });
            var editor=c.Vm.Editor;CustomCheck(editor.RawText==original&&!editor.IsDirty&&editor.DocumentId==id&&editor.CurrentOriginal!.Bytes.SequenceEqual(saved));CustomCheck(editor.SelectedSetting!.Saved=="false"&&editor.SelectedSetting.Current=="false"&&editor.HasUserDefault&&!editor.HasVerifiedDefault&&editor.ValueComparison.Contains("User Default: True")&&editor.ValueComparison.Contains("Verified Engine Default: Unknown"));
            Invoke(c.Button("Reset User Default"));await Pump();CustomCheck(editor.SettingValue=="True"&&editor.SelectedSetting.Current=="false"&&editor.RawText==original&&!editor.IsDirty&&c.Gateway.Saves.Count==0);await c.Capture("reset-proposal-only.png");
            Invoke(c.Button("Add to draft"));await Pump();CustomCheck(editor.RawText==original.Replace("BattlEye.Enabled=false","BattlEye.Enabled=True"));c.Ui.ReviewAnswers.Enqueue(false);await c.Save();var diff=c.Ui.Events.Last(e=>e.Kind=="review").Diff;CustomCheck(diff=="- BattlEye.Enabled=false"+Environment.NewLine+"+ BattlEye.Enabled=True"&&c.Gateway.Saves.Count==0&&c.Gateway.Current("UserEngine.ini").Bytes.SequenceEqual(saved));
            await DefinitionDialog(c,true,async(w,m)=>{CustomCheck(m.Default=="True"&&m.Example==""&&m.Kind=="Boolean");await Pump();},true);
            var stores=new LocalStores(Path.Combine(c.Evidence,"data"));using var reopened=new ShellViewModel(c.Ui,stores,true,_=>c.Gateway);reopened.Editor.LoadCommand.Execute(null);await Until(()=>reopened.Editor.CanInteract&&reopened.Editor.Files.Count==3);reopened.Editor.RequestFileSelection(reopened.Editor.Files.Single(f=>f.Name=="UserEngine.ini"));var row=reopened.Editor.Settings.Single(s=>s.Definition.Key=="BattlEye.Enabled");CustomCheck(row.Definition.Default=="True"&&row.Current=="false"&&!reopened.Editor.IsDirty);
        });
        add("Native metadata refinement: independent labels optional blanks and locked identity",async()=>
        {
            using var c=await Context.Create(root,"metadata-labels");await LoadBattlEye(c);var text=c.Vm.Editor.RawText;
            await DefinitionDialog(c,true,async(w,m)=>
            {
                Combo(w,"Variable value type").SelectedItem="Boolean";await Enter(w,"Variable example","");await Enter(w,"Variable default","");var labels=Descendants(w).OfType<TextBlock>().Select(t=>t.Text).ToArray();CustomCheck(labels.Contains("Example Value (Optional)")&&labels.Contains("User-Defined Default (Optional)")&&labels.Contains("An example of a valid value. Used for reference only.")&&labels.Contains("Your preferred default value. This does not establish the game's actual default."));
                var key=m.Key;var section=m.Section;m.Key="Other";m.Section="Other";CustomCheck(m.Key==key&&m.Section==section&&m.CanAccept);CustomCheck(!Descendants(w).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)=="Variable exact key"&&t.IsVisible));
            });
            CustomCheck(c.Vm.Editor.RawText==text&&!c.Vm.Editor.IsDirty&&!c.Vm.Editor.HasUserDefault&&!c.Vm.Editor.ResetUserDefaultCommand.CanExecute(null));
        });
        add("Native metadata refinement: invalid annotations disable save and focus the actual field",async()=>
        {
            using var c=await Context.Create(root,"metadata-field-errors");await LoadBattlEye(c);var text=c.Vm.Editor.RawText;
            await DefinitionDialog(c,true,async(w,m)=>
            {
                Combo(w,"Variable value type").SelectedItem="Boolean";await Enter(w,"Variable example","False");await Enter(w,"Variable default","yes");CustomCheck(m.DefaultError=="User Default must be a valid Boolean (True or False)."&&m.Example=="False"&&!m.CanAccept);var confirm=Descendants(w).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Confirm variable definition");CustomCheck(!confirm.IsEnabled);Invoke(Descendants(w).OfType<Button>().Single(b=>b.Content as string=="Go to first issue"));await Pump();CustomCheck(Field(w,"Variable default").IsKeyboardFocusWithin);CaptureWindow(w,Path.Combine(c.Evidence,"field-validation.png"));await Enter(w,"Variable default","True");CustomCheck(m.CanAccept&&m.Example=="False");
            },true);CustomCheck(c.Vm.Editor.RawText==text&&!c.Vm.Editor.IsDirty&&c.Vm.Editor.SelectedSetting!.Source=="Discovered");
        });
        add("Native metadata refinement: Boolean enum switching retains buffers and excludes irrelevant choices",async()=>
        {
            using var c=await Context.Create(root,"metadata-types");await LoadBattlEye(c);
            await DefinitionDialog(c,true,async(w,m)=>
            {
                Combo(w,"Variable value type").SelectedItem="Enumerated choice";await Enter(w,"Variable allowed choices","True||False");CustomCheck(!m.CanAccept&&m.ChoicesError.Contains("empty"));await Enter(w,"Variable allowed choices","True|true");CustomCheck(m.ChoicesError.Contains("duplicates"));await Enter(w,"Variable allowed choices","True|False");await Enter(w,"Variable example","False");await Enter(w,"Variable default","True");CustomCheck(m.CanAccept);
                Combo(w,"Variable value type").SelectedItem="Boolean";await Pump();CustomCheck(m.CanAccept&&m.Choices=="True|False"&&m.InactiveMetadataNotice.Contains("excluded")&&!Descendants(w).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)=="Variable allowed choices"&&t.IsVisible));Combo(w,"Variable value type").SelectedItem="Enumerated choice";await Pump();CustomCheck(m.Choices=="True|False"&&m.CanAccept);Combo(w,"Variable value type").SelectedItem="Boolean";await Pump();
            });
            CustomCheck(c.Vm.Editor.SelectedSetting!.Definition.Kind=="bool"&&c.Vm.Editor.SelectedSetting.Definition.Choices==null&&c.Vm.Editor.RawText.Contains("BattlEye.Enabled=false")&&!c.Vm.Editor.IsDirty);
        });
        add("Native metadata refinement: numeric to string excludes stale bounds without discarding annotations",async()=>
        {
            using var c=await Context.Create(root,"metadata-numeric");await LoadBattlEye(c);var text=c.Vm.Editor.RawText;
            await DefinitionDialog(c,true,async(w,m)=>
            {
                Combo(w,"Variable value type").SelectedItem="Decimal number";await Enter(w,"Variable minimum","2");await Enter(w,"Variable maximum","1");CustomCheck(m.MinimumError=="Minimum must not exceed Maximum.");await Enter(w,"Variable maximum","5");await Enter(w,"Variable step","0");CustomCheck(m.StepError=="Slider Step must be greater than zero.");await Enter(w,"Variable step","0.01");await Enter(w,"Variable default","8");CustomCheck(m.DefaultError.Contains("outside"));await Enter(w,"Variable default","3.25");await Enter(w,"Variable example","2.75");CustomCheck(m.CanAccept);
                Combo(w,"Variable value type").SelectedItem="String";await Pump();CustomCheck(m.CanAccept&&m.Minimum=="2"&&m.Step=="0.01"&&m.Default=="3.25"&&!Descendants(w).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)=="Variable minimum"&&t.IsVisible));
            });
            CustomCheck(c.Vm.Editor.RawText==text&&!c.Vm.Editor.IsDirty&&c.Vm.Editor.SelectedSetting!.Definition.Min==null&&c.Vm.Editor.SelectedSetting.Definition.Step==null&&c.Vm.Editor.SelectedSetting.Definition.Default=="3.25");
        });
        add("Native metadata refinement: explicit clear removes incompatible fields and preserves valid data",async()=>
        {
            using var c=await Context.Create(root,"metadata-clear");await LoadBattlEye(c);
            await DefinitionDialog(c,true,async(w,m)=>
            {
                Combo(w,"Variable value type").SelectedItem="Decimal number";await Enter(w,"Variable minimum","0");await Enter(w,"Variable maximum","2");await Enter(w,"Variable example","0.5");await Enter(w,"Variable default","1.5");await Enter(w,"Variable description","Retain this valid description");Combo(w,"Variable value type").SelectedItem="Boolean";await Pump();CustomCheck(!m.CanAccept&&m.Example=="0.5"&&m.Default=="1.5");Invoke(Descendants(w).OfType<Button>().Single(b=>b.Content as string=="Clear incompatible metadata"));await Pump();CustomCheck(m.Example==""&&m.Default==""&&m.Minimum==""&&m.Maximum==""&&m.Description=="Retain this valid description"&&m.CanAccept);
            },true);
        });
        add("Native metadata refinement: user and verified defaults have separate explicit proposal commands",async()=>
        {
            using var c=await Context.Create(root,"metadata-engine-default");await LoadBattlEye(c);var editor=c.Vm.Editor;var text=editor.RawText;var d=CustomConfigurationRegression.Definition() with{Default="True",Example="false"};var engine=new SettingDefinition(d.File,d.Section,d.Key,"Synthetic fixture","Verified default fixture","true","bool",Default:"False");editor.SelectedSetting=new SettingRow(d.ToDefinition(engine),"false");await Pump();CustomCheck(editor.HasUserDefault&&editor.HasVerifiedDefault&&editor.ValueComparison.Contains("User Default: True")&&editor.ValueComparison.Contains("Verified Engine Default: False"));editor.ResetUserDefaultCommand.Execute(null);CustomCheck(editor.SettingValue=="True"&&editor.RawText==text);editor.ResetCommand.Execute(null);CustomCheck(editor.SettingValue=="False"&&editor.RawText==text&&!editor.IsDirty);
        });
        foreach(var scale in new[]{1d,1.25,1.5})add("Native metadata refinement: keyboard resize and annotation rendering "+scale,async()=>
        {
            using var c=await Context.Create(root,"metadata-scale-"+scale);await LoadBattlEye(c);
            await DefinitionDialog(c,true,async(w,m)=>
            {
                w.Width=640;w.Height=600;Combo(w,"Variable value type").SelectedItem="Boolean";await Enter(w,"Variable default","True");await Enter(w,"Variable example","");w.UpdateLayout();var button=Descendants(w).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Confirm variable definition");var bounds=button.TransformToAncestor(w).TransformBounds(new Rect(button.RenderSize));CustomCheck(bounds.Bottom<=w.ActualHeight&&bounds.Right<=w.ActualWidth&&button.IsEnabled);Field(w,"Variable default").Focus();Field(w,"Variable default").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));CustomCheck(Keyboard.FocusedElement!=null);var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w.ActualWidth*scale),(int)(w.ActualHeight*scale),96*scale,96*scale,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(w);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=System.IO.File.Create(Path.Combine(c.Evidence,"scaled-annotations.png"));encoder.Save(file);
            },true);
        });
    }
}
