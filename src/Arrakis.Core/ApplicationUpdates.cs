using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Arrakis.Core;

public sealed record SemanticVersion(int Major,int Minor,int Patch,string Prerelease="",string Build="") : IComparable<SemanticVersion>
{
    public string Core=>$"{Major}.{Minor}.{Patch}";
    public string Display=>"v"+Core+(Prerelease.Length>0?"-"+Prerelease:"");
    public override string ToString()=>Core+(Prerelease.Length>0?"-"+Prerelease:"")+(Build.Length>0?"+"+Build:"");
    public static bool TryParse(string? text,out SemanticVersion version)
    {
        version=new(0,0,0);if(text==null||text.Length>128)return false;
        var m=Regex.Match(text,@"\A[vV]?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z");
        if(!m.Success||!int.TryParse(m.Groups[1].Value,CultureInfo.InvariantCulture,out var a)||!int.TryParse(m.Groups[2].Value,CultureInfo.InvariantCulture,out var b)||!int.TryParse(m.Groups[3].Value,CultureInfo.InvariantCulture,out var c))return false;
        var pre=m.Groups[4].Value;if(pre.Split('.').Any(p=>p.Length>1&&p.All(char.IsAsciiDigit)&&p[0]=='0'))return false;
        version=new(a,b,c,pre,m.Groups[5].Value);return true;
    }
    public static SemanticVersion Parse(string text)=>TryParse(text,out var v)?v:throw new FormatException("Invalid supported semantic version.");
    public static SemanticVersion FromAssembly(Assembly assembly)
    {
        var informational=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if(TryParse(informational,out var v))return v;
        var number=assembly.GetName().Version??throw new FormatException("Application version metadata is unavailable.");
        return Parse($"{number.Major}.{number.Minor}.{Math.Max(0,number.Build)}");
    }
    public int CompareTo(SemanticVersion? other)
    {
        if(other==null)return 1;
        foreach(var n in new[]{Major.CompareTo(other.Major),Minor.CompareTo(other.Minor),Patch.CompareTo(other.Patch)})if(n!=0)return n;
        if(Prerelease==other.Prerelease)return 0;if(Prerelease.Length==0)return 1;if(other.Prerelease.Length==0)return -1;
        var a=Prerelease.Split('.');var b=other.Prerelease.Split('.');
        for(var i=0;i<Math.Min(a.Length,b.Length);i++)
        {
            var an=a[i].All(char.IsAsciiDigit);var bn=b[i].All(char.IsAsciiDigit);int n;
            if(an&&bn){n=a[i].Length.CompareTo(b[i].Length);if(n==0)n=string.CompareOrdinal(a[i],b[i]);}
            else if(an!=bn)n=an?-1:1;else n=string.CompareOrdinal(a[i],b[i]);
            if(n!=0)return n;
        }
        return a.Length.CompareTo(b.Length);
    }
}

// Repository identity is deliberately not user/server configuration or remote-controlled data.
public static class ApplicationUpdateSource
{
    public const string Owner="sdfox945";
    public const string Repository="ArrakisServerManager";
    public const int MetadataLimit=1_048_576;
    public const int ChecksumLimit=65_536;
    public const long PackageLimit=512L*1024*1024;
    public static readonly TimeSpan CheckInterval=TimeSpan.FromHours(24);
    public static Uri LatestApi=>new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
    public static Uri ReleasesPage=>new($"https://github.com/{Owner}/{Repository}/releases");
    public static string AssetName(SemanticVersion version)=>$"ArrakisServerManager-v{version}-win-x64.zip";
    public static bool IsHttps(Uri uri)=>uri.IsAbsoluteUri&&uri.Scheme==Uri.UriSchemeHttps&&uri.Port==443&&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&uri.OriginalString.Length<=16384&&!uri.OriginalString.Any(char.IsControl)&&!uri.OriginalString.Contains('\\');
    public static bool IsReleasePage(Uri uri)
    {
        if(!IsHttps(uri)||uri.Host!="github.com"||uri.Query.Length!=0)return false;
        var prefix=$"/{Owner}/{Repository}/releases";
        if(uri.AbsolutePath==prefix||uri.AbsolutePath==prefix+"/latest")return true;
        if(!uri.AbsolutePath.StartsWith(prefix+"/tag/",StringComparison.Ordinal))return false;
        var tag=Uri.UnescapeDataString(uri.AbsolutePath[(prefix.Length+5)..]);return SemanticVersion.TryParse(tag,out _)&&uri.AbsolutePath==prefix+"/tag/"+Uri.EscapeDataString(tag);
    }
    public static bool IsAssetUrl(Uri uri,string tag,string name)=>IsHttps(uri)&&uri.Host=="github.com"&&uri.Query.Length==0&&uri.AbsolutePath==$"/{Owner}/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}";
    public static bool IsAssetApi(Uri uri,long id)=>IsHttps(uri)&&uri.Host=="api.github.com"&&uri.Query.Length==0&&uri.AbsolutePath==$"/repos/{Owner}/{Repository}/releases/assets/{id}";
    public static bool IsAssetRedirect(Uri uri)=>IsHttps(uri)&&uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    public static bool IsSha256(string? hash)=>hash!=null&&Regex.IsMatch(hash,@"\A[0-9a-fA-F]{64}\z");
}

public record ApplicationReleaseAsset(long Id,string Name,long Size,Uri DownloadUrl,string? Sha256);
public record ApplicationRelease(string Tag,SemanticVersion Version,string Name,DateTimeOffset PublishedAt,string Notes,Uri ReleaseUrl,ApplicationReleaseAsset Zip,ApplicationReleaseAsset? Checksums)
{
    public bool HasVerification=>Zip.Sha256!=null||Checksums!=null;
}
public record ApplicationUpdateCheck(ApplicationRelease Release,DateTimeOffset CheckedAt,bool FromCache,string CacheWarning="");
public record ApplicationUpdateCacheEntry(DateTimeOffset CheckedAt,string Json);
public record ApplicationDownloadProgress(long Bytes,long Total)
{
    public double Percent=>Total>0?Math.Clamp(100.0*Bytes/Total,0,100):0;
}
public record VerifiedApplicationDownload(string Path,string Sha256,long Bytes,bool Reused);
public interface IApplicationUpdateCache
{
    Task<ApplicationUpdateCacheEntry?> ReadAsync(CancellationToken token);
    Task WriteAsync(ApplicationUpdateCacheEntry entry,CancellationToken token);
}
public interface IApplicationUpdateService : IDisposable
{
    Task<ApplicationUpdateCheck> CheckAsync(bool force,CancellationToken token);
    Task<VerifiedApplicationDownload> DownloadAsync(ApplicationRelease release,string directory,IProgress<ApplicationDownloadProgress>? progress,CancellationToken token);
}
public sealed class ApplicationUpdateException(string message,Uri? releaseUrl=null) : Exception(message)
{
    public Uri ReleaseUrl {get;}=releaseUrl!=null&&ApplicationUpdateSource.IsReleasePage(releaseUrl)?releaseUrl:ApplicationUpdateSource.ReleasesPage;
}
public enum ApplicationUpdateScenario { UpToDate,UpdateAvailable,ApiUnavailable,InvalidMetadata,MissingZip,DownloadInterrupted,ChecksumMismatch,VerifiedDownload }
