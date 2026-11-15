using Avalonia.Threading;
using XShard.Models;
using XShard.Services;
namespace XShard.ViewModels;
/// <summary>
/// v0.2.0: network shares as sources and targets (planned off the UI thread, UNC normalization, library matching by
/// Title ID) and FTP transfer to an Xbox 360 console (Aurora/FSD). The FTP feature is off until switched on in Settings.
/// </summary>
public sealed partial class MainViewModel
{
 // ---------- console (FTP) settings ----------
 private bool ftpEnabled;
 /// <summary>Master switch for console transfers. Off by default.</summary>
 public bool FtpEnabled
 {
  get=>ftpEnabled;
  set
  {
   if(ftpEnabled==value)return;
   if(!value&&OutputTarget==OutputTarget.XboxConsole)OutputTarget=OutputTarget.Custom; // before the target list loses the entry
   Set(ref ftpEnabled,value);
   Changed(nameof(OutputTargets));Changed(nameof(CanSendToConsole));TestConsoleCommand?.Refresh();SendSelectedToConsoleCommand?.Refresh();
  }
 }
 private string consoleHost="",consoleUser="xboxftp",consolePassword="xboxftp",consoleDrive="Hdd1",consoleGamesFolder="Games",consoleTestState="";
 private int consolePort=21;private bool deleteTransferCopy=true;
 public string ConsoleHost {get=>consoleHost;set {if(Set(ref consoleHost,(value??"").Trim()))Changed(nameof(ConsoleTargetText));}}
 public int ConsolePort {get=>consolePort;set {if(Set(ref consolePort,value is >0 and <65536?value:21)){consolePortText=consolePort.ToString();Changed(nameof(ConsolePortText));}}}
 private string consolePortText="21",consolePortError="";
 /// <summary>Port as typed: only 1–65535 is accepted; anything else shows an error instead of silently using 21.</summary>
 public string ConsolePortText
 {
  get=>consolePortText;
  set
  {
   consolePortText=value??"";Changed();
   if(int.TryParse(consolePortText.Trim(),out var port)&&port is >0 and <65536){ConsolePortError="";if(port!=consolePort){consolePort=port;Changed(nameof(ConsolePort));}}
   else ConsolePortError=L.T("Port must be a number from 1 to 65535.");
  }
 }
 public string ConsolePortError {get=>consolePortError;private set {if(Set(ref consolePortError,value))Changed(nameof(HasConsolePortError));}}
 public bool HasConsolePortError=>ConsolePortError.Length>0;
 /// <summary>Shown instead of the output folder when the console is the target.</summary>
 public string ConsoleTargetText=>string.IsNullOrWhiteSpace(ConsoleHost)?L.T("Xbox 360 console (FTP) – set the IP address in Settings"):$"{L.T("Console")} {ConsoleHost} · /{ConsoleDrive}";
 public string ConsoleUser {get=>consoleUser;set=>Set(ref consoleUser,value??"");}
 /// <summary>Kept in memory only; saved DPAPI-protected (current Windows user).</summary>
 public string ConsolePassword {get=>consolePassword;set=>Set(ref consolePassword,value??"");}
 public string[] ConsoleDrives=>["Hdd1","Usb0","Usb1"];
 public string ConsoleDrive {get=>consoleDrive;set {if(value is not null&&ConsoleDrives.Contains(value)&&Set(ref consoleDrive,value))Changed(nameof(ConsoleTargetText));}}
 public string ConsoleGamesFolder {get=>consoleGamesFolder;set=>Set(ref consoleGamesFolder,string.IsNullOrWhiteSpace(value)?"Games":value.Trim().Trim('/','\\'));}
 public bool DeleteTransferCopy {get=>deleteTransferCopy;set=>Set(ref deleteTransferCopy,value);}
 public string ConsoleTestState {get=>consoleTestState;private set=>Set(ref consoleTestState,value);}
 public bool CanSendToConsole=>FtpEnabled;
 /// <summary>X-Shard's own folder for results that are sent to the console (deleted after a verified transfer when enabled).</summary>
 public string ConsoleTransferDirectory=>Path.Combine(Storage.DataDirectory,"console-transfer");
 /// <summary>The local copy is only deleted after a transfer when the result lives in X-Shard's own transfer folder.</summary>
 private bool DeletesTransferCopy=>DeleteTransferCopy&&OutputDirectory.Length>0&&Path.GetFullPath(OutputDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(ConsoleTransferDirectory).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);
 public AsyncRelayCommand? TestConsoleCommand {get;private set;}

