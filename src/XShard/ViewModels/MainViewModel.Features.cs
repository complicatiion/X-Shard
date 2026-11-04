using System.Collections.ObjectModel;
using XShard.Models;
using XShard.Services;
namespace XShard.ViewModels;
/// <summary>v0.1.8: theme/accent, emulator integration and library targets, queue search/filter/sort, title info, target preview, run reports, notifications and after-queue power actions.</summary>
public sealed partial class MainViewModel
{
 // ---------- appearance ----------
 /// <summary>Dark = Windows 11 Mica dark gray (default), Xbox = the former green dark theme, Light, System = follows Windows.</summary>
 public string[] ThemeModes=>["Dark","Xbox","Light","System"];
 private string theme="Dark";
 public string Theme {get=>theme;set {if(value is null||!ThemeModes.Contains(value))return;if(Set(ref theme,value)){Changed(nameof(IsAccentSelectable));foreach(var option in AccentOptions)option.Notify();AppearanceChanged?.Invoke();}}}
 /// <summary>The Xbox theme always uses the Xbox green accent.</summary>
 public bool IsAccentSelectable=>Theme!="Xbox";
 public string[] AccentNames=>["Green","Teal","Blue","Purple","Orange","Red","Windows"];
 private string accent="Green";
 public string Accent {get=>accent;set {if(value is null||!AccentNames.Contains(value))return;if(Set(ref accent,value)){foreach(var option in AccentOptions)option.Notify();AppearanceChanged?.Invoke();}}}
 private IReadOnlyList<AccentOption>? accentOptions;
 public IReadOnlyList<AccentOption> AccentOptions=>accentOptions??=
 [
  new("Green","#318B3B",this),new("Teal","#1E8A86",this),new("Blue","#2F6FD0",this),new("Purple","#7A4FD1",this),new("Orange","#C4661C",this),new("Red","#C23B4A",this),new("Windows",Views.ThemeService.WindowsAccentHex(),this)
 ];
 /// <summary>Raised when theme or accent changes; the view applies it (ThemeService).</summary>
 public event Action? AppearanceChanged;

