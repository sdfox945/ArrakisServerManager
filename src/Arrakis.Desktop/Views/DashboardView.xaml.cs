using System.Windows;
using System.Windows.Controls;
using Arrakis.Core;
using Arrakis.Desktop.ViewModels;

namespace Arrakis.Desktop.Views;
public partial class DashboardView : UserControl
{
    public DashboardView(){InitializeComponent();}
    private void NoticeClicked(object sender,RoutedEventArgs e){if(sender is Button {DataContext:HealthNotice notice}&&DataContext is ShellViewModel shell)shell.OpenNotice(notice);}
}
