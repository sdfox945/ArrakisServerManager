using System.Net;
using System.Security.Cryptography;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed class SshTransport(IProcessRunner runner, Preferences preferences, string dataRoot)
{
    public string? Ip { get; set; }
    public string KnownHosts => Path.Combine(dataRoot,"known_hosts");
    public IReadOnlyList<string> Options()
    {
        if(!IPAddress.TryParse(Ip,out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)throw new InvalidOperationException("No VM IPv4 address is available. Refresh discovery or enter the address in Tools.");
        if(!File.Exists(preferences.SshKeyPath))throw new InvalidOperationException("SSH private key was not found. Select the existing key in Tools. Initial setup is never run automatically.");
        var systemHosts=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".ssh","known_hosts").Replace('\\','/');
        var appHosts=KnownHosts.Replace('\\','/');
        return ["-o","BatchMode=yes","-o","StrictHostKeyChecking=yes","-o","IdentitiesOnly=yes","-o","ConnectTimeout=8","-o","ServerAliveInterval=10","-o","ServerAliveCountMax=2","-o",$"UserKnownHostsFile=\"{appHosts}\" \"{systemHosts}\"","-i",preferences.SshKeyPath];
    }
    public ProcessSpec Build(string command,TimeSpan? timeout=null,string? input=null,string? binary=null,bool terminal=false)
    {
        var args=Options().ToList();args.Add(terminal?"-tt":"-T");args.Add("dune@"+Ip);args.Add(command);
        return new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"OpenSSH","ssh.exe"),args,timeout??TimeSpan.FromSeconds(35),input,binary);
    }
    public Task<ProcessResult> RunAsync(string command,CancellationToken token,TimeSpan? timeout=null,string? input=null,IProgress<string>? progress=null,string? binary=null)=>runner.RunAsync(Build(command,timeout,input,binary),progress,token);
    public async Task<string> ReadAsync(string command,CancellationToken token) { var result=await RunAsync(command,token);result.EnsureSuccess();return result.Output; }
    public async Task<(string Keys,string Fingerprints)> ScanAsync(CancellationToken token)
    {
        if(!IPAddress.TryParse(Ip,out _))throw new InvalidOperationException("Set a valid VM address first.");
        var result=await runner.RunAsync(new("ssh-keyscan.exe",["-T","5","-t","ed25519",Ip!],TimeSpan.FromSeconds(10)),null,token);result.EnsureSuccess();
        var lines=result.Output.Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(l=>!l.StartsWith('#')).Select(l=>l.Trim()).ToArray();
        if(lines.Length!=1)throw new InvalidOperationException("Expected one Ed25519 identity; no host trust was changed.");
        var fields=lines[0].Split(' ',StringSplitOptions.RemoveEmptyEntries);if(fields.Length!=3 || fields[0]!=Ip || fields[1]!="ssh-ed25519")throw new InvalidOperationException("Unexpected SSH key-scan response.");
        var fp=Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(fields[2]))).TrimEnd('=');
        return(lines[0]+"\n","ED25519 SHA256:"+fp);
    }
    public async Task TrustAsync(string keys,CancellationToken token)
    {
        // Refuse replacement of any identity already pinned in either trust store.
        foreach(var file in new[]{KnownHosts,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".ssh","known_hosts")})
        {
            if(!File.Exists(file))continue;
            var found=await runner.RunAsync(new("ssh-keygen.exe",["-F",Ip!,"-f",file],TimeSpan.FromSeconds(10)),null,token);
            if(found.ExitCode==0 && found.Output.Split('\n').Any(l=>!l.StartsWith('#')&&l.Trim().Length>0))throw new InvalidOperationException("This host already has a pinned identity. Changed keys require independent investigation and manual trust-store maintenance.");
        }
        Directory.CreateDirectory(dataRoot);await File.AppendAllTextAsync(KnownHosts,keys,token);
    }
}
