using System.Diagnostics;
using System.Text;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, IProgress<string>? progress, CancellationToken token)
    {
        var start = new ProcessStartInfo(spec.FileName) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardErrorEncoding=Encoding.UTF8 };
        foreach(var argument in spec.Arguments)start.ArgumentList.Add(argument);
        using var process=new Process { StartInfo=start };
        if(!process.Start())throw new InvalidOperationException("Could not launch "+Path.GetFileName(spec.FileName));
        using var timeout=new CancellationTokenSource(spec.Timeout);using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,timeout.Token);
        var stdout=new StringBuilder();var stderr=new StringBuilder();
        async Task Drain(StreamReader reader,StringBuilder buffer)
        {
            var chars=new char[2048];int read;
            while((read=await reader.ReadAsync(chars.AsMemory(),linked.Token))>0) { var text=new string(chars,0,read); buffer.Append(text); if(buffer.Length>spec.OutputLimit)buffer.Remove(0,buffer.Length-spec.OutputLimit);progress?.Report(Redactor.Clean(text)); }
        }
        var temporary=spec.BinaryOutput is null?null:spec.BinaryOutput+".part-"+Guid.NewGuid().ToString("N");
        try
        {
            Task outputTask;
            if(temporary!=null) { Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);outputTask=CopyBinary(); }
            else outputTask=Drain(process.StandardOutput,stdout);
            var errorTask=Drain(process.StandardError,stderr);
            try { if(spec.Input!=null) await process.StandardInput.WriteAsync(spec.Input.AsMemory(),linked.Token); }
            catch(IOException) { /* Child may reject input and exit; preserve its error/exit code. */ }
            finally { process.StandardInput.Close(); }
            await Task.WhenAll(process.WaitForExitAsync(linked.Token),outputTask,errorTask);
            if(temporary!=null && process.ExitCode==0)File.Move(temporary,spec.BinaryOutput!,true);
            return new(process.ExitCode,stdout.ToString(),stderr.ToString());
        }
        catch(OperationCanceledException) { try { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }catch(InvalidOperationException){} if(token.IsCancellationRequested)throw;throw new TimeoutException($"{Path.GetFileName(spec.FileName)} exceeded {spec.Timeout.TotalSeconds:0} seconds."); }
        finally { if(temporary!=null && File.Exists(temporary))File.Delete(temporary); }
        async Task CopyBinary() { await using var file=new FileStream(temporary!,FileMode.CreateNew,FileAccess.Write,FileShare.None);await process.StandardOutput.BaseStream.CopyToAsync(file,linked.Token);await file.FlushAsync(linked.Token); }
    }
}
public static class CommandBuilder
{
    public static string ShellQuote(string text) { if(text.Contains('\0'))throw new ArgumentException("NUL is invalid.");return "'"+text.Replace("'","'\"'\"'")+"'"; }
    public static string PowerShellQuote(string text)=>"'"+text.Replace("'","''")+"'";
    public static ProcessSpec PowerShell(string script,TimeSpan? timeout=null)=>new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),["-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.UTF8Encoding]::new();try { "+script+" } catch { [Console]::Error.WriteLine($_.Exception.Message);exit 1 }"))],timeout??TimeSpan.FromSeconds(25));
    public static void ResourceName(string value) { if(!System.Text.RegularExpressions.Regex.IsMatch(value,@"^[a-z0-9][a-z0-9.-]{0,252}$"))throw new ArgumentException("Unsupported Kubernetes resource name."); }
    public static void ArtifactName(string value) { if(!System.Text.RegularExpressions.Regex.IsMatch(value,@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,170}$") || value.Contains(".."))throw new ArgumentException("Backup name must contain only letters, numbers, periods, underscores and hyphens."); }
}
