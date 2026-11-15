using XShard.Models;
namespace XShard.Services;
public sealed class JobExecutor(XisoProcessService process)
{
 /// <summary>Local working folder for results that are written as many small files (extract) before they go to a network share.</summary>
 public string WorkDirectory {get;set;}=Path.Combine(Path.GetTempPath(),"X-Shard","work");
 public RetryPolicy Network {get;set;}=RetryPolicy.Default;
 /// <summary>Files copied at the same time when a locally staged result is published to a share.</summary>
 public int PublishParallelism {get;set;}=8;
 /// <summary>How often a job is restarted after a network interruption before it fails.</summary>
 public int MaxNetworkRestarts {get;set;}=3;

 public static void Validate(XisoJobOptions options)
 {
  options=BackendPolicy.Normalize(options);
  if(options.Mode==XisoMode.Upload)
  {
   if(!Directory.Exists(options.Source)&&!File.Exists(options.Source))throw new IOException("Source does not exist: "+options.Source);
   return;
  }
  if(options.Engine==BackendEngine.XgdTool ? (!Directory.Exists(options.Source)&&!File.Exists(options.Source)) : options.Mode==XisoMode.Create ? !Directory.Exists(options.Source) : !File.Exists(options.Source))
   throw new IOException("Source does not exist: "+options.Source);
  if(options.Engine==BackendEngine.XgdTool)XgdToolService.BuildArguments(options,"validation-output");
  if(options.Mode==XisoMode.List) return;
  var source=Path.GetFullPath(options.Source).TrimEnd(Path.DirectorySeparatorChar);
  var target=Path.GetFullPath(options.Destination).TrimEnd(Path.DirectorySeparatorChar);
  if(File.Exists(target)||Directory.Exists(target)) throw new IOException("Destination already exists. Choose a new destination: "+target);
  if(source.Equals(target,StringComparison.OrdinalIgnoreCase)) throw new IOException("Source and destination must be different.");
  if(Directory.Exists(options.Source) && target.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
   throw new IOException("The destination must be outside the source folder.");
 }

 /// <summary>True when the job reads from or writes to a network share.</summary>
 public static bool UsesNetwork(XisoJobOptions options)=>NetworkPath.IsNetwork(options.Source)||options.Destination!=""&&NetworkPath.IsNetwork(options.Destination);

 /// <summary>
 /// Runs one job. Returns the published result path (file or folder), or an empty string for List.
 /// Network jobs wait for unreachable shares, and are restarted (up to <see cref="MaxNetworkRestarts"/>) when the
 /// connection drops while the backend runs; outputs are only published from private staging folders, so a restart
 /// never leaves a result that looks complete.
 /// </summary>
 public async Task<string> ExecuteAsync(XisoJobOptions options,string backend,Action<string,bool> output,CancellationToken token,Action<string>? status=null)
 {
  token.ThrowIfCancellationRequested();
  options=BackendPolicy.Normalize(options);
  if(options.Mode==XisoMode.Upload){Validate(options);return options.Source;}
  if(JobPlanner.ConsoleScrubWarning(options) is {} scrubWarning)output(scrubWarning,true);
  if(!UsesNetwork(options)){Validate(options);return await ExecuteOnceAsync(options,backend,output,token,[],status);}
  var roots=new List<string>{options.Source};if(options.Destination!="")roots.Add(Path.GetDirectoryName(options.Destination)!);
  void Status(string text){status?.Invoke(text);output(text,false);}
  for(var attempt=1;;attempt++)
  {
   await ResilientIO.WaitUntilReachableAsync(roots,Network,Status,token);
   Validate(options);
   var created=new List<string>();
   using var watch=CancellationTokenSource.CreateLinkedTokenSource(token);
   var outage=WatchConnectionAsync(roots,watch.Token);
   try
   {
    var result=await ExecuteOnceAsync(options,backend,output,token,created,Status);
    return result;
   }
   catch(Exception ex) when(ex is not OperationCanceledException&&attempt<=MaxNetworkRestarts)
   {
    // Restart only when the network was the cause: a transient I/O error, an outage seen while the backend ran, or a share that is gone now.
    var interrupted=ResilientIO.IsTransient(ex)||outage.IsCompleted&&outage.Result||!await AllReachableAsync(roots,token);
    if(!interrupted)throw;
    output($"Network interruption during the job ({ex.Message.Trim()}). The job restarts when the share is reachable again ({attempt}/{MaxNetworkRestarts}).",true);
    status?.Invoke("Network interruption – waiting to restart");
    await ResilientIO.WaitUntilReachableAsync(roots,Network,Status,token);
    foreach(var folder in created)ResilientIO.TryDeleteDirectory(folder); // only X-Shard's own staging folders of this attempt
   }
   finally{watch.Cancel();try{await outage;}catch(OperationCanceledException){}}
  }
 }
 /// <summary>Polls the shares while a backend runs; completes with true as soon as one of them is unreachable.</summary>
 private async Task<bool> WatchConnectionAsync(IReadOnlyList<string> roots,CancellationToken token)
 {
  try
  {
   while(true)
   {
    await Task.Delay(Network.ProbeInterval,token);
    if(!await AllReachableAsync(roots,token))return true;
   }
  }
  catch(OperationCanceledException){return false;}
 }
 private async Task<bool> AllReachableAsync(IReadOnlyList<string> roots,CancellationToken token)
 {
  foreach(var root in roots)if(!await NetworkPath.IsReachableAsync(root,Network.ProbeTimeout,token))return false;
  return true;
 }

 /// <summary>
 /// Local staging for results with many files (extract) when the destination is a network share: the backend writes
 /// to the local disk, the tree is then copied in parallel into a private folder on the share and renamed. Returns null
 /// for direct staging beside the destination.
 /// </summary>
 internal string? LocalStageFor(XisoJobOptions options,bool manyFiles,Action<string> status)
 {
  if(!manyFiles||options.Destination==""||!NetworkPath.IsNetwork(options.Destination))return null;
  try
  {
   Directory.CreateDirectory(WorkDirectory);
   var need=(RunReportService.MeasureSource(options)??0)*11/10;
   var free=TargetPreview.FreeBytes(WorkDirectory);
   if(free is {} available&&available<need){status($"Not enough local space for a working copy ({RunReportService.FormatBytes(available)} free); writing directly to the share.");return null;}
   var stage=Path.Combine(WorkDirectory,".xshard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   status("Network destination: the output is prepared locally and then copied to the share.");
   return stage;
  }
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){status("Local working folder unavailable ("+ex.Message+"); writing directly to the share.");return null;}
 }
 /// <summary>Copies a locally staged tree into a private folder beside the destination (parallel, resumable) and returns that folder.</summary>
 internal async Task<string> PublishLocalStageAsync(string localStage,XisoJobOptions options,ICollection<string> created,Action<string,bool> output,CancellationToken token)
 {
  var remote=Path.Combine(Path.GetDirectoryName(options.Destination)!,".xshard-"+Guid.NewGuid().ToString("N"));created.Add(remote);
  var total=RunReportService.Measure(localStage)??0;long copied=0;var lastReport=DateTime.UtcNow;
  output($"Copying {RunReportService.FormatBytes(total)} to the share ({PublishParallelism} files in parallel) …",false);
  await ResilientIO.CopyDirectoryAsync(localStage,remote,PublishParallelism,bytes=>
  {
   var now=Interlocked.Add(ref copied,bytes);
   if(DateTime.UtcNow-lastReport>TimeSpan.FromSeconds(5)&&total>0){lastReport=DateTime.UtcNow;output($"Copied {now*100/total} % to the share",false);}
  },text=>output(text,true),token,Network);
  return remote;
 }

 private async Task<string> ExecuteOnceAsync(XisoJobOptions options,string backend,Action<string,bool> output,CancellationToken token,ICollection<string> created,Action<string>? status)
 {
  if(options.Engine==BackendEngine.XgdTool) return await new XgdToolService(process).ExecuteAsync(options,backend,output,token,this,created);
  if(options.Mode==XisoMode.List)
  {
   var code=await process.RunAsync(backend,XisoProcessService.BuildArguments(options,options.Source,""),AppContext.BaseDirectory,output,token);
   if(code!=0) throw new IOException($"Backend exited with code {code}. See the log.");return "";
  }
  var parent=Path.GetDirectoryName(options.Destination)!;Directory.CreateDirectory(parent);
  var local=LocalStageFor(options,options.Mode==XisoMode.Extract,text=>output(text,false));
  var stage=local??Path.Combine(parent,".xshard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);created.Add(stage);
  bool complete=false;
  try
  {
   string input=options.Source;string outputPath=Path.Combine(stage,"result.iso");
   if(options.Mode==XisoMode.Extract) { outputPath=Path.Combine(stage,"files"); }
   if(options.Mode==XisoMode.Rewrite)
   {
    output("Preparing a working copy. The original ISO is retained.",false);
    input=Path.Combine(stage,Path.GetFileName(options.Source));
    await ResilientIO.CopyFileAsync(options.Source,input,null,text=>output(text,true),token,Network,viaPartial:false);
    outputPath=Path.Combine(stage,"rewritten");Directory.CreateDirectory(outputPath);
   }
   var args=XisoProcessService.BuildArguments(options,input,outputPath);
   output("Mode: "+options.Mode+" | source: "+options.Source,false);
   var code=await process.RunAsync(backend,args,stage,output,token);
   if(code!=0) throw new IOException($"Backend exited with code {code}. See the log.");
   token.ThrowIfCancellationRequested();
   if(options.Mode==XisoMode.Extract)
   {
    if(!Directory.Exists(outputPath)) throw new IOException("The backend did not produce an extraction folder.");
    if(local!=null)
    {
     var remote=await PublishLocalStageAsync(outputPath,options,created,output,token);
     await ResilientIO.MoveDirectoryAsync(remote,options.Destination,text=>output(text,true),token,Network);
    }
    else await ResilientIO.MoveDirectoryAsync(outputPath,options.Destination,text=>output(text,true),token,Network);
   }
   else
   {
    if(options.Mode==XisoMode.Rewrite)
    {
     var rewritten=Path.Combine(outputPath,Path.GetFileName(input));
     // An already optimized image is skipped by upstream; publish the untouched working copy.
     outputPath=File.Exists(rewritten)?rewritten:File.Exists(input)?input:throw new IOException("Rewrite did not produce an image.");
    }
    if(!File.Exists(outputPath)||new FileInfo(outputPath).Length==0) throw new IOException("The backend did not produce an ISO.");
    await ResilientIO.MoveFileAsync(outputPath,options.Destination,text=>output(text,true),token,Network);
   }
   complete=true;output("Completed: "+options.Destination,false);
   return options.Destination;
  }
  finally
  {
   // Never remove incomplete work automatically. Only the private, successful staging directories are removed.
   if(complete) {foreach(var folder in created)ResilientIO.TryDeleteDirectory(folder);}
   else output("Incomplete working files retained at: "+stage,true);
  }
 }
}