 private ConsoleProfile CurrentConsoleProfile()=>new()
 {
  Host=ConsoleHost,Port=ConsolePort,User=ConsoleUser,Drive=ConsoleDrive,GamesFolder=ConsoleGamesFolder,DeleteAfterUpload=DeleteTransferCopy
 };
 private string CurrentConsolePassword()=>ConsolePassword.Length==0?"xboxftp":ConsolePassword;

 private void InitializeNetwork()
 {
  Queue.Executor.WorkDirectory=Path.Combine(Storage.DataDirectory,"work");
  // Leftovers of interrupted runs in X-Shard's own local working folder (never user folders).
  // Only folders untouched for 12 hours: a scheduled xshard-cli run may be using the same working folder right now.
  _=Task.Run(()=>{try{if(Directory.Exists(Queue.Executor.WorkDirectory))foreach(var stale in Directory.GetDirectories(Queue.Executor.WorkDirectory,".xshard-*").Where(d=>DateTime.UtcNow-Directory.GetLastWriteTimeUtc(d)>TimeSpan.FromHours(12)))ResilientIO.TryDeleteDirectory(stale);}catch(Exception){}});
  TestConsoleCommand=new AsyncRelayCommand(TestConsoleAsync,ReportError,()=>FtpEnabled);
  CancelAddCommand=new RelayCommand(()=>addCancel?.Cancel(),()=>IsAdding);
  CleanTransferCommand=new AsyncRelayCommand(CleanTransferFolderAsync,ReportError,()=>!IsRunning);
  SendSelectedToConsoleCommand=new AsyncRelayCommand(()=>SelectedJob is {} job&&ResendablePath(job) is {} path?SendToConsoleAsync([path]):Task.CompletedTask,ReportError,()=>SelectedJob is {} job&&ResendablePath(job)!=null);
  _=RefreshTransferSizeAsync();
  Queue.Uploader=async(job,local,log,progress,token)=>
  {
   if(!FtpEnabled)throw new IOException(L.T("Console transfer (FTP) is disabled in Settings."));
   var service=new ConsoleTransferService(CurrentConsoleProfile(),CurrentConsolePassword());
   return await service.UploadAsync(local,log,percent=>Dispatcher.UIThread.Post(()=>progress(percent)),token);
  };
 }
 public async Task TestConsoleAsync()
 {
  ConsoleTestState=L.T("Testing");
  try
  {
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
   var drives=await new ConsoleTransferService(CurrentConsoleProfile(),CurrentConsolePassword()){Policy=RetryPolicy.Default with {ProbeTimeout=TimeSpan.FromSeconds(4)}}.TestAsync(timeout.Token);
   ConsoleTestState=L.T("Connected")+" · "+drives;
  }
  catch(Exception ex){ConsoleTestState=L.T("Test Failed")+": "+ex.Message;}
 }
 private void LoadNetworkSettings(AppSettings s)
 {
  ftpEnabled=s.FtpEnabled;
  var c=s.XboxConsole??new ConsoleProfile();
  consoleHost=c.Host??"";consolePort=c.Port is >0 and <65536?c.Port:21;consolePortText=consolePort.ToString();consoleUser=string.IsNullOrEmpty(c.User)?"xboxftp":c.User;
  consoleDrive=ConsoleDrives.Contains(c.Drive)?c.Drive:"Hdd1";consoleGamesFolder=string.IsNullOrWhiteSpace(c.GamesFolder)?"Games":c.GamesFolder;deleteTransferCopy=c.DeleteAfterUpload;
  var password=SecretProtector.Unprotect(c.ProtectedPassword??"");
  if(password==null){consolePassword="";Message=L.T("The saved FTP password could not be read on this account. Enter it again in Settings.");}
  else consolePassword=password.Length==0?"xboxftp":password;
 }
 private void StoreNetworkSettings(AppSettings s)
 {
  s.FtpEnabled=FtpEnabled;
  string protectedPassword="";
  try{protectedPassword=ConsolePassword is "" or "xboxftp"?"":SecretProtector.Protect(ConsolePassword);}
  catch(PlatformNotSupportedException){}
  s.XboxConsole=new ConsoleProfile{Host=ConsoleHost,Port=ConsolePort,User=ConsoleUser,ProtectedPassword=protectedPassword,Drive=ConsoleDrive,GamesFolder=ConsoleGamesFolder,DeleteAfterUpload=DeleteTransferCopy};
 }
 private void ResetNetworkSettings()
 {
  FtpEnabled=false;ConsoleHost="";ConsolePort=21;ConsolePortText="21";ConsoleUser="xboxftp";ConsolePassword="xboxftp";ConsoleDrive="Hdd1";ConsoleGamesFolder="Games";DeleteTransferCopy=true;ConsoleTestState="";
 }

