using Avalonia.Threading;
using XShard.Models;
using XShard.Services;
namespace XShard.ViewModels;
/// <summary>
/// v0.2.2: update check for X-Shard (GitHub releases of complicatiion/X-Shard) and opt-in emulator downloads
/// (Xenia Canary, xemu) from their official GitHub releases. Nothing is contacted without an explicit Yes; X-Shard
/// never bundles emulators or system files and never installs X-Shard updates by itself.
/// </summary>
public sealed partial class MainViewModel
{
 internal GitHubReleaseClient GitHub {get;set;}=new();
 /// <summary>Always follows the current data folder (it can be moved in Settings).</summary>
 public EmulatorInstaller Emulators=>new(GitHub,Path.Combine(Storage.DataDirectory,"emulators"));

 // ---------- updates ----------
 private bool updateConsent,updateAutoCheck,updateNotify=true,updateAvailable,checkingUpdates;
 private DateTimeOffset? lastUpdateCheck;private string latestRelease="",updateStatus="",updateNotes="",updateUrl=UpdateService.ReleasesPage;
 public string CurrentVersionText=>ReleaseVersion.Current.ToString(3);
 /// <summary>Automatic check at start (max. once per 24 h). Switching it on asks for consent first.</summary>
 public bool UpdateAutoCheck
 {
  get=>updateAutoCheck;
  set
  {
   if(value&&!updateConsent){_=EnableAutoCheckAsync();return;}
   Set(ref updateAutoCheck,value);
  }
 }
 public bool UpdateNotify {get=>updateNotify;set {if(Set(ref updateNotify,value))Changed(nameof(ShowUpdateBadge));}}
 public bool UpdateAvailable {get=>updateAvailable;private set {if(Set(ref updateAvailable,value))Changed(nameof(ShowUpdateBadge));}}
 public bool ShowUpdateBadge=>UpdateAvailable&&UpdateNotify;
 public bool CheckingUpdates {get=>checkingUpdates;private set=>Set(ref checkingUpdates,value);}
 public string UpdateStatus {get=>updateStatus;private set=>Set(ref updateStatus,value);}
 public string UpdateNotes {get=>updateNotes;private set {if(Set(ref updateNotes,value))Changed(nameof(HasUpdateNotes));}}
 public bool HasUpdateNotes=>UpdateAvailable&&!string.IsNullOrWhiteSpace(UpdateNotes);
 public AsyncRelayCommand? CheckUpdatesCommand {get;private set;}
 public AsyncRelayCommand? OpenReleasePageCommand {get;private set;}
 /// <summary>Raised when a check found a newer release and notifications are on (the window shows a Windows notification).</summary>
 public event Action<UpdateResult>? UpdateFound;

