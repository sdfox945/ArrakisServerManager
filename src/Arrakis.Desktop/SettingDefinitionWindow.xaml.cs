using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using Arrakis.Desktop.ViewModels;

namespace Arrakis.Desktop;

public partial class SettingDefinitionWindow : Window
{
    public SettingDefinitionWindow(DefinitionEditorViewModel model)
    {
        InitializeComponent();DataContext=model;model.Accepted+=Accept;model.FocusRequested+=FocusIssue;
        Closed+=(_,_)=>{model.Accepted-=Accept;model.FocusRequested-=FocusIssue;};
        Loaded+=(_,_)=>{Height=Math.Min(Height,SystemParameters.WorkArea.Height-30);MinHeight=Math.Min(MinHeight,Height);};
    }
    private void Accept()=>DialogResult=true;
    private void FocusIssue(string field)
    {
        var name=field switch {"Example"=>"Variable example","Default"=>"Variable default","Minimum"=>"Variable minimum","Maximum"=>"Variable maximum","Step"=>"Variable step","Choices"=>"Variable allowed choices","Section"=>"Variable INI section","Key"=>"Variable exact key","Category"=>"Variable visual category","Value"=>((DefinitionEditorViewModel)DataContext).IsBoolean?"Variable Boolean value":"Variable initial value",_=>"Variable friendly name"};
        if(field is "Example" or "Default" or "Minimum" or "Maximum" or "Step" or "Choices")AdvancedMetadata.IsExpanded=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>{var control=Descendants(this).OfType<FrameworkElement>().FirstOrDefault(e=>AutomationProperties.GetName(e)==name&&e.IsVisible);control?.BringIntoView();control?.Focus();});
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item;}
    }
    private void CancelClicked(object sender,RoutedEventArgs e)=>Close();
}
