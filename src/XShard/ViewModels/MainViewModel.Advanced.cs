using XShard.Models;
using XShard.Services;
namespace XShard.ViewModels;
public sealed partial class MainViewModel
{
 public bool NeedsOutput=>IsWriting&&string.IsNullOrWhiteSpace(OutputDirectory);
 public bool IsXbox=>Platform==ConsolePlatform.Xbox;
 public bool CanSplit=>JobPlanner.CanSplit(Mode,Format);
 public bool CanAttach=>JobPlanner.CanAttach(Platform,Mode,Format);
 public bool CanPatchXgd=>JobPlanner.CanPatchXgd(Platform,Mode,Format);
 public bool CanScrub=>JobPlanner.CanScrub(Mode,Format);
 private void NotifyCapabilities(){Changed(nameof(CanSplit));Changed(nameof(CanAttach));Changed(nameof(CanPatchXgd));Changed(nameof(CanScrub));}
 public bool CanReset=>!IsRunning;
 public double TotalProgress=>Jobs.Count==0?0:100d*Jobs.Count(j=>j.Status is not (JobStatus.Waiting or JobStatus.Running))/Jobs.Count;
 public string TotalProgressText=>$"{L.T("Total Progress")} · {Jobs.Count(j=>j.Status is not (JobStatus.Waiting or JobStatus.Running))} / {Jobs.Count}";
 private bool advanced;
 public bool Advanced {get=>advanced;set {if(Set(ref advanced,value))NotifyMode();}}
 private DeviceProfile profile;
 public DeviceProfile Profile {get=>profile;set {if(Set(ref profile,value)){ApplyProfile();NotifyMode();}}}
 public ScrubMode[] ScrubModes=>Enum.GetValues<ScrubMode>();
 private ScrubMode scrub;
 public ScrubMode Scrub {get=>scrub;set=>Set(ref scrub,value);}
 private bool split,attachXbe,patchMedia,renameXbe,offline=true;
 public bool Split {get=>split;set=>Set(ref split,value);}
 public bool AttachXbe {get=>attachXbe;set=>Set(ref attachXbe,value);}
 public bool PatchMedia {get=>patchMedia;set=>Set(ref patchMedia,value);}
 public bool RenameXbe {get=>renameXbe;set=>Set(ref renameXbe,value);}
 public bool Offline
 {
  get=>offline;
  set
  {
   if(offline==value)return;
   // Disabling offline mode permits XGDTool to contact external services; it requires an explicit Yes.
   if(!value&&!OnlineApproved){RevertOfflineToggle();_=RequestOnlineModeAsync();return;}
   Set(ref offline,value);
  }
 }
 private void ApplyProfile()
 {
  if(Profile==DeviceProfile.Custom)return;
  var preset=JobPlanner.ApplyProfile(CurrentRequest);
  Advanced=preset.Advanced;Mode=preset.Mode;Split=preset.Split;AttachXbe=preset.AttachXbe;PatchMedia=preset.PatchMedia;RenameXbe=preset.RenameXbe;Scrub=preset.Scrub;Format=preset.Format;
 }
 private int probeGeneration;
 private string testState="Test Backend";
 public string TestState {get=>testState;set {if(Set(ref testState,value)){Changed(nameof(TestBackground));Changed(nameof(TestLabel));}}}
 public string TestBackground=>TestState switch {"Testing"=>"#4A3720","Test Successful"=>"#23472A","Test Failed"=>"#4C292F",_=>"#303B30"};
 private bool detailsExpanded;
 public bool DetailsExpanded {get=>detailsExpanded;set {if(Set(ref detailsExpanded,value)){Changed(nameof(DetailsChevron));if(value)_=LoadContentsAsync();}}}
 private bool showReset;
 public bool ShowReset {get=>showReset;set=>Set(ref showReset,value);}
 public async Task ResetAppAsync()
 {
  if(IsRunning)throw new InvalidOperationException("Stop the queue before resetting.");
  ++probeGeneration;inspection?.Cancel();
  BackendPath="";XgdBackendPath="";OutputDirectory="";Platform=ConsolePlatform.Xbox;Profile=DeviceProfile.Custom;Mode=XisoMode.Extract;Format=OutputFormat.XISO;
  ResolveConfirmation(false);OnlineApproved=false;Advanced=false;Scrub=ScrubMode.None;Split=false;AttachXbe=false;PatchMedia=false;RenameXbe=false;Offline=true;
  SkipSystemUpdate=true;DisableMediaPatching=false;RecursiveFolders=true;Changed(nameof(RecursiveFolders));ContinueOnError=true;
  UiScaleMode="Auto";CustomUiScale=100;DetailsOuterScroll=true;L.SetLanguage("en");
  ResetFeatureSettings();
  SelectedJob=null;Jobs.Clear();DetailsExpanded=false;ShowDetails=false;ShowSettings=false;ShowReset=false;
  TestState="Test Backend";XisoReady=false;XgdReady=false;BackendReady=false;Message="";
  await SaveSettingsAsync();await SaveQueueSafelyAsync();Legal.ResetAcceptance();ShowLegal=true;Changed(nameof(ShowLegal));Refresh();
 }
 public void CancelInspection()=>inspection?.Cancel();
 private CancellationTokenSource? inspection;
 private string contentStatus="Select a job to inspect its source contents.";
 public string ContentStatus {get=>L.T(contentStatus);set=>Set(ref contentStatus,value);}
 public async Task LoadContentsAsync()
 {
  inspection?.Cancel();var job=SelectedJob;if(job==null){ContentStatus="Select a job to inspect its source contents.";RefreshContentFilter();return;}
  using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(60));inspection=cts;
  ContentStatus="Loading source contents…";
  try
  {
   List<IsoContentEntry> entries;
   var directorySource=await Task.Run(()=>Directory.Exists(job.Options.Source)&&!(job.Options.Engine==BackendEngine.XgdTool&&XgdToolService.IsGodDirectory(job.Options.Source,cts.Token)),cts.Token);
   if(directorySource)entries=await Task.Run(()=>EnumerateSource(job.Options.Source,cts.Token),cts.Token);
   else
   {
    entries=[];var gate=new object();
    // Content inspection never needs title/artwork lookups: always list offline.
    var options=BackendPolicy.Normalize(job.Options with {Mode=XisoMode.List,Destination="",Offline=true});
    var xgd=options.Engine==BackendEngine.XgdTool;
    var backend=(xgd?XgdToolService.FindBackend(XgdBackendPath):XisoProcessService.FindBackend(BackendPath))??throw new IOException("Detect the source backend in Settings, then refresh contents.");
    var args=xgd?XgdToolService.BuildArguments(options,""):XisoProcessService.BuildArguments(options,options.Source,"");
    var failed=false;var listed=false;
    var exit=await Process.RunAsync(backend,args,AppContext.BaseDirectory,(line,error)=>{lock(gate){if(XgdToolService.IsFailureLine(line))failed=true;if(line.Contains("Files in image:"))listed=true;if(XisoOutputParser.ParseListEntry(line) is {} entry&&entries.Count<100000)entries.Add(entry);}},cts.Token);
    if(exit!=0||failed||(xgd&&!listed))throw new IOException("The backend could not list this source. See the job log or retry after processing.");
   }
   cts.Token.ThrowIfCancellationRequested();if(SelectedJob!=job)return;
   job.Contents.Clear();foreach(var entry in entries)job.Contents.Add(entry);
   ContentStatus=$"{L.T("Source contents")} · {entries.Count:N0} {L.T("entries")}"+(entries.Count>=100000?" (display limit reached)":"");
   RefreshContentFilter();
  }
  catch(OperationCanceledException){if(SelectedJob==job&&inspection==cts)ContentStatus="Content inspection cancelled or timed out. Refresh to retry.";}
  catch(Exception ex){if(SelectedJob==job&&inspection==cts)ContentStatus=ex.Message;}
  finally{if(inspection==cts)inspection=null;}
 }
 private static List<IsoContentEntry> EnumerateSource(string root,CancellationToken token)
 {
  var result=new List<IsoContentEntry>();var pending=new Stack<string>();pending.Push(root);
  while(pending.Count>0&&result.Count<100000)
  {
   token.ThrowIfCancellationRequested();var directory=pending.Pop();
   foreach(var path in Directory.EnumerateFileSystemEntries(directory))
   {
    token.ThrowIfCancellationRequested();var attributes=File.GetAttributes(path);
    if((attributes&FileAttributes.ReparsePoint)!=0)continue;
    var isDirectory=(attributes&FileAttributes.Directory)!=0;
    result.Add(new(Path.GetRelativePath(root,path)+(isDirectory?"/":""),isDirectory?0:new FileInfo(path).Length,isDirectory));
    if(isDirectory)pending.Push(path);if(result.Count>=100000)break;
   }
  }
  return result;
 }
}
