using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Arrakis.Desktop.Controls;

// Binding updates and range coercion never become editing intent. Only native input or UIA SetValue chooses a proposal.
public sealed class ProposalSlider : Slider
{
    private bool gesture;
    public event RoutedPropertyChangedEventHandler<double>? ValueChosen;
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e){gesture=true;base.OnPreviewMouseLeftButtonDown(e);}
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e){base.OnPreviewMouseLeftButtonUp(e);Dispatcher.BeginInvoke(DispatcherPriority.Input,()=>gesture=false);}
    protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);gesture=false;}
    protected override void OnPreviewKeyDown(KeyEventArgs e){gesture=e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;base.OnPreviewKeyDown(e);Dispatcher.BeginInvoke(DispatcherPriority.Input,()=>gesture=false);}
    protected override void OnValueChanged(double oldValue,double newValue){base.OnValueChanged(oldValue,newValue);if(gesture)Choose(newValue);}
    private void Choose(double value){if(IsEnabled)ValueChosen?.Invoke(this,new(Value,value));}
    protected override AutomationPeer OnCreateAutomationPeer()=>new ProposalPeer(this);
    private sealed class ProposalPeer(ProposalSlider slider):SliderAutomationPeer(slider),IRangeValueProvider
    {
        public override object? GetPattern(PatternInterface pattern)=>pattern==PatternInterface.RangeValue?this:base.GetPattern(pattern);
        bool IRangeValueProvider.IsReadOnly=>!slider.IsEnabled;
        double IRangeValueProvider.LargeChange=>slider.LargeChange;
        double IRangeValueProvider.SmallChange=>slider.SmallChange;
        double IRangeValueProvider.Minimum=>slider.Minimum;
        double IRangeValueProvider.Maximum=>slider.Maximum;
        double IRangeValueProvider.Value=>slider.Value;
        void IRangeValueProvider.SetValue(double value){if(!slider.IsEnabled)throw new ElementNotEnabledException();if(!double.IsFinite(value)||value<slider.Minimum||value>slider.Maximum)throw new ArgumentOutOfRangeException(nameof(value));slider.SetCurrentValue(ValueProperty,value);slider.Choose(value);}
    }
}
