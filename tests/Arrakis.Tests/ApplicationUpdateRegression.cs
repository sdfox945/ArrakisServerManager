using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static class ApplicationUpdateRegression
{
    internal static readonly byte[] Bytes=ZipBytes();
    internal static readonly string Hash=Convert.ToHexStringLower(SHA256.HashData(Bytes));
    internal static void Check(bool value,string message)=>ConfigurationIntegrityRegression.Check(value,message);
    internal static JsonObject Metadata(string version="1.2.1",string? digest=null)
    {
        var tag="v"+version;var filename=ApplicationUpdateSource.AssetName(SemanticVersion.Parse(version));
        return new JsonObject
        {
            ["tag_name"]=tag,["name"]="Fixture release "+tag,["published_at"]="2026-10-23T00:00:00Z",["draft"]=false,["prerelease"]=false,
            ["html_url"]=$"https://github.com/{ApplicationUpdateSource.Owner}/{ApplicationUpdateSource.Repository}/releases/tag/{Uri.EscapeDataString(tag)}",
            ["body"]="# Plain release notes\n<script>neverExecute()</script>\npowershell.exe -Command ignored",
            ["assets"]=new JsonArray(Asset(7,filename,Bytes.Length,"sha256:"+(digest??Hash),tag))
        };
    }
    internal static JsonObject Asset(int id,string name,long size,string? digest,string tag="v1.2.1")=>new()
    {
        ["id"]=id,["name"]=name,["size"]=size,["state"]="uploaded",["digest"]=digest,
        ["url"]=$"https://api.github.com/repos/{ApplicationUpdateSource.Owner}/{ApplicationUpdateSource.Repository}/releases/assets/{id}",
        ["browser_download_url"]=$"https://github.com/{ApplicationUpdateSource.Owner}/{ApplicationUpdateSource.Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}"
    };
    private static byte[] ZipBytes()
    {
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){using var text=new StreamWriter(zip.CreateEntry("../must-never-be-extracted.txt",CompressionLevel.NoCompression).Open());text.Write(new string('x',131072));}return memory.ToArray();
    }
    internal sealed class Handler : HttpMessageHandler
    {
        public JsonObject Json=Metadata();public List<Uri> Requests=[];public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>>? Reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Check(request.Headers.Authorization==null,"Update HTTP requests must never carry credentials.");Check(request.Headers.UserAgent.Count>0,"GitHub requires an ordinary User-Agent.");Requests.Add(request.RequestUri!);
            if(Reply!=null)return Reply(request,token);
            return Task.FromResult(request.RequestUri==ApplicationUpdateSource.LatestApi?Response(Json.ToJsonString()):new(HttpStatusCode.OK){Content=new ByteArrayContent(Bytes)});
        }
    }
    internal static HttpResponseMessage Response(string text)=>new(HttpStatusCode.OK){Content=new StringContent(text,Encoding.UTF8,"application/json")};
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-23T01:00:00Z");
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>{public void Report(T value)=>report(value);}
    internal sealed class UnknownContent(Stream stream) : HttpContent
    {
        protected override bool TryComputeLength(out long length){length=0;return false;}
        protected override Task SerializeToStreamAsync(Stream target,TransportContext? context)=>stream.CopyToAsync(target);
        protected override Task<Stream> CreateContentReadStreamAsync()=>Task.FromResult(stream);
        protected override void Dispose(bool disposing){if(disposing)stream.Dispose();base.Dispose(disposing);}
    }
    internal sealed class ControlledStream(byte[] bytes,int delay=0,bool fail=false) : MemoryStream(bytes,false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {
            if(delay>0)await Task.Delay(delay,token);if(fail&&Position>0)throw new IOException("Isolated interrupted stream.");return await base.ReadAsync(buffer[..Math.Min(buffer.Length,4096)],token);
        }
    }
    internal static async Task<T> Reject<T>(Func<Task> work) where T:Exception
    {
        try{await work();}catch(T e){return e;}throw new Exception("Expected "+typeof(T).Name);
    }
    internal static string DirectoryFor(string root,string name)=>Path.Combine(root,"application-updates",name);
    public static void Register(Action<string,Func<Task>> add,string root)
    {
        void Test(string name,Action work)=>add("App updates: "+name,()=>{work();return Task.CompletedTask;});
        void Async(string name,Func<Task> work)=>add("App updates: "+name,work);
        Test("installed version comes from actual Desktop assembly metadata",()=>{var v=SemanticVersion.FromAssembly(typeof(Arrakis.Desktop.MainWindow).Assembly);Check(v.Core=="1.2.0","Actual application assembly version must be 1.2.0.");});
        foreach(var (text,order) in new[]{("1.2.0",0),("1.2.1",1),("1.3.0",1),("2.0.0",1),("1.1.9",-1),("v1.2.0",0),("v1.2.0+build.0001",0),("1.2.0-rc.1",-1),("1.2.0-beta.11",-1),("1.10.0",1)})
            Test("semantic comparison "+text,()=>Check(Math.Sign(SemanticVersion.Parse(text).CompareTo(SemanticVersion.Parse("1.2.0")))==order,"Semantic ordering must not use lexical text ordering."));
        Test("prerelease numeric identifiers and stable precedence follow SemVer",()=>{Check(SemanticVersion.Parse("1.2.0-beta.11").CompareTo(SemanticVersion.Parse("1.2.0-beta.2"))>0,"Numeric identifiers compare numerically.");Check(SemanticVersion.Parse("1.2.0").CompareTo(SemanticVersion.Parse("1.2.0-rc.1"))>0,"Stable beats prerelease.");});
        Test("malformed or unbounded semantic versions are refused",()=>{foreach(var s in new[]{"1.2","1.2.0.1","01.2.0","1.2.0-01","1.2.0+","v../evil","1.2.0\n",new string('9',200)})Check(!SemanticVersion.TryParse(s,out _),"Malformed version accepted.");});
        Test("stable metadata binds title date notes and exact asset identity",()=>{var r=GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString());Check(r.Version.Core=="1.2.1"&&r.Name.Contains("Fixture")&&r.PublishedAt.Year==2026&&r.Notes.Contains("<script>")&&r.Zip.Sha256==Hash&&r.Zip.Size==Bytes.Length,"All requested release fields must be retained as data.");});
        foreach(var (name,mutate) in new (string,Action<JsonObject>)[]
        {
            ("draft",j=>j["draft"]=true),("prerelease flag",j=>j["prerelease"]=true),("prerelease tag",j=>j["tag_name"]="v1.2.1-beta"),("bad version",j=>j["tag_name"]="not-a-version"),
            ("unpublished",j=>j["published_at"]=null),("missing ZIP",j=>j["assets"]=new JsonArray()),("duplicate ZIP",j=>((JsonArray)j["assets"]!).Add(j["assets"]![0]!.DeepClone())),
            ("wrong owner",j=>j["html_url"]="https://github.com/attacker/ArrakisServerManager/releases/tag/v1.2.1"),("HTTP asset",j=>j["assets"]![0]!["browser_download_url"]="http://github.com/sdfox945/ArrakisServerManager/releases/download/v1.2.1/x.zip"),
            ("foreign asset host",j=>j["assets"]![0]!["browser_download_url"]="https://github.com.evil.invalid/x"),("asset API identity",j=>j["assets"]![0]!["url"]="https://api.github.com/repos/attacker/repo/releases/assets/7"),
            ("unsafe filename",j=>j["assets"]![0]!["name"]="../ArrakisServerManager-v1.2.1-win-x64.zip"),("oversized asset",j=>j["assets"]![0]!["size"]=ApplicationUpdateSource.PackageLimit+1),("unuploaded asset",j=>j["assets"]![0]!["state"]="new"),("malformed digest",j=>j["assets"]![0]!["digest"]="md5:bad")
        })Async("reject metadata: "+name,async()=>{var j=Metadata();mutate(j);await Reject<ApplicationUpdateException>(()=>Task.FromResult(GitHubApplicationUpdateService.ParseRelease(j.ToJsonString())));});
        Async("malformed JSON and bounded metadata fail without success cache",async()=>
        {
            foreach(var text in new[]{"{bad-json",new string('x',ApplicationUpdateSource.MetadataLimit+1)}){var h=new Handler{Reply=(_,_)=>Task.FromResult(Response(text))};var cache=new MemoryApplicationUpdateCache();using var s=new GitHubApplicationUpdateService(cache,h);await Reject<ApplicationUpdateException>(()=>s.CheckAsync(true,default));Check(cache.Entry==null,"A failed response cannot become a successful cache.");}
        });
        foreach(var status in new[]{HttpStatusCode.NotFound,HttpStatusCode.ServiceUnavailable,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests})Async("HTTP failure/rate limit "+(int)status,async()=>{var h=new Handler{Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(status))};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);var e=await Reject<ApplicationUpdateException>(()=>s.CheckAsync(true,default));Check(status is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)||e.Message.Contains("rate-limited"),"Rate limiting needs a clear result.");});
        Async("network outage and metadata timeout remain ordinary failures",async()=>
        {
            foreach(var timeout in new[]{false,true}){var h=new Handler{Reply=async(_,t)=>{if(timeout)await Task.Delay(Timeout.Infinite,t);throw new HttpRequestException("Isolated network failure");}};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h,timeouts:new(TimeSpan.FromMilliseconds(40),TimeSpan.FromMilliseconds(40),TimeSpan.FromMilliseconds(40),TimeSpan.FromSeconds(1)));await Reject<ApplicationUpdateException>(()=>s.CheckAsync(true,default));}
        });
        Async("24-hour cache avoids requests while force and expiry make fresh requests",async()=>
        {
            var h=new Handler();var clock=new Clock();var cache=new MemoryApplicationUpdateCache();using var s=new GitHubApplicationUpdateService(cache,h,clock);var first=await s.CheckAsync(false,default);var cached=await s.CheckAsync(false,default);Check(!first.FromCache&&cached.FromCache&&h.Requests.Count==1&&cached.CheckedAt==first.CheckedAt,"Successful metadata/timestamp must be cached.");await s.CheckAsync(true,default);Check(h.Requests.Count==2,"Manual checks bypass cache.");clock.Now+=TimeSpan.FromHours(25);await s.CheckAsync(false,default);Check(h.Requests.Count==3,"Expired metadata requires a fresh check.");
        });
        Async("corrupt and future caches are not trusted",async()=>
        {
            foreach(var future in new[]{false,true}){var clock=new Clock();var cache=new MemoryApplicationUpdateCache{Entry=new(clock.Now+(future?TimeSpan.FromHours(2):TimeSpan.Zero),future?Metadata().ToJsonString():"{}")};var h=new Handler();using var s=new GitHubApplicationUpdateService(cache,h,clock);Check(!(await s.CheckAsync(false,default)).FromCache&&h.Requests.Count==1,"Invalid/future cache must not bypass validation.");}
        });
        Async("cache survives service restart and cache failure does not invalidate a network result",async()=>
        {
            var path=DirectoryFor(root,"cache");var clock=new Clock();using(var s=new GitHubApplicationUpdateService(new FileApplicationUpdateCache(path),new Handler(),clock))await s.CheckAsync(true,default);
            var h=new Handler();using(var s=new GitHubApplicationUpdateService(new FileApplicationUpdateCache(path),h,clock)){Check((await s.CheckAsync(false,default)).FromCache&&h.Requests.Count==0,"Persistent cache should survive restart.");}
            using(var s=new GitHubApplicationUpdateService(new FailingCache(),new Handler()))Check((await s.CheckAsync(true,default)).CacheWarning.Length>0,"An unwritable cache cannot turn a successful network check into false failure.");
        });
        Async("missing verification remains unavailable, not falsely verified",async()=>
        {
            var j=Metadata();j["assets"]![0]!["digest"]=null;var r=GitHubApplicationUpdateService.ParseRelease(j.ToJsonString());Check(!r.HasVerification,"Missing digests must be visible.");using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),new Handler());await Reject<ApplicationUpdateException>(()=>s.DownloadAsync(r,DirectoryFor(root,"missing-hash"),null,default));
        });
        Async("successful streaming reports bytes and hash and never extracts content",async()=>
        {
            var h=new Handler();using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);var r=(await s.CheckAsync(true,default)).Release;var reports=new List<ApplicationDownloadProgress>();var directory=DirectoryFor(root,"success");var result=await s.DownloadAsync(r,directory,new InlineProgress<ApplicationDownloadProgress>(reports.Add),default);
            Check(File.Exists(result.Path)&&result.Sha256==Hash&&result.Bytes==Bytes.Length&&!result.Reused,"Success must correspond to exact downloaded bytes and hash.");Check(reports.Count>=2&&reports[0].Bytes==0&&reports[^1].Bytes==Bytes.Length&&reports[^1].Percent==100,"Streaming progress must include bytes/total/percent.");Check(!File.Exists(Path.Combine(directory,"..","must-never-be-extracted.txt"))&&Directory.GetFiles(directory).Length==1,"ZIP content must never be extracted or executed.");
        });
        Async("existing verified ZIP is rehashed and reused without downloading",async()=>
        {
            var directory=DirectoryFor(root,"reuse");Directory.CreateDirectory(directory);var r=GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString());await File.WriteAllBytesAsync(Path.Combine(directory,r.Zip.Name),Bytes);var h=new Handler();using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);var result=await s.DownloadAsync(r,directory,null,default);Check(result.Reused&&h.Requests.Count==0,"A reused ZIP requires hash verification but no transfer.");
        });
        Async("corrupted existing ZIP is preserved, never overwritten",async()=>
        {
            var directory=DirectoryFor(root,"corrupt-existing");Directory.CreateDirectory(directory);var r=GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString());var path=Path.Combine(directory,r.Zip.Name);await File.WriteAllTextAsync(path,"existing user file");using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),new Handler());await Reject<ApplicationUpdateException>(()=>s.DownloadAsync(r,directory,null,default));Check(await File.ReadAllTextAsync(path)=="existing user file","Preexisting files must not be replaced.");
        });
        foreach(var mode in new[]{"hash mismatch","header size mismatch","incomplete stream","oversized stream","interrupted stream","idle timeout","cancellation"})Async("download failure cleans only its partial: "+mode,async()=>
        {
            var h=new Handler();var r=GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString());var directory=DirectoryFor(root,mode.Replace(' ','-'));Directory.CreateDirectory(directory);await File.WriteAllTextAsync(Path.Combine(directory,"keep.txt"),"unrelated");
            h.Reply=(_,_)=>{HttpContent content=mode switch{"hash mismatch"=>new ByteArrayContent(Bytes.Select((b,i)=>i==0?(byte)(b^1):b).ToArray()),"header size mismatch"=>new ByteArrayContent(Bytes[..^1]),"incomplete stream"=>new UnknownContent(new MemoryStream(Bytes[..^1])),"oversized stream"=>new UnknownContent(new MemoryStream(Bytes.Concat(new byte[]{0}).ToArray())),_=>new UnknownContent(new ControlledStream(Bytes,mode is "idle timeout" or "cancellation"?100:0,mode=="interrupted stream"))};return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});};
            using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h,timeouts:new(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(40),TimeSpan.FromSeconds(2)));using var cancel=new CancellationTokenSource();if(mode=="cancellation")cancel.CancelAfter(15);
            await Reject<Exception>(()=>s.DownloadAsync(r,directory,null,cancel.Token));Check(Directory.GetFiles(directory).Select(Path.GetFileName).SequenceEqual(new[]{"keep.txt"}),"Failure must remove its own partial, preserve unrelated files and create no completed ZIP.");
        });
        Async("checksum-file fallback verifies the exact filename and optional checksum-asset digest",async()=>
        {
            var j=Metadata();j["assets"]![0]!["digest"]=null;var text=Hash+"  "+ApplicationUpdateSource.AssetName(SemanticVersion.Parse("1.2.1"))+"\n";var bytes=Encoding.UTF8.GetBytes(text);((JsonArray)j["assets"]!).Add(Asset(8,"SHA256SUMS.txt",bytes.Length,"sha256:"+Convert.ToHexStringLower(SHA256.HashData(bytes))));var r=GitHubApplicationUpdateService.ParseRelease(j.ToJsonString());var h=new Handler{Reply=(q,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(q.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt")?bytes:Bytes)})};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);Check((await s.DownloadAsync(r,DirectoryFor(root,"checksum-fallback"),null,default)).Sha256==Hash,"Fallback checksum must bind this exact ZIP.");
        });
        Test("ambiguous checksums and path-prefixed entries cannot verify",()=>{var name=ApplicationUpdateSource.AssetName(SemanticVersion.Parse("1.2.1"));Check(GitHubApplicationUpdateService.ParseChecksum(Hash+"  ../"+name,name)==null,"Path-prefixed entries must not match.");Check(GitHubApplicationUpdateService.ParseChecksum(Hash+"  "+name+"\n"+Hash+"  "+name,name)==null,"Duplicate entries must fail closed.");});
        Async("bad secondary checksum content or asset digest cannot finalize a package",async()=>
        {
            foreach(var corruptDigest in new[]{false,true})
            {
                var j=Metadata();j["assets"]![0]!["digest"]=null;var bytes=Encoding.UTF8.GetBytes(corruptDigest?Hash+"  "+ApplicationUpdateSource.AssetName(SemanticVersion.Parse("1.2.1")):"<html>not a checksum</html>");((JsonArray)j["assets"]!).Add(Asset(8,"SHA256SUMS.txt",bytes.Length,corruptDigest?"sha256:"+new string('0',64):null));var h=new Handler{Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);var directory=DirectoryFor(root,"bad-checksum-"+corruptDigest);await Reject<ApplicationUpdateException>(()=>s.DownloadAsync(GitHubApplicationUpdateService.ParseRelease(j.ToJsonString()),directory,null,default));Check(!Directory.Exists(directory),"Unreliable checksums cannot create a completed package or output folder.");
            }
        });
        Async("overall download timeout is independent of idle progress",async()=>
        {
            var h=new Handler{Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new UnknownContent(new ControlledStream(Bytes,10))})};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h,timeouts:new(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(40)));var directory=DirectoryFor(root,"overall-timeout");await Reject<ApplicationUpdateException>(()=>s.DownloadAsync(GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString()),directory,null,default));Check(Directory.GetFiles(directory).Length==0,"An overall deadline must clean the partial even while bytes arrive.");
        });
        Async("oversized unknown-length API streams remain bounded",async()=>
        {
            var h=new Handler{Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new UnknownContent(new MemoryStream(new byte[ApplicationUpdateSource.MetadataLimit+1]))})};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);await Reject<ApplicationUpdateException>(()=>s.CheckAsync(true,default));
        });
        Async("explicit metadata cancellation is not cached as success",async()=>
        {
            var h=new Handler{Reply=async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return Response("{}");}};var cache=new MemoryApplicationUpdateCache();using var s=new GitHubApplicationUpdateService(cache,h);using var ct=new CancellationTokenSource(20);await Reject<OperationCanceledException>(()=>s.CheckAsync(true,ct.Token));Check(cache.Entry==null,"Cancellation cannot become a successful cached check.");
        });
        Async("failed forced refresh preserves previous successful cache and timestamp",async()=>
        {
            var h=new Handler();var cache=new MemoryApplicationUpdateCache();using var s=new GitHubApplicationUpdateService(cache,h);await s.CheckAsync(true,default);var before=cache.Entry;h.Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));await Reject<ApplicationUpdateException>(()=>s.CheckAsync(true,default));Check(cache.Entry==before,"A failed request cannot replace the last successful cache.");
        });
        Test("untrusted note/control text is bounded and cannot disguise identity",()=>
        {
            var j=Metadata();j["body"]=new string('x',70000)+"\u202e";j["name"]="a\u202eb\u0001";var r=GitHubApplicationUpdateService.ParseRelease(j.ToJsonString());Check(r.Notes.Length<66000&&r.Notes.Contains("truncated")&&r.Name=="ab","Notes must be bounded and control/bidirectional formatting removed.");
        });
        foreach(var url in new[]{"http://release-assets.githubusercontent.com/a","https://evil.invalid/a","https://github.com.evil.invalid/a","https://user:pass@release-assets.githubusercontent.com/a","https://release-assets.githubusercontent.com:444/a","https://github.com/attacker/repo/a"})Async("reject download redirect "+url,async()=>
        {
            var h=new Handler{Reply=(_,_)=>{var response=new HttpResponseMessage(HttpStatusCode.Found);response.Headers.Location=new Uri(url);return Task.FromResult(response);}};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);await Reject<ApplicationUpdateException>(()=>s.DownloadAsync(GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString()),DirectoryFor(root,Guid.NewGuid().ToString("N")),null,default));Check(h.Requests.Count==1,"An unsafe redirect must not be requested.");
        });
        Async("approved HTTPS CDN redirect succeeds without credentials",async()=>
        {
            var h=new Handler{Reply=(q,_)=>{if(q.RequestUri!.Host=="github.com"){var response=new HttpResponseMessage(HttpStatusCode.Found);response.Headers.Location=new("https://release-assets.githubusercontent.com/github-production-release-asset/fixture?signature=fixture-only");return Task.FromResult(response);}return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Bytes)});}};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);await s.DownloadAsync(GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString()),DirectoryFor(root,"cdn"),null,default);Check(h.Requests.Count==2,"A validated CDN redirect should work.");
        });
        Async("redirect loops and metadata redirects are bounded/refused",async()=>
        {
            var r=GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString());var h=new Handler{Reply=(_,_)=>{var response=new HttpResponseMessage(HttpStatusCode.Found);response.Headers.Location=r.Zip.DownloadUrl;return Task.FromResult(response);}};using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),h);await Reject<ApplicationUpdateException>(()=>s.DownloadAsync(r,DirectoryFor(root,"loops"),null,default));Check(h.Requests.Count==5,"Redirect count must be bounded.");h.Requests.Clear();await Reject<ApplicationUpdateException>(()=>s.CheckAsync(true,default));Check(h.Requests.Count==1,"Repository metadata redirects cannot change the fixed identity.");
        });
        Async("unavailable destination and protected server/application directories fail safely",async()=>
        {
            var directory=DirectoryFor(root,"dest-file");Directory.CreateDirectory(Path.GetDirectoryName(directory)!);await File.WriteAllTextAsync(directory,"keep");var r=GitHubApplicationUpdateService.ParseRelease(Metadata().ToJsonString());using var s=new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),new Handler(),forbiddenDirectories:[DirectoryFor(root,"protected")]);await Reject<IOException>(()=>s.DownloadAsync(r,directory,null,default));await Reject<IOException>(()=>s.DownloadAsync(r,DirectoryFor(root,"protected/sub"),null,default));Check(await File.ReadAllTextAsync(directory)=="keep","Destination failures cannot mutate preexisting files.");
        });
        Test("relative UNC and encoded/file browser targets are refused",()=>
        {
            foreach(var p in new[]{"relative","\\\\server\\share\\updates"}){try{ApplicationDownloadPaths.Validate(p,[]);throw new Exception("Unsafe path accepted");}catch(IOException){}}
            try{ApplicationDownloadPaths.Validate(Path.Combine(root,"root-protected"),[Path.GetPathRoot(root)!]);throw new Exception("A protected drive root was bypassed.");}catch(IOException){}
            foreach(var u in new[]{"file:///C:/Windows/notepad.exe","http://github.com/sdfox945/ArrakisServerManager/releases","https://github.com/sdfox945/ArrakisServerManager/releases/tag/v1.2.1?x=1","https://github.com/sdfox945/ArrakisServerManager/releases/tag/v1.2.1%2F.."})Check(!ApplicationUpdateSource.IsReleasePage(new(u)),"Unsafe browser URL accepted.");
        });
        Async("all eight Demo scenarios use isolated simulated release/download infrastructure",async()=>
        {
            foreach(var scenario in Enum.GetValues<ApplicationUpdateScenario>())
            {
                using var s=new SimulatedApplicationUpdateService(SemanticVersion.Parse("1.2.0")){Scenario=scenario};
                if(scenario is ApplicationUpdateScenario.ApiUnavailable or ApplicationUpdateScenario.InvalidMetadata or ApplicationUpdateScenario.MissingZip){await Reject<ApplicationUpdateException>(()=>s.CheckAsync(true,default));continue;}
                var result=await s.CheckAsync(true,default);Check(result.Release.Version.CompareTo(SemanticVersion.Parse("1.2.0"))==(scenario==ApplicationUpdateScenario.UpToDate?0:1),"Demo versions must match the scenario.");
                if(scenario is ApplicationUpdateScenario.DownloadInterrupted or ApplicationUpdateScenario.ChecksumMismatch)await Reject<Exception>(()=>s.DownloadAsync(result.Release,DirectoryFor(root,"demo-"+scenario),null,default));
                if(scenario==ApplicationUpdateScenario.VerifiedDownload)
                {
                    var directory=DirectoryFor(root,"demo-verified");Check((await s.DownloadAsync(result.Release,directory,null,default)).Bytes>0,"Demo verified fixture should complete without real software.");
                    using var restarted=new SimulatedApplicationUpdateService(SemanticVersion.Parse("1.2.0")){Scenario=scenario};var next=await restarted.CheckAsync(true,default);Check((await restarted.DownloadAsync(next.Release,directory,null,default)).Reused,"Demo ZIP bytes/digest must be deterministic across sessions.");
                }
                Check(s.Requests>0,"Demo requests must terminate in its fake handler.");
            }
        });
    }
    private sealed class FailingCache : IApplicationUpdateCache
    {
        public Task<ApplicationUpdateCacheEntry?> ReadAsync(CancellationToken token)=>throw new IOException("Isolated cache failure");
        public Task WriteAsync(ApplicationUpdateCacheEntry entry,CancellationToken token)=>throw new UnauthorizedAccessException("Isolated cache failure");
    }
}
