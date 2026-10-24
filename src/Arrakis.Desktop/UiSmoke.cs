using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using Arrakis.Desktop.Views;
using Arrakis.Desktop.ViewModels;
using Arrakis.Core;

namespace Arrakis.Desktop;

internal static class UiSmoke
{
    public static async Task RunAsync(MainWindow window,ShellViewModel vm,string directory)
    {
        // Entry point is restricted to a SimulatedServerGateway by App startup.
        if(!vm.IsDemo)throw new InvalidOperationException("UI smoke tests require isolated demo mode.");
        Directory.CreateDirectory(directory);var errors=new List<string>();var states=new List<object>();
        try
        {
            await vm.RefreshAsync();
            vm.Editor.LoadCommand.Execute(null);await Wait(vm);
            vm.RefreshBackupsCommand.Execute(null);await Wait(vm);
            for(var i=0;i<6;i++)
            {
                vm.SelectedPage=i;await Task.Delay(400);window.UpdateLayout();states.Add(new{page=i,busy=vm.IsBusy,group=vm.BattlegroupState,status=vm.StatusMessage,dirty=vm.Editor.IsDirty});
                var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));await using var file=File.Create(Path.Combine(directory,$"{i+1}-{vm.Pages[i].Title.Replace(" & ","-").Replace(" ","-")}.png"));encoder.Save(file);
            }
            vm.SelectedPage=2;await Task.Delay(100);var view=Descendants(window).OfType<ConfigurationView>().Single();var tabs=(TabControl)view.FindName("EditorModes");tabs.SelectedIndex=1;
            foreach(var config in vm.Editor.Files.ToArray()){vm.Editor.RequestFileSelection(config);await Task.Delay(100);if(vm.Editor.RawText!=vm.Editor.CurrentOriginal!.Text||vm.Editor.IsDirty)throw new InvalidOperationException("Raw editor changed original content just by loading "+config.Name);}
            tabs.SelectedIndex=0;
            var failure=new OperationFailure("UserServerCustomSettings.ini","lock","A fresh readback verified the original file unchanged.","flock: unrecognized option: w",ConfigurationFileState.VerifiedUnchanged,"Reload the current configuration and retry only after resolving the cause.","BusyBox v1.37.0 diagnostic fixture\nPath: /var/lib/rancher/k3s/storage/very-long-fixture-volume-name/Saved/UserSettings/UserServerCustomSettings.ini\nOriginal SHA-256: "+new string('a',64)+"\nNo live file was accessed.");
            var history=new OperationRecord(DateTimeOffset.Now,"Save UserServerCustomSettings.ini","Failed",failure.Summary,failure.Details);vm.History.Insert(0,history);vm.SelectedHistory=history;vm.SelectedPage=4;await Task.Delay(100);var diagnostics=Descendants(window).OfType<DiagnosticsView>().Single();((TabControl)diagnostics.FindName("DiagnosticTabs")).SelectedIndex=2;await Task.Delay(100);Capture(window,Path.Combine(directory,"configuration-save-history.png"));
            var errorWindow=new ErrorWindow("Configuration save failed",failure.Summary,failure.Details,_=>{}){Owner=window};errorWindow.Show();await Task.Delay(100);Descendants(errorWindow).OfType<Expander>().Single().IsExpanded=true;await Task.Delay(100);var detailBox=Descendants(errorWindow).OfType<TextBox>().Single();if(detailBox.TextWrapping!=TextWrapping.Wrap||detailBox.HorizontalScrollBarVisibility!=ScrollBarVisibility.Disabled)throw new InvalidOperationException("Save diagnostics must wrap without horizontal clipping.");Capture(errorWindow,Path.Combine(directory,"configuration-save-error.png"));errorWindow.Close();
            await vm.ApplicationUpdates.CheckAsync(true);var compactUpdate=new ApplicationUpdateWindow(vm.ApplicationUpdates){Owner=window};compactUpdate.Show();await Task.Delay(100);Capture(compactUpdate,Path.Combine(directory,"application-update-up-to-date.png"));compactUpdate.Close();vm.ApplicationUpdates.Scenario=ApplicationUpdateScenario.VerifiedDownload;
            for(var attempt=0;attempt<100&&vm.ApplicationUpdates.IsChecking;attempt++)await Task.Delay(30);
            if(!vm.ApplicationUpdates.IsDemo||!vm.ApplicationUpdates.HasUpdate)throw new InvalidOperationException("Application-update smoke must use the available Demo fixture.");
            vm.SelectedPage=5;await Task.Delay(100);Capture(window,Path.Combine(directory,"application-update-tools.png"));
            var appUpdate=new ApplicationUpdateWindow(vm.ApplicationUpdates){Owner=window};appUpdate.Show();await Task.Delay(100);Capture(appUpdate,Path.Combine(directory,"application-update-details.png"));
            var transfer=vm.ApplicationUpdates.DownloadAsync();for(var attempt=0;attempt<100&&vm.ApplicationUpdates.IsDownloading&&vm.ApplicationUpdates.DownloadedBytes==0;attempt++)await Task.Delay(20);if(!vm.ApplicationUpdates.IsDownloading)throw new InvalidOperationException("Expected an active simulated transfer.");Capture(appUpdate,Path.Combine(directory,"application-update-progress.png"));vm.ApplicationUpdates.CancelDownloadCommand.Execute(null);await transfer;if(vm.ApplicationUpdates.DownloadState!=ApplicationDownloadState.Canceled)throw new InvalidOperationException("Demo cancellation state was not retained.");await Task.Delay(100);Capture(appUpdate,Path.Combine(directory,"application-update-canceled.png"));
            await vm.ApplicationUpdates.DownloadAsync();if(vm.ApplicationUpdates.VerifiedDownload==null)throw new InvalidOperationException("The inert application-update fixture failed SHA verification.");
            Descendants(appUpdate).OfType<TextBox>().Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Verified application download path").BringIntoView();await Task.Delay(100);Capture(appUpdate,Path.Combine(directory,"application-update-verified.png"));appUpdate.Close();
            var helpWindow=new ApplicationUpdateWindow(vm.ApplicationUpdates){Owner=window};helpWindow.Show();((Expander)helpWindow.FindName("InstallationHelp")).IsExpanded=true;await Task.Delay(100);Capture(helpWindow,Path.Combine(directory,"application-update-install-help.png"));helpWindow.Close();
            var originalDestination=vm.ApplicationUpdates.DownloadDirectory;vm.ApplicationUpdates.Scenario=ApplicationUpdateScenario.ChecksumMismatch;for(var attempt=0;attempt<100&&vm.ApplicationUpdates.IsChecking;attempt++)await Task.Delay(30);vm.ApplicationUpdates.DownloadDirectory=Path.Combine(originalDestination,"mismatch-fixture");await vm.ApplicationUpdates.DownloadAsync();if(vm.ApplicationUpdates.DownloadState!=ApplicationDownloadState.Failed||vm.ApplicationUpdates.VerifiedDownload!=null)throw new InvalidOperationException("A Demo checksum mismatch became a success.");var mismatchWindow=new ApplicationUpdateWindow(vm.ApplicationUpdates){Owner=window};mismatchWindow.Show();await Task.Delay(100);Capture(mismatchWindow,Path.Combine(directory,"application-update-failed.png"));mismatchWindow.Close();
            vm.ApplicationUpdates.Scenario=ApplicationUpdateScenario.ApiUnavailable;for(var attempt=0;attempt<100&&vm.ApplicationUpdates.IsChecking;attempt++)await Task.Delay(30);var offlineWindow=new ApplicationUpdateWindow(vm.ApplicationUpdates){Owner=window};offlineWindow.Show();await Task.Delay(100);Capture(offlineWindow,Path.Combine(directory,"application-update-offline.png"));offlineWindow.Close();
            window.Width=1040;window.Height=700;vm.SelectedPage=1;await Task.Delay(250);window.UpdateLayout();
            var small=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);small.Render(window);var smallEncoder=new PngBitmapEncoder();smallEncoder.Frames.Add(BitmapFrame.Create(small));using(var file=File.Create(Path.Combine(directory,"minimum-size.png")))smallEncoder.Save(file);
            System.Diagnostics.Trace.Flush();foreach(var listener in System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.OfType<System.Diagnostics.TraceListener>())listener.Flush();
            await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{passed=true,pages=6,width=window.ActualWidth,height=window.ActualHeight,mode=vm.ModeLabel,applicationUpdateFixtureVerified=true,states,errors}));
        }
        catch(Exception e){errors.Add(e.ToString());await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{passed=false,errors}));}
        finally{await File.WriteAllTextAsync(Path.Combine(directory,"shutdown-start.txt"),"Requested application shutdown");vm.Dispose();Application.Current.Shutdown();await File.WriteAllTextAsync(Path.Combine(directory,"shutdown-returned.txt"),"Shutdown returned");}
    }
    private static async Task Wait(ShellViewModel vm){for(var i=0;i<100;i++){await Task.Delay(50);if(!vm.IsBusy)return;}throw new TimeoutException("UI operation timed out.");}
    private static void Capture(Window window,string path){window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);encoder.Save(stream);}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent){for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
}
