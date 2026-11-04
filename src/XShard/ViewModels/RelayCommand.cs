using System.Windows.Input;
namespace XShard.ViewModels;
public sealed class RelayCommand(Action action, Func<bool>? canExecute=null) : ICommand
{
 public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
 public void Execute(object? parameter) { if(CanExecute(parameter)) action(); }
 public event EventHandler? CanExecuteChanged;
 public void Refresh() => CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
public sealed class AsyncRelayCommand(Func<Task> action, Action<Exception> onError, Func<bool>? canExecute=null) : ICommand
{
 private bool running;
 public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);
 public async void Execute(object? parameter)
 {
  if(!CanExecute(parameter)) return;
  running=true; Refresh();
  try { await action(); } catch(Exception ex) { onError(ex); }
  finally { running=false; Refresh(); }
 }
 public event EventHandler? CanExecuteChanged;
 public void Refresh() => CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
