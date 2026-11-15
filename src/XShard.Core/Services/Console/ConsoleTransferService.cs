using System.Net.Sockets;
using FluentFTP;
using FluentFTP.Exceptions;
using XShard.Models;
namespace XShard.Services;

/// <summary>What will be sent and where (Aurora/FSD layout).</summary>
public sealed record ConsoleTransferPlan(LibraryKind Kind,string LocalRoot,string RemoteTarget,IReadOnlyList<(string Local,string Remote)> Files,IReadOnlyList<(string Local,string Remote)> CommitLast,IReadOnlyList<string> EmptyFolders,string? PartialFolder,long TotalBytes)
{
 public string Describe=>Kind==LibraryKind.GoD?$"GoD → {RemoteTarget}":$"Extracted → {RemoteTarget}";
}

/// <summary>
/// Sends Xbox 360 games to a modded console (RGH/JTAG) via the FTP server of Aurora/FSD.
/// GoD: /Hdd1/Content/0000000000000000/&lt;TitleID&gt;/00007000/… (files via temporary name + rename, STFS headers last,
/// so the dashboard only sees complete games). Extracted: uploaded into "&lt;name&gt;.xsp" and renamed to the final folder.
/// Interrupted transfers reconnect after the console is reachable again and resume at the transferred size.
/// </summary>
public sealed class ConsoleTransferService(ConsoleProfile profile,string password)
{
 public const string TempSuffix=".xstmp",PartialSuffix=".xsp";
 public const int FatxNameLimit=42;
 public RetryPolicy Policy {get;init;}=RetryPolicy.Default;
 private string? sourceRoot;
 /// <summary>Set when the console's FTP server rejects APPE/REST: interrupted files are then sent again from the start.</summary>
 private volatile bool resumeUnsupported;
 public bool ResumeUnsupported=>resumeUnsupported;
 private static bool IsNotImplemented(Exception ex)=>
  ex is FtpCommandException {CompletionCode:"500" or "502" or "504"}||ex.InnerException is {} inner&&IsNotImplemented(inner);

