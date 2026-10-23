using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Arrakis.Desktop;

public sealed class ErrorWindow : Window
{
    public ErrorWindow(string title, string summary, string details, Action<string> copy)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = title; Width = 820; Height = 540; MinWidth = 600; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var explanation = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, 0, 0, 16) };
        grid.Children.Add(explanation);
        var technical = new TextBox { Text = details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        var expander = new Expander { Header = "Technical details", Foreground = (Brush)Application.Current.FindResource("TextBrush"), Content = technical, IsExpanded = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(expander, 1); grid.Children.Add(expander);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var copyButton = new Button { Content = "Copy diagnostic details" }; copyButton.Click += (_, _) => copy(details);
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        buttons.Children.Add(copyButton); buttons.Children.Add(close); Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        Content = grid;
    }
}
