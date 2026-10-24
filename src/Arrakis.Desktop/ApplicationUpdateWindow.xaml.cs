using System.ComponentModel;
using System.Windows;
using Arrakis.Desktop.ViewModels;

namespace Arrakis.Desktop;

public partial class ApplicationUpdateWindow : Window
{
    private readonly ApplicationUpdateViewModel model;
    private bool closingAfterCancellation;
    public ApplicationUpdateWindow(ApplicationUpdateViewModel model)
    {
        InitializeComponent();this.model=model;DataContext=model;Closing+=OnClosing;
        model.PropertyChanged+=ModelChanged;Closed+=(_,_)=>model.PropertyChanged-=ModelChanged;
        Height=model.HasUpdate?700:560;
        Loaded+=(_,_)=>{var area=SystemParameters.WorkArea;MinHeight=Math.Min(MinHeight,Math.Max(420,area.Height-24));Height=Math.Min(Height,area.Height-24);if(Top+ActualHeight>area.Bottom)Top=Math.Max(area.Top,area.Bottom-ActualHeight);SizeHelp();};
        SizeChanged+=(_,_)=>SizeHelp();
    }
    private void CloseClicked(object sender,RoutedEventArgs e)=>Close();
    private void SizeHelp(){var height=Math.Clamp((ActualHeight-560)/2+40,40,120);InstallationScroll.MaxHeight=height;DiagnosticsContent.Height=height+28;}
    private void InstallationExpanded(object sender,RoutedEventArgs e){if(DiagnosticsHelp!=null)DiagnosticsHelp.IsExpanded=false;}
    private void DiagnosticsExpanded(object sender,RoutedEventArgs e){if(InstallationHelp!=null)InstallationHelp.IsExpanded=false;}
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(!model.IsDownloading)return;e.Cancel=true;closingAfterCancellation=true;model.CancelDownloadCommand.Execute(null);
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e){if(closingAfterCancellation&&e.PropertyName==nameof(model.IsDownloading)&&!model.IsDownloading){closingAfterCancellation=false;Dispatcher.BeginInvoke(Close);}}
}