 // ---------- adding sources (network-safe) ----------
 private bool isAdding;
 /// <summary>True while sources are checked in the background (network shares can take a while).</summary>
 public bool IsAdding {get=>isAdding;private set {if(Set(ref isAdding,value)){if(!value)AddingStatus="";CancelAddCommand?.Refresh();}}}
 private string addingStatus="";
 /// <summary>Progress text while sources or the target library are checked (folders, games found).</summary>
 public string AddingStatus {get=>addingStatus;private set=>Set(ref addingStatus,value);}
 private CancellationTokenSource? addCancel;
 public RelayCommand? CancelAddCommand {get;private set;}
 private readonly Dictionary<string,(DateTime Time,HashSet<string> Keys)> libraryIndex=new(StringComparer.OrdinalIgnoreCase);

 /// <summary>Games (Title ID + Media ID + disc) that already exist in the output folder, in any format. Cached for two minutes.</summary>
 private async Task<HashSet<string>> TargetLibraryKeysAsync(string folder,CancellationToken token)
 {
  if(!SkipExistingGames||string.IsNullOrWhiteSpace(folder)||OutputTarget==OutputTarget.XboxConsole)return [];
  if(libraryIndex.TryGetValue(folder,out var cached)&&DateTime.UtcNow-cached.Time<TimeSpan.FromMinutes(2))return cached.Keys;
  if(!await NetworkPath.IsReachableAsync(folder,TimeSpan.FromSeconds(8),token)||!await Task.Run(()=>Directory.Exists(folder),token))return [];
  AddingStatus=L.T("Checking the target library for games that already exist …");
  var entries=await LibraryScanner.ScanAsync(folder,text=>Dispatcher.UIThread.Post(()=>AddingStatus=text),token);
  // A probably scrubbed GoD is not counted: the ISO may be added to rebuild it with Scrub None.
  var keys=LibraryScanner.Keys(entries.Where(e=>!e.ProbablyScrubbed));
  libraryIndex[folder]=(DateTime.UtcNow,keys);
  return keys;
 }

