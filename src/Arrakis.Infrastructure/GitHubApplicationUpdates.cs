using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed record ApplicationUpdateTimeouts(TimeSpan Metadata,TimeSpan Headers,TimeSpan Idle,TimeSpan Download)
{
    public static ApplicationUpdateTimeouts Default=>new(TimeSpan.FromSeconds(20),TimeSpan.FromSeconds(20),TimeSpan.FromSeconds(30),TimeSpan.FromMinutes(15));
}

public sealed class GitHubApplicationUpdateService : IApplicationUpdateService
{
    private readonly HttpClient client;
    private readonly IApplicationUpdateCache cache;
    private readonly TimeProvider time;
    private readonly ApplicationUpdateTimeouts timeouts;
    private readonly string[] forbiddenDirectories;
    private readonly SemaphoreSlim gate=new(1,1);
    private ApplicationUpdateCacheEntry? memory;
    public GitHubApplicationUpdateService(IApplicationUpdateCache cache,HttpMessageHandler? handler=null,TimeProvider? time=null,ApplicationUpdateTimeouts? timeouts=null,IEnumerable<string>? forbiddenDirectories=null)
    {
        this.cache=cache;this.time=time??TimeProvider.System;this.timeouts=timeouts??ApplicationUpdateTimeouts.Default;this.forbiddenDirectories=(forbiddenDirectories??[]).ToArray();
        client=new(handler??new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,ConnectTimeout=TimeSpan.FromSeconds(15),Credentials=null}){Timeout=Timeout.InfiniteTimeSpan};
    }
    public async Task<ApplicationUpdateCheck> CheckAsync(bool force,CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if(!force)
            {
                try{memory??=await cache.ReadAsync(token).ConfigureAwait(false);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or FormatException){}
                if(memory is {} saved&&saved.CheckedAt<=time.GetUtcNow()&&time.GetUtcNow()-saved.CheckedAt<ApplicationUpdateSource.CheckInterval)
                {
                    try{return new(ParseRelease(saved.Json),saved.CheckedAt,true);}catch(Exception e)when(e is ApplicationUpdateException or JsonException or ArgumentException){memory=null;}
                }
            }
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeouts.Metadata);
            try
            {
                using var response=await SendAsync(ApplicationUpdateSource.LatestApi,false,deadline.Token).ConfigureAwait(false);EnsureSuccess(response);
                var json=Encoding.UTF8.GetString(await ReadBoundedAsync(response.Content,ApplicationUpdateSource.MetadataLimit,deadline.Token).ConfigureAwait(false));var release=ParseRelease(json);var checkedAt=time.GetUtcNow();memory=new(checkedAt,json);var warning="";
                try{await cache.WriteAsync(memory,deadline.Token).ConfigureAwait(false);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){warning="Check succeeded; the local cache could not be saved.";}
                return new(release,checkedAt,false,warning);
            }
            catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new ApplicationUpdateException("GitHub update check timed out. Check again later.");}
            catch(HttpRequestException){throw new ApplicationUpdateException("GitHub could not be reached. The application remains available offline.");}
            catch(JsonException){throw new ApplicationUpdateException("GitHub returned invalid release metadata. View releases on GitHub.");}
        }
        finally{gate.Release();}
    }
    public static ApplicationRelease ParseRelease(string json)
    {
        if(Encoding.UTF8.GetByteCount(json)>ApplicationUpdateSource.MetadataLimit)throw new ApplicationUpdateException("Release metadata exceeds the supported limit.");
        using var doc=JsonDocument.Parse(json,new(){MaxDepth=32});var root=doc.RootElement;Uri? link=null;
        try
        {
            var tag=root.GetProperty("tag_name").GetString()??"";
            if(!SemanticVersion.TryParse(tag,out var version)||version.Prerelease.Length>0||root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())throw new ApplicationUpdateException("No valid published stable release was returned.");
            if(!Uri.TryCreate(root.GetProperty("html_url").GetString(),UriKind.Absolute,out link)||!ApplicationUpdateSource.IsReleasePage(link)||link.AbsolutePath!=$"/{ApplicationUpdateSource.Owner}/{ApplicationUpdateSource.Repository}/releases/tag/{Uri.EscapeDataString(tag)}")throw new ApplicationUpdateException("The release URL does not belong to the expected GitHub repository.");
            var published=root.GetProperty("published_at").GetDateTimeOffset();if(published==default)throw new ApplicationUpdateException("The release publication date is missing.",link);
            var name=CleanText(root.GetProperty("name").GetString()??tag,200);var notes=CleanText(root.TryGetProperty("body",out var body)?body.GetString()??"":"",65536);
            var assets=root.GetProperty("assets");if(assets.ValueKind!=JsonValueKind.Array||assets.GetArrayLength()>100)throw new ApplicationUpdateException("Release assets are invalid.",link);
            var expected=ApplicationUpdateSource.AssetName(version);var zips=assets.EnumerateArray().Where(a=>a.TryGetProperty("name",out var n)&&n.GetString()==expected).ToArray();
            if(zips.Length!=1)throw new ApplicationUpdateException("The release does not contain one expected Windows x64 ZIP. View it on GitHub.",link);
            var zip=ParseAsset(zips[0],tag,expected,ApplicationUpdateSource.PackageLimit,link);var sums=assets.EnumerateArray().Where(a=>a.TryGetProperty("name",out var n)&&n.GetString()=="SHA256SUMS.txt").ToArray();
            if(sums.Length>1)throw new ApplicationUpdateException("Checksum assets are ambiguous.",link);
            var checksum=sums.Length==1?ParseAsset(sums[0],tag,"SHA256SUMS.txt",ApplicationUpdateSource.ChecksumLimit,link):null;
            return new(tag,version,name,published,notes,link,zip,checksum);
        }
        catch(Exception e)when(e is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException){throw new ApplicationUpdateException("GitHub returned invalid release metadata. View releases on GitHub.",link);}
    }
    private static ApplicationReleaseAsset ParseAsset(JsonElement asset,string tag,string name,long limit,Uri link)
    {
        var id=asset.GetProperty("id").GetInt64();var size=asset.GetProperty("size").GetInt64();
        if(id<=0||size<=0||size>limit||asset.GetProperty("state").GetString()!="uploaded"||!Uri.TryCreate(asset.GetProperty("url").GetString(),UriKind.Absolute,out var api)||!ApplicationUpdateSource.IsAssetApi(api,id)||!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(),UriKind.Absolute,out var url)||!ApplicationUpdateSource.IsAssetUrl(url,tag,name))throw new ApplicationUpdateException("Release asset identity, URL or size is invalid.",link);
        string? hash=null;
        if(asset.TryGetProperty("digest",out var digest)&&digest.ValueKind!=JsonValueKind.Null)
        {
            var value=digest.GetString()??"";if(!value.StartsWith("sha256:",StringComparison.Ordinal)||!ApplicationUpdateSource.IsSha256(value[7..]))throw new ApplicationUpdateException("Release asset digest is invalid.",link);hash=value[7..].ToLowerInvariant();
        }
        return new(id,name,size,url,hash);
    }
    private static string CleanText(string text,int max)
    {
        var result=new string(text.Where(c=>(!char.IsControl(c)||c is '\n' or '\r' or '\t')&&char.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.Format).Take(max).ToArray());return text.Length>max?result+"\n[Release text truncated]":result;
    }
    private async Task<HttpResponseMessage> SendAsync(Uri uri,bool asset,CancellationToken token)
    {
        var original=uri;
        for(var hop=0;hop<=4;hop++)
        {
            if(!ApplicationUpdateSource.IsHttps(uri)||(!asset&&uri!=ApplicationUpdateSource.LatestApi)||(asset&&uri!=original&&!ApplicationUpdateSource.IsAssetRedirect(uri)))throw new ApplicationUpdateException("An unsafe GitHub redirect was refused.");
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ArrakisServerManager","1"));request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(asset?"application/octet-stream":"application/vnd.github+json"));if(!asset)request.Headers.Add("X-GitHub-Api-Version","2026-03-10");
            using var headers=CancellationTokenSource.CreateLinkedTokenSource(token);headers.CancelAfter(timeouts.Headers);
            var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,headers.Token).ConfigureAwait(false);
            if((int)response.StatusCode is >=300 and <400)
            {
                var next=response.Headers.Location;response.Dispose();if(!asset||next==null||hop==4)throw new ApplicationUpdateException("GitHub returned an unsupported redirect.");
                uri=next.IsAbsoluteUri?next:new Uri(uri,next);continue;
            }
            return response;
        }
        throw new ApplicationUpdateException("Too many GitHub redirects.");
    }
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)throw new ApplicationUpdateException("GitHub declined or rate-limited this request. Try again later; no account or token is required.");
        if(!response.IsSuccessStatusCode)throw new ApplicationUpdateException("GitHub request failed (HTTP "+(int)response.StatusCode+"). Try again or view releases on GitHub.");
    }
    private async Task<byte[]> ReadBoundedAsync(HttpContent content,int limit,CancellationToken token)
    {
        if(content.Headers.ContentLength>limit)throw new ApplicationUpdateException("GitHub response exceeds the supported size limit.");
        using var stream=await content.ReadAsStreamAsync(token).ConfigureAwait(false);using var output=new MemoryStream();var buffer=new byte[8192];
        while(true){using var idle=CancellationTokenSource.CreateLinkedTokenSource(token);idle.CancelAfter(timeouts.Idle);var read=await stream.ReadAsync(buffer,idle.Token).ConfigureAwait(false);if(read==0)break;if(output.Length+read>limit)throw new ApplicationUpdateException("GitHub response exceeds the supported size limit.");await output.WriteAsync(buffer.AsMemory(0,read),token).ConfigureAwait(false);}
        return output.ToArray();
    }
    public async Task<VerifiedApplicationDownload> DownloadAsync(ApplicationRelease release,string directory,IProgress<ApplicationDownloadProgress>? progress,CancellationToken token)
    {
        ValidateSelection(release);directory=await Task.Run(()=>ApplicationDownloadPaths.Validate(directory,forbiddenDirectories),token).ConfigureAwait(false);string? partial=null;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeouts.Download);var ct=deadline.Token;
        try
        {
            var hash=release.Zip.Sha256;
            if(hash==null&&release.Checksums is {} sums)
            {
                using var response=await SendAsync(sums.DownloadUrl,true,ct).ConfigureAwait(false);EnsureSuccess(response);var bytes=await ReadBoundedAsync(response.Content,ApplicationUpdateSource.ChecksumLimit,ct).ConfigureAwait(false);
                if(bytes.Length!=sums.Size||(sums.Sha256!=null&&Convert.ToHexStringLower(SHA256.HashData(bytes))!=sums.Sha256))throw new ApplicationUpdateException("The checksum asset did not match its published identity.",release.ReleaseUrl);
                hash=ParseChecksum(Encoding.UTF8.GetString(bytes),release.Zip.Name);
            }
            if(!ApplicationUpdateSource.IsSha256(hash))throw new ApplicationUpdateException("Reliable SHA-256 information is unavailable. Use the official release page; no verified download was created.",release.ReleaseUrl);
            Directory.CreateDirectory(directory);ApplicationDownloadPaths.Validate(directory,forbiddenDirectories);var final=Path.Combine(directory,release.Zip.Name);
            if(File.Exists(final))
            {
                if(await VerifyFileAsync(final,release.Zip.Size,hash!,ct).ConfigureAwait(false)){progress?.Report(new(release.Zip.Size,release.Zip.Size));return new(final,hash!,release.Zip.Size,true);}
                throw new ApplicationUpdateException("An existing ZIP failed verification and was preserved. Choose an empty download folder or rename that file before retrying.",release.ReleaseUrl);
            }
            var drive=new DriveInfo(Path.GetPathRoot(directory)!);if(drive.IsReady&&drive.AvailableFreeSpace<release.Zip.Size+65536)throw new IOException("Not enough disk space for the application update.");
            partial=Path.Combine(directory,"."+release.Zip.Name+"."+Guid.NewGuid().ToString("N")+".partial");
            using var responseZip=await SendAsync(release.Zip.DownloadUrl,true,ct).ConfigureAwait(false);EnsureSuccess(responseZip);
            if(responseZip.Content.Headers.ContentLength is {} length&&length!=release.Zip.Size)throw new ApplicationUpdateException("The ZIP size differs from the selected release asset.",release.ReleaseUrl);
            await using(var file=new FileStream(partial,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.SequentialScan))
            {
                using var stream=await responseZip.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);var buffer=new byte[65536];long received=0;progress?.Report(new(0,release.Zip.Size));
                while(true)
                {
                    using var idle=CancellationTokenSource.CreateLinkedTokenSource(ct);idle.CancelAfter(timeouts.Idle);var count=await stream.ReadAsync(buffer,idle.Token).ConfigureAwait(false);if(count==0)break;
                    received+=count;if(received>release.Zip.Size||received>ApplicationUpdateSource.PackageLimit)throw new ApplicationUpdateException("The download exceeded the selected release size.",release.ReleaseUrl);
                    await file.WriteAsync(buffer.AsMemory(0,count),ct).ConfigureAwait(false);progress?.Report(new(received,release.Zip.Size));
                }
                if(received!=release.Zip.Size)throw new ApplicationUpdateException("The ZIP transfer was incomplete. No completed package was created.",release.ReleaseUrl);
                await file.FlushAsync(ct).ConfigureAwait(false);file.Position=0;var actual=Convert.ToHexStringLower(await SHA256.HashDataAsync(file,ct).ConfigureAwait(false));if(!actual.Equals(hash,StringComparison.OrdinalIgnoreCase))throw new ApplicationUpdateException("SHA-256 mismatch. No verified ZIP was created; invalid partial files are removed when possible.",release.ReleaseUrl);
            }
            ct.ThrowIfCancellationRequested();ApplicationDownloadPaths.Validate(directory,forbiddenDirectories);File.Move(partial,final,false);partial=null;return new(final,hash!,release.Zip.Size,false);
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new ApplicationUpdateException("The download timed out. No unverified package was finalized.",release.ReleaseUrl);}
        catch(HttpRequestException){throw new ApplicationUpdateException("The update transfer was interrupted. Retry when connected; no unverified ZIP was finalized.",release.ReleaseUrl);}
        finally{if(partial!=null){try{File.Delete(partial);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){/* An unfinalized .partial is never presented as a ZIP. */}}}
    }
    public static void ValidateSelection(ApplicationRelease release)
    {
        if(release.Version.Prerelease.Length>0||!SemanticVersion.TryParse(release.Tag,out var tag)||tag!=release.Version||!ApplicationUpdateSource.IsReleasePage(release.ReleaseUrl)||release.ReleaseUrl.AbsolutePath!=$"/{ApplicationUpdateSource.Owner}/{ApplicationUpdateSource.Repository}/releases/tag/{Uri.EscapeDataString(release.Tag)}"||release.Zip.Name!=ApplicationUpdateSource.AssetName(tag))throw new ApplicationUpdateException("The selected application release is invalid.");
        foreach(var asset in new[]{release.Zip,release.Checksums}.OfType<ApplicationReleaseAsset>())
        {
            if(asset.Id<=0||asset.Size<=0||asset.Size>(asset==release.Zip?ApplicationUpdateSource.PackageLimit:ApplicationUpdateSource.ChecksumLimit)||!ApplicationUpdateSource.IsAssetUrl(asset.DownloadUrl,release.Tag,asset.Name)||(asset!=release.Zip&&asset.Name!="SHA256SUMS.txt")||(asset.Sha256!=null&&!ApplicationUpdateSource.IsSha256(asset.Sha256)))throw new ApplicationUpdateException("The selected application asset is invalid.",release.ReleaseUrl);
        }
    }
    public static string? ParseChecksum(string text,string filename)
    {
        var matches=new List<string>();foreach(var line in text.TrimStart('\uFEFF').Split('\n'))
        {
            var match=System.Text.RegularExpressions.Regex.Match(line.TrimEnd('\r'),@"\A([0-9a-fA-F]{64}) [ *]([^\r\n]+)\z");
            if(match.Success&&match.Groups[2].Value==filename)matches.Add(match.Groups[1].Value.ToLowerInvariant());
        }
        return matches.Count==1?matches[0]:null;
    }
    private static async Task<bool> VerifyFileAsync(string path,long size,string hash,CancellationToken token)
    {
        if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("A linked download file was refused.");
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
        return stream.Length==size&&Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,token).ConfigureAwait(false)).Equals(hash,StringComparison.OrdinalIgnoreCase);
    }
    public void Dispose()=>client.Dispose();
}

public static class ApplicationDownloadPaths
{
    public static string Validate(string directory,IEnumerable<string> forbiddenDirectories)
    {
        if(!Path.IsPathFullyQualified(directory)||directory.StartsWith("\\\\",StringComparison.Ordinal))throw new IOException("Choose an absolute local download folder.");
        var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if(new DriveInfo(Path.GetPathRoot(full)!).DriveType==DriveType.Network)throw new IOException("Choose a local download folder, not a mapped network drive.");
        foreach(var blocked in forbiddenDirectories.Where(Path.IsPathFullyQualified))
        {
            var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(blocked));var prefix=Path.EndsInDirectorySeparator(root)?root:root+Path.DirectorySeparatorChar;if(full.Equals(root,StringComparison.OrdinalIgnoreCase)||full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a separate application-download folder outside the application, server installation, backups and logs.");
        }
        for(var item=new DirectoryInfo(full);item!=null;item=item.Parent)if(item.Exists&&item.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Linked download directories are not supported. Choose a local folder.");
        return full;
    }
}
