using Avalonia.Threading;
using XShard.Models;
using XShard.Services;
namespace XShard.ViewModels;
/// <summary>
/// Consent: X-Shard never contacts external services on its own.
/// Opening a website and enabling XGDTool online lookups both require an explicit Yes in a confirmation card.
/// Online consent is granted per session only; every start of X-Shard begins offline.
/// </summary>
public sealed partial class MainViewModel
{
 private TaskCompletionSource<bool>? confirmation;
 private bool showConfirm;
 public bool ShowConfirm {get=>showConfirm;private set=>Set(ref showConfirm,value);}
 private string confirmTitle="",confirmMessage="",confirmDetail="";
 public string ConfirmTitle {get=>confirmTitle;private set=>Set(ref confirmTitle,value);}
 public string ConfirmMessage {get=>confirmMessage;private set=>Set(ref confirmMessage,value);}
 public string ConfirmDetail {get=>confirmDetail;private set {if(Set(ref confirmDetail,value))Changed(nameof(HasConfirmDetail));}}
 public bool HasConfirmDetail=>!string.IsNullOrWhiteSpace(ConfirmDetail);
 private string confirmIcon="Shield";
 public string ConfirmIcon {get=>confirmIcon;private set=>Set(ref confirmIcon,value);}
 private bool onlineApproved;
 /// <summary>True after the user explicitly allowed online backend lookups in this session.</summary>
 public bool OnlineApproved {get=>onlineApproved;private set=>Set(ref onlineApproved,value);}
 /// <summary>Shows the Yes/No card. Any earlier pending request is answered with No.</summary>
 public Task<bool> ConfirmAsync(string title,string message,string detail="",string icon="Shield")
 {
  confirmation?.TrySetResult(false);
  var pending=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);confirmation=pending;
  ConfirmTitle=title;ConfirmMessage=message;ConfirmDetail=detail;ConfirmIcon=icon;ShowConfirm=true;
  return pending.Task;
 }
 public void ResolveConfirmation(bool accepted)
 {
  var pending=confirmation;confirmation=null;ShowConfirm=false;pending?.TrySetResult(accepted);
 }
 private async Task RequestOnlineModeAsync()
 {
  var loc=LocalizationService.Instance;
  if(await ConfirmAsync(loc.T("Allow online access?"),loc.T("online_mode_consent_message"),"Google · GitHub · XboxUnity")){OnlineApproved=true;offline=false;}
  Changed(nameof(Offline));
 }
 /// <summary>Opens an http(s) link in the default browser only after an explicit Yes.</summary>
 public async Task OpenExternalLinkAsync(string url)
 {
  if(!Uri.TryCreate(url?.Trim(),UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))return;
  var loc=LocalizationService.Instance;
  if(!await ConfirmAsync(loc.T("Open external website?"),loc.T("external_link_consent_message"),uri.AbsoluteUri,"Globe"))return;
  try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(Exception ex){ReportError(ex);}
 }
 /// <summary>Queued jobs may carry online mode from an earlier session. They need consent again before they run; No runs them offline.</summary>
 private async Task ConfirmQueuedOnlineJobsAsync()
 {
  var online=Jobs.Where(j=>j.Status==JobStatus.Waiting&&j.Options.Engine==BackendEngine.XgdTool&&!j.Options.Offline).ToList();
  if(online.Count==0||OnlineApproved)return;
  var loc=LocalizationService.Instance;
  if(await ConfirmAsync(loc.T("Allow online access?"),string.Format(loc.T("queued_online_jobs_consent_message"),online.Count),"Google · GitHub · XboxUnity")){OnlineApproved=true;return;}
  foreach(var job in online)
  {
   job.Options=job.Options with {Offline=true};
   job.Log+="[X-Shard] Online access was not permitted. This job runs in offline mode."+Environment.NewLine;
  }
  Changed(nameof(SelectedLog));queueDirty=true;
 }
 private void RevertOfflineToggle()=>Dispatcher.UIThread.Post(()=>Changed(nameof(Offline)));
}
