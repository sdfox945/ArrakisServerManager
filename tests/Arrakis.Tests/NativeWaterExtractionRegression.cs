using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Arrakis.Core;

namespace Arrakis.Tests;

internal static partial class NativeConfigurationRegression
{
    public static void RegisterWaterExtraction(Action<string,Func<Task>> add,string root)
    {
        add("Native v1.1.2: water details and rendered slider endpoints show the observed speed direction",async()=>
        {
            using var c=await Context.Create(root,"v112-water-guidance");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");var original=c.Vm.Editor.RawText;await SelectSetting(c,"WaterExtractionRate");
            var editor=c.Vm.Editor;var details=Descendants(c.Window).OfType<TextBlock>().Single(t=>t.Text==editor.SettingDetails);var low=Descendants(c.Window).OfType<TextBlock>().Single(t=>t.Text==editor.SliderMinimumLabel);var high=Descendants(c.Window).OfType<TextBlock>().Single(t=>t.Text==editor.SliderMaximumLabel);
            Check(details.Text.Contains("Higher values make water extraction faster; lower values make it slower.")&&details.Text.Contains("not independently verified"),"Native setting details must show corrected direction and evidence limits.");
            Check(low.Text=="0.1 · Slower"&&high.Text=="10 · Faster"&&low.IsVisible&&high.IsVisible,"Actual WPF endpoint bindings must show lower/slower and higher/faster.");
            Check(Slider(c).Minimum==.1&&Slider(c).Maximum==10&&editor.SettingValue=="5.000000"&&!c.Button("Add to draft").IsEnabled,"Metadata correction cannot alter bounds, loaded value or no-op state.");
            details.BringIntoView();await Pump();await c.Capture("water-description.png");low.BringIntoView();await Pump();await c.Capture("water-slider-labels.png");
            var peer=(IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(Slider(c))!.GetPattern(PatternInterface.RangeValue);peer.SetValue(.1);await Pump();Check(editor.SettingValue=="0.1"&&editor.RawText==original,"Lower endpoint chooses only a proposed value.");peer.SetValue(10);await Pump();Check(editor.SettingValue=="10"&&editor.RawText==original&&!editor.IsDirty,"Higher endpoint cannot auto-apply a change.");
            await c.Mode(1);Check(c.Raw.Text==original,"Raw mode must retain the authoritative original bytes.");await c.Mode(0);
            foreach(var (key,lower,upper) in new[]{("LootRespawnSpeed","Faster","Slower"),("ResourceRespawnSpeed","Slower","Faster"),("NPCRespawnMultiplier","Slower","Faster"),("PlayerStaminaDrain","Less drain","More drain"),("CraftingTimeMultiplier","Minimum","Maximum")})
            {
                await SelectSetting(c,key);Check(editor.SliderMinimumLabel.EndsWith(" · "+lower)&&editor.SliderMaximumLabel.EndsWith(" · "+upper),"Unrelated effect labels must remain unchanged: "+key);
            }
            Check(editor.RawText==original&&!editor.IsDirty&&c.Gateway.Saves.Count==0&&c.Ui.SwitchPrompts==0,"Setting selection, endpoints and mode changes cannot mutate or switch the document.");
        });
        add("Native v1.1.2: exact water edit keeps review cancellation and frozen save payload safeguards",async()=>
        {
            using var c=await Context.Create(root,"v112-water-save");await c.Load();await c.SelectFile("UserServerCustomSettings.ini");await SelectSetting(c,"WaterExtractionRate");var original=c.Vm.Editor.RawText;var proposed=original.Replace("WaterExtractionRate=5.000000","WaterExtractionRate=6.250000");
            Input(c).Focus();Input(c).SetCurrentValue(TextBox.TextProperty,"6.250000");await Until(()=>c.Button("Add to draft").IsEnabled);Check(c.Vm.Editor.RawText==original,"Typing must not change the draft.");Invoke(c.Button("Add to draft"));await Pump();Check(c.Vm.Editor.RawText==proposed,"A water edit must change exactly the selected value.");await c.Mode(1);Check(c.Raw.Text==proposed,"Raw editor must match exact typed precision.");await c.Mode(0);
            c.Ui.ReviewAnswers.Enqueue(false);await c.Save();Check(c.Gateway.Saves.Count==0&&c.Vm.Editor.RawText==proposed&&c.Vm.Editor.IsDirty,"Review cancellation cannot send a write or discard the draft.");
            await c.Save();Check(c.Gateway.Saves.Count==1&&c.Gateway.Saves[0].Payload.SequenceEqual(c.Gateway.Saves[0].Original.Encode(proposed)),"The simulated save must receive precisely the approved bytes.");
            Check(c.Ui.Events.Where(e=>e.Kind=="review").All(e=>e.Diff==ConfigurationIntegrity.Diff(original,proposed))&&c.Ui.SwitchPrompts==0,"Water review must contain only the intended diff, without file switching.");await c.Load();Check(c.Vm.Editor.RawText==proposed&&!c.Vm.Editor.IsDirty,"Reopening the simulated save must retain every other byte.");await c.Capture("water-saved.png");
            File.WriteAllText(Path.Combine(c.Evidence,"payload-verification.txt"),"One WaterExtractionRate value changed from 5.000000 to 6.250000; exact reviewed bytes matched the simulated payload. All unrelated bytes retained. No production connection.");
        });
    }
}
