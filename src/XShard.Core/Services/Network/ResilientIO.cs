namespace XShard.Services;
/// <summary>How long X-Shard waits for a share or console that went away, and how often it checks.</summary>
public sealed record RetryPolicy(TimeSpan MaxOutage,TimeSpan ProbeInterval,int MaxAttempts,TimeSpan ProbeTimeout)
{
 public static RetryPolicy Default {get;}=new(TimeSpan.FromMinutes(10),TimeSpan.FromSeconds(5),12,TimeSpan.FromSeconds(8));
}

/// <summary>
/// File operations that survive network interruptions: transient errors are recognized, the share is polled until it
/// returns, and copies resume at the last confirmed offset. Every write that can be seen by the user goes through a
/// temporary name or a private staging folder first, so an interruption never leaves a file that looks complete.
/// </summary>
public static class ResilientIO
{
 public const int BufferSize=1024*1024;

 /// <summary>Testing only: called before each buffer is written (destination path, offset); a returned exception is thrown.</summary>
 public static Func<string,long,Exception?>? FaultInjection {get;set;}

 // Win32 codes (HRESULT 0x8007xxxx): 21 not ready, 51 remote not listening, 53 bad net path, 54 net busy, 55 device gone,
 // 59 unexpected net error, 64 net name deleted, 65 network access denied (transient on SMB reconnect), 67 bad net name,
 // 71 too many sessions, 121 semaphore timeout, 1231 network unreachable, 1232 host unreachable, 1236 connection aborted,
 // 1240 connection aborted (local), 2250 network connection does not exist.
 private static readonly HashSet<int> Win32Network=[21,51,53,54,55,59,64,65,67,71,121,1231,1232,1236,1240,2250];
 // errno (Linux/macOS IOException.HResult): EIO 5, ENETDOWN 100, ENETUNREACH 101, ECONNABORTED 103, ECONNRESET 104,
 // ENOTCONN 107, ETIMEDOUT 110, EHOSTDOWN 112, EHOSTUNREACH 113, ESTALE 116.
 private static readonly HashSet<int> Errno=[5,100,101,103,104,107,110,112,113,116];

 /// <summary>True for errors caused by a lost or slow network connection (retry after the share is reachable again).</summary>
 public static bool IsTransient(Exception? ex)
 {
  for(var current=ex;current!=null;current=current.InnerException)
  {
   if(current is TimeoutException or System.Net.Sockets.SocketException)return true;
   if(current is IOException io)
   {
    var hr=io.HResult;
    if((hr&unchecked((int)0xFFFF0000))==unchecked((int)0x80070000)&&Win32Network.Contains(hr&0xFFFF))return true;
    if(hr is >0 and <1000&&Errno.Contains(hr))return true;
   }
  }
  return false;
 }

 private static bool IsRetryCandidate(Exception ex)=>ex is IOException or UnauthorizedAccessException or TimeoutException or System.Net.Sockets.SocketException;
 /// <summary>
 /// A transient network error, or any I/O error while one of the shares is unreachable (an SMB reconnect can surface as
 /// "path not found" or "access denied").
 /// </summary>
 private static async Task<bool> IsNetworkCauseAsync(Exception ex,IEnumerable<string> paths,RetryPolicy policy,CancellationToken token)
 {
  if(IsTransient(ex))return true;
  foreach(var path in paths.Where(p=>!string.IsNullOrWhiteSpace(p)&&NetworkPath.IsNetwork(p)))
   if(!await NetworkPath.IsReachableAsync(path,policy.ProbeTimeout,token))return true;
  return false;
 }

