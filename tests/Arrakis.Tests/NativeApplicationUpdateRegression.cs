using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Arrakis.Core;
using Arrakis.Desktop;
using Arrakis.Desktop.ViewModels;
using Arrakis.Infrastructure;

namespace Arrakis.Tests;

internal static partial class NativeConfigurationRegression
{
    public static void RegisterApplicationUpdates(Action<string,Func<Task>> add,string root)
    {
        add("Native app updates: assembly version and successful equal/older checks render up to date",async()=>
        {
            using var c=await UpdateContext.Create(root,"up-to-date");foreach(var version in new[]{"1.2.0","1.1.2"})
            {
                c.Http.Json=ApplicationUpdateRegression.Metadata(version);await c.Updates.CheckAsync(true);Check(c.FooterText=="v1.2.0 · Up to date"&&!c.Updates.HasUpdate&&!c.Updates.DownloadCommand.CanExecute(null),"Equal/older releases must never offer a downgrade.");
            }
            Check(c.Updates.InstalledVersion.Core==SemanticVersion.FromAssembly(typeof(MainWindow).Assembly).Core,"Sidebar must derive from actual application assembly metadata.");await c.Capture("up-to-date.png");
        });
        add("Native app updates: newer patch minor and major releases notify without downloading",async()=>
        {
            using var c=await UpdateContext.Create(root,"available");foreach(var version in new[]{"1.2.1","1.3.0","2.0.0"})
            {
                c.Http.Json=ApplicationUpdateRegression.Metadata(version);await c.Updates.CheckAsync(true);Check(c.FooterText=="Update available · v"+version&&c.Updates.CanDownload,"Newer stable releases need a clickable notice.");
            }
            Check(c.Http.Requests.All(u=>u==ApplicationUpdateSource.LatestApi)&&!Directory.Exists(c.Updates.DownloadDirectory),"Checking cannot start downloads.");await c.Capture("update-available.png");
        });
        add("Native app updates: checking and offline states never display false up-to-date",async()=>
        {
            using var c=await UpdateContext.Create(root,"offline");var hold=new TaskCompletionSource<HttpResponseMessage>();c.Http.Reply=(_,_)=>hold.Task;var work=c.Updates.CheckAsync(true);await Pump();Check(c.FooterText=="Checking for updates…"&&c.Updates.FooterTooltip.Contains("v1.2.0"),"Checking must remain asynchronous and retain installed metadata.");hold.SetResult(new(HttpStatusCode.ServiceUnavailable));await work;await Pump();Check(c.FooterText=="v1.2.0 · Check unavailable"&&!c.Updates.StatusMessage.Contains("Up to date"),"Offline state must not pretend success.");await c.Capture("offline.png");
        });
        add("Native app updates: footer opens actual modal details and release notes stay inert plaintext",async()=>
        {
            using var c=await UpdateContext.Create(root,"details");await c.Updates.CheckAsync(true);Exception? failure=null;var inspected=false;var finished=new TaskCompletionSource();
            _=c.Window.Dispatcher.BeginInvoke(async()=>
            {
                ApplicationUpdateWindow? dialog=null;try
                {
                    await Until(()=>c.Window.OwnedWindows.OfType<ApplicationUpdateWindow>().Any());dialog=c.Window.OwnedWindows.OfType<ApplicationUpdateWindow>().Single();await Pump();var notes=Descendants(dialog).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Application release notes");
                    Check(notes.IsReadOnly&&notes.Text.Contains("<script>neverExecute()</script>")&&notes.Text.Contains("powershell.exe"),"Untrusted notes must be plain selectable text, not executable markup.");Check(!Descendants(dialog).Any(d=>d is WebBrowser)&&c.Ui.Opens.Count==0,"Opening details cannot execute links or commands.");Check(c.Updates.LatestVersion=="1.2.1"&&c.Updates.AssetCaption.Contains("win-x64.zip")&&c.Updates.VerificationCaption.Contains("SHA-256"),"Version/asset/verification metadata must be displayed.");CaptureWindow(dialog,Path.Combine(c.Evidence,"details-dialog.png"));Invoke(UpdateContext.Button(dialog,"View on GitHub"));await Until(()=>c.Ui.Opens.Count==1);Check(c.Ui.Opens.Single()==c.Updates.BrowserUrl.AbsoluteUri&&ApplicationUpdateSource.IsReleasePage(new(c.Ui.Opens[0])),"Only a validated official HTTPS release URL may reach the browser interaction.");inspected=true;
                }
                catch(Exception e){failure=e;}
                finally{dialog?.Close();finished.TrySetResult();}
            });
            Invoke(c.Footer);await finished.Task.WaitAsync(TimeSpan.FromSeconds(8));if(failure!=null)throw failure;Check(inspected,"The footer must actually open the dedicated modal window.");
        });
        add("Native app updates: manual command bypasses cache and remains separate from server updates",async()=>
        {
            using var c=await UpdateContext.Create(root,"manual");await c.Updates.CheckAsync(false);await c.Updates.CheckAsync(false);Check(c.Http.Requests.Count==1,"Ordinary checks should cache.");c.Vm.SelectedPage=5;await Pump();Invoke(c.Button("Check for application updates"));await Until(()=>!c.Updates.IsChecking&&c.Http.Requests.Count==2);Check(!ReferenceEquals(c.Vm.CheckUpdatesCommand,c.Updates.CheckCommand)&&c.Vm.History.Count==0&&!c.Vm.IsBusy,"App checks cannot use the Funcom gateway/history operation identity.");await c.Capture("tools-application-updates.png");
        });
        add("Native app updates: disabled automatic checks persist without saving unrelated form edits",async()=>
        {
            using var c=await UpdateContext.Create(root,"preferences");c.Vm.SelectedPage=5;await Pump();c.Vm.Preferences.VmName="unsaved-connection-edit";var check=Descendants(c.Window).OfType<CheckBox>().Single(b=>b.Content as string=="Automatically check GitHub for Arrakis updates");((IToggleProvider)new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)).Toggle();await Pump();
            Check(!c.Stores.Load().AutomaticallyCheckApplicationUpdates&&c.Stores.Load().VmName=="dune-awakening","Only the update preference may be persisted by this checkbox.");await c.Updates.InitializeAsync();Check(c.Http.Requests.Count==0,"Disabled startup checks must make no requests.");Invoke(c.Button("Check for application updates"));await Until(()=>!c.Updates.IsChecking&&c.Http.Requests.Count==1);Check(c.Updates.HasUpdate,"Manual checking remains available when automatic checks are disabled.");await c.Capture("automatic-checks-off.png");
        });
        add("Native app updates: automatic check starts after rendering and is independent of Dashboard refresh",async()=>
        {
            using var c=await UpdateContext.Create(root,"startup",startup:true);Check(c.Window.IsVisible,"The native window must render before a network check completes.");await Until(()=>c.Updates.HasUpdate);var count=c.Http.Requests.Count;await c.Vm.RefreshAsync();await c.Vm.RefreshAsync(true);Check(c.Http.Requests.Count==count&&!c.Vm.IsBusy,"Dashboard refreshes cannot repeat update requests or wait for them.");await c.Capture("startup.png");
        });
        add("Native app updates: streaming progress cancellation cleans partial and permits retry",async()=>
        {
            using var c=await UpdateContext.Create(root,"cancel");await c.Updates.CheckAsync(true);c.SlowDownloads();var dialog=new ApplicationUpdateWindow(c.Updates){Owner=c.Window};dialog.Show();await Pump();Invoke(UpdateContext.Button(dialog,"Download Update"));await Until(()=>c.Updates.IsDownloading&&c.Updates.DownloadedBytes>0);Check(c.Updates.DownloadPercent>0&&c.Updates.DownloadPercent<100&&c.Updates.VerifiedDownload==null,"Progress must not imply a verified result.");CaptureWindow(dialog,Path.Combine(c.Evidence,"download-progress.png"));Invoke(UpdateContext.Button(dialog,"Cancel download"));await Until(()=>!c.Updates.IsDownloading);Check(c.Updates.StatusMessage.Contains("cancelled")&&c.Updates.VerifiedDownload==null&&Directory.GetFiles(c.Updates.DownloadDirectory).Length==0,"Cancel must leave no completed ZIP or owned partial.");Check(c.Updates.CanDownload,"Cancellation must allow a new attempt.");dialog.Close();
        });
        add("Native app updates: successful verified download enables folder/path actions only",async()=>
        {
            using var c=await UpdateContext.Create(root,"verified");await c.Updates.CheckAsync(true);var alternate=Path.Combine(c.Evidence,"chosen-downloads");c.Ui.NextFolder=alternate;var dialog=new ApplicationUpdateWindow(c.Updates){Owner=c.Window};dialog.Show();await Pump();Invoke(UpdateContext.Button(dialog,"Choose folder…"));await Until(()=>c.Updates.DownloadDirectory==alternate);Check(c.Updates.DownloadDirectory==alternate,"Destination selection should remain independent of backups/logs.");Invoke(UpdateContext.Button(dialog,"Download Update"));await Until(()=>!c.Updates.IsDownloading&&c.Updates.VerifiedDownload!=null&&UpdateContext.Button(dialog,"Open Download Folder").IsEnabled&&UpdateContext.Button(dialog,"Copy Download Path").IsEnabled);Check(c.Updates.VerificationCaption=="Download complete — SHA-256 verified"&&c.Updates.DownloadPercent==100,"Success requires explicit verified completion.");Invoke(UpdateContext.Button(dialog,"Open Download Folder"));Invoke(UpdateContext.Button(dialog,"Copy Download Path"));await Until(()=>c.Ui.Opens.Count==1&&c.Ui.Copies.Count==1);Check(c.Ui.Opens.Single()==alternate&&c.Ui.Copies.Single()==c.Updates.VerifiedDownload!.Path,"Only the folder and path can be opened/copied; ZIP/executable must never launch.");Check(!c.Ui.Opens.Any(p=>p.EndsWith(".zip")||p.EndsWith(".exe"))&&c.Vm.History.Count==0,"No downloaded content or server operation may execute.");CaptureWindow(dialog,Path.Combine(c.Evidence,"verified-completion.png"));dialog.Close();
        });
        add("Native app updates: checksum mismatch never enables verified completion actions",async()=>
        {
            using var c=await UpdateContext.Create(root,"mismatch");await c.Updates.CheckAsync(true);c.Http.Reply=(q,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(ApplicationUpdateRegression.Bytes.Select((b,i)=>i==0?(byte)(b^1):b).ToArray())});await c.Updates.DownloadAsync();
            Check(c.Updates.StatusMessage.Contains("SHA-256 mismatch")&&c.Updates.VerifiedDownload==null&&!c.Updates.OpenDownloadFolderCommand.CanExecute(null)&&!c.Updates.CopyDownloadPathCommand.CanExecute(null),"Hash failure must prevent all success actions.");Check(Directory.GetFiles(c.Updates.DownloadDirectory).Length==0,"Invalid owned partial must be removed.");await c.Capture("mismatch.png");
        });
        add("Native app updates: missing digest and invalid metadata keep safe browser fallback",async()=>
        {
            using var c=await UpdateContext.Create(root,"invalid");c.Http.Json["assets"]![0]!["digest"]=null;await c.Updates.CheckAsync(true);Check(c.Updates.HasUpdate&&!c.Updates.CanDownload&&c.Updates.VerificationCaption.Contains("unavailable"),"No reliable digest means no in-app verified download.");c.Http.Json["assets"]=new System.Text.Json.Nodes.JsonArray();await c.Updates.CheckAsync(true);Check(c.Updates.State==ApplicationUpdateState.Unavailable&&ApplicationUpdateSource.IsReleasePage(c.Updates.BrowserUrl)&&!c.Updates.CanDownload,"Missing assets must fail safely with an official fallback.");await c.Capture("verification-unavailable.png");
        });
        add("Native app updates: unknown server outcome remains guarded across app update failure/success",async()=>
        {
            using var c=await UpdateContext.Create(root,"server-guard");await c.Vm.RunAsync("Isolated server outcome",true,_=>throw new OutcomeUnknownException("simulated uncertain server operation"),background:true);Check(c.Vm.OperationState.Contains("Review unknown outcome"),"Fixture must establish the durable server guard.");var history=c.Vm.History.Count;c.Http.Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));await c.Updates.CheckAsync(true);Check(c.Vm.OperationState.Contains("Review unknown outcome")&&c.Vm.History.Count==history,"Network failure cannot alter server history/reconciliation.");c.Http.Reply=null;await c.Updates.CheckAsync(true);await c.Updates.DownloadAsync();Check(c.Updates.VerifiedDownload!=null&&c.Vm.OperationState.Contains("Review unknown outcome")&&c.Vm.History.Count==history,"Local update success cannot acknowledge the server guard.");
        });
        add("Native app updates: closing an active download dialog cancels it without executing anything",async()=>
        {
            using var c=await UpdateContext.Create(root,"close-download");await c.Updates.CheckAsync(true);c.SlowDownloads();var dialog=new ApplicationUpdateWindow(c.Updates){Owner=c.Window};dialog.Show();Invoke(UpdateContext.Button(dialog,"Download Update"));await Until(()=>c.Updates.IsDownloading&&c.Updates.DownloadedBytes>0);dialog.Close();await Until(()=>!dialog.IsVisible&&!c.Updates.IsDownloading);Check(c.Updates.VerifiedDownload==null&&c.Ui.Opens.Count==0,"Closing must cancel/clean up, never execute/install.");
        });
        add("Native app updates: Demo scenario controls remain offline and support verified inert fixtures",async()=>
        {
            using var c=await UpdateContext.Create(root,"demo",demo:true,realDemo:true);c.Vm.SelectedPage=5;await Pump();var choice=Descendants(c.Window).OfType<ComboBox>().Single(b=>AutomationProperties.GetName(b)=="Demo application update scenario");Check(choice.Items.Count==8,"All required simulated scenarios must be selectable.");await SelectCombo(choice,ApplicationUpdateScenario.ApiUnavailable);await Until(()=>!c.Updates.IsChecking&&c.Updates.State==ApplicationUpdateState.Unavailable);await SelectCombo(choice,ApplicationUpdateScenario.VerifiedDownload);await Until(()=>c.Updates.HasUpdate);Check(!c.Updates.ViewOnGitHubCommand.CanExecute(null)&&c.Updates.ReleaseNotes.Contains("DEMO"),"Demo cannot silently open real GitHub content.");await c.Updates.DownloadAsync();Check(c.Updates.VerifiedDownload!=null&&c.Updates.DownloadDirectory.Contains(Path.Combine("Demo","ApplicationUpdates")),"Demo downloads must be inert fixtures under isolated paths.");await c.Capture("demo-scenarios.png");
        });
        add("Native app updates: minimum footer keyboard navigation and supported rendering scales fit",async()=>
        {
            using var c=await UpdateContext.Create(root,"layout");await c.Updates.CheckAsync(true);c.Window.Width=1040;c.Window.Height=700;await Pump();Check(c.Footer.ActualWidth<=186&&c.Footer.ActualHeight<=56&&c.Footer.IsVisible,"Version/update footer must remain compact in the existing sidebar.");c.Footer.Focus();Keyboard.Focus(c.Footer);Check(c.Footer.IsKeyboardFocused&&c.Footer.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),"Footer must support keyboard focus and traversal.");
            foreach(var scale in new[]{1d,1.25,1.5}){var image=new RenderTargetBitmap((int)(c.Window.ActualWidth*scale),(int)(c.Window.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(c.Window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(c.Evidence,$"footer-{scale:0.00}.png"));encoder.Save(file);}await c.Capture("minimum-window.png");
        });
        add("Native app updates: mode changes discard late check results and cancel the old service",async()=>
        {
            var held=new HeldUpdateService();var second=new SimulatedApplicationUpdateService(SemanticVersion.Parse("1.2.0"));using var c=await UpdateContext.Create(root,"mode-race",factory:demo=>demo?second:held);var work=c.Updates.CheckAsync(true);await Pump();c.Updates.SetMode(true);held.Result.SetResult(new(GitHubApplicationUpdateService.ParseRelease(ApplicationUpdateRegression.Metadata().ToJsonString()),DateTimeOffset.UtcNow,false));await work;Check(c.Updates.IsDemo&&c.Updates.Release==null&&!c.Updates.HasUpdate&&held.Disposed,"Late responses cannot poison a new mode or its cache.");c.Updates.SetMode(false);Check(!c.Updates.IsDemo,"Repeated mode changes must not cancel disposed token sources.");
        });
    }
    private sealed class HeldUpdateService : IApplicationUpdateService
    {
        public readonly TaskCompletionSource<ApplicationUpdateCheck> Result=new();public bool Disposed;
        public Task<ApplicationUpdateCheck> CheckAsync(bool force,CancellationToken token)=>Result.Task;
        public Task<VerifiedApplicationDownload> DownloadAsync(ApplicationRelease release,string directory,IProgress<ApplicationDownloadProgress>? progress,CancellationToken token)=>throw new InvalidOperationException("No download fixture configured.");
        public void Dispose()=>Disposed=true;
    }
    private sealed class UpdateContext : IDisposable
    {
        public required MainWindow Window;public required ShellViewModel Vm;public required ApplicationUpdateRegression.Handler Http;public required UpdateUi Ui;public required string Evidence;public required LocalStores Stores;
        public ApplicationUpdateViewModel Updates=>Vm.ApplicationUpdates;
        public Button Footer=>Descendants(Window).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Arrakis application version and updates");
        public string FooterText=>Descendants(Footer).OfType<TextBlock>().Last().Text;
        public Button Button(string text)=>Button(Window,text);
        public static Button Button(Window window,string text)=>Descendants(window).OfType<Button>().Single(b=>b.Content as string==text);
        public static async Task<UpdateContext> Create(string root,string name,bool demo=false,bool realDemo=false,bool startup=false,Func<bool,IApplicationUpdateService>? factory=null)
        {
            if(realDemo&&!demo)throw new InvalidOperationException("Default real-network update service is forbidden in tests.");
            if(Application.Current==null){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/ArrakisServerManager;component/Theme.xaml",UriKind.Relative)});}
            var path=Path.Combine(root,"native-app-updates",name);Directory.CreateDirectory(path);var stores=new LocalStores(Path.Combine(path,"data"));var ui=new UpdateUi();var http=new ApplicationUpdateRegression.Handler();factory??=realDemo?null:_=>new GitHubApplicationUpdateService(new MemoryApplicationUpdateCache(),http);
            var vm=new ShellViewModel(ui,stores,demo,_=>new SimulatedServerGateway(new(){BackupDirectory=Path.Combine(path,"server-backups"),LogDirectory=Path.Combine(path,"server-logs")},Path.Combine(path,"server")),factory);var window=new MainWindow{DataContext=vm};if(startup)window.Configure(stores,vm,false);window.Show();window.Activate();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);return new(){Window=window,Vm=vm,Http=http,Ui=ui,Evidence=path,Stores=stores};
        }
        public void SlowDownloads()=>Http.Reply=(request,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri==ApplicationUpdateSource.LatestApi?new StringContent(Http.Json.ToJsonString()):new ApplicationUpdateRegression.UnknownContent(new ApplicationUpdateRegression.ControlledStream(ApplicationUpdateRegression.Bytes,40))});
        public Task Capture(string name){CaptureWindow(Window,Path.Combine(Evidence,name));return Task.CompletedTask;}
        public void Dispose(){Vm.Dispose();foreach(var window in Window.OwnedWindows.OfType<Window>().ToArray())window.Close();Window.Close();}
    }
    private sealed class UpdateUi : IUserInteraction
    {
        public List<string> Opens=[],Copies=[];public string? NextFolder;
        public void Open(string path)=>Opens.Add(path);public void Copy(string text)=>Copies.Add(text);
        public string? ChooseFolder(string title,string initial)=>NextFolder;public string? ChooseFile(string title,string filter)=>null;
        public bool Confirm(string title,string message)=>throw new InvalidOperationException("App updater must not request server mutation confirmation.");
        public bool Review(string title,string summary,string diff)=>throw new InvalidOperationException("App updater cannot enter server review.");
        public bool ReviewRawRemovals(string title,string summary,string diff,string fileName)=>throw new InvalidOperationException();
        public void Preview(string title,string summary,string diff)=>throw new InvalidOperationException();
        public void ShowError(string title,string summary,string details)=>throw new InvalidOperationException(summary);
    }
}
