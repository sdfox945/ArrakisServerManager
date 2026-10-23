using System.Windows.Controls;
using System.Windows.Media;
using Arrakis.Desktop.ViewModels;

namespace Arrakis.Desktop.Views;
public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView(){InitializeComponent();}
    private void LogContentChanged(object sender,TextChangedEventArgs e)
    {
        if(sender is TextBox text && DataContext is ShellViewModel {AutoScroll:true})
        {
            var scroll=FindScroll(text);
            if(scroll==null||scroll.ScrollableHeight-scroll.VerticalOffset<30)text.Dispatcher.BeginInvoke(()=>text.ScrollToEnd());
        }
    }
    private static ScrollViewer? FindScroll(System.Windows.DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is ScrollViewer scroll)return scroll;var found=FindScroll(child);if(found!=null)return found;}return null;
    }
}
