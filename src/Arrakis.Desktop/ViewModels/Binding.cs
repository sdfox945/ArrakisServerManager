using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Arrakis.Desktop.ViewModels;

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? property=null) { if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;OnPropertyChanged(property);return true; }
    protected void OnPropertyChanged([CallerMemberName]string? property=null)=>PropertyChanged?.Invoke(this,new(property));
}
public sealed class RelayCommand(Action execute,Func<bool>? canExecute=null) : ICommand
{
    public bool CanExecute(object? p)=>canExecute?.Invoke()??true;public void Execute(object? p)=>execute();
    public event EventHandler? CanExecuteChanged { add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value; }
}
public sealed class AsyncCommand(Func<Task> execute,Func<bool>? canExecute=null) : ICommand
{
    private bool running;
    public bool CanExecute(object? p)=>!running&&(canExecute?.Invoke()??true);
    public async void Execute(object? p) { if(!CanExecute(p))return;running=true;CommandManager.InvalidateRequerySuggested();try{await execute();}finally{running=false;CommandManager.InvalidateRequerySuggested();} }
    public event EventHandler? CanExecuteChanged { add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value; }
}
public interface IUserInteraction
{
    bool Confirm(string title,string message);
    bool Review(string title,string summary,string diff);
    bool ReviewRawRemovals(string title,string summary,string diff,string fileName);
    void Preview(string title,string summary,string diff);
    void ShowError(string title,string summary,string details);
    string? ChooseFile(string title,string filter);
    string? ChooseFolder(string title,string initial);
    void Copy(string text);
    void Open(string path);
}
