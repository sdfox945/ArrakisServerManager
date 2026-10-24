using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

// Every request terminates in this in-memory handler. Demo never constructs a network handler.
public sealed class SimulatedApplicationUpdateService : IApplicationUpdateService
{
    private readonly MemoryApplicationUpdateCache cache=new();
    private readonly DemoHandler handler;
    private readonly GitHubApplicationUpdateService service;
    public SimulatedApplicationUpdateService(SemanticVersion installed)
    {
        handler=new(installed);service=new(cache,handler);
    }
    public ApplicationUpdateScenario Scenario {get=>handler.Scenario;set{handler.Scenario=value;cache.Entry=null;}}
    public int Requests=>handler.Requests;
    public Task<ApplicationUpdateCheck> CheckAsync(bool force,CancellationToken token)=>service.CheckAsync(force,token);
    public Task<VerifiedApplicationDownload> DownloadAsync(ApplicationRelease release,string directory,IProgress<ApplicationDownloadProgress>? progress,CancellationToken token)=>service.DownloadAsync(release,directory,progress,token);
    public void Dispose()=>service.Dispose();
    private sealed class DemoHandler(SemanticVersion installed) : HttpMessageHandler
    {
        public ApplicationUpdateScenario Scenario;
        public int Requests;
        private readonly byte[] zip=CreateZip();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Requests++;
            if(request.RequestUri==ApplicationUpdateSource.LatestApi)
            {
                if(Scenario==ApplicationUpdateScenario.ApiUnavailable)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                if(Scenario==ApplicationUpdateScenario.InvalidMetadata)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{invalid-json")});
                var version=Scenario==ApplicationUpdateScenario.UpToDate?new SemanticVersion(installed.Major,installed.Minor,installed.Patch):new(installed.Major,installed.Minor,installed.Patch+1);
                var tag="v"+version;var filename=ApplicationUpdateSource.AssetName(version);var asset=new{id=1,name=filename,state="uploaded",size=zip.Length,digest="sha256:"+Convert.ToHexStringLower(SHA256.HashData(zip)),url=$"https://api.github.com/repos/{ApplicationUpdateSource.Owner}/{ApplicationUpdateSource.Repository}/releases/assets/1",browser_download_url=$"https://github.com/{ApplicationUpdateSource.Owner}/{ApplicationUpdateSource.Repository}/releases/download/{tag}/{filename}"};
                var json=JsonSerializer.Serialize(new{tag_name=tag,name="Simulated Arrakis release "+tag,published_at="2026-10-23T00:00:00Z",draft=false,prerelease=false,html_url=$"https://github.com/{ApplicationUpdateSource.Owner}/{ApplicationUpdateSource.Repository}/releases/tag/{tag}",body="DEMO — simulated release, not actual software.\n\n# Example notes\n<html><script>Nothing here executes.</script></html>\n\nPortable installation is always manual.",assets=Scenario==ApplicationUpdateScenario.MissingZip?[]:new[]{asset}});
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
            }
            var bytes=zip.ToArray();if(Scenario==ApplicationUpdateScenario.ChecksumMismatch)bytes[^1]^=1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new DemoStream(bytes,Scenario==ApplicationUpdateScenario.DownloadInterrupted))});
        }
        private static byte[] CreateZip()
        {
            using var output=new MemoryStream();using(var archive=new ZipArchive(output,ZipArchiveMode.Create,true))
            {
                var entry=archive.CreateEntry("DEMO-NOT-AN-APPLICATION.txt",CompressionLevel.NoCompression);entry.LastWriteTime=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);using var writer=new StreamWriter(entry.Open());writer.Write("Simulated update fixture only. This archive contains no application executable.\n");writer.Write(new string(' ',1024*1024));
            }
            return output.ToArray();
        }
    }
    private sealed class DemoStream(byte[] bytes,bool interrupt) : MemoryStream(bytes,false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {
            await Task.Delay(45,token);if(interrupt&&Position>Length/2)throw new IOException("Simulated interrupted transfer.");return await base.ReadAsync(buffer,token);
        }
    }
}
