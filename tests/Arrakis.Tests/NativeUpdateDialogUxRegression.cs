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
using Arrakis.Core;
using Arrakis.Desktop;
using Arrakis.Desktop.ViewModels;

namespace Arrakis.Tests;

internal static partial class NativeConfigurationRegression
{
    public static void RegisterUpdateDialogUx(Action<string,Func<Task>> add,string root)
    {
        foreach(var (version,message) in new[]{("1.3.0","You're running the latest stable version."),("1.1.2","You're running a newer version than the latest published stable release.")})
            add("Native update dialog UX: compact version comparison "+version,async()=>
            {
                using var c=await UpdateContext.Create(root,"ux-compact-"+version);c.Http.Json=ApplicationUpdateRegression.Metadata(version);await c.Updates.CheckAsync(true);var dialog=OpenUpdateDialog(c);
                Check(c.Updates.SummaryStatus==message&&((TextBlock)dialog.FindName("AvailabilityStatus")).Text==message,"Equal and installed-newer states must be explicit.");
                Check(!UxElement(dialog,"DownloadArea").IsVisible&&!UxElement(dialog,"ProgressArea").IsVisible&&!UxElement(dialog,"DownloadAction").IsVisible&&!UxElement(dialog,"InstallationHelp").IsVisible,"No update must hide download counters, empty bars, disabled actions and installation help.");
                Check(dialog.Height<=600,"The no-update dialog should use compact default dimensions.");AssertUxBounds(dialog,"ReleaseSummary","CloseAction");CaptureWindow(dialog,Path.Combine(c.Evidence,"compact.png"));dialog.Close();
            });
        add("Native update dialog UX: offline status preserves installed version and copyable diagnostics",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-offline");c.Http.Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));await c.Updates.CheckAsync(true);var dialog=OpenUpdateDialog(c);
            Check(c.Updates.SummaryStatus=="Unable to check for updates. Your installed application remains available."&&!c.Updates.SummaryStatus.Contains("latest stable"),"A failed check cannot become up-to-date.");Check(((TextBlock)dialog.FindName("AvailabilityStatus")).Text==c.Updates.SummaryStatus&&c.Updates.InstalledCaption.StartsWith("v1.3.0"),"Installed version and failure must remain in the fixed summary.");
            Check(!UxElement(dialog,"DownloadArea").IsVisible&&c.Updates.HasDiagnostics,"Offline failures must not show irrelevant downloads.");var details=(Expander)dialog.FindName("DiagnosticsHelp");details.IsExpanded=true;await Pump();Invoke(UpdateContext.Button(dialog,"Copy diagnostics"));await Until(()=>c.Ui.Copies.Count==1);Check(c.Ui.Copies[0].Contains("503"),"Technical error detail must remain copyable.");AssertUxBounds(dialog,"ReleaseSummary","DiagnosticsHelp","ActionArea");CaptureWindow(dialog,Path.Combine(c.Evidence,"offline.png"));dialog.Close();
        });
        add("Native update dialog UX: long plain notes scroll independently of summary and actions",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-long-notes");c.Http.Json["body"]=string.Join('\n',Enumerable.Repeat("<script>neverExecute()</script> long fixture release notes only",1500));c.Http.Json["name"]="Fixture "+new string('X',190);await c.Updates.CheckAsync(true);var dialog=OpenUpdateDialog(c);var notes=(TextBox)dialog.FindName("ReleaseNotesBox");
            var releaseScroll=(ScrollViewer)dialog.FindName("ReleaseInfoScroll");Check(notes.IsReadOnly&&notes.Text.Contains("<script>")&&notes.VerticalScrollBarVisibility==ScrollBarVisibility.Disabled&&notes.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled&&releaseScroll.VerticalScrollBarVisibility==ScrollBarVisibility.Auto,"Release information must use one independent scroller with wrapping plaintext and no nested notes scrollbar.");
            Check(!UxAncestors(UxElement(dialog,"ReleaseSummary")).OfType<ScrollViewer>().Any()&&!UxAncestors(UxElement(dialog,"OperationArea")).OfType<ScrollViewer>().Any(),"Fixed summary/download areas cannot be inside an outer scroll viewer.");
            var before=UxBounds(dialog,"ActionArea");releaseScroll.ScrollToEnd();await Pump();Check(releaseScroll.VerticalOffset>0&&UxBounds(dialog,"ActionArea")==before,"Scrolling notes cannot move fixed actions.");Check(c.Ui.Opens.Count==0&&!Descendants(dialog).Any(x=>x is WebBrowser),"Release-note content cannot execute or open links.");AssertUxBounds(dialog,"ReleaseSummary","DownloadStatus","DestinationArea","ActionArea");Check(releaseScroll.ActualHeight<=280,"Release information must have a reasonable maximum viewport height.");CaptureWindow(dialog,Path.Combine(c.Evidence,"long-notes.png"));dialog.Close();
        });
        add("Native update dialog UX: available destination and primary controls remain fixed at minimum size",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-available");await c.Updates.CheckAsync(true);var dialog=OpenUpdateDialog(c);dialog.Width=640;dialog.Height=560;await Pump();Check(c.Updates.SummaryStatus=="A newer version of Arrakis Server Manager is available."&&UpdateContext.Button(dialog,"Download Update").IsEnabled,"A newer release must offer an explicit download.");
            Check(UxElement(dialog,"DestinationArea").IsVisible&&!UxElement(dialog,"ProgressArea").IsVisible&&!UxElement(dialog,"CancelDownloadAction").IsVisible,"An unstarted download must show destination without empty progress or cancel.");var next=Path.Combine(c.Evidence,"chosen");c.Ui.NextFolder=next;Invoke(UpdateContext.Button(dialog,"Choose folder…"));await Until(()=>c.Updates.DownloadDirectory==next);AssertUxBounds(dialog,"ReleaseSummary","DownloadStatus","DestinationArea","ActionArea");CaptureWindow(dialog,Path.Combine(c.Evidence,"available-minimum.png"));dialog.Close();
        });
        add("Native update dialog UX: active progress and cancel remain visible after notes scroll and resize",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-progress");c.Http.Json["body"]=string.Join('\n',Enumerable.Repeat("Long release information",2000));await c.Updates.CheckAsync(true);c.SlowDownloads();var dialog=OpenUpdateDialog(c);Invoke(UpdateContext.Button(dialog,"Download Update"));await Until(()=>c.Updates.IsDownloading&&c.Updates.DownloadedBytes>0);((ScrollViewer)dialog.FindName("ReleaseInfoScroll")).ScrollToEnd();dialog.Width=640;dialog.Height=560;await Pump();
            Check(UxElement(dialog,"ProgressArea").IsVisible&&UxElement(dialog,"DownloadStatus").IsVisible&&!UxElement(dialog,"DownloadAction").IsVisible&&UpdateContext.Button(dialog,"Cancel download").IsEnabled,"Only the active cancel action should compete while progress remains fixed.");AssertUxBounds(dialog,"ReleaseSummary","DownloadStatus","ProgressArea","CancelDownloadAction","CloseAction");CaptureWindow(dialog,Path.Combine(c.Evidence,"active-minimum.png"));Invoke(UpdateContext.Button(dialog,"Cancel download"));await Until(()=>!c.Updates.IsDownloading);
            Check(c.Updates.DownloadState==ApplicationDownloadState.Canceled&&c.Updates.DownloadStatusCaption.Contains($"{c.Updates.DownloadPercent:0.0}%")&&c.Updates.DownloadStatusCaption.Contains("No verified ZIP"),"Canceled progress must have an explicit state and retained percentage.");await Until(()=>UpdateContext.Button(dialog,"Retry Download").IsEnabled&&!UxElement(dialog,"CancelDownloadAction").IsVisible);Check(UpdateContext.Button(dialog,"Retry Download").IsEnabled&&!UxElement(dialog,"CancelDownloadAction").IsVisible,"Cancel should provide retry, not an apparent active transfer.");AssertUxBounds(dialog,"DownloadStatus","ProgressArea","ActionArea");CaptureWindow(dialog,Path.Combine(c.Evidence,"canceled.png"));dialog.Close();
        });
        add("Native update dialog UX: rejected destination remains visible and does not replace the safe folder",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-destination-error");await c.Updates.CheckAsync(true);var dialog=OpenUpdateDialog(c);var original=c.Updates.DownloadDirectory;c.Ui.NextFolder=c.Vm.Preferences.InstallationDirectory;Invoke(UpdateContext.Button(dialog,"Choose folder…"));await Until(()=>c.Updates.HasActionError);Check(c.Updates.DownloadDirectory==original&&c.Updates.ActionErrorMessage.Contains("separate")&&c.Updates.HasDiagnostics,"An unsafe destination must retain the prior safe folder and show a visible error.");AssertUxBounds(dialog,"LocalActionError","DownloadStatus","ActionArea");CaptureWindow(dialog,Path.Combine(c.Evidence,"destination-error.png"));c.Ui.NextFolder=Path.Combine(c.Evidence,"safe-folder");Invoke(UpdateContext.Button(dialog,"Choose folder…"));await Until(()=>c.Updates.DownloadDirectory==c.Ui.NextFolder&&!c.Updates.HasActionError);dialog.Close();
        });
        add("Native update dialog UX: interrupted transfer offers retry with diagnostics instead of success",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-failed");await c.Updates.CheckAsync(true);c.Http.Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ApplicationUpdateRegression.UnknownContent(new ApplicationUpdateRegression.ControlledStream(ApplicationUpdateRegression.Bytes,20,true))});var dialog=OpenUpdateDialog(c);Invoke(UpdateContext.Button(dialog,"Download Update"));await Until(()=>c.Updates.DownloadState==ApplicationDownloadState.Failed&&!c.Updates.IsDownloading);
            Check(c.Updates.DownloadStatusCaption.StartsWith("Download failed")&&c.Updates.HasDiagnostics&&c.Updates.VerifiedDownload==null,"A transfer error cannot present verified completion.");Check(UpdateContext.Button(dialog,"Retry Download").IsEnabled&&!UxElement(dialog,"VerifiedArea").IsVisible,"Failure needs a clear retry action and no success actions.");((Expander)dialog.FindName("DiagnosticsHelp")).IsExpanded=true;dialog.Width=640;dialog.Height=560;await Pump();AssertUxBounds(dialog,"DownloadStatus","DiagnosticsHelp","ActionArea");CaptureWindow(dialog,Path.Combine(c.Evidence,"failed.png"));dialog.Close();
        });
        add("Native update dialog UX: checksum mismatch stays failed and diagnostic text is copyable",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-checksum");await c.Updates.CheckAsync(true);c.Http.Reply=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(ApplicationUpdateRegression.Bytes.Select((b,i)=>i==0?(byte)(b^1):b).ToArray())});var dialog=OpenUpdateDialog(c);Invoke(UpdateContext.Button(dialog,"Download Update"));await Until(()=>c.Updates.DownloadState==ApplicationDownloadState.Failed&&!c.Updates.IsDownloading);
            Check(((TextBlock)dialog.FindName("DownloadStatus")).Text.Contains("SHA-256 mismatch")&&!UxElement(dialog,"VerifiedArea").IsVisible,"Hash mismatch cannot look successful.");((Expander)dialog.FindName("DiagnosticsHelp")).IsExpanded=true;await Pump();Invoke(UpdateContext.Button(dialog,"Copy diagnostics"));await Until(()=>c.Ui.Copies.Count==1);Check(c.Ui.Copies[0].Contains("SHA-256 mismatch"),"Diagnostic copying must retain the underlying failure.");CaptureWindow(dialog,Path.Combine(c.Evidence,"checksum-mismatch.png"));c.Ui.NextFolder=c.Vm.Preferences.InstallationDirectory;Invoke(UpdateContext.Button(dialog,"Choose folder…"));await Until(()=>c.Updates.HasActionError);Check(c.Updates.DownloadStatusCaption.Contains("SHA-256 mismatch"),"A later local-action error cannot mislabel the download outcome.");dialog.Close();
        });
        add("Native update dialog UX: verified Download Again preserves and reuses the previous package",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-verified");await c.Updates.CheckAsync(true);var dialog=OpenUpdateDialog(c);Invoke(UpdateContext.Button(dialog,"Download Update"));await Until(()=>c.Updates.DownloadState==ApplicationDownloadState.Verified&&!c.Updates.IsDownloading&&((Button)dialog.FindName("DownloadAction")).IsEnabled);var result=c.Updates.VerifiedDownload!;var original=await File.ReadAllBytesAsync(result.Path);
            Check(((TextBlock)dialog.FindName("DownloadStatus")).Text=="Download complete — SHA-256 verified."&&UpdateContext.Button(dialog,"Download Again").IsEnabled,"Verified state needs an exact success label and contextual action.");var path=(TextBox)dialog.FindName("VerifiedPath");Check(path.IsReadOnly&&path.Text==result.Path&&path.TextWrapping==TextWrapping.Wrap,"The full verified path must remain readable/copyable.");Check(!UxElement(dialog,"ProgressArea").IsVisible&&!UxElement(dialog,"DestinationArea").IsVisible&&UxElement(dialog,"VerifiedArea").IsVisible,"Verified content should replace irrelevant progress/edit controls.");AssertUxBounds(dialog,"DownloadStatus","VerifiedArea","ActionArea");CaptureWindow(dialog,Path.Combine(c.Evidence,"verified.png"));
            Invoke(UpdateContext.Button(dialog,"Download Again"));await Until(()=>!c.Updates.IsDownloading&&c.Updates.VerifiedDownload?.Reused==true);Check((await File.ReadAllBytesAsync(result.Path)).SequenceEqual(original),"Repeat action must reverify/reuse rather than overwrite or discard the verified ZIP.");dialog.Close();
        });
        add("Native update dialog UX: installation help is collapsed and scrolls independently when expanded",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-install-help");await c.Updates.CheckAsync(true);await c.Updates.DownloadAsync();var dialog=OpenUpdateDialog(c);var help=(Expander)dialog.FindName("InstallationHelp");Check(!help.IsExpanded&&help.Header as string=="How to install this update","Installation instructions must begin collapsed.");((IExpandCollapseProvider)new ExpanderAutomationPeer(help).GetPattern(PatternInterface.ExpandCollapse)).Expand();dialog.Width=640;dialog.Height=560;await Pump();var scroll=(ScrollViewer)dialog.FindName("InstallationScroll");Check(help.IsExpanded&&scroll.IsVisible&&c.Updates.InstallationInstructions.Contains("Do not delete them during an upgrade")&&c.Updates.InstallationInstructions.Contains("1. Close Arrakis"),"Help must retain manual-install and separate-preference guidance.");scroll.ScrollToBottom();await Pump();AssertUxBounds(dialog,"ReleaseSummary","DownloadStatus","InstallationHelp","ActionArea");CaptureWindow(dialog,Path.Combine(c.Evidence,"install-expanded-minimum.png"));help.IsExpanded=false;await Pump();Check(!scroll.IsVisible,"Collapsing installation help must reclaim space.");dialog.Close();
        });
        add("Native update dialog UX: long paths titles and keyboard focus remain accessible while resizing",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-resize");c.Http.Json["name"]="Fixture release title "+new string('X',175);await c.Updates.CheckAsync(true);c.Updates.DownloadDirectory=Path.Combine(c.Evidence,new string('a',35),new string('b',35));await c.Updates.DownloadAsync();var dialog=OpenUpdateDialog(c);
            foreach(var (width,height) in new[]{(800d,760d),(640d,560d),(920d,820d)}){dialog.Width=width;dialog.Height=height;await Pump();AssertUxBounds(dialog,"ReleaseSummary","DownloadStatus","VerifiedArea","ActionArea");Check(((TextBox)dialog.FindName("VerifiedPath")).HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled,"Long paths must wrap instead of horizontally scrolling.");}
            var action=UpdateContext.Button(dialog,"Download Again");action.Focus();Keyboard.Focus(action);Check(action.IsKeyboardFocused&&action.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),"Fixed actions must remain keyboard navigable.");CaptureWindow(dialog,Path.Combine(c.Evidence,"long-title-path.png"));dialog.Close();
        });
        add("Native update dialog UX: minimum layout renders at 100 125 and 150 percent",async()=>
        {
            using var c=await UpdateContext.Create(root,"ux-scaling");await c.Updates.CheckAsync(true);var dialog=OpenUpdateDialog(c);dialog.Width=640;dialog.Height=560;await Pump();AssertUxBounds(dialog,"ReleaseSummary","DownloadStatus","DestinationArea","ActionArea");foreach(var scale in new[]{1d,1.25,1.5}){var image=new RenderTargetBitmap((int)(dialog.ActualWidth*scale),(int)(dialog.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(c.Evidence,$"dialog-{scale:0.00}.png"));encoder.Save(file);}dialog.Close();
        });
    }
    private static ApplicationUpdateWindow OpenUpdateDialog(UpdateContext context){var dialog=new ApplicationUpdateWindow(context.Updates){Owner=context.Window};dialog.Show();dialog.Activate();dialog.UpdateLayout();return dialog;}
    private static FrameworkElement UxElement(ApplicationUpdateWindow window,string name)=>(FrameworkElement)window.FindName(name);
    private static Rect UxBounds(ApplicationUpdateWindow window,string name){var element=UxElement(window,name);return new(element.TranslatePoint(new Point(),(FrameworkElement)window.Content),element.RenderSize);}
    private static IEnumerable<DependencyObject> UxAncestors(DependencyObject element){for(var current=VisualTreeHelper.GetParent(element);current!=null;current=VisualTreeHelper.GetParent(current))yield return current;}
    private static void AssertUxBounds(ApplicationUpdateWindow window,params string[] names)
    {
        window.UpdateLayout();var root=(FrameworkElement)window.Content;foreach(var name in names){var element=UxElement(window,name);var bounds=UxBounds(window,name);Check(element.IsVisible&&bounds.Width>0&&bounds.Height>0&&bounds.Left>=-1&&bounds.Top>=-1&&bounds.Right<=root.ActualWidth+1&&bounds.Bottom<=root.ActualHeight+1,$"{name} must remain visible inside the dialog without outer scrolling; bounds={bounds}, root={root.ActualWidth}x{root.ActualHeight}.");}
    }
}
