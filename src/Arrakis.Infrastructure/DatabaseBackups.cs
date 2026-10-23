using System.Globalization;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed partial class LiveServerGateway
{
    public async Task<IReadOnlyList<BackupArtifact>> ListBackupsAsync(CancellationToken token)
    {
        var name=snapshot.BattlegroupName??throw new InvalidOperationException("Connect to a battlegroup first.");CommandBuilder.ResourceName(name);var root="/funcom/artifacts/database-dumps/"+name;
        var command="if sudo -n test -d "+Q(root)+"; then sudo -n find "+Q(root)+" -maxdepth 1 -type f -name '*.backup' -exec stat -c '%n|%s|%Y' {} \\;; fi";
        var raw=await Ssh.ReadAsync(command,token);var list=new List<BackupArtifact>();
        foreach(var line in raw.Split('\n',StringSplitOptions.RemoveEmptyEntries))
        {
            var fields=line.Trim().Split('|');if(fields.Length!=3 || !long.TryParse(fields[1],out var size)||!long.TryParse(fields[2],out var unix))continue;var filename=fields[0].Split('/').Last();CommandBuilder.ArtifactName(filename);if(fields[0]!=root+"/"+filename)continue;list.Add(new(filename,fields[0],size,DateTimeOffset.FromUnixTimeSeconds(unix)));
        }
        return list.OrderByDescending(b=>b.Modified).ToArray();
    }
    public async Task<string> DownloadBackupAsync(BackupArtifact artifact,CancellationToken token)
    {
        var current=(await ListBackupsAsync(token)).SingleOrDefault(a=>a.Name==artifact.Name&&a.Path==artifact.Path&&a.Size==artifact.Size&&a.Modified==artifact.Modified)??throw new InvalidOperationException("Backup identity changed. Reload the backup list.");
        if(current.Size<=0)throw new InvalidOperationException("Backup is empty.");Directory.CreateDirectory(Path.Combine(preferences.BackupDirectory,"Database"));var path=Path.Combine(preferences.BackupDirectory,"Database",current.Name);
        if(File.Exists(path))throw new InvalidOperationException("A local file already has that name. Choose a different backup directory or move the existing file; it will not be overwritten.");
        var remoteHash=(await Ssh.ReadAsync("sudo -n sha256sum "+Q(current.Path),token)).Split(' ')[0];
        var result=await Ssh.RunAsync("sudo -n cat "+Q(current.Path),token,TimeSpan.FromMinutes(10),binary:path);result.EnsureSuccess();
        await using var stream=File.OpenRead(path);var hash=Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stream,token));
        if(hash!=remoteHash || stream.Length!=current.Size) { stream.Close();File.Move(path,path+".unverified",true);throw new IOException("Downloaded backup failed size/hash verification. An .unverified file was retained for inspection."); }
        await BackupVerification.RecordAsync(path,current,hash,token);
        return "Database backup downloaded and SHA-256 verified: "+path;
    }
}
