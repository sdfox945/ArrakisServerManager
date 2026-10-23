using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Arrakis.Desktop;
public sealed class ReviewWindow : Window
{
    public ReviewWindow(string title,string summary,string diff,bool confirmNeeded=true,string? requiredFileName=null)
    {
        Style=(Style)Application.Current.FindResource(typeof(Window));Title=title;Width=850;Height=640;MinWidth=620;MinHeight=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var grid=new Grid{Margin=new Thickness(24)};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var intro=new TextBlock{Text=summary,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)};grid.Children.Add(intro);
        var text=new TextBox{Text=diff,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Cascadia Mono, Consolas"),FontSize=13};Grid.SetRow(text,1);grid.Children.Add(text);
        var footer=new StackPanel();var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};var cancel=new Button{Content=confirmNeeded?"Cancel":"Close preview",IsCancel=true};var apply=new Button{Content="Confirm reviewed change",Style=(Style)Application.Current.FindResource("PrimaryButton")};apply.Click+=(_,_)=>DialogResult=true;
        if(requiredFileName!=null){footer.Children.Add(new TextBlock{Text="These raw edits remove existing content. Type "+requiredFileName+" to authorize these exact removals.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,6)});var name=new TextBox();System.Windows.Automation.AutomationProperties.SetName(name,"Confirm configuration filename");name.TextChanged+=(_,_)=>apply.IsEnabled=name.Text==requiredFileName;apply.IsEnabled=false;footer.Children.Add(name);}
        buttons.Children.Add(cancel);if(confirmNeeded)buttons.Children.Add(apply);footer.Children.Add(buttons);Grid.SetRow(footer,2);grid.Children.Add(footer);Content=grid;
    }
}