 /// <summary>
 /// Adds files, image folders or game folders without blocking the window: existence checks and planning run in the
 /// background (a dead share can block file APIs for a minute). Image folders are expanded; images whose game already
 /// exists in the output folder (ISO, GoD or extracted) are skipped and listed.
 /// </summary>
 public async Task AddSourcesAsync(IEnumerable<string> paths,bool expandFolders=false,bool folderFallback=true)
 {
  if(ShowLegal){Message=L.T("Review and accept the legal documents first.");return;}
  if(NeedsOutput){Message="";return;}
  if(IsAdding){Message=L.T("Checking sources …");return;} // one batch at a time: planning checks collisions against the queue
  var request=CurrentRequest;var raw=paths.Where(p=>!string.IsNullOrWhiteSpace(p)).ToList();
  if(raw.Count==0)return;
  IsAdding=true;
  try
  {
   using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(30));addCancel=cancel;AddingStatus=L.T("Checking sources …");
   var mode=Mode;var recursive=RecursiveFolders;var usesXgd=UsesXgd;
   // 1. Expand image folders (background).
   var sources=await Task.Run(()=>
   {
    var list=new List<string>();
    foreach(var path in raw)
    {
     var full=NetworkPath.ToUniversal(path);
     if(expandFolders&&Directory.Exists(full)&&mode!=XisoMode.Create&&!(usesXgd&&TitleInfoReader.FindStfsHeader(full,cancel.Token)!=null))
     {
      var images=FolderBatchService.EnumerateImages(full,recursive,usesXgd,cancel.Token);
      if(images.Count>0)list.AddRange(images);else if(folderFallback)list.Add(full);
     }
     else list.Add(full);
    }
    return list;
   },cancel.Token);
   // 2. Games that already exist in the target library.
   var existing=sources.Count>0&&IsWriting?await TargetLibraryKeysAsync(NetworkPath.ToUniversal(OutputDirectory),cancel.Token):new HashSet<string>();
   // 3. Plan every source in the background; collisions are checked against the queue and this batch.
   var active=Jobs.Where(j=>j.Status is JobStatus.Waiting or JobStatus.Running).Select(j=>j.Options).ToList();
   var platform=Platform;
   var (planned,skipped,errors)=await Task.Run(()=>
   {
    var ok=new List<(XisoJobOptions Options,TitleInfo? Title)>();var already=new List<string>();var failed=new List<string>();
    foreach(var source in sources)
    {
     cancel.Token.ThrowIfCancellationRequested();
     try
     {
      TitleInfo? title=existing.Count>0?TitleInfoReader.TryRead(source,platform,cancel.Token):null;
      if(LibraryScanner.KeyOf(title) is {} key&&existing.Contains(key)){already.Add(title?.Name??Path.GetFileName(source));continue;}
      var options=JobPlanner.Plan(request,source,active);
      active.Add(options);ok.Add((options,title));
     }
     catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException){failed.Add(ex.Message);}
    }
    return (ok,already,failed);
   },cancel.Token);
   XisoJob? last=null;
   foreach(var (options,title) in planned)
   {
    var job=new XisoJob{Options=options,Status=JobStatus.Waiting};
    if(title!=null)job.Title=title;
    Jobs.Add(job);last=job;if(title==null)QueueTitleRead(job);
   }
   if(last!=null)SelectedJob=last;
   var notes=new List<string>();
   if(skipped.Count>0)notes.Add(string.Format(L.T("already_in_library_skipped"),skipped.Count)+" "+string.Join(", ",skipped.Take(8))+(skipped.Count>8?" …":""));
   if(errors.Count>0)notes.Add(errors.Count==1?errors[0]:$"{errors.Count} × {errors[0]}");
   Message=string.Join(Environment.NewLine,notes);
   Refresh();
   await SaveQueueSafelyAsync();
  }
  catch(OperationCanceledException){Message=L.T("Adding sources was stopped.");}
  finally{addCancel=null;IsAdding=false;}
 }

 // ---------- transfer folder ----------
 private string transferSize="";
 public string TransferFolderSize {get=>transferSize;private set=>Set(ref transferSize,value);}
 public AsyncRelayCommand? CleanTransferCommand {get;private set;}
 public AsyncRelayCommand? SendSelectedToConsoleCommand {get;private set;}
 /// <summary>A finished GoD/extract result (or upload source) of the selected job that can be sent to the console again.</summary>
 private string? ResendablePath(XisoJob job)
 {
  if(!FtpEnabled||job.Status!=JobStatus.Succeeded||job.Options.Platform!=ConsolePlatform.Xbox360)return null;
  var path=job.Options.Mode==XisoMode.Upload?job.Options.Source:job.PublishedPath;
  // No Directory.Exists on a share here (UI thread): a dead SMB session could block; the transfer checks the folder anyway.
  if(path==null||!NetworkPath.IsUnc(path)&&!Directory.Exists(path))return null;
  var format=job.Options.Format==OutputFormat.ConsoleDefault?OutputFormat.GoD:job.Options.Format;
  return job.Options.Mode is XisoMode.Upload or XisoMode.Extract||job.Options.Mode==XisoMode.Convert&&format is OutputFormat.GoD or OutputFormat.Extract?path:null;
 }
 /// <summary>Paths in the transfer folder that queued jobs still need (results waiting for a retried transfer).</summary>
 private HashSet<string> TransferPathsInUse()=>Jobs
  .SelectMany(j=>new[]{j.PublishedPath,j.Options.Mode==XisoMode.Upload?j.Options.Source:null,j.Options.Destination})
  .OfType<string>().Where(p=>p.Length>0).Select(p=>Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar)).ToHashSet(StringComparer.OrdinalIgnoreCase);
 public async Task RefreshTransferSizeAsync()
 {
  var folder=ConsoleTransferDirectory;
  var size=await Task.Run(()=>{try{return Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length):0L;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return 0L;}});
  TransferFolderSize=string.Format(L.T("transfer_folder_size"),RunReportService.FormatBytes(size));
 }
 /// <summary>Deletes leftovers in X-Shard's own transfer folder (never results that queued jobs still need) after a Yes.</summary>
 public async Task CleanTransferFolderAsync()
 {
  var folder=ConsoleTransferDirectory;if(!Directory.Exists(folder)){await RefreshTransferSizeAsync();return;}
  if(!await ConfirmAsync(L.T("Clean up transfer folder?"),L.T("clean_transfer_message"),folder,"Trash"))return;
  var keep=TransferPathsInUse();
  var removed=await Task.Run(()=>
  {
   var count=0;
   foreach(var entry in Directory.EnumerateFileSystemEntries(folder).ToList())
   {
    // Kept: results of jobs that are still in the queue (also finished ones that can be sent again) and live working folders.
    if(keep.Contains(Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar))||Path.GetFileName(entry).StartsWith(".xshard-",StringComparison.Ordinal))continue;
    try{if(Directory.Exists(entry))Directory.Delete(entry,true);else File.Delete(entry);count++;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
   }
   return count;
  });
  Message=string.Format(L.T("transfer_cleaned"),removed);
  await RefreshTransferSizeAsync();
 }

 /// <summary>Console jobs need FTP switched on and a console address before the run starts (not only after converting).</summary>
 private bool ConsoleJobsReady()
 {
  var needsConsole=Jobs.Any(j=>j.Status==JobStatus.Waiting&&(j.Options.UploadToConsole||j.Options.Mode==XisoMode.Upload));
  if(!needsConsole)return true;
  if(!FtpEnabled){Message=L.T("Enable console transfer (FTP) in Settings first.");return false;}
  if(string.IsNullOrWhiteSpace(ConsoleHost)){Message=L.T("Set the console IP address in Settings › Xbox 360 console (FTP).");return false;}
  if(HasConsolePortError){Message=ConsolePortError;return false;}
  return true;
 }

 /// <summary>Queues existing GoD packages or extracted Xbox 360 game folders for transfer to the console.</summary>
 public async Task SendToConsoleAsync(IEnumerable<string> folders)
 {
  if(ShowLegal){Message=L.T("Review and accept the legal documents first.");return;}
  if(!FtpEnabled){Message=L.T("Enable console transfer (FTP) in Settings first.");return;}
  if(IsAdding){Message=L.T("Checking sources …");return;}
  var raw=folders.ToList();if(raw.Count==0)return;
  IsAdding=true;
  try
  {
   var active=Jobs.Where(j=>j.Status is JobStatus.Waiting or JobStatus.Running).Select(j=>j.Options).ToList();
   var extracted=0;
   var (planned,errors)=await Task.Run(()=>
   {
    var ok=new List<XisoJobOptions>();var failed=new List<string>();
    foreach(var folder in raw)
    {
     try{if(ConsoleTransferService.Plan(NetworkPath.ToUniversal(folder),CurrentConsoleProfile()).Kind==LibraryKind.Extracted)Interlocked.Increment(ref extracted);var options=JobPlanner.PlanUpload(folder,active);active.Add(options);ok.Add(options);}
     catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException){failed.Add(ex.Message);}
    }
    return (ok,failed);
   });
   XisoJob? last=null;
   foreach(var options in planned){var job=new XisoJob{Options=options,Status=JobStatus.Waiting};Jobs.Add(job);last=job;QueueTitleRead(job);}
   if(last!=null)SelectedJob=last;
   var notes=new List<string>();
   if(errors.Count>0)notes.Add(errors.Count==1?errors[0]:$"{errors.Count} × {errors[0]}");
   if(extracted>0)notes.Add(L.T("extracted_console_hint"));
   Message=string.Join(Environment.NewLine,notes);
   Refresh();await SaveQueueSafelyAsync();
  }
  finally{IsAdding=false;}
 }
}
