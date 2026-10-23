using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Arrakis.Core;

public sealed record ConfigurationFileChoice(string Name, string RemotePath);
public sealed record VisualSettingChange(string Section, string Key, string Value);

/// <summary>A loaded document owns its original bytes, draft text and explicit editing intent.</summary>
public sealed class ConfigurationDraft
{
    private readonly ConfigFile original;
    private readonly Dictionary<(string Section, string Key), VisualSettingChange> changes = [];
    private string text;
    private string? authorizedRemovalHash;
    public ConfigurationDraft(ConfigFile file)
    {
        original = file with { Bytes = file.Bytes.ToArray() };
        ConfigurationIntegrity.ValidateOriginal(original);
        text = original.Text;
    }
    public Guid Id { get; } = Guid.NewGuid();
    public long Revision { get; private set; }
    public string Text => text;
    public ConfigFile Original => original with { Bytes = original.Bytes.ToArray() };
    public bool HasRawEdits { get; private set; }
    public bool IsDirty => !original.Encode(text).SequenceEqual(original.Bytes);
    public void ApplyVisual(SettingDefinition setting, string value)
    {
        if (setting.File != original.Name) throw new InvalidOperationException("Setting belongs to a different configuration file.");
        var known = SettingCatalog.All.SingleOrDefault(d => d.File == setting.File && d.Section == setting.Section && d.Key == setting.Key) ?? throw new InvalidOperationException("Unknown settings require explicit raw editing.");
        var error = known.Validate(value); if (error != null) throw new InvalidOperationException(error);
        var doc = new IniDocument(text); doc.Set(setting.Section, setting.Key, value);
        var next = doc.ToString();
        if (next == text) return;
        changes[(setting.Section, setting.Key)] = new(setting.Section, setting.Key, value);
        text = next; Revision++; authorizedRemovalHash = null;
    }
    public void EditRaw(string value)
    {
        if (value == text) return;
        text = value; HasRawEdits = true; Revision++; authorizedRemovalHash = null;
    }
    public void Undo() { text = original.Text; changes.Clear(); HasRawEdits = false; authorizedRemovalHash = null; Revision++; }
    public ConfigurationReview PrepareReview(ConfigurationFileChoice choice, bool forRemovalAuthorization = false)
    {
        if (choice.Name != original.Name || choice.RemotePath != original.RemotePath) throw new InvalidOperationException("The active file identity changed. Reload and review again.");
        ConfigurationIntegrity.ValidateProposal(original, text);
        if (!HasRawEdits) ConfigurationIntegrity.ValidateVisual(original, text, changes.Values);
        var payload = original.Encode(text);
        var removals = HasRawEdits && ConfigurationIntegrity.RemovesOriginalContent(original.Text, text);
        if (removals && !forRemovalAuthorization && authorizedRemovalHash != ConfigurationIntegrity.Hash(payload))
            throw new InvalidOperationException("Raw edits remove existing content. Use 'Review raw removals' and type the filename to authorize those exact removals before saving.");
        return new(Id, Revision, original, payload, HasRawEdits, removals, !removals || authorizedRemovalHash == ConfigurationIntegrity.Hash(payload), changes.Values.ToArray());
    }
    public void AuthorizeRemovals(ConfigurationReview review)
    {
        if (!Matches(review)) throw new InvalidOperationException("The draft changed while removals were reviewed; review it again.");
        authorizedRemovalHash = review.ProposedHash;
    }
    public bool Matches(ConfigurationReview review) => review.DocumentId == Id && review.Revision == Revision && review.Matches(original, original.Encode(text));
}