 // ---------- notifications, reports, after-queue action ----------
 private bool notificationsEnabled=true;
 public bool NotificationsEnabled {get=>notificationsEnabled;set=>Set(ref notificationsEnabled,value);}
 public string[] ReportModes=>["Off","Txt","Html"];
 private string reportMode="Off";
 public string ReportMode {get=>reportMode;set {if(value is null||!ReportModes.Contains(value))return;Set(ref reportMode,value);}}
 public string ReportsDirectory=>Path.Combine(Storage.DataDirectory,"reports");
 public string LogsDirectory=>CrashLog.Folder(Storage.DataDirectory);
 private RunReport? lastReport;
 public RunReport? LastReport {get=>lastReport;private set {if(Set(ref lastReport,value)){Changed(nameof(HasReport));Changed(nameof(ReportSummary));}}}
 public bool HasReport=>LastReport!=null;
 public string ReportSummary=>LastReport is {} r?$"{r.Succeeded} {L.T("Count completed")} · {r.Failed} {L.T("Count failed")} · {L.T("Space saved")} {RunReportService.FormatBytes(r.SavedBytes)}":"";
 /// <summary>Session-only on purpose: shutting down after a queue is never restored automatically on the next start.</summary>
 public string[] AfterQueueOptions=>["Nothing","Hibernate","Shutdown"];
 private string afterQueue="Nothing";
 public string AfterQueue {get=>afterQueue;set {if(value is null||!AfterQueueOptions.Contains(value))return;Set(ref afterQueue,value);}}
 /// <summary>Raised after every queue run with the report; the view shows the toast, flashes the taskbar and starts the power countdown.</summary>
 public event Action<RunReport>? RunFinished;
 private async Task FinishRunAsync(DateTimeOffset started,IReadOnlyList<XisoJob> ran)
 {
  if(ran.Count==0)return;
  var report=await Task.Run(()=>RunReportService.Build(started,DateTimeOffset.UtcNow,ran,Version));
  LastReport=report;
  if(ReportMode!="Off")
  {
   try{var file=Path.Combine(ReportsDirectory,RunReportService.DefaultFileName(report,ReportMode));await RunReportService.WriteAsync(report,file);Message=L.T("Run report saved:")+" "+file;}
   catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Message="Report could not be saved: "+ex.Message;}
  }
  RunFinished?.Invoke(report);
 }
 public async Task ExportReportAsync(string path){if(LastReport is {} report){await RunReportService.WriteAsync(report,path);Message=L.T("Run report saved:")+" "+path;}}

 // ---------- power countdown ----------
 private CancellationTokenSource? powerCountdown;
 private bool showPowerCountdown;private int powerSeconds;
 public bool ShowPowerCountdown {get=>showPowerCountdown;private set=>Set(ref showPowerCountdown,value);}
 public string PowerCountdownText=>string.Format(L.T(AfterQueue=="Hibernate"?"hibernate_countdown":"shutdown_countdown"),powerSeconds);
 /// <summary>Counts down 60 seconds with a visible cancel option, then runs the selected power action.</summary>
 public async Task RunAfterQueueAsync(Func<string,bool> perform)
 {
  if(AfterQueue=="Nothing")return;
  runNow=false;powerCountdown?.Cancel();var cts=new CancellationTokenSource();powerCountdown=cts;ShowPowerCountdown=true;
  try
  {
   for(powerSeconds=60;powerSeconds>0;powerSeconds--){Changed(nameof(PowerCountdownText));await Task.Delay(1000,cts.Token);}
   ShowPowerCountdown=false;var action=AfterQueue;AfterQueue="Nothing";
   if(!perform(action))Message=L.T("The power action could not be started.");
  }
  catch(OperationCanceledException)
  {
   if(powerCountdown!=cts)return; // superseded by a newer countdown
   if(runNow){runNow=false;ShowPowerCountdown=false;var action=AfterQueue;AfterQueue="Nothing";if(!perform(action))Message=L.T("The power action could not be started.");}
   else Message=L.T("Power action cancelled.");
  }
  finally{if(powerCountdown==cts){ShowPowerCountdown=false;powerCountdown=null;}cts.Dispose();}
 }
 public void CancelPowerCountdown()=>powerCountdown?.Cancel();
 private bool runNow;
 public void RunPowerActionNow(){runNow=true;powerCountdown?.Cancel();}

 // ---------- emulators and library targets ----------
 private string xemuPath="",xeniaPath="",xemuLibrary="",xeniaLibrary="",retroBatRoot="",batoceraShare="";
 public string XemuPath {get=>xemuPath;set {if(Set(ref xemuPath,value??""))Changed(nameof(CanLaunch));}}
 public string XeniaPath {get=>xeniaPath;set {if(Set(ref xeniaPath,value??""))Changed(nameof(CanLaunch));}}
 public string XemuLibrary {get=>xemuLibrary;set {if(Set(ref xemuLibrary,value??""))ApplyOutputTarget();}}
 public string XeniaLibrary {get=>xeniaLibrary;set {if(Set(ref xeniaLibrary,value??""))ApplyOutputTarget();}}
 public string RetroBatRoot {get=>retroBatRoot;set {if(Set(ref retroBatRoot,value??""))ApplyOutputTarget();}}
 public string BatoceraShare {get=>batoceraShare;set {if(Set(ref batoceraShare,value??""))ApplyOutputTarget();}}
 public OutputTarget[] OutputTargets=>Enum.GetValues<OutputTarget>().Where(t=>EmulatorService.IsValidFor(t,Platform)).ToArray();
 private OutputTarget outputTarget=OutputTarget.Custom;
 public OutputTarget OutputTarget {get=>outputTarget;set {if(!Enum.IsDefined(value))return;if(Set(ref outputTarget,value)){Changed(nameof(IsLibraryTarget));ApplyOutputTarget();}}}
 public bool IsLibraryTarget=>OutputTarget!=OutputTarget.Custom;
 private AppSettings LibrarySettings=>new(){XemuLibrary=XemuLibrary,XeniaLibrary=XeniaLibrary,RetroBatRoot=RetroBatRoot,BatoceraShare=BatoceraShare};
 private void ApplyOutputTarget()
 {
  if(OutputTarget==OutputTarget.Custom)return;
  if(EmulatorService.LibraryFolder(OutputTarget,Platform,LibrarySettings) is {} folder)OutputDirectory=folder;
  else Message=L.T("Set the library folder for this target in Settings first.");
 }
 /// <summary>Cheap check (no disk scan); LaunchSelected locates the bootable file.</summary>
 public bool CanLaunch=>SelectedJob is {} job&&!job.IsRunning&&!string.IsNullOrWhiteSpace(job.Options.Platform==ConsolePlatform.Xbox?XemuPath:XeniaPath);
 public void LaunchSelected()
 {
  if(SelectedJob is not {} job)return;
  var emulator=job.Options.Platform==ConsolePlatform.Xbox?XemuPath:XeniaPath;
  if(!File.Exists(emulator)){Message=L.T("Choose the emulator executable in Settings first.");return;}
  if(EmulatorService.FindLaunchTarget(job) is not {} target){Message=L.T("No bootable image, XEX or GoD header found for this job.");return;}
  try{using var process=System.Diagnostics.Process.Start(EmulatorService.BuildLaunch(job.Options.Platform,emulator,target));Message=L.T("Started in emulator:")+" "+Path.GetFileName(target);}
  catch(Exception ex){ReportError(ex);}
 }

 // ---------- queue search / filter / sort ----------
 public ObservableCollection<XisoJob> FilteredJobs {get;}=[];
 public string[] StatusFilters=>["All","Waiting","Running","Succeeded","Failed"];
 public string[] SortModes=>["Added","Name","Status"];
 private string queueSearch="",statusFilter="All",sortMode="Added";
 public string QueueSearch {get=>queueSearch;set {if(Set(ref queueSearch,value??""))RefreshFilter();}}
 public string StatusFilter {get=>statusFilter;set {if(value is null||!StatusFilters.Contains(value))return;if(Set(ref statusFilter,value))RefreshFilter();}}
 public string SortMode {get=>sortMode;set {if(value is null||!SortModes.Contains(value))return;if(Set(ref sortMode,value))RefreshFilter();}}
 public bool IsFilterActive=>QueueSearch.Length>0||StatusFilter!="All";
 public bool IsFilterEmpty=>Jobs.Count>0&&FilteredJobs.Count==0;
 private bool Matches(XisoJob job)
 {
  var statusOk=StatusFilter switch
  {
   "Waiting"=>job.Status==JobStatus.Waiting,"Running"=>job.Status==JobStatus.Running,"Succeeded"=>job.Status==JobStatus.Succeeded,
   "Failed"=>job.Status is JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted,_=>true
  };
  if(!statusOk)return false;
  if(QueueSearch.Length==0)return true;
  return job.Name.Contains(QueueSearch,StringComparison.OrdinalIgnoreCase)||job.Options.Source.Contains(QueueSearch,StringComparison.OrdinalIgnoreCase)||job.TitleSummary.Contains(QueueSearch,StringComparison.OrdinalIgnoreCase);
 }
 private void RefreshFilter()
 {
  IEnumerable<XisoJob> query=Jobs.Where(Matches);
  query=SortMode switch
  {
   "Name"=>query.OrderBy(j=>j.Name,StringComparer.CurrentCultureIgnoreCase),
   "Status"=>query.OrderBy(j=>j.Status switch {JobStatus.Running=>0,JobStatus.Waiting=>1,JobStatus.Failed=>2,JobStatus.Interrupted=>3,JobStatus.Cancelled=>4,_=>5}).ThenBy(j=>Jobs.IndexOf(j)),
   _=>query
  };
  var desired=query.ToList();
  if(!desired.SequenceEqual(FilteredJobs))
  {
   // Incremental sync (no Clear): the ListBox keeps its selection, so details and contents are not reloaded.
   var selected=SelectedJob;
   for(var i=FilteredJobs.Count-1;i>=0;i--)if(!desired.Contains(FilteredJobs[i]))FilteredJobs.RemoveAt(i);
   for(var i=0;i<desired.Count;i++)
   {
    var current=FilteredJobs.IndexOf(desired[i]);
    if(current<0)FilteredJobs.Insert(i,desired[i]);else if(current!=i)FilteredJobs.Move(current,i);
   }
   if(selected!=null&&!desired.Contains(selected))SelectedJob=null;
   else if(selected!=null&&SelectedJob!=selected){selectedJob=selected;Changed(nameof(SelectedJob));}
  }
  Changed(nameof(IsFilterActive));Changed(nameof(IsFilterEmpty));
 }
 public void ClearFilter(){QueueSearch="";StatusFilter="All";}

 // ---------- contents filter ----------
 public ObservableCollection<IsoContentEntry> FilteredContents {get;}=[];
 private string contentSearch="";
 public string ContentSearch {get=>contentSearch;set {if(Set(ref contentSearch,value??""))RefreshContentFilter();}}
 private void RefreshContentFilter()
 {
  FilteredContents.Clear();
  if(SelectedJob is not {} job)return;
  foreach(var entry in job.Contents)
  {
   if(ContentSearch.Length>0&&!entry.Path.Contains(ContentSearch,StringComparison.OrdinalIgnoreCase))continue;
   FilteredContents.Add(entry);if(FilteredContents.Count>=20000)break;
  }
 }

 // ---------- title info and target preview ----------
 private readonly SemaphoreSlim titleGate=new(1,1);
 private void QueueTitleRead(XisoJob job)
 {
  _=Task.Run(async()=>
  {
   await titleGate.WaitAsync();
   try
   {
    TitleInfo? info=null;
    try{info=TitleInfoReader.TryRead(job.Options.Source,job.Options.Platform);if(info==null&&job.PublishedPath is {} published)info=TitleInfoReader.TryRead(published,job.Options.Platform);}
    catch(Exception){info=null;}
    if(info!=null)Avalonia.Threading.Dispatcher.UIThread.Post(()=>{job.Title=info;if(job==SelectedJob)RefreshSelectionDetails();RefreshFilter();});
   }
   finally{titleGate.Release();}
  });
 }
 /// <summary>Label/value pairs for the Title tab.</summary>
 public IReadOnlyList<TitleField> TitleFields
 {
  get
  {
   var list=new List<TitleField>();if(SelectedJob?.Title is not {} t)return list;
   void Add(string label,string? value){if(!string.IsNullOrWhiteSpace(value))list.Add(new(L.T(label),value));}
   Add("Name",t.Name);Add("Title ID",t.TitleIdHex+(t.TitleIdCode is {} code?$" ({code})":""));Add("Media ID",t.MediaIdHex);Add("Region",t.Region);Add("Version",t.Version);
   Add("Disc",t.Disc is {} d?(t.DiscCount is {} c and >0?$"{d} / {c}":d.ToString()):null);Add("Read from",t.SourceKind);
   return list;
  }
 }
 public byte[]? SelectedIcon=>SelectedJob?.Title?.IconPng;
 public bool HasTitleInfo=>SelectedJob?.Title!=null;
 public string TitleStatus=>SelectedJob==null?L.T("Select a job to inspect its source contents."):SelectedJob.Title==null?L.T("No title information found. Supported: XISO/ISO, XBE, XEX, extracted game folders and GoD (Xbox 360 name and icon only from GoD)."):"";
 private TargetPreviewInfo? preview;
 public string PreviewText=>SelectedJob==null?"":preview is {} p?string.Join(Environment.NewLine,p.Layout)+(p.Expected?Environment.NewLine+Environment.NewLine+L.T("Backend structure is expected, not guaranteed."):""):"";
 public string PreviewSpaceText=>preview is {} p&&SelectedJob?.Options.Mode!=XisoMode.List?$"{L.T("Estimated size")}: {RunReportService.FormatBytes(p.EstimatedBytes)} · {L.T("Free space")}: {RunReportService.FormatBytes(p.FreeBytes)}":"";
 public bool PreviewInsufficient=>preview?.InsufficientSpace==true;
 private void RefreshSelectionDetails()
 {
  preview=SelectedJob is {} job?TargetPreview.Describe(job.Options,job.Title):null;
  foreach(var name in new[]{nameof(TitleFields),nameof(SelectedIcon),nameof(HasTitleInfo),nameof(TitleStatus),nameof(PreviewText),nameof(PreviewSpaceText),nameof(PreviewInsufficient),nameof(CanLaunch)})Changed(name);
  RefreshContentFilter();
 }
 private async Task<bool> ConfirmFreeSpaceAsync()
 {
  var waiting=Jobs.Where(j=>j.Status==JobStatus.Waiting).Select(j=>j.Options).ToList();
  var shortages=await Task.Run(()=>TargetPreview.FindShortages(waiting));
  if(shortages.Count==0)return true;
  var detail=string.Join(Environment.NewLine,shortages.Select(s=>$"{s.Volume}  {L.T("needed")} {RunReportService.FormatBytes(s.Needed)} · {L.T("free")} {RunReportService.FormatBytes(s.Free)}"));
  return await ConfirmAsync(L.T("Not enough free space?"),L.T("free_space_warning_message"),detail,"Warning");
 }

 // ---------- persistence ----------
 private void LoadFeatureSettings(AppSettings s)
 {
  theme=ThemeModes.Contains(s.Theme)?s.Theme:"Dark";accent=AccentNames.Contains(s.Accent)?s.Accent:"Green";
  notificationsEnabled=s.NotificationsEnabled;reportMode=ReportModes.Contains(s.ReportMode)?s.ReportMode:"Off";
  xemuPath=s.XemuPath??"";xeniaPath=s.XeniaPath??"";xemuLibrary=s.XemuLibrary??"";xeniaLibrary=s.XeniaLibrary??"";retroBatRoot=s.RetroBatRoot??"";batoceraShare=s.BatoceraShare??"";
  outputTarget=Enum.IsDefined(s.OutputTarget)&&EmulatorService.IsValidFor(s.OutputTarget,platform)?s.OutputTarget:OutputTarget.Custom;
 }
 private void StoreFeatureSettings(AppSettings s)
 {
  s.Theme=Theme;s.Accent=Accent;s.NotificationsEnabled=NotificationsEnabled;s.ReportMode=ReportMode;
  s.XemuPath=XemuPath;s.XeniaPath=XeniaPath;s.XemuLibrary=XemuLibrary;s.XeniaLibrary=XeniaLibrary;s.RetroBatRoot=RetroBatRoot;s.BatoceraShare=BatoceraShare;s.OutputTarget=OutputTarget;
 }
 private void ResetFeatureSettings()
 {
  Theme="Dark";Accent="Green";NotificationsEnabled=true;ReportMode="Off";AfterQueue="Nothing";CancelPowerCountdown();
  OutputTarget=OutputTarget.Custom;XemuPath="";XeniaPath="";XemuLibrary="";XeniaLibrary="";RetroBatRoot="";BatoceraShare="";
  ClearFilter();SortMode="Added";ContentSearch="";LastReport=null;
  ResetRequested?.Invoke();
 }
 /// <summary>Raised during app reset so the view can remove per-user shell registrations (context menu, toast ID).</summary>
 public event Action? ResetRequested;
}
public sealed record TitleField(string Label,string Value);
public sealed class AccentOption(string name,string hex,MainViewModel owner):ViewModelBase
{
 public string Name=>name;
 public string Hex=>hex;
 public string Label=>LocalizationService.Instance.T(name);
 public bool IsSelected=>owner.IsAccentSelectable?owner.Accent==name:name=="Green";
 public void Notify(){Changed(nameof(IsSelected));Changed(nameof(Label));}
}
