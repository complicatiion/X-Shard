using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using XShard.Models;
using XShard.Services;
namespace XShard.ViewModels;
public sealed partial class MainViewModel : ViewModelBase
{
 public SettingsService Storage { get; }
 public XisoProcessService Process { get; } = new();
 public JobQueueService Queue { get; }
 public LegalService Legal { get; }
 private readonly ConcurrentQueue<(XisoJob Job,string Line,bool Error)> output=new();
 private readonly SemaphoreSlim saveGate=new(1,1);
 private static LocalizationService L=>LocalizationService.Instance;
 public ObservableCollection<XisoJob> Jobs => Queue.Jobs;

 // ---------- console / operation / format ----------
 public ConsolePlatform[] Platforms=>Enum.GetValues<ConsolePlatform>();
 public XisoMode[] Modes => Enum.GetValues<XisoMode>();
 public OutputFormat[] Formats=>JobPlanner.FormatsFor(Platform);
 public DeviceProfile[] Profiles=>JobPlanner.ProfilesFor(Platform);
 private ConsolePlatform platform;
 public ConsolePlatform Platform
 {
  get=>platform;
  set
  {
   if(!Set(ref platform,value))return;
   NotifyMode();Changed(nameof(Profiles));Changed(nameof(OutputTargets));
   if(!Formats.Contains(Format))Format=OutputFormat.XISO;
   if(!Profiles.Contains(Profile))Profile=DeviceProfile.Custom;
   if(!EmulatorService.IsValidFor(OutputTarget,value))OutputTarget=OutputTarget.Custom;else ApplyOutputTarget();
   BackendReady=false;
  }
 }
 private XisoMode mode;
 public XisoMode Mode
 {
  get=>mode;
  set
  {
   if(!Set(ref mode,value))return;
   NotifyMode();Changed(nameof(Profiles));
   if(!Formats.Contains(Format))Format=OutputFormat.XISO;
   if(!Profiles.Contains(Profile))Profile=DeviceProfile.Custom;
   BackendReady=false;
  }
 }
 private OutputFormat format=OutputFormat.GoD;
 public OutputFormat Format {get=>format;set {if(Set(ref format,value))NotifyCapabilities();}}
 public bool UsesXgd=>JobPlanner.UsesXgd(Mode,Platform,Profile,Advanced);
 public bool IsWriting=>JobPlanner.IsWriting(Mode);
 public bool SupportsPatching=>JobPlanner.SupportsPatching(UsesXgd,Mode);
 public bool SupportsSkip=>JobPlanner.SupportsSkip(UsesXgd,Mode);
 public bool CanBatchImages=>Mode!=XisoMode.Create;
 public bool ShowFormat=>Mode==XisoMode.Convert;
 public string AddLabel=>L.T(Mode==XisoMode.Create?"Add source folder":"Add ISO files");
 private void NotifyMode()
 {
  foreach(var name in new[]{nameof(Formats),nameof(NeedsOutput),nameof(OutputButtonBackground),nameof(OutputMissing),nameof(IsXbox),nameof(IsWriting),nameof(SupportsPatching),nameof(SupportsSkip),nameof(AddLabel),nameof(ShowFormat),nameof(CanBatchImages),nameof(UsesXgd)})Changed(name);
  NotifyCapabilities();
 }

 // ---------- backends ----------
 private string backendPath="";
 public string BackendPath {get=>backendPath;set {if(Set(ref backendPath,value)){BackendReady=false;XisoReady=false;}}}
 private string xgdBackendPath="";
 public string XgdBackendPath {get=>xgdBackendPath;set {if(Set(ref xgdBackendPath,value))XgdReady=false;}}
 private bool xgdReady,xisoReady,backendReady;
 public bool XgdReady {get=>xgdReady;set {if(Set(ref xgdReady,value)){Changed(nameof(XgdDetectedText));Changed(nameof(XgdStatusBrush));}}}
 public bool XisoReady {get=>xisoReady;set {if(Set(ref xisoReady,value)){Changed(nameof(XisoDetectedText));Changed(nameof(XisoStatusBrush));}}}
 public bool BackendReady {get=>backendReady;set {Set(ref backendReady,value);StartCommand?.Refresh();}}
 public string XisoDetectedText=>L.T(XisoReady?"DETECTED":"NOT DETECTED");
 public string XgdDetectedText=>L.T(XgdReady?"DETECTED":"NOT DETECTED");
 public string XisoStatusBrush=>XisoReady?"#244A2A":"#542A30";
 public string XgdStatusBrush=>XgdReady?"#244A2A":"#542A30";