/// <summary>Frozen original/proposal bytes, target identity and the full diff presented for approval.</summary>
public sealed class ConfigurationReview
{
    private readonly ConfigFile original;
    private readonly byte[] payload;
    private readonly VisualSettingChange[] intentions;
    internal ConfigurationReview(Guid documentId, long revision, ConfigFile file, byte[] proposed, bool raw, bool removals, bool removalsAuthorized, VisualSettingChange[] changes)
    {
        DocumentId = documentId; Revision = revision; original = file with { Bytes = file.Bytes.ToArray() }; payload = proposed.ToArray();
        IsRaw = raw; RemovesContent = removals; OriginalHash = ConfigurationIntegrity.Hash(original.Bytes); ProposedHash = ConfigurationIntegrity.Hash(payload);
        intentions = changes.ToArray();
        RemovalsAuthorized = removalsAuthorized;
        Diff = ConfigurationIntegrity.Diff(original.Text, (original with { Bytes = payload }).Text);
    }
    public Guid DocumentId { get; }
    public long Revision { get; }
    public bool IsRaw { get; }
    public bool RemovesContent { get; }
    public bool RemovalsAuthorized { get; }
    public string OriginalHash { get; }
    public string ProposedHash { get; }
    public string Diff { get; }
    public ConfigFile Original => original with { Bytes = original.Bytes.ToArray() };
    public byte[] Payload => payload.ToArray();
    public bool Matches(ConfigFile file, byte[] bytes) => file.Name == original.Name && file.RemotePath == original.RemotePath && file.Bytes.SequenceEqual(original.Bytes) && bytes.SequenceEqual(payload) && ConfigurationIntegrity.Hash(file.Bytes) == OriginalHash && ConfigurationIntegrity.Hash(bytes) == ProposedHash;
    public void Validate()
    {
        ConfigurationIntegrity.ValidateProposal(original, (original with { Bytes = payload }).Text);
        if (!IsRaw) ConfigurationIntegrity.ValidateVisual(original, (original with { Bytes = payload }).Text, intentions);
        if (RemovesContent && !RemovalsAuthorized) throw new InvalidOperationException("Raw removals were not explicitly authorized for this exact payload.");
        if (!Matches(original, payload) || Diff != ConfigurationIntegrity.Diff(original.Text, (original with { Bytes = payload }).Text)) throw new InvalidOperationException("Preview and payload no longer match.");
    }
}

