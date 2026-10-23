using System.IO;
using System.Windows;
using Arrakis.Desktop.ViewModels;
using Arrakis.Infrastructure;

namespace Arrakis.Desktop;
public partial class App : Application
{
    private Mutex? instanceMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);var smokeArg=Array.IndexOf(e.Args,"--ui-smoke");var smoke=smokeArg>=0;if(smoke&&e.Args.Length<=smokeArg+1){Shutdown(2);return;}var demo=e.Args.Contains("--demo")||smoke;var localRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ArrakisServerManager");
        if(!demo){instanceMutex=new Mutex(true,@"Local\ArrakisServerManager.Live."+Environment.UserName,out var created);if(!created){MessageBox.Show("Arrakis Server Manager is already running in Live mode for this Windows session.","Arrakis Server Manager");instanceMutex.Dispose();instanceMutex=null;Shutdown();return;}}
        var dataArg=Array.IndexOf(e.Args,"--data-dir");if(dataArg>=0&&e.Args.Length>dataArg+1)localRoot=Path.GetFullPath(e.Args[dataArg+1]);
        Directory.CreateDirectory(localRoot);
        if(smoke){System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Path.Combine(localRoot,"binding-errors.log")));System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level=System.Diagnostics.SourceLevels.Warning;}
        DispatcherUnhandledException+=(_,args)=>{if(smoke){File.WriteAllText(Path.Combine(localRoot,"unhandled-error.txt"),args.Exception.ToString());args.Handled=true;Environment.Exit(1);}else{MessageBox.Show(Arrakis.Core.Redactor.Clean(args.Exception.Message),"Arrakis Server Manager",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true;}};
        var window=new MainWindow();var interaction=new DesktopInteraction(window);var stores=new LocalStores(localRoot);var vm=new ShellViewModel(interaction,stores,demo);window.DataContext=vm;window.Configure(stores,vm,!smoke);MainWindow=window;var smokeStarted=false;if(smoke)window.ContentRendered+=async(_,_)=>{if(smokeStarted)return;smokeStarted=true;await UiSmoke.RunAsync(window,vm,Path.GetFullPath(e.Args[smokeArg+1]));};window.Show();
    }
    protected override void OnExit(ExitEventArgs e){if(instanceMutex!=null){instanceMutex.ReleaseMutex();instanceMutex.Dispose();}base.OnExit(e);}
}