 // ---------- general state ----------
 private string outputDirectory="";
 public string OutputDirectory {get=>outputDirectory;set {if(Set(ref outputDirectory,value)){Changed(nameof(NeedsOutput));Changed(nameof(OutputButtonBackground));Changed(nameof(OutputMissing));}}}
 private string message="";
 public string Message {get=>message;set {if(!Set(ref message,value)&&!string.IsNullOrEmpty(value))Changed(nameof(Message));}}
 private bool skipSystemUpdate=true,disableMediaPatching,continueOnError=true;
 public bool SkipSystemUpdate {get=>skipSystemUpdate;set=>Set(ref skipSystemUpdate,value);}
 public bool DisableMediaPatching {get=>disableMediaPatching;set=>Set(ref disableMediaPatching,value);}
 public bool ContinueOnError {get=>continueOnError;set=>Set(ref continueOnError,value);}
 public bool RecursiveFolders {get;set;}=true;
 private bool showDetails,showSettings,showAbout;
 public bool ShowDetails {get=>showDetails;set=>Set(ref showDetails,value);}
 public bool ShowSettings {get=>showSettings;set=>Set(ref showSettings,value);}
 public bool ShowAbout {get=>showAbout;set=>Set(ref showAbout,value);}
 public bool ShowLegal {get;private set;}
 public bool CanAcceptLegal=>Legal.DocumentsReady;
 public string LegalMarkdown=>Legal.CurrentMarkdown;
 private bool queueDirty;
 private XisoJob? selectedJob;
 public XisoJob? SelectedJob
 {
  get=>selectedJob;
  set
  {
   if(!Set(ref selectedJob,value))return;
   Changed(nameof(SelectedLog));Changed(nameof(HasSelectedJob));RemoveCommand.Refresh();RetryCommand.Refresh();
   RefreshSelectionDetails();_=LoadContentsAsync();
  }
 }
 public string SelectedLog=>SelectedJob?.Log??L.T("Select a job to inspect its log.");
 public bool IsRunning=>Queue.IsRunning;
 public bool IsQueueEmpty=>Jobs.Count==0;
 public string CompletedCount=>$"{Jobs.Count(j=>j.Status==JobStatus.Succeeded)} {L.T("Count completed")}";
 public string WaitingCount=>$"{Jobs.Count(j=>j.Status==JobStatus.Waiting)} {L.T("Count waiting")}";
 public string FailedCount=>$"{Jobs.Count(j=>j.Status==JobStatus.Failed)} {L.T("Count failed")}";
 public string TotalCount=>$"{Jobs.Count} {L.T("Count total")}";
 public string VersionLabel=>$"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)} · {BuildConfiguration}";
 public static string Version=>typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)??"?";
 public string DataDirectory=>Storage.DataDirectory;
 public static string BuildConfiguration
 {
  get
  {
#if DEBUG
   return "Debug";
#else
   return "Release";
#endif
  }
 }

 public AsyncRelayCommand StartCommand {get;}
 public AsyncRelayCommand TestBackendCommand {get;}
 public AsyncRelayCommand SaveSettingsCommand {get;}
 public RelayCommand CancelCommand {get;}
 public RelayCommand StopCommand {get;}
 public RelayCommand CancelPendingCommand {get;}
 public RelayCommand RemoveCommand {get;}
 public RelayCommand RetryCommand {get;}
 public RelayCommand ClearCommand {get;}
 public RelayCommand ClearAllCommand {get;}

 public MainViewModel(SettingsService? storage=null,string? legalDirectory=null)
 {
  Storage=storage??new SettingsService();
  Legal=new LegalService(Storage,legalDirectory);ShowLegal=!Legal.IsAccepted;
  Queue=new JobQueueService(new JobExecutor(Process));
  var hadSettings=File.Exists(Path.Combine(Storage.DataDirectory,"settings.json"));
  var settings=Storage.LoadSettings();
  uiScaleMode=ScaleModes.Contains(settings.UiScaleMode)?settings.UiScaleMode:"Auto";
  customUiScale=double.IsFinite(settings.CustomUiScale)?Math.Clamp(settings.CustomUiScale,100,200):100;
  detailsOuterScroll=settings.DetailsOuterScroll;
  L.SetLanguage(settings.Language);
  L.PropertyChanged+=(_,_)=>RefreshLanguage();
  backendPath=settings.BackendPath??"";outputDirectory=settings.OutputDirectory??"";xgdBackendPath=settings.XgdBackendPath??"";
  platform=Enum.IsDefined(settings.Platform)?settings.Platform:ConsolePlatform.Xbox;
  format=Enum.IsDefined(settings.OutputFormat)?settings.OutputFormat:OutputFormat.GoD;
  RecursiveFolders=settings.RecursiveFolders;
  advanced=settings.Advanced;profile=Profiles.Contains(settings.Profile)?settings.Profile:DeviceProfile.Custom;
  scrub=Enum.IsDefined(settings.Scrub)?settings.Scrub:ScrubMode.None;
  split=settings.Split;attachXbe=settings.AttachXbe;patchMedia=settings.PatchMedia;renameXbe=settings.RenameXbe;
  offline=true; // every session starts offline; online lookups need fresh consent
  skipSystemUpdate=settings.DefaultsRevision<1||settings.SkipSystemUpdate;
  // v0.1.9 defaults: Dark (gray, Mica) + Green accent. Settings written by 0.1.9 pre-release builds (revision 2)
  // were migrated to "Xbox"/"Windows" automatically; restore the final defaults once.
  if(hadSettings&&settings.DefaultsRevision==2){if(settings.Theme=="Xbox")settings.Theme="Dark";if(settings.Accent=="Windows")settings.Accent="Green";}
  disableMediaPatching=settings.DisableMediaPatching;continueOnError=settings.ContinueOnError;
  LoadFeatureSettings(settings);
  foreach(var saved in Storage.Load("queue.json",()=>new List<SavedJob>()))
  {
   if(saved is null||saved.Options is null||string.IsNullOrWhiteSpace(saved.Options.Source)||saved.Options.Destination is null||!Enum.IsDefined(saved.Options.Mode)||!Enum.IsDefined(saved.Options.Platform)||!Enum.IsDefined(saved.Status)){Message="An invalid saved queue entry was skipped.";continue;}
   var state=saved.Status==JobStatus.Running?JobStatus.Interrupted:saved.Status;
   var job=new XisoJob{Id=saved.Id,Options=BackendPolicy.Normalize(saved.Options),Status=state,Result=state==JobStatus.Interrupted?"Interrupted. Retry to restart.":saved.Result,PublishedPath=saved.PublishedPath};
   Jobs.Add(job);QueueTitleRead(job);
  }
  StartCommand=new AsyncRelayCommand(StartAsync,ReportError,()=>!ShowLegal&&!IsRunning&&Jobs.Any(j=>j.Status==JobStatus.Waiting));
  TestBackendCommand=new AsyncRelayCommand(TestBackendAsync,ReportError,()=>!IsRunning);
  SaveSettingsCommand=new AsyncRelayCommand(async()=>{await SaveSettingsAsync();await TestBackendAsync();ShowSettings=false;},ReportError,()=>!IsRunning);
  CancelCommand=new RelayCommand(()=>Queue.CancelCurrent(),()=>IsRunning);
  StopCommand=new RelayCommand(()=>{Queue.StopAfterCurrent=true;Refresh();},()=>IsRunning);
  CancelPendingCommand=new RelayCommand(()=>{Queue.CancelPending();Refresh();},()=>Jobs.Any(j=>j.Status==JobStatus.Waiting));
  RemoveCommand=new RelayCommand(()=>{if(SelectedJob is {} job){Jobs.Remove(job);SelectedJob=null;Refresh();}},()=>SelectedJob is {IsRunning:false});
  RetryCommand=new RelayCommand(()=>
  {
   if(SelectedJob is not {} job)return;
   try
   {
    JobExecutor.Validate(job.Options);
    if(Jobs.Any(other=>other!=job&&other.Status is JobStatus.Waiting or JobStatus.Running&&other.Options.Destination!=""&&other.Options.Destination.Equals(job.Options.Destination,StringComparison.OrdinalIgnoreCase)))throw new IOException("Another queued job uses this destination.");
    job.Status=JobStatus.Waiting;job.Result="Ready to retry";Refresh();
   }
   catch(Exception ex){ReportError(ex);}
  },()=>SelectedJob is {Status:JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted});
  ClearAllCommand=new RelayCommand(()=>{CancelInspection();SelectedJob=null;Jobs.Clear();Refresh();},()=>!IsRunning&&Jobs.Count>0);
  ClearCommand=new RelayCommand(()=>{foreach(var job in Jobs.Where(j=>j.Status is JobStatus.Succeeded or JobStatus.Cancelled).ToArray())Jobs.Remove(job);Refresh();},()=>!IsRunning);
  Queue.Changed+=Refresh;
  Jobs.CollectionChanged+=(_,_)=>RefreshFilter();
  RefreshFilter();
  if(!string.IsNullOrEmpty(Storage.Warning))Message=Storage.Warning;
  if(CrashLog.TakePendingCrash(Storage.DataDirectory) is {} crash)Message=L.T("X-Shard closed unexpectedly last time. A crash log was saved locally:")+" "+crash;
 }
 public void ReportError(Exception ex)=>Message=ex.Message;
 public async Task AcceptLegalAsync(){await Legal.AcceptAsync();ShowLegal=false;Changed(nameof(ShowLegal));StartCommand.Refresh();}
 public async Task TestBackendAsync()
 {
  var generation=++probeGeneration;TestState="Testing";BackendReady=false;
  async Task<bool> Probe(bool xgd)
  {
   var file=xgd?XgdToolService.FindBackend(XgdBackendPath):XisoProcessService.FindBackend(BackendPath);
   if(file==null)return false;
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
   try{if(xgd)await new XgdToolService(Process).ProbeAsync(file,timeout.Token);else await Process.ProbeAsync(file,timeout.Token);return true;}
   catch{return false;}
  }
  var result=await Task.WhenAll(Probe(false),Probe(true));if(generation!=probeGeneration)return;
  TestState=result.All(r=>r)?"Test Successful":"Test Failed";XisoReady=result[0];XgdReady=result[1];
  BackendReady=UsesXgd?XgdReady:XisoReady;
 }
 /// <summary>Current job settings as a planner request.</summary>
 public JobRequest CurrentRequest=>new(Mode,Platform,OutputDirectory,Format,Profile,Scrub,Split,AttachXbe,PatchMedia,RenameXbe,Offline,Advanced,SkipSystemUpdate,DisableMediaPatching,OutputTarget!=OutputTarget.Custom);
 public void AddSources(IEnumerable<string> paths)
 {
  if(ShowLegal){Message=L.T("Review and accept the legal documents first.");return;}
  if(NeedsOutput){Message="";return;}
  var request=CurrentRequest;
  foreach(var raw in paths)
  {
   try
   {
    var active=Jobs.Where(j=>j.Status is JobStatus.Waiting or JobStatus.Running).Select(j=>j.Options).ToList();
    var job=new XisoJob{Options=JobPlanner.Plan(request,raw,active),Status=JobStatus.Waiting};
    Jobs.Add(job);SelectedJob=job;Message="";QueueTitleRead(job);
   }
   catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException){ReportError(ex);}
  }
  Refresh();
 }
 private async Task StartAsync()
 {
  await TestBackendAsync();
  string Resolve(XisoJobOptions option)=>(BackendPolicy.For(option)==BackendEngine.XgdTool?XgdToolService.FindBackend(XgdBackendPath):XisoProcessService.FindBackend(BackendPath))??throw new IOException("Backend not detected for "+option.Engine);
  foreach(var job in Jobs.Where(j=>j.Status==JobStatus.Waiting))
   if(BackendPolicy.For(job.Options)==BackendEngine.XgdTool?!XgdReady:!XisoReady)throw new IOException("Backend not detected for "+job.Options.Engine);
  await ConfirmQueuedOnlineJobsAsync();
  if(!await ConfirmFreeSpaceAsync())return;
  await SaveSettingsAsync();
  var started=DateTimeOffset.UtcNow;var runJobs=Jobs.Where(j=>j.Status==JobStatus.Waiting).ToList();
  await Queue.RunAsync(Resolve,ContinueOnError,(job,line,error)=>output.Enqueue((job,line,error)),SaveQueueSafelyAsync);
  DrainOutput();Message="";
  await FinishRunAsync(started,runJobs.Where(j=>j.Status!=JobStatus.Waiting).ToList());
 }
 public void DrainOutput()
 {
  var grouped=new Dictionary<XisoJob,List<string>>();
  for(int i=0;i<4000&&output.TryDequeue(out var item);i++)
  {
   if(!grouped.TryGetValue(item.Job,out var lines))grouped[item.Job]=lines=[];
   if(item.Job.Options.Engine==BackendEngine.XgdTool){var match=System.Text.RegularExpressions.Regex.Match(item.Line,@"\]\s*(\d+(?:\.\d+)?)%");if(match.Success&&double.TryParse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture,out var percent))item.Job.Progress=Math.Clamp(percent,0,100);}
   item.Job.LastOutputAt=DateTimeOffset.UtcNow;
   lines.Add((item.Error&&item.Job.Options.Engine!=BackendEngine.XgdTool?"[!] ":"")+item.Line);
   if(item.Job.Options.Mode==XisoMode.List&&(!item.Error||item.Job.Options.Engine==BackendEngine.XgdTool)&&item.Job.Contents.Count<100000&&XisoOutputParser.ParseListEntry(item.Line) is {} entry)item.Job.Contents.Add(entry);
  }
  foreach(var pair in grouped)
  {
   var text=pair.Key.Log+string.Join(Environment.NewLine,pair.Value)+Environment.NewLine;
   pair.Key.Log=text.Length>1048576?"[Earlier log output omitted]\n"+text[^900000..]:text;
  }
  foreach(var job in Jobs.Where(j=>j.IsRunning&&j.StartedAt!=null))job.Elapsed=(DateTimeOffset.UtcNow-job.StartedAt!.Value).ToString(@"hh\:mm\:ss");
  Changed(nameof(ProcessingFeedback));Changed(nameof(TotalProgress));
  if(grouped.Count>0){Changed(nameof(SelectedLog));if(SelectedJob is {} selected&&grouped.ContainsKey(selected))RefreshContentFilter();}
  if(queueDirty){queueDirty=false;_=SaveQueueSafelyAsync();}
 }
 public async Task SaveSettingsAsync()
 {
  var settings=new AppSettings
  {
   Language=L.Language,UiScaleMode=UiScaleMode,CustomUiScale=CustomUiScale,DetailsOuterScroll=DetailsOuterScroll,Profile=Profile,Scrub=Scrub,Split=Split,AttachXbe=AttachXbe,
   PatchMedia=PatchMedia,RenameXbe=RenameXbe,Offline=Offline,Advanced=Advanced,BackendPath=BackendPath,XgdBackendPath=XgdBackendPath,Platform=Platform,OutputFormat=Format,
   RecursiveFolders=RecursiveFolders,OutputDirectory=OutputDirectory,SkipSystemUpdate=SkipSystemUpdate,DisableMediaPatching=DisableMediaPatching,ContinueOnError=ContinueOnError,
   DefaultsRevision=AppSettings.CurrentDefaultsRevision
  };
  StoreFeatureSettings(settings);
  await Storage.SaveAsync("settings.json",settings);
 }
 public Task SaveQueueSafelyAsync()=>SaveQueueAsync(false);
 private async Task SaveQueueAsync(bool throwOnError)
 {
  await saveGate.WaitAsync();
  try{await Storage.SaveAsync("queue.json",Jobs.Select(j=>new SavedJob(j.Id,j.Options,j.Status,j.Result,j.PublishedPath)).ToList());}
  catch(Exception ex) when(!throwOnError){Message="Queue could not be saved: "+ex.Message;}
  finally{saveGate.Release();}
 }
 public void Refresh()
 {
  queueDirty=true;
  foreach(var name in new[]{nameof(TotalProgress),nameof(TotalProgressText),nameof(IsRunning),nameof(IsQueueEmpty),nameof(CanReset),nameof(CanChangeDataLocation),nameof(CompletedCount),nameof(WaitingCount),nameof(FailedCount),nameof(TotalCount),nameof(CanLaunch)})Changed(name);
  StartCommand.Refresh();CancelCommand.Refresh();StopCommand.Refresh();CancelPendingCommand.Refresh();RemoveCommand.Refresh();RetryCommand.Refresh();ClearCommand.Refresh();ClearAllCommand.Refresh();TestBackendCommand.Refresh();SaveSettingsCommand.Refresh();
  RefreshFilter();
 }
}
