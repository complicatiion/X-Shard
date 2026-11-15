using System.Collections.ObjectModel;
using System.Diagnostics;
using XShard.Models;
namespace XShard.Services;
/// <summary>Sends a finished result to the console. Returns the remote path, or null when the game was already there.</summary>
public delegate Task<string?> ConsoleUploader(XisoJob job,string localPath,Action<string,bool> output,Action<double> progress,CancellationToken token);

public sealed class JobQueueService(JobExecutor executor)
{
 public ObservableCollection<XisoJob> Jobs { get; } = [];
 public bool IsRunning { get; private set; }
 public bool StopAfterCurrent { get; set; }
 public JobExecutor Executor => executor;
 /// <summary>v0.2.0: console transfer for jobs with UploadToConsole (set by the GUI/CLI when FTP is enabled).</summary>
 public ConsoleUploader? Uploader { get; set; }
 private CancellationTokenSource? current,currentUpload;
 public event Action? Changed;
 /// <summary>Cancels the running conversion; when only a console transfer is running, cancels that transfer.</summary>
 public void CancelCurrent() { if(current is {} conversion)conversion.Cancel(); else currentUpload?.Cancel(); }
 /// <summary>Cancels the running conversion and the running console transfer (CLI Ctrl+C).</summary>
 public void CancelAll() { current?.Cancel(); currentUpload?.Cancel(); }
 public void CancelPending()
 { foreach(var job in Jobs.Where(j=>j.Status==JobStatus.Waiting)) { job.Status=JobStatus.Cancelled;job.Result="Cancelled before start"; } Changed?.Invoke(); }
 public Task RunAsync(string backend,bool continueOnError,Action<XisoJob,string,bool> output,Func<Task> checkpoint) => RunAsync(_=>backend,continueOnError,output,checkpoint);
 /// <summary>
 /// Runs waiting jobs one at a time. Console transfers run in a pipeline: while job N is being sent to the console,
 /// job N+1 is already converted (the console's network link is the bottleneck). The run ends when all transfers ended.
 /// </summary>
 public async Task RunAsync(Func<XisoJobOptions,string> resolveBackend,bool continueOnError,Action<XisoJob,string,bool> output,Func<Task> checkpoint)
 {
  if(IsRunning) return;IsRunning=true;StopAfterCurrent=false;Changed?.Invoke();
  // Jobs run on the thread pool (a dead share can block file APIs for a minute); status updates return to the caller's context (UI).
  var context=SynchronizationContext.Current;
  void OnContext(Action action){if(context==null)action();else context.Post(_=>action(),null);}
  var uploads=Task.CompletedTask;var uploadFailed=false;
  try
  {
   while(!StopAfterCurrent && !(uploadFailed&&!continueOnError) && Jobs.FirstOrDefault(j=>j.Status==JobStatus.Waiting) is {} job)
   {
    using var cts=new CancellationTokenSource();current=cts;
    job.StartedAt=DateTimeOffset.UtcNow;job.PublishedPath=null;job.RemotePath=null;job.Duration=null;job.Progress=0;job.Status=JobStatus.Running;job.Result="Processing";job.Log="";job.Contents.Clear();
    var watch=Stopwatch.StartNew();Changed?.Invoke();await checkpoint();
    var upload=false;
    try
    {
     var executionOptions=BackendPolicy.Normalize(job.Options);
     var backend=executionOptions.Mode==XisoMode.Upload?"":resolveBackend(executionOptions);
     var token=cts.Token;
     var published=await Task.Run(()=>executor.ExecuteAsync(executionOptions,backend,(line,error)=>output(job,line,error),token,text=>OnContext(()=>{if(job.IsRunning)job.Result=text;})),CancellationToken.None);
     job.PublishedPath=string.IsNullOrEmpty(published)||executionOptions.Mode==XisoMode.Upload?null:published;
     if((job.Options.UploadToConsole||job.Options.Mode==XisoMode.Upload)&&!string.IsNullOrEmpty(published))
     {
      if(Uploader==null)throw new IOException("Console transfer (FTP) is not enabled in Settings.");
      upload=true;job.Result="Waiting for console transfer";job.Progress=0;
      var previous=uploads;var startedAt=watch;
      uploads=TransferAsync(previous,job,published,startedAt,output,checkpoint);
     }
     else {job.Status=JobStatus.Succeeded;job.Result=job.Options.Mode==XisoMode.List?"Contents listed":"Completed";}
    }
    catch(OperationCanceledException) { job.Status=JobStatus.Cancelled;job.Result="Cancelled; original source retained"; }
    catch(Exception ex) { job.Status=JobStatus.Failed;job.Result=ex.Message;output(job,ex.Message,true); }
    finally
    {
     current=null;
     if(!upload){job.StartedAt=null;job.Duration=watch.Elapsed;job.Elapsed=watch.Elapsed.ToString(@"hh\:mm\:ss");}
     Changed?.Invoke();await checkpoint();
    }
    if(job.Status==JobStatus.Failed && !continueOnError) break;
   }
   await uploads;
  }
  finally { current=null;IsRunning=false;StopAfterCurrent=false;Changed?.Invoke();await checkpoint(); }

  async Task TransferAsync(Task previous,XisoJob job,string local,Stopwatch watch,Action<XisoJob,string,bool> log,Func<Task> save)
  {
   await previous; // one transfer at a time, in queue order
   using var cts=new CancellationTokenSource();currentUpload=cts;
   try
   {
    if(uploadFailed&&!continueOnError){job.Status=JobStatus.Cancelled;job.Result="Not sent: an earlier console transfer failed";return;}
    job.Result="Sending to console";Changed?.Invoke();
    var token=cts.Token;var uploader=Uploader!;
    var remote=await Task.Run(()=>uploader(job,local,(line,error)=>log(job,line,error),percent=>OnContext(()=>job.Progress=percent),token),CancellationToken.None);
    job.RemotePath=remote;
    job.Status=JobStatus.Succeeded;job.Result=remote==null?"Already on the console":"Sent to console";
    // Only X-Shard's own transfer copy: the converted result, or the copy a retried transfer sends (never folders the user chose).
    var transferCopy=job.Options.Mode==XisoMode.Upload?job.Options.Source:job.PublishedPath;
    if(job.Options.DeleteAfterUpload&&transferCopy!=null)
    {
     try{await Task.Run(()=>{if(Directory.Exists(transferCopy))Directory.Delete(transferCopy,true);else if(File.Exists(transferCopy))File.Delete(transferCopy);});log(job,"Local transfer copy deleted: "+transferCopy,false);}
     catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){log(job,"Local transfer copy could not be deleted: "+ex.Message,true);}
    }
   }
   catch(OperationCanceledException){job.Status=JobStatus.Cancelled;job.Result="Console transfer cancelled; local result retained";}
   catch(Exception ex){uploadFailed=true;job.Status=JobStatus.Failed;job.Result="Console transfer failed: "+ex.Message;log(job,ex.Message,true);}
   finally
   {
    if(currentUpload==cts)currentUpload=null;
    job.StartedAt=null;job.Duration=watch.Elapsed;job.Elapsed=watch.Elapsed.ToString(@"hh\:mm\:ss");
    Changed?.Invoke();await save();
   }
  }
 }
}
