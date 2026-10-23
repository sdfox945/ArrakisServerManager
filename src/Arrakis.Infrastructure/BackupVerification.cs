using System.Security.Cryptography;
using System.Text.Json;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public static class BackupVerification
{
    private sealed record Receipt(string Name,string RemotePath,long Size,DateTimeOffset Modified,string Sha256);
    public static async Task RecordAsync(string local,BackupArtifact artifact,string hash,CancellationToken token)
    {
        var receipt=new Receipt(artifact.Name,artifact.Path,artifact.Size,artifact.Modified,hash);
        await File.WriteAllTextAsync(local+".verified.json",JsonSerializer.Serialize(receipt),token);
    }
    public static async Task<bool> VerifyAsync(BackupArtifact artifact,string directory,CancellationToken token)
    {
        CommandBuilder.ArtifactName(artifact.Name);
        var path=Path.Combine(directory,"Database",artifact.Name);
        try
        {
            if(!File.Exists(path)||!File.Exists(path+".verified.json"))return false;
            var receipt=JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(path+".verified.json",token));
            if(receipt==null||receipt.Name!=artifact.Name||receipt.RemotePath!=artifact.Path||receipt.Size!=artifact.Size||receipt.Modified!=artifact.Modified||new FileInfo(path).Length!=artifact.Size)return false;
            await using var file=File.OpenRead(path);var hash=Convert.ToHexStringLower(await SHA256.HashDataAsync(file,token));return hash==receipt.Sha256;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return false;}
    }
}
