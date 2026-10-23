using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Arrakis.Core;

public static class SettingValueComparison
{
    // No guessed tolerance, slider rounding or loss of typed precision. Decimal coefficient/exponent
    // normalization also works outside System.Decimal's range and distinguishes large integer values.
    public static bool Equivalent(SettingDefinition setting,string current,string proposed)
    {
        if(current==proposed)return true;
        if(setting.Validate(current)!=null||setting.Validate(proposed)!=null)return false;
        if(setting.Kind is "number" or "integer")return Canonical(current)==Canonical(proposed);
        if(setting.Kind=="bool"||setting.Choices?.Length>0)return current.Equals(proposed,StringComparison.OrdinalIgnoreCase);
        return false;
    }
    private static (bool Negative,string Digits,BigInteger Power) Canonical(string text)
    {
        var match=Regex.Match(text.Trim(),@"^(?<sign>[+-]?)(?<whole>[0-9]*)(?:\.(?<fraction>[0-9]*))?(?:[eE](?<power>[+-]?[0-9]+))?$");
        var fraction=match.Groups["fraction"].Value;
        var digits=(match.Groups["whole"].Value+fraction).TrimStart('0');
        if(digits.Length==0)return(false,"0",BigInteger.Zero);
        var significant=digits.TrimEnd('0');
        var power=match.Groups["power"].Success?BigInteger.Parse(match.Groups["power"].Value,CultureInfo.InvariantCulture):BigInteger.Zero;
        return(match.Groups["sign"].Value=="-",significant,power-fraction.Length+digits.Length-significant.Length);
    }
}
