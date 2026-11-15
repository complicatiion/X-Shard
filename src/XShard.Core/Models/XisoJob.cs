using System.Collections.ObjectModel;
using XShard.ViewModels;
namespace XShard.Models;
public sealed class XisoJob : ViewModelBase
{
 public Guid Id { get; init; } = Guid.NewGuid();
 private XisoJobOptions options=null!;
 public required XisoJobOptions Options { get=>options; set=>Set(ref options,value); }
 public string Name => System.IO.Path.GetFileName(Options.Source.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar));
 /// <summary>v0.2.0: source or destination on a network share (shown as a small marker in the queue).</summary>
 public bool IsNetwork=>XShard.Services.JobExecutor.UsesNetwork(Options);
 public string ModeText => XShard.Services.LocalizationService.Instance.T(Options.Mode.ToString());
 public string PlatformText=>Options.Profile switch {DeviceProfile.Xemu=>"Xbox Classic · Xemu",DeviceProfile.Xenia=>"Xbox 360 · Xenia",_=>Options.Platform==ConsolePlatform.Xbox360?"Xbox 360":"Xbox Classic"};
 private JobStatus status;
 public JobStatus Status { get=>status; set { if(Set(ref status,value)) { Changed(nameof(StatusText)); Changed(nameof(IsRunning)); Changed(nameof(StatusColor));Changed(nameof(StatusBackground));Changed(nameof(ResultLabel));Changed(nameof(ResultBackground));Changed(nameof(CanOpenOutput)); } } }
 public string StatusText => XShard.Services.LocalizationService.Instance.T("State "+Status.ToString());
 public string StatusColor=>Status switch {JobStatus.Succeeded=>"#9BE584",JobStatus.Waiting=>"#FFC173",JobStatus.Failed=>"#FFA9B0",JobStatus.Cancelled=>"#CB9BA0",_=>"#C5D9BF"};
 public string StatusBackground=>Status switch {JobStatus.Succeeded=>"#23472A",JobStatus.Waiting=>"#4A3720",JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted=>"#4C292F",_=>"#293E30"};
 public string ResultLabel=>XShard.Services.LocalizationService.Instance.T("State "+(Status switch {JobStatus.Succeeded=>"Completed",JobStatus.Failed=>"Failed",JobStatus.Cancelled=>"Cancelled",JobStatus.Interrupted=>"Interrupted",JobStatus.Running=>IsTransferring?"Sending":"Processing",_=>"Ready"}));
 /// <summary>v0.2.0: the job waits for or performs its console transfer.</summary>
 public bool IsTransferring=>Status==JobStatus.Running&&Result is "Sending to console" or "Waiting for console transfer";
 public string ResultBackground=>Status is JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted?"#4C292F":"#23472A";
 public string OutputFolder
 {
  get
  {
   if(PublishedPath is {Length:>0} published){if(Directory.Exists(published))return published;if(File.Exists(published))return Path.GetDirectoryName(published)??"";}
   return Directory.Exists(Options.Destination)?Options.Destination:Path.GetDirectoryName(Options.Destination)??"";
  }
 }
 private string? publishedPath;
 /// <summary>Actual published result (file or folder). Differs from Options.Destination when a single-file result was flattened into the output folder.</summary>
 public string? PublishedPath {get=>publishedPath;set {if(Set(ref publishedPath,value))Changed(nameof(CanOpenOutput));}}
 private string? remotePath;
 /// <summary>v0.2.0: where the result was placed on the console (FTP path), if it was sent.</summary>
 public string? RemotePath {get=>remotePath;set=>Set(ref remotePath,value);}
 /// <summary>Processing time of the last run; null until the job ran.</summary>
 public TimeSpan? Duration {get;set;}
 private TitleInfo? title;
 public TitleInfo? Title {get=>title;set {if(Set(ref title,value)){Changed(nameof(TitleSummary));Changed(nameof(HasTitle));}}}
 public string TitleSummary=>Title?.Summary??"";
 public bool HasTitle=>!string.IsNullOrEmpty(TitleSummary);
 public bool CanOpenOutput=>!string.IsNullOrEmpty(Options.Destination)&&Directory.Exists(OutputFolder);
 public DateTimeOffset? LastOutputAt {get;set;}
 public void RefreshLanguage(){Changed(nameof(ModeText));Changed(nameof(StatusText));Changed(nameof(ResultLabel));Changed(nameof(PlatformText));}
 public DateTimeOffset? StartedAt {get;set;}
 private double progress;
 public double Progress {get=>progress;set {if(Set(ref progress,value))Changed(nameof(ProgressUnknown));}}
 public bool ProgressUnknown=>Progress<=0;
 public bool IsRunning => Status==JobStatus.Running;
 private string result="Ready";
 public string Result { get=>result; set { if(Set(ref result,value)) Changed(nameof(ResultLabel)); } }
 private string elapsed="—";
 public string Elapsed { get=>elapsed; set=>Set(ref elapsed,value); }
 private string log="";
 public string Log { get=>log; set=>Set(ref log,value); }
 public ObservableCollection<IsoContentEntry> Contents { get; } = [];
}