 // ---------- planning ----------
 /// <summary>Builds the transfer plan for a GoD package or an extracted Xbox 360 game (folder, container or STFS header file).</summary>
 public static ConsoleTransferPlan Plan(string localPath,ConsoleProfile profile)
 {
  var drive="/"+Segment(string.IsNullOrWhiteSpace(profile.Drive)?"Hdd1":profile.Drive.Trim().Trim('/'));
  var header=File.Exists(localPath)?localPath:Directory.Exists(localPath)?TitleInfoReader.FindStfsHeader(localPath):null;
  if(header!=null&&string.Equals(Path.GetFileName(Path.GetDirectoryName(header)),"00007000",StringComparison.OrdinalIgnoreCase))
  {
   var typeFolder=Path.GetDirectoryName(header)!;var titleFolder=Path.GetDirectoryName(typeFolder)!;var baseFolder=Path.GetDirectoryName(titleFolder)!;
   var titleId=Path.GetFileName(titleFolder);
   if(titleId.Length!=8||!titleId.All(Uri.IsHexDigit))throw new IOException("GoD package without a Title ID folder: "+titleFolder);
   var remoteBase=drive+"/Content/0000000000000000";
   string Remote(string file)=>remoteBase+"/"+string.Join('/',Path.GetRelativePath(baseFolder,file).Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
   var all=Directory.GetFiles(titleFolder,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint});
   // STFS headers (one per disc) directly in 00007000 are what the dashboard detects: send them last.
   var headers=all.Where(f=>string.Equals(Path.GetDirectoryName(f),typeFolder,StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(f)=="").ToHashSet(StringComparer.OrdinalIgnoreCase);
   var files=all.Where(f=>!headers.Contains(f)).Select(f=>(f,Remote(f))).ToList();
   var last=headers.OrderBy(f=>f,StringComparer.OrdinalIgnoreCase).Select(f=>(f,Remote(f))).ToList();
   foreach(var (_,remote) in files.Concat(last))CheckFatx(remote+TempSuffix); // GoD files travel under a temporary name
   return new(LibraryKind.GoD,titleFolder,remoteBase+"/"+titleId+"/00007000",files,last,[],null,all.Sum(f=>new FileInfo(f).Length));
  }
  if(Directory.Exists(localPath))
  {
   var options=new EnumerationOptions{RecurseSubdirectories=true,MaxRecursionDepth=3,MatchCasing=MatchCasing.CaseInsensitive,AttributesToSkip=FileAttributes.ReparsePoint};
   if(Directory.EnumerateFiles(localPath,"default.xbe",options).Any()&&!Directory.EnumerateFiles(localPath,"default.xex",options).Any())
    throw new IOException("Console transfer supports Xbox 360 games only (found default.xbe).");
   var xex=Directory.EnumerateFiles(localPath,"default.xex",options).OrderBy(f=>f.Length).FirstOrDefault()
    ??throw new IOException("No GoD package or extracted Xbox 360 game (default.xex) found in: "+localPath);
   var game=Path.GetDirectoryName(xex)!;
   var name=FatxName(Path.GetFileName(game));
   var gamesFolder=string.Join('/',(string.IsNullOrWhiteSpace(profile.GamesFolder)?"Games":profile.GamesFolder).Split(['/','\\'],StringSplitOptions.RemoveEmptyEntries).Select(Segment));
   var final=drive+"/"+gamesFolder+"/"+name;var partial=final+PartialSuffix;
   string Remote(string path)=>partial+"/"+string.Join('/',Path.GetRelativePath(game,path).Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
   var enumeration=new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint};
   var all=Directory.GetFiles(game,"*",enumeration);
   var files=all.Select(f=>(f,Remote(f))).ToList();
   var empty=Directory.GetDirectories(game,"*",enumeration).Where(d=>!Directory.EnumerateFileSystemEntries(d).Any()).Select(Remote).ToList();
   foreach(var (_,remote) in files)CheckFatx(remote[(partial.Length+1)..]);
   return new(LibraryKind.Extracted,game,final,files,[],empty,partial,all.Sum(f=>new FileInfo(f).Length));
  }
  throw new IOException("Not a GoD package or extracted game: "+localPath);
 }
 private static string Segment(string value)=>value.Replace("/","_").Replace("\\","_");
 /// <summary>FATX (console file system): at most 42 characters per name, ASCII only, no &lt;&gt;:"|?* characters.</summary>
 public static void CheckFatx(string relative)
 {
  foreach(var part in relative.Split('/',StringSplitOptions.RemoveEmptyEntries))
  {
   if(part.Length>FatxNameLimit)throw new IOException($"Name too long for the console file system (FATX allows {FatxNameLimit} characters): {part}");
   if(part.Any(c=>c<32||c>126||"<>:\"|?*".Contains(c)))throw new IOException("Name not allowed on the console file system (FATX): "+part);
  }
 }
 /// <summary>Folder name for an extracted game: FATX-safe and short enough for the ".xsp" transfer suffix.</summary>
 public static string FatxName(string name)
 {
  var safe=new string(name.Select(c=>c<32||c>126||"<>:\"|?*/\\".Contains(c)?'_':c).ToArray()).Trim().TrimEnd('.');
  if(safe.Length==0)safe="Game";
  return safe.Length>FatxNameLimit-PartialSuffix.Length?safe[..(FatxNameLimit-PartialSuffix.Length)].TrimEnd():safe;
 }

 // ---------- connection ----------
 private sealed class Connection(ConsoleTransferService owner)
 {
  private AsyncFtpClient? client;
  public async Task<AsyncFtpClient> GetAsync(CancellationToken token)
  {
   if(client is {IsConnected:true})return client;
   Reset();
   var created=owner.CreateClient();
   try{await created.Connect(token);}
   catch{created.Dispose();throw;}
   return client=created;
  }
  /// <summary>Drops the connection. Disposing may try a QUIT on a dead socket, so it never runs on the caller's (UI) thread.</summary>
  public void Reset(){var old=client;client=null;if(old!=null)_=Task.Run(()=>{try{old.Dispose();}catch(Exception){}});}
 }
 private AsyncFtpClient CreateClient()
 {
  var client=new AsyncFtpClient(profile.Host.Trim(),string.IsNullOrEmpty(profile.User)?"xboxftp":profile.User,password,profile.Port is >0 and <65536?profile.Port:21);
  client.Config.EncryptionMode=FtpEncryptionMode.None;            // console FTP servers do not support TLS
  client.Config.DataConnectionType=FtpDataConnectionType.PASV;   // Aurora/FSD: classic passive mode
  client.Config.ConnectTimeout=10000;client.Config.ReadTimeout=30000;
  client.Config.DataConnectionConnectTimeout=15000;client.Config.DataConnectionReadTimeout=30000;
  client.Config.RetryAttempts=1;
  return client;
 }
 /// <summary>Opens a TCP connection to the FTP port (no login) to check that the console is on and reachable.</summary>
 public async Task<bool> IsReachableAsync(CancellationToken token)
 {
  using var tcp=new TcpClient();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(Policy.ProbeTimeout);
  try{await tcp.ConnectAsync(profile.Host.Trim(),profile.Port is >0 and <65536?profile.Port:21,timeout.Token);return true;}
  catch(Exception ex) when(ex is SocketException or OperationCanceledException&&!token.IsCancellationRequested){return false;}
 }
 private async Task WaitForConsoleAsync(Action<string,bool> log,CancellationToken token)
 {
  var deadline=DateTime.UtcNow+Policy.MaxOutage;var announced=false;
  while(!await IsReachableAsync(token))
  {
   if(DateTime.UtcNow>=deadline)throw new IOException($"Console {profile.Host} is not reachable (waited {Policy.MaxOutage.TotalMinutes:0} min).");
   if(!announced){log($"Waiting for console {profile.Host} – reconnecting …",true);announced=true;}
   await Task.Delay(Policy.ProbeInterval,token);
  }
  if(announced)log("Console reachable again; resuming.",false);
 }
 /// <summary>Errors that a reconnect cannot fix (wrong data, 5xx replies, local files missing).</summary>
 private sealed class PermanentTransferException(string message):IOException(message);
 private static bool IsPermanent(Exception ex)=>
  IsLoginFailure(ex)||ex is PermanentTransferException or FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or ArgumentException or NotSupportedException
  ||ex is FtpCommandException command&&command.CompletionCode is {Length:>0} code&&code[0]=='5'
  ||ex.InnerException is FtpCommandException {CompletionCode:[ '5', ..]};
 /// <summary>Wrong user name or password (FTP reply 530, FluentFTP's authentication exception).</summary>
 private static bool IsLoginFailure(Exception ex)=>
  ex is FtpCommandException {CompletionCode:"530"}||ex.GetType().Name=="FtpAuthenticationException"||ex.InnerException is {} inner&&IsLoginFailure(inner);
 private const string LoginFailed="FTP login failed. Check user name and password in Settings (Aurora default: xboxftp / xboxftp).";

 /// <summary>Logs in and lists the root (drives). Returns e.g. "Hdd1, Usb0".</summary>
 public async Task<string> TestAsync(CancellationToken token)
 {
  if(string.IsNullOrWhiteSpace(profile.Host))throw new IOException("Enter the console IP address first.");
  if(!await IsReachableAsync(token))throw new IOException($"No FTP server answers at {profile.Host}:{profile.Port}. Is the console on and FTP enabled in Aurora?");
  using var client=CreateClient();
  try{await client.Connect(token);}
  catch(Exception ex) when(IsLoginFailure(ex)){throw new IOException(LoginFailed);}
  var listing=await client.GetListing("/",token);
  var drives=listing.Select(i=>i.Name).Where(n=>!string.IsNullOrWhiteSpace(n)).ToArray();
  await client.Disconnect(token);
  if(drives.Length>0&&!drives.Contains(profile.Drive,StringComparer.OrdinalIgnoreCase))
   throw new IOException($"Connected, but drive {profile.Drive} was not found. Available: {string.Join(", ",drives)}");
  return drives.Length>0?string.Join(", ",drives):"/";
 }

 // ---------- transfer ----------
 /// <summary>Sends a game. Returns the remote target, or null when the game is already on the console (skipped).</summary>
 public async Task<string?> UploadAsync(string localPath,Action<string,bool> log,Action<double>? progress,CancellationToken token)
 {
  if(string.IsNullOrWhiteSpace(profile.Host))throw new IOException("Set the console IP address in Settings › Xbox 360 console (FTP).");
  var plan=await Task.Run(()=>Plan(localPath,profile),token);
  sourceRoot=plan.LocalRoot;
  log($"Console transfer: {plan.Describe} ({RunReportService.FormatBytes(plan.TotalBytes)}, {plan.Files.Count+plan.CommitLast.Count} files)",false);
  await WaitForConsoleAsync(log,token);
  var primary=new Connection(this);
  try
  {
   if(await AlreadyPresentAsync(primary,plan,log,token)){progress?.Invoke(100);return null;}
   long done=0;
   void Advance(long bytes){var total=Interlocked.Add(ref done,bytes);if(plan.TotalBytes>0)progress?.Invoke(Math.Min(100,total*100.0/plan.TotalBytes));}
   if(plan.Kind==LibraryKind.GoD)
   {
    // One connection: GoD consists of a few large files; the console's 100 Mbit link is the limit.
    foreach(var (local,remote) in plan.Files.Concat(plan.CommitLast))await UploadFileAsync(primary,local,remote,true,Advance,log,token);
   }
   else
   {
    // All folders first, on one connection: parallel MKD of the same new folder fails on real console servers.
    var folders=plan.Files.Select(f=>f.Remote[..f.Remote.LastIndexOf('/')]).Concat(plan.EmptyFolders).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f=>f.Length).ToList();
    foreach(var folder in folders)await RunAsync(primary,async c=>{if(!await c.DirectoryExists(folder,token))await c.CreateDirectory(folder,true,token);return true;},log,token);
    var workers=Math.Clamp(profile.ParallelUploads,1,4);
    var queues=Enumerable.Range(0,workers).Select(_=>new List<(string Local,string Remote)>()).ToArray();
    var index=0;foreach(var file in plan.Files.OrderByDescending(f=>new FileInfo(f.Local).Length))queues[index++%workers].Add(file);
    await Task.WhenAll(queues.Select(async(queue,worker)=>
    {
     var connection=worker==0?primary:new Connection(this);
     try{foreach(var (local,remote) in queue)await UploadFileAsync(connection,local,remote,false,Advance,log,token);}
     finally{if(worker!=0)connection.Reset();}
    }));
    await RunAsync(primary,async c=>
    {
     if(await c.DirectoryExists(plan.RemoteTarget,token))
     {
      // A rename that completed on the console before the reply was lost: the transfer folder is gone, the game is there.
      if(!await c.DirectoryExists(plan.PartialFolder!,token))return true;
      throw new PermanentTransferException("A game folder with this name already exists on the console: "+plan.RemoteTarget);
     }
     await c.Rename(plan.PartialFolder!,plan.RemoteTarget,token);
     return true;
    },log,token);
   }
   log("Transfer complete: "+plan.RemoteTarget+(plan.Kind==LibraryKind.Extracted?" (add the Games folder as a scan path in Aurora)":" (rescan in Aurora if the game does not appear)"),false);
   return plan.RemoteTarget;
  }
  finally{primary.Reset();}
 }

