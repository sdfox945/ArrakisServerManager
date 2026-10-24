using System.Windows.Controls;
namespace Arrakis.Desktop.Views;

public partial class MapsView : UserControl { public MapsView(){InitializeComponent();} }
public partial class BackupsView : UserControl { public BackupsView(){InitializeComponent();} }

public partial class ToolsView : UserControl
{
    public ToolsView(){InitializeComponent();}
    private void ApplicationUpdateClicked(object sender,System.Windows.RoutedEventArgs e)
    {
        if(DataContext is Arrakis.Desktop.ViewModels.ShellViewModel vm)new Arrakis.Desktop.ApplicationUpdateWindow(vm.ApplicationUpdates){Owner=System.Windows.Window.GetWindow(this)}.ShowDialog();
    }
}
