using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Arrakis.Core;

namespace Arrakis.Desktop.Controls;

public sealed class ResourceChart : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty=DependencyProperty.Register(nameof(Samples),typeof(IReadOnlyList<ResourceSample>),typeof(ResourceChart),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MetricProperty=DependencyProperty.Register(nameof(Metric),typeof(string),typeof(ResourceChart),new FrameworkPropertyMetadata("Host",FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MinutesProperty=DependencyProperty.Register(nameof(Minutes),typeof(int),typeof(ResourceChart),new FrameworkPropertyMetadata(15,FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<ResourceSample>? Samples {get=>(IReadOnlyList<ResourceSample>?)GetValue(SamplesProperty);set=>SetValue(SamplesProperty,value);}
    public string Metric {get=>(string)GetValue(MetricProperty);set=>SetValue(MetricProperty,value);}
    public int Minutes {get=>(int)GetValue(MinutesProperty);set=>SetValue(MinutesProperty,value);}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var left=30.0;var width=Math.Max(1,ActualWidth-left-8);var height=Math.Max(1,ActualHeight-28);
        void Text(string text,double x,double y)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.LightGray,VisualTreeHelper.GetDpi(this).PixelsPerDip),new(x,y));
        foreach(var n in new[]{0,50,100}){var y=8+height*(1-n/100.0);dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52,58,60)),1),new(left,y),new(left+width,y));Text(n.ToString(CultureInfo.InvariantCulture),0,y-6);}
        var samples=Samples??[];var end=samples.LastOrDefault()?.Time??DateTimeOffset.Now;Point? previous=null;
        var pen=new Pen(new SolidColorBrush(Color.FromRgb(210,171,104)),2);
        foreach(var s in samples)
        {
            var value=Metric=="Host"?s.HostRam:Metric=="Guest"?s.GuestRam:s.Swap;
            if(value==null){previous=null;continue;}
            var point=new Point(left+Math.Clamp(1-(end-s.Time).TotalMinutes/Minutes,0,1)*width,8+height*(1-Math.Clamp(value.Value,0,100)/100));
            if(previous is {} p)dc.DrawLine(pen,p,point);dc.DrawEllipse(pen.Brush,null,point,2,2);previous=point;
        }
        Text($"−{Minutes}m",left,ActualHeight-14);Text("now",left+Math.Max(0,width-24),ActualHeight-14);
        if(samples.Count<2)Text("Collecting samples…",left+6,20);
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo){base.OnRenderSizeChanged(sizeInfo);InvalidateVisual();}
}
