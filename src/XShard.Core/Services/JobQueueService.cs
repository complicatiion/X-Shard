using System.Collections.ObjectModel;
using System.Diagnostics;
using XShard.Models;
namespace XShard.Services;
public sealed class JobQueueService(JobExecutor executor)
{
 public ObservableCollection<XisoJob> Jobs { get; } = [];
 public bool IsRunning { get; private set; }
 public bool StopAfterCurrent { get; set; }
 private CancellationTokenSource? current;
 public event Action? Changed;
 public void CancelCurrent() => current?.Cancel();
 public void CancelPending()
 { foreach(var job in Jobs.Where(j=>j.Status==JobStatus.Waiting)) { job.Status=JobStatus.Cancelled;job.Result="Cancelled before start"; } Changed?.Invoke(); }
 public Task RunAsync(string backend,bool continueOnError,Action<XisoJob,string,bool> output,Func<Task> checkpoint) => RunAsync(_=>backend,continueOnError,output,checkpoint);
 public async Task RunAsync(Func<XisoJobOptions,string> resolveBackend,bool continueOnError,Action<XisoJob,string,bool> output,Func<Task> checkpoint)
 {
  if(IsRunning) return;IsRunning=true;StopAfterCurrent=false;Changed?.Invoke();
  try
  {
   while(!StopAfterCurrent && Jobs.FirstOrDefault(j=>j.Status==JobStatus.Waiting) is {} job)
   {
    using var cts=new CancellationTokenSource();current=cts;
    job.StartedAt=DateTimeOffset.UtcNow;job.PublishedPath=null;job.Duration=null;job.Progress=0;job.Status=JobStatus.Running;job.Result="Processing";job.Log="";job.Contents.Clear();
    var watch=Stopwatch.StartNew();Changed?.Invoke();await checkpoint();
    try
    {
     var executionOptions=BackendPolicy.Normalize(job.Options);
     var published=await executor.ExecuteAsync(executionOptions,resolveBackend(executionOptions),(line,error)=>output(job,line,error),cts.Token);
     job.PublishedPath=string.IsNullOrEmpty(published)?null:published;
     job.Status=JobStatus.Succeeded;job.Result=job.Options.Mode==XisoMode.List?"Contents listed":"Completed";
    }
    catch(OperationCanceledException) { job.Status=JobStatus.Cancelled;job.Result="Cancelled; original source retained"; }
    catch(Exception ex) { job.Status=JobStatus.Failed;job.Result=ex.Message;output(job,ex.Message,true); }
    finally { job.StartedAt=null;current=null;job.Duration=watch.Elapsed;job.Elapsed=watch.Elapsed.ToString(@"hh\:mm\:ss");Changed?.Invoke();await checkpoint(); }
    if(job.Status==JobStatus.Failed && !continueOnError) break;
   }
  }
  finally { current=null;IsRunning=false;StopAfterCurrent=false;Changed?.Invoke();await checkpoint(); }
 }
}