 private void LoadUpdateSettings(AppSettings s)
 {
  updateConsent=s.UpdateConsent;updateAutoCheck=s.UpdateAutoCheck&&s.UpdateConsent;updateNotify=s.UpdateNotify;
  lastUpdateCheck=s.LastUpdateCheck;latestRelease=s.LatestRelease??"";skipExistingGames=s.SkipExistingGames;
  updateAvailable=ReleaseVersion.TryParse(latestRelease,out var known,out var pre)&&!pre&&known>ReleaseVersion.Current;
  updateStatus=DescribeUpdateState();
 }
 private void StoreUpdateSettings(AppSettings s)
 {
  s.UpdateConsent=updateConsent;s.UpdateAutoCheck=UpdateAutoCheck;s.UpdateNotify=UpdateNotify;s.LastUpdateCheck=lastUpdateCheck;s.LatestRelease=latestRelease;s.SkipExistingGames=SkipExistingGames;
 }
 private void ResetUpdateSettings()
 {
  updateConsent=false;UpdateAutoCheck=false;UpdateNotify=true;lastUpdateCheck=null;latestRelease="";UpdateAvailable=false;UpdateNotes="";SkipExistingGames=true;UpdateStatus=DescribeUpdateState();
 }
 private string DescribeUpdateState()
 {
  if(lastUpdateCheck is not {} checkedAt)return L.T("Not checked yet");
  var when=checkedAt.ToLocalTime().ToString("g");
  return UpdateAvailable?string.Format(L.T("update_available_status"),latestRelease,when):string.Format(L.T("update_current_status"),when);
 }
 private void InitializeUpdates()
 {
  CheckUpdatesCommand=new AsyncRelayCommand(()=>CheckForUpdatesAsync(true),ReportError,()=>!CheckingUpdates);
  OpenReleasePageCommand=new AsyncRelayCommand(()=>OpenExternalLinkAsync(updateUrl),ReportError);
  OpenEmulatorPageCommand=new AsyncRelayCommand<string>(id=>OpenExternalLinkAsync(SourceFor(id).ProjectUrl),ReportError);
  DownloadEmulatorCommand=new AsyncRelayCommand<string>(id=>DownloadEmulatorAsync(SourceFor(id)),ReportError,_=>!EmulatorBusy);
  CancelEmulatorCommand=new RelayCommand(()=>emulatorCancel?.Cancel(),()=>EmulatorBusy);
  RefreshEmulatorState();
  if(updateConsent&&updateAutoCheck&&(lastUpdateCheck==null||DateTimeOffset.Now-lastUpdateCheck>TimeSpan.FromHours(24)))_=AutoCheckAsync();
 }
 private async Task AutoCheckAsync()
 {
  try{await Task.Delay(TimeSpan.FromSeconds(10));await CheckForUpdatesAsync(false);}
  catch(Exception ex){UpdateStatus=L.T("Update check failed")+": "+ex.Message;}
 }
 private async Task<bool> ConfirmUpdateConsentAsync()
 {
  if(updateConsent)return true;
  if(!await ConfirmAsync(L.T("Check for updates?"),L.T("update_consent_message"),"github.com/"+UpdateService.Owner+"/"+UpdateService.Repository,"Globe"))return false;
  updateConsent=true;await SaveSettingsAsync();return true;
 }
 private async Task EnableAutoCheckAsync()
 {
  if(await ConfirmUpdateConsentAsync()){Set(ref updateAutoCheck,true,nameof(UpdateAutoCheck));await SaveSettingsAsync();await CheckForUpdatesAsync(false);}
  else Dispatcher.UIThread.Post(()=>Changed(nameof(UpdateAutoCheck)));
 }
 /// <summary>Asks GitHub for the latest release. <paramref name="manual"/> asks for consent if not given yet.</summary>
 public async Task CheckForUpdatesAsync(bool manual)
 {
  if(!updateConsent&&(!manual||!await ConfirmUpdateConsentAsync()))return;
  if(CheckingUpdates)return;
  CheckingUpdates=true;CheckUpdatesCommand?.Refresh();UpdateStatus=L.T("Checking for updates …");
  try
  {
   var result=await UpdateService.CheckAsync(GitHub,ReleaseVersion.Current,CancellationToken.None);
   lastUpdateCheck=result.CheckedAt;latestRelease=result.Latest;updateUrl=result.Url;
   UpdateAvailable=result.Available;UpdateNotes=result.Available?result.Notes:"";
   UpdateStatus=DescribeUpdateState();
   if(result.Available&&UpdateNotify)UpdateFound?.Invoke(result);
   await SaveSettingsAsync();
  }
  catch(Exception ex) when(ex is IOException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
  {
   UpdateStatus=L.T("Update check failed")+": "+ex.Message;
  }
  finally{CheckingUpdates=false;CheckUpdatesCommand?.Refresh();}
 }

 // ---------- emulator downloads ----------
 private bool emulatorBusy;private double emulatorProgress;private string xeniaState="",xemuState="",emulatorMessage="";
 public bool EmulatorBusy {get=>emulatorBusy;private set {if(Set(ref emulatorBusy,value)){DownloadEmulatorCommand?.Refresh();CancelEmulatorCommand?.Refresh();}}}
 private CancellationTokenSource? emulatorCancel;
 public RelayCommand? CancelEmulatorCommand {get;private set;}
 public double EmulatorProgress {get=>emulatorProgress;private set=>Set(ref emulatorProgress,value);}
 public string XeniaState {get=>xeniaState;private set=>Set(ref xeniaState,value);}
 public string XemuState {get=>xemuState;private set=>Set(ref xemuState,value);}
 public string EmulatorMessage {get=>emulatorMessage;private set=>Set(ref emulatorMessage,value);}
 public string XeniaFolder=>Emulators.FolderFor(EmulatorCatalog.Xenia);
 public string XemuFolder=>Emulators.FolderFor(EmulatorCatalog.Xemu);
 public string XemuSystemFolder=>Emulators.XemuSystemFolder;
 public AsyncRelayCommand<string>? DownloadEmulatorCommand {get;private set;}
 public AsyncRelayCommand<string>? OpenEmulatorPageCommand {get;private set;}
 private static EmulatorSource SourceFor(string? id)=>id=="xemu"?EmulatorCatalog.Xemu:EmulatorCatalog.Xenia;
 private void RefreshEmulatorState()
 {
  string Describe(EmulatorSource s)=>Emulators.InstalledTag(s) is {Length:>0} tag?string.Format(L.T("emulator_installed_status"),tag):L.T("Not downloaded");
  XeniaState=Describe(EmulatorCatalog.Xenia);XemuState=Describe(EmulatorCatalog.Xemu);
 }
 /// <summary>Downloads or updates an emulator from its official GitHub releases after an explicit Yes (shows project, license and source).</summary>
 public async Task DownloadEmulatorAsync(EmulatorSource source)
 {
  if(EmulatorBusy)return;
  var detail=$"{source.RepositoryUrl}\n{L.T("License")}: {source.License} · {source.ProjectUrl}";
  if(!await ConfirmAsync(string.Format(L.T("Download {0}?"),source.DisplayName),string.Format(L.T("emulator_download_consent_message"),source.DisplayName,source.License),detail,"Download"))return;
  using var cancel=new CancellationTokenSource();emulatorCancel=cancel;
  EmulatorBusy=true;EmulatorProgress=0;EmulatorMessage=L.T("Checking for updates …");
  try
  {
   var release=await Emulators.GetLatestAsync(source,cancel.Token);
   if(Emulators.InstalledTag(source)==release.Tag&&Emulators.InstalledExe(source) is {} current)
   {
    EmulatorMessage=string.Format(L.T("emulator_up_to_date"),source.DisplayName,release.Tag);SetEmulatorPath(source,current);return;
   }
   // Without a published checksum the file cannot be verified: ask again instead of installing silently.
   if(EmulatorCatalog.PickAsset(source,release) is {Sha256:null}&&!await ConfirmAsync(L.T("No checksum published"),string.Format(L.T("no_checksum_message"),source.DisplayName,release.Tag),source.RepositoryUrl,"Warning"))
   {EmulatorMessage=L.T("Download cancelled.");return;}
   var exe=await Emulators.InstallAsync(source,release,text=>Dispatcher.UIThread.Post(()=>EmulatorMessage=text),percent=>Dispatcher.UIThread.Post(()=>EmulatorProgress=percent),cancel.Token);
   SetEmulatorPath(source,exe);
   EmulatorMessage=string.Format(L.T("emulator_installed_message"),source.DisplayName,release.Tag)+(source.Id=="xemu"?" "+L.T("xemu_system_files_hint"):"");
   await SaveSettingsAsync();
  }
  catch(OperationCanceledException) when(cancel.IsCancellationRequested){EmulatorMessage=L.T("Download cancelled.");}
  catch(Exception ex) when(ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
  {
   EmulatorMessage=L.T("Download failed")+": "+ex.Message;
  }
  finally{emulatorCancel=null;EmulatorBusy=false;RefreshEmulatorState();}
 }
 private void SetEmulatorPath(EmulatorSource source,string exe){if(source.Id=="xemu")XemuPath=exe;else XeniaPath=exe;}

 // ---------- skip existing games ----------
 private bool skipExistingGames=true;
 /// <summary>Skip sources whose game already exists in the output folder. Probably scrubbed GoD packages never count.</summary>
 public bool SkipExistingGames {get=>skipExistingGames;set {if(Set(ref skipExistingGames,value))libraryIndex.Clear();}}
}