 /// <summary>Waits until all given paths' shares are reachable; reports the wait through <paramref name="status"/>. Throws IOException after <see cref="RetryPolicy.MaxOutage"/>.</summary>
 public static async Task WaitUntilReachableAsync(IEnumerable<string> paths,RetryPolicy policy,Action<string>? status,CancellationToken token)
 {
  var roots=paths.Where(p=>!string.IsNullOrWhiteSpace(p)).Select(NetworkPath.ShareRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  var deadline=DateTime.UtcNow+policy.MaxOutage;var announced=false;
  while(true)
  {
   string? missing=null;
   foreach(var root in roots)if(!await NetworkPath.IsReachableAsync(root,policy.ProbeTimeout,token)){missing=root;break;}
   if(missing==null){if(announced)status?.Invoke("Connection restored.");return;}
   if(DateTime.UtcNow>=deadline)throw new IOException($"{missing} is not reachable (waited {policy.MaxOutage.TotalMinutes:0} min).");
   if(!announced){status?.Invoke($"Waiting for {missing} – reconnecting …");announced=true;}
   await Task.Delay(policy.ProbeInterval,token);
  }
 }

 /// <summary>Runs an operation; on transient network errors waits for the shares and retries.</summary>
 public static async Task<T> RetryAsync<T>(IReadOnlyList<string> paths,Func<CancellationToken,Task<T>> operation,RetryPolicy policy,Action<string>? status,CancellationToken token)
 {
  for(var attempt=1;;attempt++)
  {
   token.ThrowIfCancellationRequested();
   try{return await operation(token);}
   catch(Exception ex) when(attempt<policy.MaxAttempts&&IsRetryCandidate(ex)&&!token.IsCancellationRequested)
   {
    if(!await IsNetworkCauseAsync(ex,paths,policy,token))throw;
    status?.Invoke($"Network error ({ex.Message.Trim()}); retrying ({attempt}/{policy.MaxAttempts - 1}).");
    await WaitUntilReachableAsync(paths,policy,status,token);
   }
  }
 }
 public static Task RetryAsync(IReadOnlyList<string> paths,Action operation,RetryPolicy policy,Action<string>? status,CancellationToken token)=>
  RetryAsync(paths,_=>Task.Run(()=>{operation();return true;}),policy,status,token);

 /// <summary>
 /// Copies one file and resumes after network interruptions. With <paramref name="viaPartial"/> the data is written to
 /// "name.partial" and renamed after the size check; otherwise (inside a private staging folder) the final name is used.
 /// </summary>
 public static async Task CopyFileAsync(string source,string destination,Action<long>? progress,Action<string>? status,CancellationToken token,RetryPolicy? policy=null,bool viaPartial=true)
 {
  policy??=RetryPolicy.Default;
  var paths=new[]{source,destination};
  var target=viaPartial?destination+".partial":destination;
  var total=await RetryAsync(paths,_=>Task.Run(()=>new FileInfo(source).Length),policy,status,token);
  long reported=0;
  void Report(long bytes){reported+=bytes;progress?.Invoke(bytes);}
  for(var attempt=1;;attempt++)
  {
   var resume=attempt>1||!viaPartial; // a ".partial" left by another run is never trusted; inside private staging folders it is ours
   try
   {
    await Task.Run(async()=>
    {
     Directory.CreateDirectory(Path.GetDirectoryName(target)!);
     await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,BufferSize,FileOptions.Asynchronous|FileOptions.SequentialScan);
     await using var output=new FileStream(target,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None,BufferSize,FileOptions.Asynchronous);
     // Resume one buffer before the confirmed end: bytes are always rewritten from the same source offset, so overlap is harmless.
     var offset=!resume||output.Length>total?0:Math.Max(0,output.Length-BufferSize);
     output.SetLength(offset);output.Position=offset;input.Position=offset;
     if(offset>reported)Report(offset-reported);
     var buffer=new byte[BufferSize];
     while(true)
     {
      token.ThrowIfCancellationRequested();
      var read=await input.ReadAsync(buffer,token);if(read==0)break;
      if(FaultInjection?.Invoke(target,output.Position) is {} fault)throw fault;
      await output.WriteAsync(buffer.AsMemory(0,read),token);
      var end=output.Position;if(end>reported)Report(end-reported);
     }
     await output.FlushAsync(token);
    },token);
    break;
   }
   catch(Exception ex) when(attempt<policy.MaxAttempts&&IsRetryCandidate(ex)&&!token.IsCancellationRequested)
   {
    if(!await IsNetworkCauseAsync(ex,paths,policy,token))throw;
    status?.Invoke($"Copy of {Path.GetFileName(source)} interrupted ({ex.Message.Trim()}); resuming ({attempt}/{policy.MaxAttempts - 1}).");
    await WaitUntilReachableAsync(paths,policy,status,token);
   }
  }
  var written=await RetryAsync(paths,_=>Task.Run(()=>new FileInfo(target).Length),policy,status,token);
  if(written!=total)throw new IOException($"Copy of {Path.GetFileName(source)} is incomplete ({written} of {total} bytes).");
  if(viaPartial)await MoveFileAsync(target,destination,status,token,policy);
 }