public static class ConfigurationIntegrity
{
    public const string CustomSection = "/Script/DuneSandbox.UserServerCustomSettings";
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static void ValidateOriginal(ConfigFile file)
    {
        if (!new[] { "UserGame.ini", "UserEngine.ini", "UserServerCustomSettings.ini" }.Contains(file.Name) || !file.RemotePath.StartsWith('/') || !file.RemotePath.EndsWith('/'+file.Name,StringComparison.Ordinal) || file.RemotePath.Any(char.IsControl)) throw new InvalidOperationException("Unsupported or mismatched configuration target.");
        if (file.Hash != Hash(file.Bytes)) throw new InvalidOperationException("The original snapshot hash is invalid. Reload the file.");
        if (file.Bytes.Length > 2_000_000) throw new InvalidOperationException("The original exceeds the supported configuration byte limit.");
        ValidateStructure(file.Name, file.Text);
    }
    public static void ValidateProposal(ConfigFile original, string proposed)
    {
        ValidateOriginal(original); ValidateStructure(original.Name, proposed);
        if (original.Encode(proposed).Length > 2_000_000) throw new InvalidOperationException("The encoded proposal exceeds the supported configuration byte limit.");
        var errors = SettingCatalog.ValidateChanges(original.Name, original.Text, proposed);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        var before = new IniDocument(original.Text).Entries(); var after = new IniDocument(proposed).Entries();
        var protectedOnly = original.Name == "UserServerCustomSettings.ini" ? 1 : 0;
        if (before.Count > protectedOnly && after.Count <= protectedOnly) throw new InvalidOperationException("The proposal is unexpectedly truncated: original settings are missing. Reload instead of saving.");
    }
    private static void ValidateStructure(string file, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0') || text.Length > 2_000_000) throw new InvalidOperationException("Empty, invalid or unexpectedly truncated configuration is not allowed.");
        var sections = Sections(text);
        if (sections.Count == 0 || new IniDocument(text).Entries().Count == 0) throw new InvalidOperationException("The configuration must retain valid INI sections and settings.");
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith(';') || value.StartsWith('#')) continue;
            if (value.StartsWith('[') && !Regex.IsMatch(value, @"^\[[^\]\r\n]+\]\s*(?:[;#].*)?$")) throw new InvalidOperationException("Malformed or truncated INI section header.");
            if (!value.StartsWith('[') && value.IndexOf('=') < 1) throw new InvalidOperationException("Malformed or truncated INI setting line.");
        }
        if (file == "UserServerCustomSettings.ini")
        {
            var keys = new IniDocument(text).Entries().Where(e => e.Section.Equals(CustomSection,StringComparison.OrdinalIgnoreCase) && e.Key.Equals("DifficultyLevel",StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!sections.Contains(CustomSection) || keys.Length != 1 || keys[0].Key != "DifficultyLevel" || keys[0].Value != "Custom") throw new InvalidOperationException("UserServerCustomSettings.ini must retain [/Script/DuneSandbox.UserServerCustomSettings] and exactly one DifficultyLevel=Custom.");
        }
    }
    public static void ValidateVisual(ConfigFile original, string proposed, IEnumerable<VisualSettingChange> changes)
    {
        var replay = new IniDocument(original.Text);
        foreach (var change in changes) replay.Set(change.Section, change.Key, change.Value);
        if (!original.Encode(replay.ToString()).SequenceEqual(original.Encode(proposed))) throw new InvalidOperationException("Visual-editor integrity check failed: unrelated bytes changed. Reload and reapply only the intended settings.");
        if (!Sections(original.Text).IsSubsetOf(Sections(proposed))) throw new InvalidOperationException("Visual editing cannot remove original INI section structure.");
    }
    private static HashSet<string> Sections(string text) => Regex.Matches(text, @"(?m)^\s*\[([^\]\r\n]+)\]").Select(m => m.Groups[1].Value.Trim()).ToHashSet(StringComparer.Ordinal);
    public static bool RemovesOriginalContent(string before, string after)
    {
        // Values may change freely in explicit raw mode; removing sections, keys or comments needs separate authorization.
        if (!Sections(before).IsSubsetOf(Sections(after))) return true;
        var oldKeys = new IniDocument(before).Entries().Select(e => (e.Section, e.Key)).ToHashSet();
        var newKeys = new IniDocument(after).Entries().Select(e => (e.Section, e.Key)).ToHashSet();
        if (!oldKeys.IsSubsetOf(newKeys)) return true;
        var oldComments = before.Replace("\r\n", "\n").Split('\n').Where(l => l.TrimStart().StartsWith(';') || l.TrimStart().StartsWith('#'));
        var newLines = after.Replace("\r\n", "\n").Split('\n').ToHashSet();
        return oldComments.Any(l => !newLines.Contains(l));
    }
    public static string Diff(string before, string after)
    {
        var a = before.Replace("\r\n", "\n").Split('\n'); var b = after.Replace("\r\n", "\n").Split('\n');
        var first = 0; while (first < Math.Min(a.Length, b.Length) && a[first] == b[first]) first++;
        var endA = a.Length; var endB = b.Length; while (endA > first && endB > first && a[endA - 1] == b[endB - 1]) { endA--; endB--; }
        var output = new List<string>();
        if (a.Length == b.Length) { for (var i = first; i < endA; i++) if (a[i] != b[i]) { output.Add("- " + Redactor.Clean(a[i])); output.Add("+ " + Redactor.Clean(b[i])); } }
        else { for (var i = first; i < endA; i++) output.Add("- " + Redactor.Clean(a[i])); for (var i = first; i < endB; i++) output.Add("+ " + Redactor.Clean(b[i])); }
        if (before != after && output.Count == 0) output.Add("File line endings changed (the encoded payload hash also changes).");
        return output.Count == 0 ? "No changes." : string.Join(Environment.NewLine, output);
    }
}
