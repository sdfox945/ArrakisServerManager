using System.Text.Json;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed class FileApplicationUpdateCache(string root) : IApplicationUpdateCache
{
    private readonly string path=Path.Combine(root,"application-update-cache.json");
    public async Task<ApplicationUpdateCacheEntry?> ReadAsync(CancellationToken token)
    {
        if(!File.Exists(path))return null;
        await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,8192,FileOptions.Asynchronous);using var bytes=new MemoryStream();var buffer=new byte[8192];
        while(true){var count=await file.ReadAsync(buffer,token).ConfigureAwait(false);if(count==0)break;if(bytes.Length+count>ApplicationUpdateSource.MetadataLimit*2)throw new IOException("Update cache is too large.");await bytes.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);}
        return JsonSerializer.Deserialize<ApplicationUpdateCacheEntry>(bytes.ToArray());
    }
    public async Task WriteAsync(ApplicationUpdateCacheEntry entry,CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{var text=JsonSerializer.Serialize(entry);if(System.Text.Encoding.UTF8.GetByteCount(text)>ApplicationUpdateSource.MetadataLimit*2)throw new IOException("Update cache is too large.");await File.WriteAllTextAsync(temp,text,token).ConfigureAwait(false);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
public sealed class MemoryApplicationUpdateCache : IApplicationUpdateCache
{
    public ApplicationUpdateCacheEntry? Entry {get;set;}
    public Task<ApplicationUpdateCacheEntry?> ReadAsync(CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(Entry);}
    public Task WriteAsync(ApplicationUpdateCacheEntry entry,CancellationToken token){token.ThrowIfCancellationRequested();Entry=entry;return Task.CompletedTask;}
}
