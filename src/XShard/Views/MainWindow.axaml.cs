using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XShard.Models;
using XShard.Services;
using XShard.ViewModels;
namespace XShard.Views;
public partial class MainWindow : Window
{
 private readonly MainViewModel vm;
 private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(150)};
 private CancellationTokenSource? noticeCancellation;
 private bool closing;
 private static LocalizationService L=>LocalizationService.Instance;
 public MainWindow():this(new MainViewModel()) {}
 public MainWindow(MainViewModel model)
 {
  vm=model;
  InitializeComponent();DataContext=vm;
  ThemeService.Apply(vm.Theme,vm.Accent,this);
  vm.AppearanceChanged+=()=>ThemeService.Apply(vm.Theme,vm.Accent,this);
  vm.RunFinished+=OnRunFinished;
  vm.ResetRequested+=()=>{WindowsIntegration.UnregisterContextMenu();WindowsIntegration.UnregisterToasts();UpdateContextMenuCheck();};
  timer.Tick+=(_,_)=>{vm.DrainOutput();UpdateTaskbar();};timer.Start();
  SizeChanged+=(_,_)=>{vm.UpdateViewport(ClientSize.Width,ClientSize.Height);ResizeCards();};
  this.FindControl<Border>("JobCard")!.SizeChanged+=(_,_)=>ResizeCards();
  vm.PropertyChanged+=(_,args)=>
  {
   if(args.PropertyName is nameof(vm.DetailsExpanded) or nameof(vm.EffectiveScale) or nameof(vm.CurrentLanguageName))ResizeCards();
   if(args.PropertyName==nameof(vm.DetailsExpanded)&&vm.DetailsExpanded)RevealSelectedJob();
   if(args.PropertyName==nameof(vm.Message))
   {
    var visible=!string.IsNullOrWhiteSpace(vm.Message);this.FindControl<Border>("NoticeCard")!.IsVisible=visible;
    noticeCancellation?.Cancel();noticeCancellation?.Dispose();noticeCancellation=null;
    if(visible){noticeCancellation=new();_=DismissNoticeAfterDelayAsync(noticeCancellation.Token);}
   }
   if(args.PropertyName is nameof(vm.ShowLegal) or nameof(vm.ShowSettings) or nameof(vm.ShowAbout) or nameof(vm.ShowDetails) or nameof(vm.ShowReset) or nameof(vm.ShowConfirm) or nameof(vm.ShowPowerCountdown))UpdateBackdrop();
   if(args.PropertyName==nameof(vm.ShowConfirm)&&vm.ShowConfirm)Dispatcher.UIThread.Post(()=>this.FindControl<Button>("ConfirmNoButton")?.Focus());
   if(args.PropertyName==nameof(vm.ShowPowerCountdown)&&vm.ShowPowerCountdown)Dispatcher.UIThread.Post(()=>this.FindControl<Button>("PowerCancelButton")?.Focus());
   if(args.PropertyName==nameof(vm.ShowSettings)&&vm.ShowSettings)UpdateContextMenuCheck();
  };
  Opened+=async(_,_)=>
  {
   vm.UpdateViewport(ClientSize.Width,ClientSize.Height);ResizeCards();UpdateBackdrop();
   if(vm.ShowLegal)this.FindControl<Border>("LegalCard")?.Focus();
   await vm.TestBackendAsync();
  };
  AddHandler(DragDrop.DropEvent,OnDrop);
  AddHandler(DragDrop.DragOverEvent,OnDragOver);
  KeyDown+=OnKeyDown;
  Closing+=OnClosing;
 }
 private bool AnyOverlay=>vm.ShowLegal||vm.ShowSettings||vm.ShowAbout||vm.ShowDetails||vm.ShowReset||vm.ShowConfirm||vm.ShowPowerCountdown;
 private void UpdateBackdrop()
 {
  var surface=this.FindControl<Grid>("MainSurface")!;var overlay=AnyOverlay;
  surface.Effect=overlay?new BlurEffect {Radius=11}:null;surface.IsEnabled=!overlay;
 }
 /// <summary>
 /// Sizes the workspace to exactly viewport/scale logical units (manual zoom above the minimum layout scrolls instead),
 /// gives the inline drawer only the space left after the queue minimum, and falls back to the modal details when too small.
 /// </summary>
 private void ResizeCards()
 {
  var width=Math.Max(1,ClientSize.Width);var height=Math.Max(1,ClientSize.Height);var scale=vm.EffectiveScale;
  if(this.FindControl<Button>("BackendTestButton") is {} test){var longest=new[]{"Test Backend","Testing","Test Successful","Test Failed"}.Max(label=>new FormattedText(L.T(label),System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),14,Brushes.White).Width);test.Width=Math.Clamp(longest+58,170,240);}
  if(Resources["UiScaleTransform"] is ScaleTransform transform){transform.ScaleX=scale;transform.ScaleY=scale;}
  var logicalWidth=Math.Floor(Math.Max(width/scale,820));var logicalHeight=Math.Floor(Math.Max(height/scale,580));
  if(this.FindControl<Grid>("MainSurface") is {} surface){surface.Width=logicalWidth-surface.Margin.Left-surface.Margin.Right;surface.Height=logicalHeight-surface.Margin.Top-surface.Margin.Bottom;}
  void Size(string name,double maxWidth,double maxHeight,bool exact=true){if(this.FindControl<Border>(name) is {} card){card.Width=Math.Min(width-40,maxWidth*scale);card.MaxHeight=Math.Min(height-40,maxHeight*scale);if(exact)card.Height=card.MaxHeight;}}
  Size("DetailsCard",1080,760);Size("SettingsCard",1040,820,false);Size("AboutCard",540,720,false);Size("LegalCard",940,720);Size("ResetCard",560,340,false);Size("ConfirmCard",600,460,false);Size("PowerCard",560,300,false);
  if(this.FindControl<Grid>("SettingsColumns") is {} columns&&this.FindControl<StackPanel>("SettingsSecondary") is {} secondary&&this.FindControl<Border>("SettingsCard") is {} settingsCard)
  {
   var twoColumns=(settingsCard.Width-40)/scale>=760;
   if(columns.ColumnDefinitions.Count!=(twoColumns?2:1))columns.ColumnDefinitions=new ColumnDefinitions(twoColumns?"*,*":"*");
   Grid.SetColumn(secondary,twoColumns?1:0);Grid.SetRow(secondary,twoColumns?0:1);columns.RowSpacing=twoColumns?0:12;
  }
  var surfaceHeight=logicalHeight-40;
  if(this.FindControl<ScrollViewer>("JobOptionsScroll") is {} options)options.MaxHeight=Math.Clamp(surfaceHeight-440,150,430);
  if(this.FindControl<Border>("InlineCard") is {} drawer&&this.FindControl<Border>("JobCard") is {} job&&this.FindControl<Grid>("MainSurface") is {} main)
  {
   // The chevron always expands inline (never switches to the modal). The drawer takes its height from the queue
   // (queue keeps QueueMinimum); if even that does not fit, the workspace grows and the outer ScrollViewer scrolls.
   var available=surfaceHeight-HeaderHeight-job.Bounds.Height-FooterHeight-RowGaps-QueueMinimum;
   var drawerHeight=Math.Clamp(available,DrawerMinimum,Math.Max(DrawerMinimum,Math.Min(DrawerMaximum,surfaceHeight*0.45)));
   drawer.Height=drawerHeight;
   if(vm.DetailsExpanded&&available<DrawerMinimum)main.Height+=DrawerMinimum-available;
  }
 }
 private const double HeaderHeight=64+18,FooterHeight=40,RowGaps=3*12,QueueMinimum=120,DrawerMinimum=220,DrawerMaximum=420;
 /// <summary>After expanding, bring the selected job and the drawer into view.</summary>
 private void RevealSelectedJob()
 {
  Dispatcher.UIThread.Post(()=>
  {
   if(vm.SelectedJob is {} selected)this.FindControl<ListBox>("QueueList")?.ScrollIntoView(selected);
   // When the workspace had to grow, scroll the outer view so the opened drawer is visible.
   this.FindControl<Border>("InlineCard")?.BringIntoView();
  },DispatcherPriority.Background);
 }
 private void OpenDetailsModal(){_=vm.LoadContentsAsync();vm.ShowDetails=true;this.FindControl<Border>("DetailsCard")?.Focus();}

 // ---------- taskbar, notifications, power ----------
 private IntPtr Hwnd=>TryGetPlatformHandle()?.Handle??IntPtr.Zero;
 private TaskbarState lastTaskbarState=(TaskbarState)(-1);private double lastTaskbarValue=-1;
 private void UpdateTaskbar()
 {
  TaskbarState state;double value=0;
  if(vm.IsRunning){var running=vm.Jobs.FirstOrDefault(j=>j.IsRunning);var total=vm.Jobs.Count(j=>j.Status!=JobStatus.Waiting||j.IsRunning);
   var done=vm.Jobs.Count(j=>j.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled);
   value=total==0?0:100.0*(done+(running?.Progress??0)/100.0)/Math.Max(1,vm.Jobs.Count(j=>j.Status is not JobStatus.Interrupted));
   state=running is {ProgressUnknown:true}&&done==0?TaskbarState.Indeterminate:TaskbarState.Normal;}
  else if(lastTaskbarState==TaskbarState.Error)return; // keep the error marker of the last run until the next run
  else state=TaskbarState.None;
  if(state==lastTaskbarState&&Math.Abs(value-lastTaskbarValue)<0.5)return;
  lastTaskbarState=state;lastTaskbarValue=value;WindowsIntegration.SetTaskbarProgress(Hwnd,state,value);
 }
 private void OnRunFinished(RunReport report)
 {
  lastTaskbarState=report.Failed>0?TaskbarState.Error:TaskbarState.None;lastTaskbarValue=100;
  WindowsIntegration.SetTaskbarProgress(Hwnd,lastTaskbarState,100);
  if(!IsActive)WindowsIntegration.Flash(Hwnd);
  if(vm.NotificationsEnabled)
   WindowsIntegration.ShowToast(L.T("X-Shard queue finished"),$"{report.Succeeded} {L.T("Count completed")} · {report.Failed} {L.T("Count failed")} · {L.T("Space saved")} {RunReportService.FormatBytes(report.SavedBytes)}");
  if(vm.AfterQueue!="Nothing")_=vm.RunAfterQueueAsync(action=>action=="Hibernate"?WindowsIntegration.Hibernate():WindowsIntegration.Shutdown());
 }
 private void PowerCancel_Click(object? s,RoutedEventArgs e)=>vm.CancelPowerCountdown();
 private void PowerNow_Click(object? s,RoutedEventArgs e)=>vm.RunPowerActionNow();

 // ---------- single instance / command line (Explorer context menu) ----------
 /// <summary>Handles arguments of this or a forwarded start: --extract|--convert|--create|--add &lt;path&gt;, or plain paths.</summary>
 public async void HandleCommandLine(string[] args)
 {
  if(WindowState==WindowState.Minimized)WindowState=WindowState.Maximized;
  Activate();Topmost=true;Topmost=false;
  if(args.Length==0)return;
  XisoMode? requested=null;var paths=new List<string>();
  foreach(var arg in args)
  {
   switch(arg.ToLowerInvariant())
   {
    case "--extract":requested=XisoMode.Extract;break;
    case "--convert":requested=XisoMode.Convert;break;
    case "--create":requested=XisoMode.Create;break;
    case "--list":requested=XisoMode.List;break;
    case "--add":break;
    default:if(!arg.StartsWith("--")&&(File.Exists(arg)||Directory.Exists(arg)))paths.Add(Path.GetFullPath(arg));break;
   }
  }
  if(paths.Count==0)return;
  await Guard(async()=>
  {
   if(vm.ShowLegal){vm.Message=L.T("Review and accept the legal documents first.");return;}
   if(vm.IsRunning){vm.Message=L.T("Jobs can be added after the current queue run.");}
   if(requested is {} mode)
   {
    if(mode==XisoMode.Convert&&!vm.UsesXgd)vm.Advanced=true;
    vm.Mode=mode;
   }
   if(vm.NeedsOutput)
   {
    var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title=L.T("Choose output folder")});
    if(folders.Count==0||folders[0].TryGetLocalPath() is not {} output)return;
    vm.OutputTarget=OutputTarget.Custom;vm.OutputDirectory=output;
   }
   await AddPathsAsync(paths);
  });
 }
 private async Task AddPathsAsync(IEnumerable<string> paths)
 {
  foreach(var path in paths)
  {
   if(Directory.Exists(path)&&vm.Mode!=XisoMode.Create&&!(vm.UsesXgd&&TitleInfoReader.FindStfsHeader(path)!=null))
   {var images=await Task.Run(()=>FolderBatchService.EnumerateImages(path,vm.RecursiveFolders,vm.UsesXgd,CancellationToken.None));vm.AddSources(images.Count>0?images:[path]);}
   else vm.AddSources([path]);
  }
  await vm.SaveQueueSafelyAsync();
 }

 // ---------- settings helpers ----------
 private void UpdateContextMenuCheck()
 {
  if(this.FindControl<CheckBox>("ContextMenuCheck") is not {} check)return;
  var allUsers=WindowsIntegration.IsContextMenuInstalledForAllUsers();
  check.IsChecked=allUsers||WindowsIntegration.IsContextMenuRegistered();check.IsEnabled=OperatingSystem.IsWindows()&&!allUsers;
 }
 private void ContextMenu_Click(object? s,RoutedEventArgs e)
 {
  if(s is not CheckBox check)return;
  if(check.IsChecked==true){if(!WindowsIntegration.RegisterContextMenu(L.T("Extract with X-Shard"),L.T("Convert with X-Shard"),L.T("Create XISO with X-Shard")))vm.Message=L.T("The context menu could not be registered.");}
  else WindowsIntegration.UnregisterContextMenu();
  UpdateContextMenuCheck();
 }
 private void Accent_Click(object? s,RoutedEventArgs e){if(s is Button {Tag:string name})vm.Accent=name;}
 private async void BrowseSetting_Click(object? s,RoutedEventArgs e)=>await Guard(async()=>
 {
  if(s is not Button {Tag:string target})return;
  if(target is "XemuPath" or "XeniaPath")
  {
   var files=await StorageProvider.OpenFilePickerAsync(new(){Title=L.T(target=="XemuPath"?"Xemu executable":"Xenia executable"),FileTypeFilter=[new(L.T("Executable")){Patterns=["*.exe"]}]});
   if(files.Count==0||files[0].TryGetLocalPath() is not {} file)return;
   if(target=="XemuPath")vm.XemuPath=file;else vm.XeniaPath=file;
   return;
  }
  var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title=L.T("Choose folder")});
  if(folders.Count==0||folders[0].TryGetLocalPath() is not {} folder)return;
  switch(target){case "XemuLibrary":vm.XemuLibrary=folder;break;case "XeniaLibrary":vm.XeniaLibrary=folder;break;case "RetroBatRoot":vm.RetroBatRoot=folder;break;case "BatoceraShare":vm.BatoceraShare=folder;break;}
 });
 private void OpenFolder(string folder){try{Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){vm.ReportError(ex);}}
 private void OpenLogs_Click(object? s,RoutedEventArgs e)=>OpenFolder(vm.LogsDirectory);
 private void OpenReports_Click(object? s,RoutedEventArgs e)=>OpenFolder(vm.ReportsDirectory);
 private async void ExportReport_Click(object? s,RoutedEventArgs e)=>await Guard(async()=>
 {
  if(vm.LastReport is not {} report)return;
  var file=await StorageProvider.SaveFilePickerAsync(new(){Title=L.T("Save run report"),SuggestedFileName=Path.GetFileNameWithoutExtension(RunReportService.DefaultFileName(report,"Html")),DefaultExtension="html",
   FileTypeChoices=[new(L.T("HTML report")){Patterns=["*.html"]},new(L.T("TXT report")){Patterns=["*.txt"]}]});
  if(file?.TryGetLocalPath() is {} path)await vm.ExportReportAsync(path);
 });
 private void Launch_Click(object? s,RoutedEventArgs e)=>vm.LaunchSelected();

 // ---------- existing actions ----------
 private async void DataLocation_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>{if(!vm.CanChangeDataLocation)return;var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title=L.T("Choose settings location")});if(folders.Count>0&&folders[0].TryGetLocalPath() is {} path)await vm.MoveDataLocationAsync(path);});
 private void Language_Click(object? sender,RoutedEventArgs e){if(sender is Button {Tag:string language})L.SetLanguage(language);}
 private void DismissBackdrop_Pressed(object? sender,PointerPressedEventArgs e)
 {
  if(vm.ShowLegal||sender is not Border backdrop)return;
  var cards=new[]{"DetailsCard","SettingsCard","AboutCard","ResetCard"}.Select(name=>this.FindControl<Border>(name)).ToArray();
  if(e.Source is Visual source&&(cards.Contains(source as Border)||source.GetVisualAncestors().OfType<Border>().Any(b=>cards.Contains(b))))return;
  if(backdrop.Name=="DetailsBackdrop")vm.ShowDetails=false;
  if(backdrop.Name=="SettingsBackdrop")vm.ShowSettings=false;
  if(backdrop.Name=="AboutBackdrop")vm.ShowAbout=false;
  if(backdrop.Name=="ResetBackdrop")vm.ShowReset=false;
  e.Handled=true;
 }
 private void ToggleDetails_Click(object? s,RoutedEventArgs e){vm.DetailsExpanded=!vm.DetailsExpanded;e.Handled=true;}
 private async void RefreshContents_Click(object? s,RoutedEventArgs e)=>await vm.LoadContentsAsync();
 private void Reset_Click(object? s,RoutedEventArgs e)=>vm.ShowReset=true;
 private void CancelReset_Click(object? s,RoutedEventArgs e)=>vm.ShowReset=false;
 private async void ConfirmReset_Click(object? s,RoutedEventArgs e)=>await Guard(vm.ResetAppAsync);
 private async Task DismissNoticeAfterDelayAsync(CancellationToken cancellation)
 {
  try{await Task.Delay(TimeSpan.FromSeconds(5),cancellation).ConfigureAwait(false);Dispatcher.UIThread.Post(()=>{if(!cancellation.IsCancellationRequested)vm.Message="";});}
  catch(OperationCanceledException){}
 }
 private void DismissNotice_Click(object? s,RoutedEventArgs e)=>vm.Message="";
 private void OpenOutput_Click(object? s,RoutedEventArgs e)
 {
  if(s is Button {DataContext:XisoJob job}&&job.CanOpenOutput)
   try{Process.Start(new ProcessStartInfo(job.OutputFolder){UseShellExecute=true});}catch(Exception ex){vm.ReportError(ex);}
  e.Handled=true;
 }
 private async Task Guard(Func<Task> operation){try{await operation();}catch(Exception ex){vm.ReportError(ex);}}
 private static FilePickerFileType IsoType=>new("Xbox ISO images"){Patterns=["*.iso","*.xiso","*.cci","*.cso","*.zar"]};
 private async void Add_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>
 {
  if(vm.Mode==XisoMode.Create)
  {
   var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title=L.T("Choose source folder"),AllowMultiple=true});
   vm.AddSources(folders.Select(f=>f.TryGetLocalPath()).OfType<string>());
  }
  else
  {
   var files=await StorageProvider.OpenFilePickerAsync(new(){Title=L.T("Add ISO images"),AllowMultiple=true,FileTypeFilter=[IsoType,FilePickerFileTypes.All]});
   vm.AddSources(files.Select(f=>f.TryGetLocalPath()).OfType<string>());
  }
  await vm.SaveQueueSafelyAsync();
 });
 private async void Output_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>
 {var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title=L.T("Choose output folder")});if(folders.Count>0&&folders[0].TryGetLocalPath() is {} path)vm.OutputDirectory=path;});
 private async void Backend_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>
 {var files=await StorageProvider.OpenFilePickerAsync(new(){Title=L.T("Choose extract-xiso.exe"),FileTypeFilter=[new(L.T("Executable")){Patterns=["*.exe"]}]});if(files.Count>0&&files[0].TryGetLocalPath() is {} path){vm.BackendPath=path;await vm.TestBackendAsync();}});
 private async void ExportLog_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>
 {
  if(vm.SelectedJob is not {} job)return;
  vm.DrainOutput();
  var file=await StorageProvider.SaveFilePickerAsync(new(){Title=L.T("Export job log"),SuggestedFileName=job.Name+".txt",DefaultExtension="txt",FileTypeChoices=[new(L.T("Text file")){Patterns=["*.txt"]}]});
  if(file!=null){await using var stream=await file.OpenWriteAsync();stream.SetLength(0);await using var writer=new StreamWriter(stream);await writer.WriteAsync(job.Log);vm.Message=L.T("Log exported");}
 });
 private async void BatchFolder_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>
 {
  var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title=L.T("Choose folder containing ISO images"),AllowMultiple=true});
  foreach(var folder in folders)
   if(folder.TryGetLocalPath() is {} path){var images=await Task.Run(()=>FolderBatchService.EnumerateImages(path,vm.RecursiveFolders,vm.UsesXgd,CancellationToken.None));vm.AddSources(images);}
  await vm.SaveQueueSafelyAsync();
 });
 private async void GameFolder_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>
 {
  var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title=L.T("Choose extracted game or GoD folder"),AllowMultiple=true});
  vm.AddSources(folders.Select(f=>f.TryGetLocalPath()).OfType<string>());await vm.SaveQueueSafelyAsync();
 });
 private async void XgdBackend_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>
 {
  var files=await StorageProvider.OpenFilePickerAsync(new(){Title=L.T("Choose XGDTool CLI"),FileTypeFilter=[new(L.T("Executable")){Patterns=["*.exe"]}]});
  if(files.Count>0&&files[0].TryGetLocalPath() is {} path){vm.XgdBackendPath=path;await vm.TestBackendAsync();}
 });
 private async void Accept_Click(object? sender,RoutedEventArgs e)=>await Guard(async()=>{await vm.AcceptLegalAsync();UpdateBackdrop();});
 private void Decline_Click(object? sender,RoutedEventArgs e){closing=true;timer.Stop();Close();}
 private void Details_Click(object? s,RoutedEventArgs e)=>OpenDetailsModal();
 private void CloseDetails_Click(object? s,RoutedEventArgs e)=>vm.ShowDetails=false;
 private void Settings_Click(object? s,RoutedEventArgs e){if(vm.IsRunning){vm.Message=L.T("Stop the queue before changing settings.");return;}vm.ShowSettings=true;this.FindControl<Border>("SettingsCard")?.Focus();}
 private void About_Click(object? s,RoutedEventArgs e){vm.ShowAbout=true;this.FindControl<Border>("AboutCard")?.Focus();}
 private void CloseSettings_Click(object? s,RoutedEventArgs e)=>vm.ShowSettings=false;
 private void CloseAbout_Click(object? s,RoutedEventArgs e)=>vm.ShowAbout=false;
 private void XgdRepo_Click(object? sender,RoutedEventArgs e)=>OpenLink("https://github.com/wiredopposite/XGDTool");
 private void Website_Click(object? s,RoutedEventArgs e)=>OpenLink("https://sksdesign.de/");
 private void GitHub_Click(object? s,RoutedEventArgs e)=>OpenLink("https://github.com/complicatiion/X-Shard");
 private void XboxDev_Click(object? s,RoutedEventArgs e)=>OpenLink("https://github.com/XboxDev/extract-xiso");
 // External links never open directly: the view model asks for an explicit Yes first.
 private async void OpenLink(string url)=>await Guard(()=>vm.OpenExternalLinkAsync(url));
 private void ConfirmYes_Click(object? s,RoutedEventArgs e){vm.ResolveConfirmation(true);e.Handled=true;}
 private void ConfirmNo_Click(object? s,RoutedEventArgs e){vm.ResolveConfirmation(false);e.Handled=true;}
 private void DismissConfirm_Pressed(object? sender,PointerPressedEventArgs e)
 {
  var card=this.FindControl<Border>("ConfirmCard");
  if(e.Source is Visual source&&(source==card||source.GetVisualAncestors().Contains(card)))return;
  vm.ResolveConfirmation(false);e.Handled=true;
 }
 private void OnKeyDown(object? sender,KeyEventArgs e)
 {
  if(vm.ShowConfirm){if(e.Key==Key.Escape){vm.ResolveConfirmation(false);e.Handled=true;}return;}
  if(vm.ShowPowerCountdown){if(e.Key==Key.Escape){vm.CancelPowerCountdown();e.Handled=true;}return;}
  if(vm.ShowLegal){if(e.Key==Key.Escape)e.Handled=true;return;}
  if(e.Key==Key.Escape){if(vm.ShowReset){vm.ShowReset=false;e.Handled=true;return;}vm.ShowAbout=false;vm.ShowSettings=false;vm.ShowDetails=false;e.Handled=true;}
  if(e.Key==Key.F&&e.KeyModifiers.HasFlag(KeyModifiers.Control)&&!AnyOverlay){this.FindControl<TextBox>("QueueSearchBox")?.Focus();e.Handled=true;}
  if(e.Key==Key.Delete&&!AnyOverlay&&FocusManager?.GetFocusedElement() is not TextBox&&vm.RemoveCommand.CanExecute(null)){vm.RemoveCommand.Execute(null);e.Handled=true;}
 }
 private void OnDragOver(object? sender,DragEventArgs e)
 {e.DragEffects=!AnyOverlay&&e.DataTransfer.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
 private async void OnDrop(object? sender,DragEventArgs e)=>await Guard(async()=>
 {
  if(AnyOverlay)return;
  if(e.DataTransfer.TryGetFiles() is {} files)await AddPathsAsync(files.Select(f=>f.TryGetLocalPath()).OfType<string>().ToList());
  e.Handled=true;
 });
 private async void OnClosing(object? sender,WindowClosingEventArgs e)
 {
  if(closing)return;e.Cancel=true;
  if(vm.IsRunning){vm.Message=L.T("Cancel the current job or stop the queue before closing X-Shard.");return;}
  await Guard(async()=>{await vm.SaveSettingsAsync();await vm.SaveQueueSafelyAsync();});
  vm.CancelInspection();vm.CancelPowerCountdown();noticeCancellation?.Cancel();noticeCancellation?.Dispose();timer.Stop();
  WindowsIntegration.SetTaskbarProgress(Hwnd,TaskbarState.None,0);closing=true;Close();
 }
}
