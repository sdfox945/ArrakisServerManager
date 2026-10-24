using System.Text.RegularExpressions;

namespace Arrakis.Core;

public sealed record IniEntry(string Section, string Key, string Value, int Line);
public sealed class IniDocument
{
    private readonly List<string> lines;
    private readonly string newline;
    public IniDocument(string text)
    {
        newline = text.Contains("\r\n") ? "\r\n" : "\n";
        lines = Regex.Matches(text, @"[^\r\n]*(?:\r\n|\r|\n|$)").Select(m => m.Value).Where(s => s.Length > 0).ToList();
    }
    public IReadOnlyList<IniEntry> Entries()
    {
        var entries = new List<IniEntry>(); var section = "";
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i].Trim();
            if (raw.StartsWith(';') || raw.StartsWith('#') || raw.Length == 0) continue;
            if (raw.StartsWith('[') && raw.Contains(']')) { section = raw[1..raw.IndexOf(']')].Trim(); continue; }
            var equal = raw.IndexOf('=');
            if (equal < 1) continue;
            var key = raw[..equal].Trim(); var value = raw[(equal + 1)..];
            entries.Add(new(section, key, SplitComment(value).Value.Trim(), i));
        }
        return entries;
    }
    public string? Get(string section, string key) => Entries().LastOrDefault(e => Eq(e.Section, section) && Eq(e.Key, key))?.Value;
    public IReadOnlyList<string> Sections()=>lines.Select(l=>Regex.Match(l.Trim(),@"^\[([^\]\r\n]+)\]\s*(?:[;#].*)?$")).Where(m=>m.Success).Select(m=>m.Groups[1].Value.Trim()).ToArray();
    public void Set(string section, string key, string value)
    {
        if (new[] { section, key, value }.Any(s => s.Contains('\n') || s.Contains('\r') || s.Contains('\0')) || key.Contains('=') || section.Contains(']')) throw new ArgumentException("INI values must be single lines.");
        var matches = Entries().Where(e => Eq(e.Section, section) && Eq(e.Key, key)).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("This key occurs more than once. Resolve its ordering in the raw editor before using visual controls.");
        if (matches.Length == 1)
        {
            var index = matches[0].Line; var old = lines[index]; var end = Ending(old); var content = old[..(old.Length - end.Length)];
            var equal = content.IndexOf('='); var rhs = content[(equal+1)..]; var (body, comment) = SplitComment(rhs);
            var leading = body[..(body.Length - body.TrimStart().Length)]; var trailing = body[(body.TrimEnd().Length)..];
            lines[index] = content[..(equal+1)] + leading + value + trailing + comment + end;
            return;
        }
        var target = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var m = Regex.Match(lines[i].Trim(), @"^\[([^\]]+)\]");
            if (m.Success && Eq(m.Groups[1].Value.Trim(), section)) { target = i+1; while (target < lines.Count && !lines[target].TrimStart().StartsWith('[')) target++; break; }
        }
        if (target < 0)
        {
            EnsureTerminated(); if (lines.Count > 0) lines.Add(newline);
            lines.Add($"[{section}]{newline}"); lines.Add($"{key}={value}{newline}");
        }
        else { if (target == lines.Count) EnsureTerminated(); lines.Insert(target, $"{key}={value}{newline}"); }
    }
    private void EnsureTerminated() { if (lines.Count > 0 && Ending(lines[^1]).Length == 0) lines[^1] += newline; }
    private static string Ending(string s) => s.EndsWith("\r\n") ? "\r\n" : s.EndsWith('\n') ? "\n" : s.EndsWith('\r') ? "\r" : "";
    private static bool Eq(string a, string b) => string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static (string Value, string Comment) SplitComment(string text)
    {
        var quoted = false; var escaped = false;
        for (var i=0;i<text.Length;i++) { if (text[i]=='"' && !escaped) quoted=!quoted; if (!quoted && (text[i] is ';' or '#') && (i==0 || char.IsWhiteSpace(text[i-1]))) return (text[..i],text[i..]); escaped=text[i]=='\\' && !escaped; }
        return (text, "");
    }
    public override string ToString() => string.Concat(lines);
    public static string Diff(string before, string after)
    {
        var a=before.Replace("\r\n","\n").Split('\n'); var b=after.Replace("\r\n","\n").Split('\n'); var output=new List<string>();
        for(var i=0;i<Math.Max(a.Length,b.Length);i++) { var x=i<a.Length?a[i]:null;var y=i<b.Length?b[i]:null;if(x==y)continue;output.Add($"@@ line {i+1} @@"); if(x!=null)output.Add("− "+Redactor.Clean(x));if(y!=null)output.Add("+ "+Redactor.Clean(y));if(output.Count>1500){output.Add("Preview truncated; inspect raw editor for full contents.");break;} }
        return output.Count==0?"No changes.":string.Join(Environment.NewLine,output);
    }
}
