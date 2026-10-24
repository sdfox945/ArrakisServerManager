using System.Globalization;
using System.Text.RegularExpressions;

namespace Arrakis.Core;

public record SettingDefinition(string File, string Section, string Key, string Category, string Description, string TemplateValue, string Kind = "number", double? Min = null, double? Max = null, string[]? Choices = null, string Direction = "Behavior not documented; verify in game.", bool ClientRequired = false, string Evidence = "Documented in installed Funcom template", string? Default = null, string? FriendlyName=null, string Source="Built-in", string Restart="Restart required", double? Step=null, string Notes="", string BuiltInDetails="",string? BuiltInEngineDefault=null)
{
    public string? VerifiedEngineDefault=>Source=="Built-in"?Default:BuiltInEngineDefault;
    public string Name => FriendlyName??SettingIdentity.Friendly(Key);
    public string Details => $"{Key} · {File} [{Section}]\n{Description}\n{Direction}\nAccepted range: {Min?.ToString(CultureInfo.InvariantCulture) ?? "not verified"} – {Max?.ToString(CultureInfo.InvariantCulture) ?? "not verified"}.\n{Evidence}. "+(Source=="Built-in"?$"Verified Engine Default: {Default ?? "Unknown"}; supplied template: {TemplateValue}.":$"User Default: {Default??"Unknown"}; Example Value: {TemplateValue}.")+$" {Restart}." + (ClientRequired ? " ALSO requires matching client configuration. Client files are never changed by this app." : "")+(Notes.Length>0?"\nPersonal notes: "+Notes:"")+(BuiltInDetails.Length>0?"\n\nBuilt-in documentation (local annotations above take precedence; built-in validation still applies):\n"+BuiltInDetails:"");
    public string? Validate(string value)
    {
        if (value.Any(c => c is '\r' or '\n' or '\0')) return "Value must be a single line.";
        if (Kind == "bool" && !new[] { "True", "False" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "Choose True or False.";
        if (Choices?.Length > 0 && !Choices.Contains(value, StringComparer.OrdinalIgnoreCase)) return "Choose: "+string.Join(", ", Choices);
        if(Kind=="quotedString"&&(value.Length<2||value[0]!='"'||value[^1]!='"'||value.Contains('\'')||value.Contains('|')||value[1..^1].Contains('"')))return "Use double quotes around the value. Apostrophes, pipes and embedded double quotes are not supported by Funcom's template.";
        if (Kind is "number" or "integer")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) return "Enter a finite number using a decimal point.";
            if (Kind == "integer" && n != Math.Truncate(n)) return "Enter a whole number.";
            if (Min is not null && n < Min || Max is not null && n > Max) return $"Accepted range: {Min?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}–{Max?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.";
        }
        return null;
    }
}
public static class SettingCatalog
{
    public static readonly string[] Categories = ["General","Harvesting","Crafting","Combat","Experience and progression","Survival","Water","Fuel consumption","Vehicle durability","Sandworms","Sandstorms","Building","Base backups","Landsraad","Respawning","Advanced / Experimental"];
    public static IReadOnlyList<SettingDefinition> All { get; } = Build();
    private static List<SettingDefinition> Build()
    {
        var list = new List<SettingDefinition>();
        void Custom(string key,string cat,string description,string template="1.000000",double? min=0,double? max=10,string direction="Behavior not documented; verify in game.",string kind="number",string[]? choices=null,string evidence="Documented in installed Funcom template") => list.Add(new("UserServerCustomSettings.ini","/Script/DuneSandbox.UserServerCustomSettings",key,cat,description,template,kind,min,max,choices,direction,Evidence:evidence));
        Custom("DifficultyLevel","General","Custom rules preset.","Custom",null,null,kind:"choice",choices:["Custom"]);
        Custom("CraftingCost","Crafting","Crafting cost multiplier. Zero removes crafting costs.");
        Custom("CraftingTimeMultiplier","Crafting","Crafting/refining duration. Zero is instant.",max:5);
        Custom("WaterExtractionRate","Water","Water extraction speed multiplier.",min:0.1,direction:"Higher values make water extraction faster; lower values make it slower.",evidence:"Effect direction is based on user-reported gameplay observations, contradicting Funcom's supplied template comment; not independently verified");
        Custom("LootRespawnSpeed","Respawning","Loot chest respawn timing.",min:0.1,direction:"Higher values make chests take longer to return.");
        Custom("ResourceRespawnSpeed","Respawning","Resource respawn timing.",min:0.1,direction:"Higher values respawn resources sooner.");
        Custom("BuildingCostMultiplier","Building","Building cost multiplier. Zero removes costs.");
        Custom("FuelBurnTimeMultiplier","Fuel consumption","Fuel burn multiplier. Zero removes fuel burn.");
        Custom("InventoryVolumeMultiplier","Survival","Carrying capacity multiplier.",min:0.1,direction:"Higher values allow carrying more items.");
        foreach(var key in new[]{"PlayerDamageToPlayer","PlayerDamageToNPC","PlayerDamageToVehicle","NPCHealth","NPCDamageToPlayer","NPCDamageToNPC"}) Custom(key,"Combat","Combat balance multiplier.",min:0.1);
        Custom("NPCRespawnMultiplier","Respawning","NPC respawn multiplier.",min:0.1,direction:"Higher values respawn sooner.");
        Custom("PVPDamageStructures","Combat","Player damage to unshielded structures. Zero disables PvP structure damage.");
        foreach(var key in new[]{"GlobalXpMultiplier","CombatXp","GatheringXp","MissionXp","IntelPointsGainMultiplier"}) Custom(key,"Experience and progression","Progression multiplier. Zero disables the corresponding gain.");
        Custom("ItemDurabilityDrainMultiplier","Survival","Durability drain. Zero disables wear.");
        Custom("bEnableItemMaxDurabilityLoss","Survival","Enable loss of maximum durability.","True",null,null,kind:"bool");
        Custom("PlayerStaminaDrain","Survival","Player stamina consumption.",min:0.1,direction:"Higher drains faster; lower lasts longer.");
        foreach(var key in new[]{"PlayerShieldDamageAbsorptionMultiplier","NPCShieldDamageAbsorptionMultiplier"}) Custom(key,"Combat","Shield damage absorption.",min:0.1,direction:"Higher values make shields last longer.");
        Custom("HeatBuildupRate","Survival","Heat buildup. Zero disables it."); Custom("ThirstMultiplier","Water","Thirst. Zero disables it.");
        Custom("DropEquipmentOnDeath","Survival","Equipment retained/dropped on death.","Default",null,null,kind:"choice",choices:["All","Backpack","Default","None"]);
        Custom("bAllowDynamicBuildingDamage","Building","Allow environmental damage, sandstorms and decay.","True",null,null,kind:"bool");
        Custom("SandwormConsequences","Sandworms","Equipment consequences of sandworm encounters.","All",null,null,kind:"choice",choices:["All","Backpack","Default","None"]);
        Custom("PlayerDeathLootRule","Survival","Who may access death loot.","DependsOnSecurityZone",null,null,kind:"choice",choices:["DependsOnSecurityZone","NeverAllowOtherPlayers","AlwaysAllowOtherPlayers"]);
        Custom("bIsBuildingRestrictionsEnabled","Building","Enforce no-build zones. False permits building in normally restricted areas.","True",null,null,kind:"bool");
        foreach(var key in new[]{"LandsraadContributionMultiplier","LandsraadSpecializationXpMultiplier","LandsraadFactionStandingMultiplier"}) Custom(key,"Landsraad","Contribution/progression multiplier. Zero disables the corresponding gain.");
        Custom("FiefdomLimit","Building","Number of bases owned. Complementary to landclaim segment count.","3",0,10,kind:"integer");
        Custom("BuildingPieceLimitMultiplier","Building","Building piece limit multiplier.",min:0.1);
        Custom("bBuildingInfiniteStability","Building","Infinite building stability.","False",null,null,kind:"bool");
        Custom("BaseBackupToolTimeRestriction","Base backups","Hours after construction or last backup during which a sub-fief can be backed up. This is an in-game tool, separate from database backups.","16.000000",0,null);
        void Engine(string key,string cat,string description,string template,string kind="number",double? min=null,double? max=null,string direction="Behavior not documented; verify in game.")=>list.Add(new("UserEngine.ini","ConsoleVariables",key,cat,description,template,kind,min,max,Direction:direction));
        foreach(var key in new[]{"Dune.GlobalMiningOutputMultiplier","Dune.GlobalVehicleMiningOutputMultiplier","SecurityZones.PvpResourceMultiplier"}) Engine(key,"Harvesting","Mining output multiplier.",key.StartsWith("Security")?"2.5":"1.0",min:0.1,max:10,direction:"Higher produces more resources.");
        Engine("Bgd.ServerDisplayName","General","Sietch name applied to all servers. Empty quotes are an example only; enter your own name. No active template/default is verified.","\"\"","quotedString");
        Engine("Bgd.ServerLoginPassword","General","Shared Sietch password. Empty quotes are an example only; enter your own password. No active template/default is verified.","\"\"","quotedString");
        Engine("dw.VehicleDurabilityDamageMultiplier","Vehicle durability","Durability damage. Zero disables it.","1.0",min:0,max:10);
        foreach(var key in new[]{"Sandstorm.Enabled","Sandstorm.Treasure.Enabled","sandworm.dune.Enabled"}) Engine(key,key.StartsWith("Sandstorm.")?"Sandstorms":"Sandworms","Enable feature (1) or disable (0).","1","integer",0,1);
        Engine("Vehicle.SandwormCollisionInteraction","Sandworms","Sandworms can push/damage vehicles.","false","bool");
        Engine("Sandworm.SandwormDangerZonesEnabled","Sandworms","Enable danger zones where worms attack.","true","bool");
        Engine("Vehicle.SandwormInvulnerabilitySecondsOnExit","Sandworms","Invulnerability after exiting a vehicle, in seconds.","900.0",min:0);
        Engine("Vehicle.SandwormInvulnerabilitySecondsOnServerRestart","Sandworms","Invulnerability after server restart, in seconds.","7200.0",min:0);
        foreach(var key in new[]{"Port","IGWPort"})list.Add(new("UserEngine.ini","URL",key,"Advanced / Experimental","Starting sequential port. Player and inter-server ranges must not overlap.",key=="Port"?"7777":"7888","integer",1,65535));
        void Game(string section,string key,string cat,string desc,string template,string kind="number",double? min=null,double? max=null,bool client=false,string direction="Behavior not documented; verify in game.")=>list.Add(new("UserGame.ini",section,key,cat,desc,template,kind,min,max,Direction:direction,ClientRequired:client));
        Game("/Script/DuneSandbox.PvpPveSettings","m_bShouldForceEnablePvpOnAllPartitions","Combat","Force PvP in every partition.","False","bool");
        Game("/Script/DuneSandbox.SecurityZonesSubsystem","m_bAreSecurityZonesEnabled","Combat","Security zones constrain PvP and ability usage.","True","bool");
        Game("/DeteriorationSystem.ItemDeteriorationConstants","UpdateRateInSeconds","Survival","Item deterioration rate. Template describes zero as off.","1.0",min:0,max:10);
        Game("/Script/DuneSandbox.SandStormConfig","m_bCoriolisAutoSpawnEnabled","Sandstorms","Automatically spawn Coriolis storms.","True","bool");
        Game("/Script/DuneSandbox.BuildingSettings","m_MaxNumLandclaimSegments","Building","Maximum landclaim extension segments.","6","integer",0,client:true);
        Game("/Script/DuneSandbox.BuildingSettings","m_BuildingBlueprintMaxExtensions","Building","Landclaim blueprint expansion count.","4","integer",0);
        Game("/Script/DuneSandbox.BuildingSettings","m_BaseBackupMaxExtensions","Base backups","Base backup expansion count.","8","integer",0);
        Game("/Script/DuneSandbox.BuildingSettings","m_bBuildingRestrictionLimitsEnabled","Building","False removes structure-category caps and global buildable limit.","True","bool",client:true);
        var threat=new Dictionary<string,string>{{"HarvestSpicePickupThreatUnit","10"},{"HarvestSpiceCoalesceThreatUnit","10"},{"HarvestFlourSandPickupThreatUnit","10"},{"HarvestFlourSandCoalesceThreatUnit","10"},{"WalkingThreatPerSec","15"},{"CrouchingThreatPerSec","15"},{"RunningThreatPerSec","20"},{"SprintingThreatPerSec","20"},{"HyperSprintingThreatPerSec","90"},{"DashingThreatPerSec","90"},{"SuspendingThreatPerSec","200"},{"DrumsandThreatPerSec","200"},{"ShieldingThreatPerSec","500"}};
        foreach(var (key,value) in threat)Game("/Script/DuneSandbox.SandwormSettings",key,"Sandworms","Threat generated by the named action.",value,min:0,direction:"Higher values make worms breach and attack earlier.");
        return list;
    }
    public static IReadOnlyList<string> Validate(string file,string text)
    {
        var errors=new List<string>();if(text.Length>2_000_000)errors.Add("Configuration exceeds 2 MB.");if(text.Contains('\0'))errors.Add("NUL bytes are not allowed.");
        var doc=new IniDocument(text);
        foreach(var entry in doc.Entries()) { var definition=All.FirstOrDefault(d=>d.File==file && d.Section.Equals(entry.Section,StringComparison.OrdinalIgnoreCase)&&d.Key.Equals(entry.Key,StringComparison.OrdinalIgnoreCase)); var error=definition?.Validate(entry.Value);if(error!=null)errors.Add($"{entry.Key}: {error}"); }
        return errors;
    }
    public static IReadOnlyList<string> ValidateChanges(string file,string before,string after)
    {
        var errors=new List<string>();if(after.Length>2_000_000)errors.Add("Configuration exceeds 2 MB.");if(after.Contains('\0'))errors.Add("NUL bytes are not allowed.");
        var old=new IniDocument(before);var next=new IniDocument(after);
        foreach(var entry in next.Entries())
        {
            if(old.Get(entry.Section,entry.Key)==entry.Value)continue;
            var definition=All.FirstOrDefault(d=>d.File==file&&d.Section.Equals(entry.Section,StringComparison.OrdinalIgnoreCase)&&d.Key.Equals(entry.Key,StringComparison.OrdinalIgnoreCase));var error=definition?.Validate(entry.Value);if(error!=null)errors.Add($"{entry.Key}: {error}");
        }
        return errors;
    }
}
