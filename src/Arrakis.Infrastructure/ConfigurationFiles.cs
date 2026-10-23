using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed partial class LiveServerGateway
{
    private readonly Dictionary<string,string> discoveredPaths=new(StringComparer.Ordinal);
    public async Task<IReadOnlyList<ConfigFile>> LoadConfigurationAsync(CancellationToken token)
    {
        if(snapshot.Namespace==null)throw new InvalidOperationException("Connect to the battlegroup before loading settings.");
        var ns=snapshot.Namespace;CommandBuilder.ResourceName(ns);
        var pvcs=J.Parse(await Ssh.ReadAsync("sudo -n kubectl get pvc -n "+Q(ns)+" -l role=igw-server -o json",token))["items"].Items().ToArray();
        if(pvcs.Length!=1)throw new InvalidOperationException("Could not identify a unique server PVC. Use Funcom File Browser; no path was guessed.");
        var volume=pvcs[0].At("spec","volumeName").S();CommandBuilder.ResourceName(volume);
        var pv=J.Parse(await Ssh.ReadAsync("sudo -n kubectl get pv "+Q(volume)+" -o json",token));
        var path=pv.At("spec","local","path").S(pv.At("spec","hostPath","path").S());
        if(pv.At("spec","claimRef","namespace").S()!=ns || pv.At("spec","claimRef","name").S()!=pvcs[0].At("metadata","name").S())throw new InvalidOperationException("PVC/PV identity mismatch.");
        if(!path.StartsWith("/var/lib/rancher/k3s/storage/",StringComparison.Ordinal) || path.Contains("..") || path.Any(c=>c is '\r' or '\n'))throw new InvalidOperationException("Unrecognized persistent-volume path; use the official File Browser.");
        var root=(await Ssh.ReadAsync("sudo -n readlink -f "+Q(path+"/Saved/UserSettings"),token)).Trim();
        if(!root.StartsWith(path+"/Saved/",StringComparison.Ordinal))throw new InvalidOperationException("UserSettings resolves outside the verified server volume.");
        var result=new List<ConfigFile>();discoveredPaths.Clear();
        foreach(var name in new[]{"UserGame.ini","UserEngine.ini","UserServerCustomSettings.ini"})
        {
            var full=root+"/"+name;var bytes=await ReadConfigBytes(full,token);discoveredPaths.Add(name,full);result.Add(new(name,full,bytes,Convert.ToHexStringLower(SHA256.HashData(bytes))));
        }
        return result;
    }
    private async Task<byte[]> ReadConfigBytes(string path,CancellationToken token)
    {
        var command="set -e; test ! -L "+Q(path)+"; test $(sudo -n stat -c %s "+Q(path)+") -le 2000000; sudo -n base64 "+Q(path);
        var spec=Ssh.Build(command) with { OutputLimit=3_000_000 };var result=await runner.RunAsync(spec,null,token);result.EnsureSuccess();return Convert.FromBase64String(result.Output);
    }
    public async Task<ConfigFile> SaveConfigurationAsync(ConfigFile original,byte[] proposed,CancellationToken token)
    {
        await EnsureCliAsync(token);
        if(!discoveredPaths.TryGetValue(original.Name,out var path)||path!=original.RemotePath)throw new InvalidOperationException("Configuration target was not discovered in this session. Reload settings.");
        var text=(original with{Bytes=proposed}).Text;ConfigurationIntegrity.ValidateProposal(original,text);
        var current=await ReadConfigBytes(path,token);if(!SHA256.HashData(current).SequenceEqual(SHA256.HashData(original.Bytes)))throw new ConfigurationSaveException(new(original.Name,"original-check","An external change was detected before any save command was submitted.","Loaded and current configuration hashes differ.",ConfigurationFileState.VerifiedExternalChange,"Reload the current configuration and review the draft again; nothing was overwritten.",$"Target: {path}\nLoaded SHA-256: {Convert.ToHexStringLower(SHA256.HashData(original.Bytes))}\nCurrent SHA-256: {Convert.ToHexStringLower(SHA256.HashData(current))}"));
        return await ConfigurationSaveTransaction.ExecuteAsync(
            original, proposed,
            () => new ConfigBackupStore(Path.Combine(preferences.BackupDirectory,"Configuration")).Save(original),
            (plan, input, cancellation) => Ssh.RunAsync("sudo -n sh -c " + Q(plan.Script), cancellation, TimeSpan.FromSeconds(40), input),
            cancellation => ReadConfigBytes(path, cancellation), token);
    }
}

public sealed record ConfigBackupEnvelope(string Name,string Target,string Hash,DateTimeOffset Created,byte[] ProtectedBytes);
public sealed class ConfigBackupStore(string directory)
{
    public string Save(ConfigFile file)
    {
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+"-"+file.Name+".asmconfig");
        var data=new ConfigBackupEnvelope(file.Name,file.RemotePath,file.Hash,DateTimeOffset.Now,WindowsProtection.Protect(file.Bytes));File.WriteAllText(path,JsonSerializer.Serialize(data));return path;
    }
    public ConfigFile Read(string path)
    {
        var envelope=JsonSerializer.Deserialize<ConfigBackupEnvelope>(File.ReadAllText(path))??throw new InvalidOperationException("Invalid backup.");var bytes=WindowsProtection.Unprotect(envelope.ProtectedBytes);if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=envelope.Hash)throw new InvalidOperationException("Backup hash mismatch.");return new(envelope.Name,envelope.Target,bytes,envelope.Hash);
    }
}
public static class WindowsProtection
{
    [StructLayout(LayoutKind.Sequential)]private struct Blob { public int Size;public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CryptProtectData(ref Blob input,string description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr ptr);
    public static byte[] Protect(byte[] bytes)=>Transform(bytes,true);
    public static byte[] Unprotect(byte[] bytes)=>Transform(bytes,false);
    private static byte[] Transform(byte[] bytes,bool encrypt)
    {
        var input=new Blob{Size=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};try { Marshal.Copy(bytes,0,input.Data,bytes.Length);var ok=encrypt?CryptProtectData(ref input,"Arrakis configuration backup",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out var output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);if(!ok)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());try { var result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result; }finally{LocalFree(output.Data);} }finally{Marshal.FreeHGlobal(input.Data);}
    }
}
