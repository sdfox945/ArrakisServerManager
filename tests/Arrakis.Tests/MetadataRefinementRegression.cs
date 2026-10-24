using System.IO;
using System.Text.Json;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static class MetadataRefinementRegression
{
    private static void Check(bool value,string message="Metadata refinement assertion failed"){if(!value)throw new InvalidOperationException(message);}
    private static void Reject(CustomSettingDefinition definition,string message){try{definition.Validate();}catch(InvalidOperationException e){Check(e.Message.Contains(message),e.Message);return;}throw new Exception("Expected field-specific validation failure.");}
    public static void Register(Action<string,Func<Task>> add,string root)
    {
        void Test(string name,Action action)=>add("Metadata refinement: "+name,()=>{action();return Task.CompletedTask;});
        Test("blank optional annotations remain unspecified without a migration",()=>
        {
            var d=CustomConfigurationRegression.Definition() with{Example=" ",Default=""};d.Validate();(d with{Key="ApiToken"}).Validate();var resolved=d.ToDefinition();Check(resolved.Default==null&&resolved.TemplateValue==""&&resolved.VerifiedEngineDefault==null);var parsed=CustomSettingStore.Parse(CustomSettingStore.Serialize([d]));Check(parsed.SchemaVersion==1&&parsed.Definitions.Single().Example==" "&&parsed.Definitions.Single().Default=="");
        });
        foreach(var value in new[]{"True","true","TRUE","False","false","fAlSe"})Test("Boolean annotation validation accepts casing "+value,()=>{var d=CustomConfigurationRegression.Definition() with{Example=value,Default=value};d.Validate();Check(d.ToDefinition().Default==value&&d.ToDefinition().TemplateValue==value);});
        Test("Example and User Default identify independent Boolean errors",()=>
        {
            Reject(CustomConfigurationRegression.Definition() with{Example="yes"},"Example Value must be a valid Boolean (True or False).");Reject(CustomConfigurationRegression.Definition() with{Default="yes"},"User Default must be a valid Boolean (True or False).");
        });
        Test("enumeration detects empty duplicate and incompatible annotation values",()=>
        {
            var d=CustomConfigurationRegression.Definition() with{Kind="choice",Choices=["True","False"],Example="false",Default="True"};d.Validate();Reject(d with{Choices=["True",""]},"Allowed Choices cannot be empty");Reject(d with{Choices=["True","true"]},"duplicates");Reject(d with{Default="Other"},"User Default must be one of the configured choices.");Reject(d with{Example="Other"},"Example Value must be one of the configured choices.");
        });
        Test("numeric bounds step and annotations have field-specific validation",()=>
        {
            var d=CustomConfigurationRegression.Definition() with{Kind="number",Minimum=0,Maximum=2};Reject(d with{Minimum=3},"Minimum must not exceed Maximum.");Reject(d with{Step=0},"Slider Step must be greater than zero");Reject(d with{Default="3"},"User Default is outside the configured numeric range.");Reject(d with{Example="NaN"},"Example Value must be a finite number");Reject(d with{Kind="integer",Default="1.5"},"User Default must be a whole number.");
        });
        Test("built-in and user defaults remain separate and examples are not defaults",()=>
        {
            var custom=CustomConfigurationRegression.Definition() with{Default="True",Example="false"};var builtIn=new SettingDefinition(custom.File,custom.Section,custom.Key,"Fixture","Synthetic default fixture","true","bool",Default:"False");var effective=custom.ToDefinition(builtIn);Check(effective.Default=="True"&&effective.VerifiedEngineDefault=="False"&&effective.TemplateValue=="false");var exampleOnly=(custom with{Default=null}).ToDefinition();Check(exampleOnly.Default==null&&exampleOnly.VerifiedEngineDefault==null);
        });
        Test("schema one persistence and export retain both old annotation fields exactly",()=>
        {
            var d=CustomConfigurationRegression.Definition() with{Example="false",Default="True"};var dir=Path.Combine(root,"metadata-refinement-store");var store=new CustomSettingStore(dir,true);store.Load();store.Save([d]);var reopened=new CustomSettingStore(dir,true).Load().Single();Check(reopened.Example=="false"&&reopened.Default=="True");var export=Path.Combine(root,"metadata-refinement-export.json");CustomSettingStore.Export(export,[reopened]);var imported=CustomSettingStore.Import(export).Single();Check(imported.Example==d.Example&&imported.Default==d.Default&&new CustomSettingStore(dir,false).Load().Count==0);
        });
    }
}
