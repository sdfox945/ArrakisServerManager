using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Arrakis.Desktop.ViewModels;
using Arrakis.Infrastructure;
using Microsoft.Win32;

namespace Arrakis.Desktop;
public partial class MainWindow : Window
{
    public MainWindow(){InitializeComponent();}
    public void Configure(LocalStores store,ShellViewModel vm,bool autoRefresh=true)
    {
        var p=vm.Preferences;Width=Math.Clamp(p.WindowWidth,1040,SystemParameters.VirtualScreenWidth);Height=Math.Clamp(p.WindowHeight,700,SystemParameters.VirtualScreenHeight);
        if(double.IsFinite(p.WindowLeft)&&double.IsFinite(p.WindowTop)&&p.WindowLeft+100>SystemParameters.VirtualScreenLeft&&p.WindowLeft<SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-100&&p.WindowTop+100>SystemParameters.VirtualScreenTop&&p.WindowTop<SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-100){WindowStartupLocation=WindowStartupLocation.Manual;Left=p.WindowLeft;Top=p.WindowTop;}
        if(p.Maximized)WindowState=WindowState.Maximized;
        if(!autoRefresh)return;
        if(autoRefresh)Loaded+=async(_,_)=>await vm.InitializeAsync();
        Closing+=(_,e)=>{if(vm.IsBusy){e.Cancel=true;MessageBox.Show(this,"An operation is running. Wait for it to finish, or cancel a read-only operation before closing.","Operation in progress",MessageBoxButton.OK,MessageBoxImage.Information);return;}if(vm.Editor.IsDirty&&MessageBox.Show(this,"Discard the unsaved configuration draft and close?","Unsaved changes",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes){e.Cancel=true;return;}p.Maximized=WindowState==WindowState.Maximized;p.WindowWidth=RestoreBounds.Width;p.WindowHeight=RestoreBounds.Height;p.WindowLeft=RestoreBounds.Left;p.WindowTop=RestoreBounds.Top;try{store.Save(p);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not save preferences");}vm.Dispose();};
    }
}
public sealed class DesktopInteraction(Window owner) : IUserInteraction
{
    public bool Confirm(string title,string message)=>MessageBox.Show(owner,message,title,MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
    public bool Review(string title,string summary,string diff)
    {
        var window=new ReviewWindow(title,summary,diff){Owner=owner};return window.ShowDialog()==true;
    }
    public bool ReviewRawRemovals(string title,string summary,string diff,string fileName)=>new ReviewWindow(title,summary,diff,true,fileName){Owner=owner}.ShowDialog()==true;
    public void Preview(string title,string summary,string diff)=>new ReviewWindow(title,summary,diff,false){Owner=owner}.ShowDialog();
    public void ShowError(string title,string summary,string details)=>new ErrorWindow(title,summary,details,Copy){Owner=owner}.ShowDialog();
    public string? ChooseFile(string title,string filter){var d=new OpenFileDialog{Title=title,Filter=filter,CheckFileExists=true};return d.ShowDialog(owner)==true?d.FileName:null;}
    public string? ChooseFolder(string title,string initial){var d=new OpenFolderDialog{Title=title,InitialDirectory=System.IO.Directory.Exists(initial)?initial:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};return d.ShowDialog(owner)==true?d.FolderName:null;}
    public void Copy(string text){try{Clipboard.SetText(text);}catch(System.Runtime.InteropServices.COMException){MessageBox.Show(owner,"Clipboard is in use by another application. Try again.");}}
    public void Open(string path)=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
}
