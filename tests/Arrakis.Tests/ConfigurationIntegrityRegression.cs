using System.IO;
using System.Text;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static class ConfigurationIntegrityRegression
{
    internal static ConfigFile Fixture(string file = "UserServerCustomSettings.ini", Encoding? encoding = null, string newline = "\n")
    {
        var text = file switch
        {
            "UserGame.ini" => "; Fixture comments\n[/Script/DuneSandbox.PvpPveSettings]\nm_bShouldForceEnablePvpOnAllPartitions=False\n\n[Custom]\nUnknown=preserve\n",
            "UserEngine.ini" => "; Preserve ports and comments\n[URL]\nPort=7777\nIGWPort=7888\n\n[ConsoleVariables]\nDune.GlobalMiningOutputMultiplier=1.0\nFixtureUnknown=keep\n",
            _ => System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "UserServerCustomSettings.representative.ini")) + "\n; Explicit unknown-key fixture\nFixtureUnknownKey=keep-exactly\n"
        };
        text = text.Replace("\r\n", "\n").Replace("\n", newline);
        encoding ??= new UTF8Encoding(false, true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        return new(file, "/fixture/UserSettings/" + file, bytes, ConfigurationIntegrity.Hash(bytes));
    }
    internal static SettingDefinition Loot => SettingCatalog.All.Single(d => d.Key == "LootRespawnSpeed");
    private static ConfigurationFileChoice Choice(ConfigFile f) => new(f.Name, f.RemotePath);
    internal static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action work) { try { work(); } catch (InvalidOperationException) { return; } throw new Exception("Expected fail-closed validation."); }
    public static void Register(Action<string, Func<Task>> add)
    {
        void Test(string name, Action test) => add(name, () => { test(); return Task.CompletedTask; });
        Test("Editor integrity: every supported original snapshot validates with explicit identity", () =>
        {
            foreach (var name in new[] { "UserGame.ini", "UserEngine.ini", "UserServerCustomSettings.ini" }) { var file = Fixture(name); var draft = new ConfigurationDraft(file); Check(draft.Text == file.Text && !draft.IsDirty, name); }
            var original = Fixture(); Reject(() => new ConfigurationDraft(original with { Hash = "bad" }));
            Reject(() => new ConfigurationDraft(original with { RemotePath = "/fixture/UserEngine.ini" }));
            var d = new ConfigurationDraft(original); Reject(() => d.PrepareReview(new("UserEngine.ini", original.RemotePath)));
        });
        Test("Editor integrity: one visual edit yields exactly two diff lines and no unrelated byte change", () =>
        {
            var original = Fixture(); var d = new ConfigurationDraft(original); d.ApplyVisual(Loot, "0.200000"); var review = d.PrepareReview(Choice(original)); review.Validate();
            Check(review.Diff.Replace("\r\n", "\n") == "- LootRespawnSpeed=0.100000\n+ LootRespawnSpeed=0.200000", "Preview must contain one replacement only.");
            Check(review.Payload.SequenceEqual(original.Encode(original.Text.Replace("LootRespawnSpeed=0.100000", "LootRespawnSpeed=0.200000"))), "Comments, blanks, other settings and unknown keys must match exact original bytes.");
        });
        Test("Editor integrity: UTF8/UTF16 BOM and LF/CRLF survive visual editing", () =>
        {
            foreach (var encoding in new Encoding[] { new UTF8Encoding(false, true), new UTF8Encoding(true, true), Encoding.Unicode, Encoding.BigEndianUnicode })
            foreach (var ending in new[] { "\n", "\r\n" }) { var f = Fixture(encoding: encoding, newline: ending); var d = new ConfigurationDraft(f); d.ApplyVisual(Loot, "0.200000"); var r = d.PrepareReview(Choice(f)); Check(r.Payload.SequenceEqual(f.Encode(f.Text.Replace("LootRespawnSpeed=0.100000", "LootRespawnSpeed=0.200000"))), "Encoding/line endings changed."); }
        });
        Test("Editor integrity: empty/truncated proposals and removed protected custom entries are rejected", () =>
        {
            var f = Fixture();
            foreach (var bad in new[] { "", "\n", "[", "[" + ConfigurationIntegrity.CustomSection + "]\nDifficultyLevel=Custom\n", f.Text.Replace("DifficultyLevel=Custom", ""), f.Text.Replace("[/Script/DuneSandbox.UserServerCustomSettings]", "[WrongSection]"), f.Text+"\nTruncatedSetting", f.Text+"\ndifficultylevel=Other\n" }) Reject(() => ConfigurationIntegrity.ValidateProposal(f, bad));
        });
        Test("Editor integrity: visual replay rejects unrelated changes including comments and missing settings", () =>
        {
            var f = Fixture(); var expected = new IniDocument(f.Text); expected.Set(ConfigurationIntegrity.CustomSection, "LootRespawnSpeed", "0.200000");
            var intended = new[] { new VisualSettingChange(ConfigurationIntegrity.CustomSection, "LootRespawnSpeed", "0.200000") };
            Reject(() => ConfigurationIntegrity.ValidateVisual(f, expected + "\nUnintended=1", intended));
            Reject(() => ConfigurationIntegrity.ValidateVisual(f, expected.ToString().Replace("DifficultyLevel=Custom", ""), intended));
            Reject(() => ConfigurationIntegrity.ValidateVisual(f, expected.ToString().Replace("; Explicit unknown-key fixture", "; silently changed comment"), intended));
        });
        Test("Editor integrity: explicit raw removals require separate authorization bound to exact draft", () =>
        {
            var f = Fixture(); var d = new ConfigurationDraft(f); d.EditRaw(f.Text.Replace("FixtureUnknownKey=keep-exactly\n", "")); Reject(() => d.PrepareReview(Choice(f)));
            var removal = d.PrepareReview(Choice(f), true); Reject(removal.Validate); d.AuthorizeRemovals(removal); d.PrepareReview(Choice(f)).Validate();
            d.ApplyVisual(Loot, "0.200000"); Reject(() => d.PrepareReview(Choice(f)));
            d.EditRaw(""); Reject(() => d.PrepareReview(Choice(f), true));
        });
        Test("Editor integrity: review freezes bytes and detects stale revisions or external byte-array mutation", () =>
        {
            var f = Fixture(); var d = new ConfigurationDraft(f); d.ApplyVisual(Loot, "0.200000"); var review = d.PrepareReview(Choice(f)); var payload = review.Payload; payload[0] ^= 1; var source = review.Original; source.Bytes[0] ^= 1;
            review.Validate(); Check(d.Matches(review), "Returned arrays must not mutate the frozen approval.");
            d.ApplyVisual(Loot, "0.300000"); Check(!d.Matches(review), "An approval cannot authorize a different later revision.");
        });
        add("Editor integrity: lower save transaction rejects empty payload before backup or remote execution", async () =>
        {
            var called = false;
            try { await ConfigurationSaveTransaction.ExecuteAsync(Fixture(), [], () => called = true, (_, _, _) => { called = true; return Task.FromResult(new ProcessResult(0, "", "")); }, _ => Task.FromResult(Array.Empty<byte>()), default); throw new Exception("Expected validation rejection."); }
            catch (InvalidOperationException) { Check(!called, "An invalid proposal must never reach backups/write execution."); }
        });
    }
}
