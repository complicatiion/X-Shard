using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using XShard.Views;
namespace XShard;
public partial class App : Application
{
 public override void Initialize() => AvaloniaXamlLoader.Load(this);
 public override void OnFrameworkInitializationCompleted()
 {
  if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
  {
   var window=new MainWindow();desktop.MainWindow=window;
   var startArgs=desktop.Args??[];
   if(startArgs.Length>0)window.Opened+=(_,_)=>Dispatcher.UIThread.Post(()=>window.HandleCommandLine(startArgs));
   // Later starts (Explorer context menu, double click while running) forward their arguments here.
   if(Program.OwnsInstance)SingleInstance.Listen(args=>Dispatcher.UIThread.Post(()=>window.HandleCommandLine(args)));
  }
  base.OnFrameworkInitializationCompleted();
 }
}
