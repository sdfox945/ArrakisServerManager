using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Arrakis.Core;
using Arrakis.Desktop.ViewModels;

namespace Arrakis.Desktop.Views;

public partial class ConfigurationView : UserControl
{
    private bool synchronizing, cancelPicker, pickerSession, syncQueued;
    private bool splitInitialized;
    public ConfigurationView() { InitializeComponent(); }
    private ConfigurationViewModel? Editor => DataContext is ShellViewModel shell ? shell.Editor : DataContext as ConfigurationViewModel;
    private void SliderChosen(object sender, RoutedPropertyChangedEventArgs<double> e) => Editor?.ChooseSliderValue(e.NewValue);
    private void InitializeSplit()
    {
        if(!splitInitialized&&Editor!=null&&VisualSplit.ActualWidth>=608&&VisualSplit.IsVisible){splitInitialized=true;DetailsColumn.Width=new GridLength(Math.Clamp(Editor.DetailsPaneWidth,280,Math.Max(280,VisualSplit.ActualWidth-380)));}
    }
    private void SplitLoaded(object sender,RoutedEventArgs e)=>Dispatcher.BeginInvoke(DispatcherPriority.Loaded,InitializeSplit);
    private void SplitSized(object sender,SizeChangedEventArgs e){InitializeSplit();var maximum=Math.Max(280,VisualSplit.ActualWidth-380);if(splitInitialized&&VisualSplit.ActualWidth>=608&&DetailsColumn.ActualWidth>maximum)DetailsColumn.Width=new GridLength(maximum);}
    private void SplitDragged(object sender,DragCompletedEventArgs e)=>Editor?.SavePaneWidth(DetailsColumn.ActualWidth);
    private void DefinitionDialog(bool edit)
    {
        if(Editor==null)return;
        try{var model=Editor.CreateDefinitionEditor(edit);using var lease=Editor.BeginMetadataReview();new SettingDefinitionWindow(model){Owner=Window.GetWindow(this)}.ShowDialog();}
        catch(Exception e){Editor.Validation=e.Message;}
    }
    private void AddVariableClicked(object sender,RoutedEventArgs e)=>DefinitionDialog(false);
    private void ManageClicked(object sender,RoutedEventArgs e)=>DefinitionDialog(true);
    private void CopyKeyClicked(object sender,RoutedEventArgs e)=>Editor?.CopyKey();
    private void RemoveClicked(object sender,RoutedEventArgs e)=>Editor?.RemoveDefinition();
    private void ImportClicked(object sender,RoutedEventArgs e)=>Editor?.ImportDefinitions();
    private void ExportClicked(object sender,RoutedEventArgs e)=>Editor?.ExportDefinitions();
    private void ReloadMetadataClicked(object sender,RoutedEventArgs e)=>Editor?.ReloadMetadata();
    private void DemoDefinitionsClicked(object sender,RoutedEventArgs e)=>Editor?.LoadDemoDefinitions();
    private void SyncPicker()
    {
        if (synchronizing || FilePicker == null) return;
        synchronizing = true;
        try { FilePicker.SetCurrentValue(Selector.SelectedItemProperty, Editor?.SelectedFile); }
        finally { synchronizing = false; }
    }
    private void QueuePickerSync()
    {
        if (syncQueued) return;
        syncQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, () => { syncQueued = false; if (!FilePicker.IsDropDownOpen) { SyncPicker(); FilePicker.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget(); } });
    }
    private void FilePickerOpened(object sender, EventArgs e) { cancelPicker = false; pickerSession = true; }
    private void FilePickerClosed(object sender, EventArgs e)
    {
        var commit = pickerSession; pickerSession = false;
        if (commit && !synchronizing && !cancelPicker && Editor?.CanInteract == true && FilePicker.SelectedItem is ConfigurationFileChoice choice) Editor.RequestFileSelection(choice);
        SyncPicker(); QueuePickerSync(); cancelPicker = false;
    }
    private void FilePickerChanged(object sender, SelectionChangedEventArgs e)
    {
        // Collection/binding updates are not user commits. In particular, null never owns a document.
        if (!synchronizing && FilePicker != null && !FilePicker.IsDropDownOpen) { SyncPicker(); QueuePickerSync(); }
    }
    private void FilePickerKeyDown(object sender, KeyEventArgs e)
    {
        if (FilePicker.IsDropDownOpen) { if (e.Key == Key.Escape) cancelPicker = true; return; }
        if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Editor?.CanInteract != true) return;
        var index = Editor.SelectedFile == null ? 0 : FilePicker.Items.IndexOf(Editor.SelectedFile);
        var next = e.Key switch { Key.Up => index - 1, Key.Down => index + 1, Key.Home => 0, _ => FilePicker.Items.Count - 1 };
        if (next >= 0 && next < FilePicker.Items.Count) Editor.RequestFileSelection((ConfigurationFileChoice)FilePicker.Items[next]);
        SyncPicker(); e.Handled = true;
    }
    private void RawEditorChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox text && text.IsKeyboardFocusWithin && Editor?.CanInteract == true && text.Text != Editor.RawText) Editor.EditRawText(text.Text);
    }
}
