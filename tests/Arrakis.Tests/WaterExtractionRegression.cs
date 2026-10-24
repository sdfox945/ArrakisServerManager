using System.IO;
using System.Text;
using Arrakis.Core;

namespace Arrakis.Tests;

internal static class WaterExtractionRegression
{
    private static void Check(bool value,string message)=>ConfigurationIntegrityRegression.Check(value,message);
    internal static SettingDefinition Setting=>SettingCatalog.All.Single(d=>d.Key=="WaterExtractionRate");
    public static void Register(Action<string,Func<Task>> add)
    {
        void Test(string name,Action work)=>add("v1.1.2: "+name,()=>{work();return Task.CompletedTask;});
        Test("water speed description discloses observed rather than independently verified evidence",()=>
        {
            var d=Setting;
            Check(d.Description=="Water extraction speed multiplier."&&d.Direction=="Higher values make water extraction faster; lower values make it slower.","Water guidance must describe speed in both directions.");
            Check(d.Evidence.Contains("user-reported gameplay observations")&&d.Evidence.Contains("contradicting Funcom's supplied template comment")&&d.Evidence.Contains("not independently verified"),"The contradictory template and limits of observation must be explicit.");
            Check(d.Details.Contains(d.Direction)&&d.Details.Contains(d.Evidence),"Displayed setting details must include both corrected direction and provenance.");
            Check(d.File=="UserServerCustomSettings.ini"&&d.Section=="/Script/DuneSandbox.UserServerCustomSettings"&&d.Kind=="number"&&d.Min==.1&&d.Max==10&&d.TemplateValue=="1.000000"&&d.Default==null,"Identity, numerical bounds, example and unknown default must be preserved.");
            foreach(var value in new[]{"0.1","5.000000","6.250000","10"})Check(d.Validate(value)==null,"Existing in-range values must stay valid.");
            Check(d.Validate("0.09")!=null&&d.Validate("10.1")!=null,"Bounds must not be relaxed by a description correction.");
        });
        Test("representative water fixture corrects its comment without changing its value",()=>
        {
            var text=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","UserServerCustomSettings.representative.ini"));
            Check(text.Contains("; User-reported gameplay (not independently verified): higher = faster extraction; lower = slower, contrary to Funcom's supplied template comment."),"The fixture must not present the old template direction as true.");
            Check(new IniDocument(text).Get(Setting.Section,Setting.Key)=="5.000000","The fixture's user-entered value must not change.");
        });
        Test("water visual edits preserve even contradictory existing server comments and exact typed bytes",()=>
        {
            // Intentional legacy input: a server file may still carry the known-incorrect upstream comment.
            const string original="; Supplied template direction (known incorrect): higher values make extraction take longer.\r\n[/Script/DuneSandbox.UserServerCustomSettings]\r\nDifficultyLevel=Custom\r\nWaterExtractionRate=5.000000\r\nUnknown=keep\r\n";
            var bytes=Encoding.UTF8.GetBytes(original);var file=new ConfigFile(Setting.File,"/fixture/UserSettings/"+Setting.File,bytes,ConfigurationIntegrity.Hash(bytes));var draft=new ConfigurationDraft(file);
            Check(draft.Text==original&&!draft.IsDirty,"Loading corrected metadata cannot rewrite existing INI comments or values.");
            draft.ApplyVisual(Setting,"6.250000");var proposed=original.Replace("WaterExtractionRate=5.000000","WaterExtractionRate=6.250000");var review=draft.PrepareReview(new(file.Name,file.RemotePath));
            Check(draft.Text==proposed&&review.Payload.SequenceEqual(Encoding.UTF8.GetBytes(proposed)),"Only the intended value may change, including in the frozen payload.");
            Check(review.Diff==ConfigurationIntegrity.Diff(original,proposed)&&draft.Matches(review),"Reviewed bytes must match the unchanged integrity contract.");
        });
    }
}
