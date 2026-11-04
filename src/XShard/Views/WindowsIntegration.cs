using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
namespace XShard.Views;
public enum TaskbarState { None=0, Indeterminate=0x1, Normal=0x2, Error=0x4, Paused=0x8 }
/// <summary>
/// Windows shell integration: AppUserModelID (taskbar grouping + toasts), toast notifications, taskbar progress,
/// window flashing, hibernate/shutdown and the classic Explorer context menu. Every call is a no-op outside Windows and never throws.
/// </summary>
public static class WindowsIntegration
{
 /// <summary>Same ID as the installer shortcuts (setup.iss AppUserModelID) so the pinned and the running icon are one taskbar button.</summary>
 public const string AppId="sksdesign.XShard";
 public const string DisplayName="X-Shard";
 /// <summary>False in automated tests: no registry writes or deletions (context menu, toast registration).</summary>
 public static bool ShellWritesEnabled {get;set;}=true;

 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
 public static void SetProcessAppId(){if(OperatingSystem.IsWindows())try{SetCurrentProcessExplicitAppUserModelID(AppId);}catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException){}}

 // ---------- toasts ----------
 private const string AumidKey=@"Software\Classes\AppUserModelId\"+AppId;
 /// <summary>Registers the AppUserModelID for toasts without a Start-menu shortcut (portable). Stored under HKCU; removed again by <see cref="UnregisterToasts"/>.</summary>
 [SupportedOSPlatform("windows")]
 private static void RegisterToasts()
 {
  using var key=Registry.CurrentUser.CreateSubKey(AumidKey);
  key.SetValue("DisplayName",DisplayName);
  var icon=Path.Combine(AppContext.BaseDirectory,"Assets","toast-icon.png");
  if(File.Exists(icon))key.SetValue("IconUri",icon);
  key.SetValue("IconBackgroundColor","FF111A14");
 }
 public static void UnregisterToasts(){if(ShellWritesEnabled&&OperatingSystem.IsWindows())try{Registry.CurrentUser.DeleteSubKeyTree(AumidKey,false);}catch(Exception ex) when(ex is UnauthorizedAccessException or IOException or SecurityException){}}
 public static bool ShowToast(string title,string body)
 {
  if(!ShellWritesEnabled||!OperatingSystem.IsWindowsVersionAtLeast(10,0,17763))return false;
  try
  {
   RegisterToasts();
   var xml=new global::Windows.Data.Xml.Dom.XmlDocument();
   xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text></binding></visual></toast>");
   global::Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(AppId).Show(new global::Windows.UI.Notifications.ToastNotification(xml));
   return true;
  }
  catch(Exception){return false;}
 }

 // ---------- taskbar ----------
 [ComImport,Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 private interface ITaskbarList3
 {
  void HrInit();void AddTab(IntPtr hwnd);void DeleteTab(IntPtr hwnd);void ActivateTab(IntPtr hwnd);void SetActiveAlt(IntPtr hwnd);
  void MarkFullscreenWindow(IntPtr hwnd,[MarshalAs(UnmanagedType.Bool)] bool fullscreen);
  void SetProgressValue(IntPtr hwnd,ulong completed,ulong total);
  void SetProgressState(IntPtr hwnd,TaskbarState state);
 }
 [ComImport,Guid("56fdf344-fd6d-11d0-958a-006097c9a090"),ClassInterface(ClassInterfaceType.None)] private class TaskbarListClass {}
 private static ITaskbarList3? taskbar;private static bool taskbarFailed;
 private static ITaskbarList3? Taskbar()
 {
  if(taskbar!=null||taskbarFailed||!OperatingSystem.IsWindows())return taskbar;
  try{var list=(ITaskbarList3)new TaskbarListClass();list.HrInit();taskbar=list;}catch(Exception){taskbarFailed=true;}
  return taskbar;
 }
 public static void SetTaskbarProgress(IntPtr hwnd,TaskbarState state,double percent)
 {
  if(hwnd==IntPtr.Zero||Taskbar() is not {} list)return;
  try{list.SetProgressState(hwnd,state);if(state is TaskbarState.Normal or TaskbarState.Error or TaskbarState.Paused)list.SetProgressValue(hwnd,(ulong)Math.Clamp(percent*10,0,1000),1000);}catch(Exception){}
 }
 [StructLayout(LayoutKind.Sequential)] private struct FLASHWINFO {public uint cbSize;public IntPtr hwnd;public uint dwFlags;public uint uCount;public uint dwTimeout;}
 [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
 /// <summary>Flashes the taskbar button until the window is activated.</summary>
 public static void Flash(IntPtr hwnd)
 {
  if(hwnd==IntPtr.Zero||!OperatingSystem.IsWindows())return;
  try{var info=new FLASHWINFO{cbSize=(uint)Marshal.SizeOf<FLASHWINFO>(),hwnd=hwnd,dwFlags=0x3|0xC,uCount=0,dwTimeout=0};FlashWindowEx(ref info);}catch(Exception){}
 }

 // ---------- power ----------
 // powrprof returns and takes 1-byte BOOLEAN values.
 [DllImport("powrprof.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool IsPwrHibernateAllowed();
 [DllImport("powrprof.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,[MarshalAs(UnmanagedType.U1)] bool forceCritical,[MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
 public static bool CanHibernate(){if(!OperatingSystem.IsWindows())return false;try{return IsPwrHibernateAllowed();}catch(Exception){return false;}}
 public static bool Hibernate(){if(!OperatingSystem.IsWindows())return false;try{return SetSuspendState(true,false,false);}catch(Exception){return false;}}
 public static bool Shutdown()
 {
  if(!OperatingSystem.IsWindows())return false;
  try{using var process=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"shutdown.exe")){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"/s","/t","0","/d","p:0:0"}});return process!=null;}
  catch(Exception){return false;}
 }

 // ---------- Explorer context menu (classic; Windows 11: "Show more options") ----------
 private static readonly string[] ImageExtensions=[".iso",".xiso"];
 private static IEnumerable<(string Key,string Verb,string Argument)> Verbs(string extract,string convert,string create)
 {
  foreach(var extension in ImageExtensions)
  {
   yield return ($@"Software\Classes\SystemFileAssociations\{extension}\shell\XShard.Extract",extract,"--extract");
   yield return ($@"Software\Classes\SystemFileAssociations\{extension}\shell\XShard.Convert",convert,"--convert");
  }
  yield return (@"Software\Classes\Directory\shell\XShard.Create",create,"--create");
 }
 /// <summary>True if the per-user entries exist (portable toggle). Installer entries live in HKLM and are managed by setup.</summary>
 public static bool IsContextMenuRegistered()
 {
  if(!OperatingSystem.IsWindows())return false;
  try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\.iso\shell\XShard.Extract");return key!=null;}catch(Exception){return false;}
 }
 public static bool IsContextMenuInstalledForAllUsers()
 {
  if(!OperatingSystem.IsWindows())return false;
  try{using var key=Registry.LocalMachine.OpenSubKey(@"Software\Classes\SystemFileAssociations\.iso\shell\XShard.Extract");return key!=null;}catch(Exception){return false;}
 }
 public static bool RegisterContextMenu(string extract,string convert,string create)
 {
  if(!ShellWritesEnabled||!OperatingSystem.IsWindows()||Environment.ProcessPath is not {} exe)return false;
  try
  {
   foreach(var (path,verb,argument) in Verbs(extract,convert,create))
   {
    using var key=Registry.CurrentUser.CreateSubKey(path);key.SetValue("MUIVerb",verb);key.SetValue("Icon",$"\"{exe}\",0");
    using var command=key.CreateSubKey("command");command.SetValue("",$"\"{exe}\" {argument} \"%1\"");
   }
   return true;
  }
  catch(Exception ex) when(ex is UnauthorizedAccessException or IOException or SecurityException){return false;}
 }
 public static void UnregisterContextMenu()
 {
  if(!ShellWritesEnabled||!OperatingSystem.IsWindows())return;
  foreach(var (path,_,_) in Verbs("","",""))try{Registry.CurrentUser.DeleteSubKeyTree(path,false);}catch(Exception ex) when(ex is UnauthorizedAccessException or IOException or SecurityException){}
 }
}

/// <summary>One X-Shard window per user. Later starts (e.g. from the Explorer context menu) hand their arguments to the running instance over a local named pipe (current user only, no network).</summary>
public static class SingleInstance
{
 private const string PipeName="XShard.Instance.v1";
 private static Mutex? mutex;
 public static bool TryAcquire()
 {
  try{mutex=new Mutex(true,@"Local\XShard.SingleInstance",out var created);return created;}catch(Exception){return true;}
 }
 public static bool Forward(string[] args)
 {
  try
  {
   using var client=new NamedPipeClientStream(".",PipeName,PipeDirection.Out,PipeOptions.CurrentUserOnly);
   client.Connect(4000);
   using var writer=new StreamWriter(client);
   writer.WriteLine(args.Length);foreach(var arg in args)writer.WriteLine(arg.Replace('\r',' ').Replace('\n',' '));
   writer.Flush();return true;
  }
  catch(Exception ex) when(ex is TimeoutException or IOException or UnauthorizedAccessException){return false;}
 }
 public static void Listen(Action<string[]> received)
 {
  _=Task.Run(async()=>
  {
   while(true)
   {
    try
    {
     await using var server=new NamedPipeServerStream(PipeName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
     await server.WaitForConnectionAsync();
     using var reader=new StreamReader(server);
     if(!int.TryParse(await reader.ReadLineAsync(),out var count)||count is <0 or >512)continue;
     var args=new List<string>();
     for(var i=0;i<count&&await reader.ReadLineAsync() is {} line;i++)args.Add(line);
     received(args.ToArray());
    }
    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ObjectDisposedException){await Task.Delay(250);}
   }
  });
 }
}
