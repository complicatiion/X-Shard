using Avalonia;
using XShard.Services;
using XShard.Views;
namespace XShard;
internal static class Program
{
 /// <summary>True when this process is the primary X-Shard instance of the current user.</summary>
 public static bool OwnsInstance {get;private set;}
 [STAThread]
 public static void Main(string[] args)
 {
  // Must run before any window exists: groups the window with pinned/installer shortcuts and enables toasts.
  WindowsIntegration.SetProcessAppId();
  OwnsInstance=SingleInstance.TryAcquire();
  if(!OwnsInstance&&SingleInstance.Forward(args))return;
  var dataDirectory=SafeDataDirectory();
  AppDomain.CurrentDomain.UnhandledException+=(_,e)=>{if(e.ExceptionObject is Exception ex)CrashLog.Write(dataDirectory,ex,"Unhandled exception");};
  TaskScheduler.UnobservedTaskException+=(_,e)=>{CrashLog.Write(dataDirectory,e.Exception,"Unobserved task exception");e.SetObserved();};
  try{BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);}
  catch(Exception ex){CrashLog.Write(dataDirectory,ex,"Startup or UI loop");throw;}
 }
 private static string SafeDataDirectory(){try{return new SettingsService().DataDirectory;}catch(Exception){return Path.Combine(Path.GetTempPath(),"X-Shard");}}
 public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
