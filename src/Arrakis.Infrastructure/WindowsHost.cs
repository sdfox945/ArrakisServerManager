using System.Diagnostics;
using System.Runtime.InteropServices;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed class WindowsHost(IProcessRunner runner,Preferences preferences,string dataRoot)
{
    public static bool IsAdministrator => OperatingSystem.IsWindows() && new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    [StructLayout(LayoutKind.Sequential)]private struct Memory { public uint Length,Load;public ulong Total,Available,PageTotal,PageAvailable,VirtualTotal,VirtualAvailable,Extended; }
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GlobalMemoryStatusEx(ref Memory status);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    private long lastIdle,lastTotal;
    public HostStatus ReadHost()
    {
        var m=new Memory { Length=(uint)Marshal.SizeOf<Memory>() };var ok=GlobalMemoryStatusEx(ref m);double? cpu=null;
        if(GetSystemTimes(out var idle,out var kernel,out var user)) { var total=kernel+user;if(lastTotal>0&&total>lastTotal)cpu=Math.Clamp(100.0*(1-(double)(idle-lastIdle)/(total-lastTotal)),0,100);lastTotal=total;lastIdle=idle; }
        bool? game;try{game=Process.GetProcessesByName("DuneSandbox-Win64-Shipping").Length>0;}catch{game=null;}
        return new(ok?(long)m.Total:null,ok?(long)m.Available:null,cpu,game,ok?null:"Windows memory API unavailable.");
    }
    public string ReadVmScript => "$vm=Get-VM -Name "+CommandBuilder.PowerShellQuote(preferences.VmName)+" -ErrorAction Stop;$a=Get-VMNetworkAdapter -VM $vm | Select-Object -First 1;[pscustomobject]@{Name=$vm.Name;State=$vm.State.ToString();Configured=$vm.MemoryStartup;Assigned=$vm.MemoryAssigned;Demand=$vm.MemoryDemand;Uptime=$vm.Uptime.ToString();Ip=($a.IPAddresses|Where-Object{$_ -match '^\\d+\\.\\d+\\.\\d+\\.\\d+$'}|Select-Object -First 1);Adapter=($a.Name+' / '+$a.SwitchName)}|ConvertTo-Json -Compress";
    public async Task<VmStatus> ReadVmAsync(CancellationToken token)
    {
        try { var result=await runner.RunAsync(CommandBuilder.PowerShell(ReadVmScript),null,token);result.EnsureSuccess();return ParseVm(result.Output); }
        catch(Exception e)when(e is not OperationCanceledException) { return new(preferences.VmName,Ip:ResolveFallbackIp(),Error:Redactor.Clean(e.Message)); }
    }
    public VmStatus ParseVm(string json)
    {
        var n=J.Parse(json);return new(n["Name"].S(preferences.VmName),n["State"].S("Unknown"),n["Configured"].L(),n["Assigned"].L(),n["Demand"].L(),n["Uptime"].S("Unavailable"),n["Ip"].S() is {Length:>0} ip?ip:ResolveFallbackIp(),n["Adapter"].S("Unavailable"));
    }
    private string? ResolveFallbackIp()
    {
        if(preferences.ManualIp.Length>0)return preferences.ManualIp;
        try
        {
            var logs=Path.Combine(preferences.InstallationDirectory,".logs");
            foreach(var path in Directory.EnumerateFiles(logs,"battlegroup-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
            {
                try { using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);var text=reader.ReadToEnd();var found=System.Text.RegularExpressions.Regex.Matches(text,@"(?:VM ready at |VM IP address: )(\d+\.\d+\.\d+\.\d+)");if(found.Count>0)return found[^1].Groups[1].Value; }catch(IOException){continue;}
            }
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        return null;
    }
    public async Task<string> VmActionAsync(ServerAction action,CancellationToken token)
    {
        try{if(IsAdministrator){var result=await runner.RunAsync(CommandBuilder.PowerShell(BuildVmActionScript(action)),null,token);result.EnsureSuccess();if(!result.Output.Contains("Verified VM state:"))throw new OutcomeUnknownException("VM completion marker was not returned.");return result.Output;}return await ElevatedAsync(BuildVmActionScript(action),token);}
        catch(System.ComponentModel.Win32Exception e)when(e.NativeErrorCode==1223){throw new InvalidOperationException("Windows administrator approval was cancelled. No VM command was launched.",e);}
        catch(Exception e)when(e is not OutcomeUnknownException){throw new OutcomeUnknownException("VM operation did not report a verified state. Inspect Hyper-V before retrying. "+e.Message,e);}
    }
    public string BuildVmActionScript(ServerAction action)
    {
        // Stop-VM's default parameter set requests guest shutdown. There is no -Shutdown switch.
        var command=action switch {ServerAction.StartVm=>"Start-VM -Name ",ServerAction.ShutdownVm=>"Stop-VM -Name ",ServerAction.ForceOffVm=>"Stop-VM -TurnOff -Force -Name ",_=>throw new ArgumentOutOfRangeException(nameof(action))};
        var name=CommandBuilder.PowerShellQuote(preferences.VmName);var expected=action==ServerAction.StartVm?"Running":"Off";
        var required=action==ServerAction.StartVm?"Off":"Running";
        return "$before=(Get-VM -Name "+name+" -ErrorAction Stop).State.ToString();if($before -ne '"+required+"'){throw 'VM state changed; refresh and review again.'};"+command+name+" -Confirm:$false -ErrorAction Stop | Out-Null;$deadline=(Get-Date).AddSeconds(120);do { $state=(Get-VM -Name "+name+" -ErrorAction Stop).State.ToString();if($state -eq '"+expected+"'){Write-Output 'Verified VM state: "+expected+"';return};Start-Sleep -Seconds 2 } while((Get-Date) -lt $deadline);throw 'Requested VM state was not observed before timeout.'";
    }
    public async Task<string> ElevatedAsync(string script,CancellationToken token)
    {
        // UAC elevates only this fixed, application-generated Hyper-V command, never a general shell or the GUI.
        Directory.CreateDirectory(Path.Combine(dataRoot,"elevation"));var receipt=Path.Combine(dataRoot,"elevation",Guid.NewGuid().ToString("N")+".json");
        var wrapped="try { $v= & { "+script+" }; @{Ok=$true;Output=($v|Out-String)}|ConvertTo-Json|Set-Content -LiteralPath "+CommandBuilder.PowerShellQuote(receipt)+" -Encoding UTF8 } catch { @{Ok=$false;Output=$_.Exception.Message}|ConvertTo-Json|Set-Content -LiteralPath "+CommandBuilder.PowerShellQuote(receipt)+" -Encoding UTF8; exit 1 }";
        var spec=CommandBuilder.PowerShell(wrapped);var start=new ProcessStartInfo(spec.FileName){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};foreach(var a in spec.Arguments)start.ArgumentList.Add(a);
        using var process=Process.Start(start)??throw new InvalidOperationException("Windows elevation was cancelled.");
        try { using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(3));await process.WaitForExitAsync(timeout.Token);var n=J.Parse(await File.ReadAllTextAsync(receipt,token));if(n["Ok"].S()!="true")throw new InvalidOperationException(n["Output"].S());return n["Output"].S(); }
        catch(OperationCanceledException e){throw new OutcomeUnknownException("Elevated operation is still unresolved. Inspect Hyper-V before retrying.",e);}
        finally { if(File.Exists(receipt))File.Delete(receipt); }
    }
}
