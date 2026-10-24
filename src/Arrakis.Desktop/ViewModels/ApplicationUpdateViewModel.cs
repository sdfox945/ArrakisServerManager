using System.IO;
using System.Windows.Input;
using Arrakis.Core;
using Arrakis.Infrastructure;

namespace Arrakis.Desktop.ViewModels;

public enum ApplicationUpdateState { NotChecked,Checking,UpToDate,Available,Unavailable }
public enum ApplicationDownloadState { None,Downloading,Canceled,Failed,Verified }

public sealed class ApplicationUpdateViewModel : Bindable,IDisposable
{
    private readonly IUserInteraction ui;
    private readonly LocalStores stores;
    private readonly Preferences preferences;
    private readonly Func<bool,IApplicationUpdateService> factory;
    private readonly CancellationTokenSource lifetime=new();
    private IApplicationUpdateService service;
    private CancellationTokenSource? checkCancellation,downloadCancellation;
    private bool initialized,checkingAutomatically,disposed;
    private int generation;
    private string downloadDirectory="";
    private string downloadFailureSummary="";
    public ApplicationUpdateViewModel(IUserInteraction ui,LocalStores stores,Preferences preferences,bool demo,Func<bool,IApplicationUpdateService>? factory=null,SemanticVersion? installed=null)
    {
        this.ui=ui;this.stores=stores;this.preferences=preferences;InstalledVersion=installed??SemanticVersion.FromAssembly(typeof(ApplicationUpdateViewModel).Assembly);
        this.factory=factory??(simulate=>simulate?new SimulatedApplicationUpdateService(InstalledVersion):new GitHubApplicationUpdateService(new FileApplicationUpdateCache(stores.Root),forbiddenDirectories:ProtectedDirectories()));
        IsDemo=demo;service=this.factory(demo);ResetDirectory();
        CheckCommand=new AsyncCommand(()=>CheckAsync(true),()=>!IsChecking&&!IsDownloading);
        DownloadCommand=new AsyncCommand(DownloadAsync,()=>CanDownload);
        CancelDownloadCommand=new RelayCommand(()=>downloadCancellation?.Cancel(),()=>IsDownloading);
        BrowseDestinationCommand=new RelayCommand(()=>LocalAction(()=>{var chosen=ui.ChooseFolder("Arrakis application-update downloads",DownloadDirectory);if(chosen!=null)DownloadDirectory=ApplicationDownloadPaths.Validate(chosen,ProtectedDirectories());}),()=>!IsDownloading);
        ViewOnGitHubCommand=new RelayCommand(()=>LocalAction(()=>{if(!IsDemo&&ApplicationUpdateSource.IsReleasePage(BrowserUrl))ui.Open(BrowserUrl.AbsoluteUri);}),()=>!IsDemo);
        OpenDownloadFolderCommand=new RelayCommand(()=>LocalAction(()=>ui.Open(Path.GetDirectoryName(VerifiedDownload!.Path)!)),()=>VerifiedDownload!=null&&!IsDownloading);
        CopyDownloadPathCommand=new RelayCommand(()=>LocalAction(()=>ui.Copy(VerifiedDownload!.Path)),()=>VerifiedDownload!=null&&!IsDownloading);
        CopyDiagnosticsCommand=new RelayCommand(()=>LocalAction(()=>ui.Copy(TechnicalDetails)),()=>HasDiagnostics);
    }
    public SemanticVersion InstalledVersion {get;}
    public string InstalledCaption=>InstalledVersion.Display+" · Windows x64";
    public bool IsDemo {get;private set;}
    public ApplicationUpdateState State {get;private set;}
    public ApplicationRelease? Release {get;private set;}
    public VerifiedApplicationDownload? VerifiedDownload {get;private set;}
    public DateTimeOffset? LastSuccessfulCheck {get;private set;}
    public bool UsedCache {get;private set;}
    public bool IsChecking=>State==ApplicationUpdateState.Checking;
    public bool IsDownloading {get;private set;}
    public ApplicationDownloadState DownloadState {get;private set;}
    public string TechnicalDetails {get;private set;}="";
    public string ActionErrorMessage {get;private set;}="";
    public bool HasActionError=>ActionErrorMessage.Length>0;
    public bool HasDiagnostics=>TechnicalDetails.Length>0;
    public bool ShowDownloadArea=>HasUpdate;
    public bool ShowDestination=>HasUpdate&&VerifiedDownload==null;
    public bool ShowVerifiedFile=>HasUpdate&&VerifiedDownload!=null;
    public bool ShowDownloadProgress=>HasUpdate&&(DownloadState is ApplicationDownloadState.Downloading or ApplicationDownloadState.Canceled||DownloadState==ApplicationDownloadState.Failed&&DownloadedBytes>0);
    public bool ShowDownloadAction=>HasUpdate&&!IsDownloading;
    public bool ShowCheckAction=>State is ApplicationUpdateState.NotChecked or ApplicationUpdateState.Unavailable;
    public string LatestSummaryVersion=>Release?.Version.Display??"Unavailable";
    public string SummaryStatus=>State switch
    {
        ApplicationUpdateState.Checking=>"Checking for application updates…",
        ApplicationUpdateState.Unavailable=>"Unable to check for updates. Your installed application remains available.",
        ApplicationUpdateState.Available=>"A newer version of Arrakis Server Manager is available.",
        ApplicationUpdateState.UpToDate when Release?.Version.CompareTo(InstalledVersion)<0=>"You're running a newer version than the latest published stable release.",
        ApplicationUpdateState.UpToDate=>"You're running the latest stable version.",
        _=>"Application updates have not been checked."
    };
    public string DownloadActionText=>DownloadState switch
    {
        ApplicationDownloadState.Verified=>"Download Again",
        ApplicationDownloadState.Canceled or ApplicationDownloadState.Failed=>"Retry Download",
        _=>"Download Update"
    };
    public string DownloadActionTooltip=>"SHA-256 verification is mandatory. An existing matching ZIP is reverified and reused; nothing is overwritten. Choose a different folder to download another copy.\nNext download folder: "+DownloadDirectory;
    public string DownloadStatusCaption=>DownloadState switch
    {
        ApplicationDownloadState.Downloading=>"Downloading update — SHA-256 verification pending.",
        ApplicationDownloadState.Canceled=>$"Download canceled at {DownloadPercent:0.0}%. No verified ZIP was finalized.",
        ApplicationDownloadState.Failed=>"Download failed — "+(downloadFailureSummary.Length>170?downloadFailureSummary[..167]+"…":downloadFailureSummary),
        ApplicationDownloadState.Verified=>"Download complete — SHA-256 verified.",
        _=>VerificationCaption
    };
    public string NextDownloadCaption=>"Next download folder: "+DownloadDirectory;
    public bool HasUpdate=>State==ApplicationUpdateState.Available&&Release?.Version.CompareTo(InstalledVersion)>0;
    public bool CanDownload=>HasUpdate&&Release?.HasVerification==true&&!IsDownloading&&!IsChecking;
    public bool CanChangeScenario=>IsDemo&&!IsChecking&&!IsDownloading;
    public bool AutomaticChecksEnabled
    {
        get=>preferences.AutomaticallyCheckApplicationUpdates;
        set
        {
            if(value==preferences.AutomaticallyCheckApplicationUpdates)return;preferences.AutomaticallyCheckApplicationUpdates=value;
            if(!value&&checkingAutomatically)checkCancellation?.Cancel();
            try{stores.SaveAutomaticApplicationUpdateChecks(value);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException){StatusMessage="The automatic-check preference could not be saved: "+e.Message;}
            Notify();if(value&&initialized&&!IsChecking&&!IsDownloading)_=CheckAsync(false);
        }
    }
    public string DownloadDirectory {get=>downloadDirectory;set{if(Set(ref downloadDirectory,value)){OnPropertyChanged(nameof(DownloadActionTooltip));OnPropertyChanged(nameof(NextDownloadCaption));}}}
    public string StatusMessage {get;private set;}="No application update check has completed.";
    public string FooterText=>State switch
    {
        ApplicationUpdateState.Checking=>"Checking for updates…",
        ApplicationUpdateState.UpToDate=>InstalledVersion.Display+" · Up to date",
        ApplicationUpdateState.Available=>"Update available · "+Release!.Version.Display,
        ApplicationUpdateState.Unavailable=>InstalledVersion.Display+" · Check unavailable",
        _=>InstalledVersion.Display+" · Not checked"
    };
    public string FooterTooltip=>InstalledCaption+"\n"+StatusMessage+"\n"+LastCheckCaption+"\n"+(AutomaticChecksEnabled?"Automatic checks: cached for up to 24 hours.":"Automatic checks are off; manual checks remain available.");
    public string FooterIcon=>HasUpdate?"↓":State==ApplicationUpdateState.UpToDate?"✓":"↻";
    public string LastCheckCaption=>LastSuccessfulCheck is {} at?"Last successful check: "+at.ToLocalTime().ToString("g")+(UsedCache?" (cached)":""):"No successful check yet.";
    public string LatestVersion=>Release?.Version.ToString()??"Unavailable";
    public string ReleaseName=>Release?.Name??"Arrakis application updates";
    public string PublishedDate=>Release?.PublishedAt.ToLocalTime().ToString("f")??"Unavailable";
    public string ReleaseNotes=>Release?.Notes??"Release details are unavailable. A manual check retries GitHub; the official release page is available as a fallback.";
    public string AssetCaption=>Release==null?"No verified release asset selected.":Release.Zip.Name+" · "+Units.Bytes(Release.Zip.Size);
    public string VerificationCaption=>VerifiedDownload!=null?"Download complete — SHA-256 verified":Release?.Zip.Sha256!=null?"SHA-256 digest published for this exact ZIP asset.":Release?.Checksums!=null?"SHA-256 will be read from the matching SHA256SUMS.txt release asset.":"In-app integrity verification is unavailable; use the official release page.";
    public Uri BrowserUrl {get;private set;}=ApplicationUpdateSource.ReleasesPage;
    public long DownloadedBytes {get;private set;}
    public long ExpectedBytes {get;private set;}
    public double DownloadPercent=>ExpectedBytes>0?Math.Clamp(100.0*DownloadedBytes/ExpectedBytes,0,100):0;
    public string ProgressCaption=>Units.Bytes(DownloadedBytes)+" / "+Units.Bytes(ExpectedBytes)+$" · {DownloadPercent:0.0}%";
    public string DownloadPath=>VerifiedDownload?.Path??"No verified ZIP has been finalized.";
    public string InstallationInstructions=>"1. Close Arrakis Server Manager.\n2. Extract the complete verified ZIP into a new or chosen application folder.\n3. Keep all adjacent files and runtime dependencies together.\n4. Launch the executable or administrator launcher.\n5. Verify the new version in the sidebar.\n\nKeep your existing %LOCALAPPDATA%\\ArrakisServerManager preferences and state. Do not delete them during an upgrade. This downloader never installs, extracts, executes, elevates or restarts anything. Funcom server updates are separate.\n\nSHA-256 establishes correspondence with the GitHub-published asset, not an independently authenticated software signature.";
    public ApplicationUpdateScenario[] Scenarios=>Enum.GetValues<ApplicationUpdateScenario>();
    public ApplicationUpdateScenario Scenario
    {
        get=>service is SimulatedApplicationUpdateService sim?sim.Scenario:ApplicationUpdateScenario.UpToDate;
        set{if(CanChangeScenario&&service is SimulatedApplicationUpdateService sim&&sim.Scenario!=value){sim.Scenario=value;OnPropertyChanged();_=CheckAsync(true);}}
    }
    public AsyncCommand CheckCommand {get;}
    public AsyncCommand DownloadCommand {get;}
    public RelayCommand CancelDownloadCommand {get;}
    public RelayCommand BrowseDestinationCommand {get;}
    public RelayCommand ViewOnGitHubCommand {get;}
    public RelayCommand OpenDownloadFolderCommand {get;}
    public RelayCommand CopyDownloadPathCommand {get;}
    public RelayCommand CopyDiagnosticsCommand {get;}
    public async Task InitializeAsync()
    {
        if(initialized||disposed)return;initialized=true;
        try{await Task.Delay(350,lifetime.Token);if(AutomaticChecksEnabled)await CheckAsync(false);_=PeriodicChecks();}catch(OperationCanceledException){}
    }
    private async Task PeriodicChecks()
    {
        try{using var timer=new PeriodicTimer(TimeSpan.FromHours(1));while(await timer.WaitForNextTickAsync(lifetime.Token))if(AutomaticChecksEnabled&&!IsChecking&&!IsDownloading)await CheckAsync(false);}catch(OperationCanceledException){}
    }
    public async Task CheckAsync(bool force)
    {
        if(disposed||IsChecking||IsDownloading||(!force&&!AutomaticChecksEnabled))return;
        var id=generation;var current=service;using var ct=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);checkCancellation=ct;checkingAutomatically=!force;State=ApplicationUpdateState.Checking;DownloadState=ApplicationDownloadState.None;TechnicalDetails="";ActionErrorMessage="";StatusMessage="Checking GitHub for Arrakis application updates…";Notify();
        try
        {
            var result=await current.CheckAsync(force,ct.Token);if(id!=generation||disposed)return;
            GitHubApplicationUpdateService.ValidateSelection(result.Release);Release=result.Release;BrowserUrl=Release.ReleaseUrl;LastSuccessfulCheck=result.CheckedAt;UsedCache=result.FromCache;VerifiedDownload=null;DownloadedBytes=0;ExpectedBytes=Release.Zip.Size;
            State=Release.Version.CompareTo(InstalledVersion)>0?ApplicationUpdateState.Available:ApplicationUpdateState.UpToDate;
            StatusMessage=(HasUpdate?"A newer stable Arrakis version is available. Open details to review it; nothing downloads automatically.":"No newer stable Arrakis version is available.")+(result.CacheWarning.Length>0?" "+result.CacheWarning:"");
        }
        catch(OperationCanceledException){if(id==generation&&!disposed){State=ApplicationUpdateState.NotChecked;StatusMessage="Application update check cancelled. Manual checking remains available.";}}
        catch(Exception e)
        {
            if(id!=generation||disposed)return;State=ApplicationUpdateState.Unavailable;Release=null;VerifiedDownload=null;BrowserUrl=e is ApplicationUpdateException failure?failure.ReleaseUrl:ApplicationUpdateSource.ReleasesPage;StatusMessage=e is ApplicationUpdateException?e.Message:"Application updates could not be checked. Try again later or view releases on GitHub.";TechnicalDetails=Redactor.Clean(e.ToString());
        }
        finally{if(id==generation){checkCancellation=null;checkingAutomatically=false;Notify();}}
    }
    public async Task DownloadAsync()
    {
        if(!CanDownload||Release==null)return;var release=Release;var id=generation;var current=service;using var ct=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);downloadCancellation=ct;VerifiedDownload=null;DownloadedBytes=0;ExpectedBytes=release.Zip.Size;IsDownloading=true;DownloadState=ApplicationDownloadState.Downloading;TechnicalDetails="";ActionErrorMessage="";StatusMessage="Downloading to a partial file; the ZIP is unavailable until SHA-256 verification passes.";Notify();
        try
        {
            var protectedRoots=ProtectedDirectories().ToArray();var directory=await Task.Run(()=>ApplicationDownloadPaths.Validate(DownloadDirectory,protectedRoots),ct.Token);
            var progress=new Progress<ApplicationDownloadProgress>(p=>{if(id==generation&&IsDownloading&&!disposed){DownloadedBytes=p.Bytes;ExpectedBytes=p.Total;Notify();}});
            var result=await current.DownloadAsync(release,directory,progress,ct.Token);if(id!=generation||disposed)return;
            VerifiedDownload=result;DownloadedBytes=result.Bytes;ExpectedBytes=result.Bytes;DownloadState=ApplicationDownloadState.Verified;StatusMessage="Download complete — SHA-256 verified"+(result.Reused?". Existing verified ZIP reused.":". Install it manually when ready.");
        }
        catch(OperationCanceledException){if(id==generation&&!disposed){DownloadState=ApplicationDownloadState.Canceled;StatusMessage=$"Download cancelled at {DownloadPercent:0.0}%. No verified ZIP was finalized.";}}
        catch(Exception e){if(id==generation&&!disposed){DownloadState=ApplicationDownloadState.Failed;downloadFailureSummary=e is ApplicationUpdateException?e.Message:"The update could not be saved. Check the destination, permissions and disk space, or view technical details.";StatusMessage=downloadFailureSummary;TechnicalDetails=Redactor.Clean(e.ToString());}}
        finally{if(id==generation){IsDownloading=false;downloadCancellation=null;Notify();}}
    }
    public void SetMode(bool demo)
    {
        if(demo==IsDemo)return;generation++;checkCancellation?.Cancel();downloadCancellation?.Cancel();checkCancellation=null;downloadCancellation=null;service.Dispose();IsDemo=demo;service=factory(demo);State=ApplicationUpdateState.NotChecked;IsDownloading=false;DownloadState=ApplicationDownloadState.None;TechnicalDetails="";ActionErrorMessage="";Release=null;VerifiedDownload=null;LastSuccessfulCheck=null;UsedCache=false;DownloadedBytes=0;ExpectedBytes=0;BrowserUrl=ApplicationUpdateSource.ReleasesPage;StatusMessage=demo?"Demo update requests are simulated; no real GitHub connection is made.":"Arrakis application updates are separate from Funcom server updates.";ResetDirectory();Notify();if(initialized&&AutomaticChecksEnabled)_=CheckAsync(false);
    }
    private IEnumerable<string> ProtectedDirectories()
    {
        var saved=stores.Load();return new[]{preferences.InstallationDirectory,preferences.BackupDirectory,preferences.LogDirectory,saved.InstallationDirectory,saved.BackupDirectory,saved.LogDirectory,AppContext.BaseDirectory};
    }
    private void ResetDirectory()=>DownloadDirectory=Path.Combine(stores.Root,IsDemo?Path.Combine("Demo","ApplicationUpdates"):"","Downloads");
    private void LocalAction(Action action){ActionErrorMessage="";try{action();}catch(Exception e){StatusMessage="Unable to complete this local action: "+e.Message;ActionErrorMessage=StatusMessage.Length>190?StatusMessage[..187]+"…":StatusMessage;TechnicalDetails=Redactor.Clean(e.ToString());}Notify();}
    private void Notify()
    {
        OnPropertyChanged(nameof(ActionErrorMessage));OnPropertyChanged(nameof(HasActionError));
        foreach(var name in new[]{nameof(State),nameof(IsChecking),nameof(IsDownloading),nameof(HasUpdate),nameof(CanDownload),nameof(CanChangeScenario),nameof(AutomaticChecksEnabled),nameof(IsDemo),nameof(Scenario),nameof(StatusMessage),nameof(FooterText),nameof(FooterTooltip),nameof(FooterIcon),nameof(LastCheckCaption),nameof(LatestVersion),nameof(ReleaseName),nameof(PublishedDate),nameof(ReleaseNotes),nameof(AssetCaption),nameof(VerificationCaption),nameof(DownloadedBytes),nameof(ExpectedBytes),nameof(DownloadPercent),nameof(ProgressCaption),nameof(DownloadPath),nameof(VerifiedDownload),nameof(DownloadState),nameof(ShowDownloadArea),nameof(ShowDestination),nameof(ShowVerifiedFile),nameof(ShowDownloadProgress),nameof(ShowDownloadAction),nameof(ShowCheckAction),nameof(LatestSummaryVersion),nameof(SummaryStatus),nameof(DownloadActionText),nameof(DownloadStatusCaption),nameof(HasDiagnostics),nameof(TechnicalDetails),nameof(DownloadActionTooltip),nameof(NextDownloadCaption)})OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
    public void Dispose(){if(disposed)return;disposed=true;generation++;lifetime.Cancel();checkCancellation?.Cancel();downloadCancellation?.Cancel();checkCancellation=null;downloadCancellation=null;IsDownloading=false;service.Dispose();Notify();}
}