 private async Task<bool> AlreadyPresentAsync(Connection connection,ConsoleTransferPlan plan,Action<string,bool> log,CancellationToken token)
 {
  return await RunAsync(connection,async client=>
  {
   if(plan.Kind==LibraryKind.Extracted)
   {
    if(!await client.DirectoryExists(plan.RemoteTarget,token))return false;
    // Folder names are shortened for FATX, so two games can map to one name: compare default.xex before skipping.
    var xex=plan.Files.FirstOrDefault(f=>f.Remote.EndsWith("/default.xex",StringComparison.OrdinalIgnoreCase));
    var remoteXex=plan.RemoteTarget+"/"+(xex.Remote is {} path?path[(plan.PartialFolder!.Length+1)..]:"default.xex");
    if(xex.Local!=null&&await RemoteSizeAsync(client,remoteXex,token)!=new FileInfo(xex.Local).Length)
     throw new PermanentTransferException("A different game already uses the folder name on the console: "+plan.RemoteTarget+". Rename the source folder and send it again.");
    log("Already on the console (folder exists): "+plan.RemoteTarget,false);return true;
   }
   if(plan.CommitLast.Count==0)return false;
   foreach(var (local,remote) in plan.CommitLast)
    if(await RemoteSizeAsync(client,remote,token)!=new FileInfo(local).Length)return false;
   log("Already on the console (same GoD headers): "+plan.RemoteTarget,false);return true;
  },log,token);
 }