 /// <summary>
 /// Copies a folder tree with several files in flight. Small files dominate extracted games; over SMB every file costs
 /// round trips, so parallel transfers are much faster than one by one. Files already present with the right size are
 /// kept, which makes a repeated call a resume.
 /// </summary>
 public static async Task CopyDirectoryAsync(string source,string destination,int parallelism,Action<long>? progress,Action<string>? status,CancellationToken token,RetryPolicy? policy=null)
 {
  policy??=RetryPolicy.Default;
  var paths=new[]{source,destination};
  var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=false,AttributesToSkip=FileAttributes.ReparsePoint};
  var (directories,files)=await RetryAsync(paths,_=>Task.Run(()=>(Directory.GetDirectories(source,"*",options),Directory.GetFiles(source,"*",options))),policy,status,token);
  await RetryAsync(paths,()=>
  {
   Directory.CreateDirectory(destination);
   foreach(var directory in directories)Directory.CreateDirectory(Path.Combine(destination,Path.GetRelativePath(source,directory)));
  },policy,status,token);
  await Parallel.ForEachAsync(files.OrderByDescending(f=>new FileInfo(f).Length),new ParallelOptions{MaxDegreeOfParallelism=Math.Max(1,parallelism),CancellationToken=token},async(file,ct)=>
  {
   var target=Path.Combine(destination,Path.GetRelativePath(source,file));
   var size=new FileInfo(file).Length;
   var existing=await RetryAsync(paths,_=>Task.Run(()=>File.Exists(target)?new FileInfo(target).Length:-1),policy,status,ct);
   if(existing==size){progress?.Invoke(size);return;}
   await CopyFileAsync(file,target,progress,status,ct,policy,viaPartial:false);
  });
 }

 /// <summary>Moves a folder (a rename when both sides are on the same share) with retries.</summary>
 public static Task MoveDirectoryAsync(string source,string destination,Action<string>? status,CancellationToken token,RetryPolicy? policy=null)=>
  RetryAsync([source,destination],()=>
  {
   // A retried rename may already have happened on the server before the connection dropped.
   if(Directory.Exists(destination)){if(Directory.Exists(source))throw new IOException("Destination already exists: "+destination);return;}
   Directory.Move(source,destination);
  },policy??RetryPolicy.Default,status,token);
 public static Task MoveFileAsync(string source,string destination,Action<string>? status,CancellationToken token,RetryPolicy? policy=null)=>
  RetryAsync([source,destination],()=>
  {
   if(File.Exists(destination)){if(File.Exists(source))throw new IOException("Destination already exists: "+destination);return;}
   File.Move(source,destination,false);
  },policy??RetryPolicy.Default,status,token);

 /// <summary>Best-effort delete of a private working folder (never user data).</summary>
 public static void TryDeleteDirectory(string path)
 {
  try{if(Directory.Exists(path))Directory.Delete(path,true);}
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
 }
}