 /// <summary>Runs a command with reconnect-and-retry on network errors.</summary>
 private async Task<T> RunAsync<T>(Connection connection,Func<AsyncFtpClient,Task<T>> operation,Action<string,bool> log,CancellationToken token)
 {
  for(var attempt=1;;attempt++)
  {
   try{return await operation(await connection.GetAsync(token));}
   catch(Exception ex) when(IsLoginFailure(ex)){throw new IOException(LoginFailed);}
   catch(Exception ex) when(!token.IsCancellationRequested&&attempt<Policy.MaxAttempts&&!IsPermanent(ex))
   {
    connection.Reset();
    // The game may be read from a NAS: when that share is gone, wait for it instead of reconnecting to the console.
    if(sourceRoot!=null&&NetworkPath.IsNetwork(sourceRoot)&&!await NetworkPath.IsReachableAsync(sourceRoot,Policy.ProbeTimeout,token))
    {
     log($"Source share not reachable ({ex.Message.Trim()}); waiting ({attempt}/{Policy.MaxAttempts - 1}).",true);
     await ResilientIO.WaitUntilReachableAsync([sourceRoot],Policy,text=>log(text,true),token);
    }
    else
    {
     log($"FTP connection problem ({ex.Message.Trim()}); reconnecting ({attempt}/{Policy.MaxAttempts - 1}).",true);
     await WaitForConsoleAsync(log,token);
    }
   }
  }
 }

 private sealed class Reporter(Action<long> advance) : IProgress<FtpProgress>
 {
  public long Reported;
  public void Report(FtpProgress value){var delta=value.TransferredBytes-Reported;if(delta>0){Reported=value.TransferredBytes;advance(delta);}}
 }

 /// <summary>
 /// Uploads one file and verifies its size. With <paramref name="viaTemp"/> it is written as "name.xstmp" and renamed
 /// afterwards (used where the dashboard could see the file, i.e. GoD). Retries resume at the size already on the console.
 /// </summary>
 private async Task UploadFileAsync(Connection connection,string local,string remote,bool viaTemp,Action<long> advance,Action<string,bool> log,CancellationToken token)
 {
  var size=new FileInfo(local).Length;var target=viaTemp?remote+TempSuffix:remote;
  var reporter=new Reporter(advance);var first=true;
  await RunAsync(connection,async client=>
  {
   // GoD data files that already arrived in an earlier run (final name, right size) are not sent again.
   if(first&&viaTemp&&await RemoteSizeAsync(client,remote,token)==size){reporter.Reported=size;advance(size);return true;}
   // First attempt: overwrite leftovers of other runs; retries of this call resume at the size already on the console.
   var mode=first||resumeUnsupported?FtpRemoteExists.Overwrite:FtpRemoteExists.Resume;first=false;
   FtpStatus status;
   try{status=await client.UploadFile(local,target,mode,true,FtpVerify.None,reporter,token);}
   catch(Exception ex) when(mode==FtpRemoteExists.Resume&&IsNotImplemented(ex))
   {
    resumeUnsupported=true;
    log("The console's FTP server cannot resume transfers (APPE/REST); files are sent again from the start after interruptions.",true);
    status=await client.UploadFile(local,target,FtpRemoteExists.Overwrite,true,FtpVerify.None,reporter,token);
   }
   if(status==FtpStatus.Failed)throw new IOException("Upload failed: "+remote);
   var sent=await RemoteSizeAsync(client,target,token);
   if(sent!=size)
   {
    // Wrong size (e.g. a stale temporary file): remove it so the next attempt starts clean.
    try{await client.DeleteFile(target,token);}catch(Exception ex) when(ex is not OperationCanceledException){}
    reporter.Reported=0;
    throw new IOException($"Size check failed for {Path.GetFileName(remote)} ({sent} of {size} bytes); sending again.");
   }
   if(viaTemp)
   {
    if(await client.FileExists(remote,token))await client.DeleteFile(remote,token);
    await client.Rename(target,remote,token);
   }
   return true;
  },log,token);
  if(reporter.Reported<size)advance(size-reporter.Reported);
 }

 private static async Task<long> RemoteSizeAsync(AsyncFtpClient client,string path,CancellationToken token)
 {
  var size=await client.GetFileSize(path,-1,token);
  if(size>=0)return size;
  var slash=path.LastIndexOf('/');var parent=slash>0?path[..slash]:"/";var name=path[(slash+1)..];
  if(!await client.DirectoryExists(parent,token))return -1;
  var item=(await client.GetListing(parent,token)).FirstOrDefault(i=>i.Type==FtpObjectType.File&&string.Equals(i.Name,name,StringComparison.OrdinalIgnoreCase));
  return item?.Size??-1;
 }
}
