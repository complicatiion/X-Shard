using XShard.Services;
using XShard.Models;
namespace XShard.ViewModels;
public sealed partial class MainViewModel
{
 private LegalDocument? selectedLegalDocument;
 public LegalDocument? SelectedLegalDocument {get=>selectedLegalDocument??Legal.Documents.FirstOrDefault();set {if(Set(ref selectedLegalDocument,value)){Changed(nameof(SelectedLegalMarkdown));Changed(nameof(IsGplDocument));}}}
 public string SelectedLegalMarkdown=>!Legal.DocumentsReady?LegalMarkdown:SelectedLegalDocument?.Markdown??LegalMarkdown;
 public bool IsGplDocument=>SelectedLegalDocument?.FileName=="GPL-3.0.md";
 public IReadOnlyList<LanguageOption> Languages=>LocalizationService.Instance.GetLanguages();
 /// <summary>Red while an output folder is required; otherwise the accent (resolved in the view).</summary>
 public bool OutputMissing=>NeedsOutput;
 public string OutputButtonBackground=>NeedsOutput?"#462B2E":"#318B3B";
 private bool relocating;
 public bool IsRelocating {get=>relocating;private set {if(Set(ref relocating,value))Changed(nameof(CanChangeDataLocation));}}
 public bool CanChangeDataLocation=>!IsRunning&&!IsRelocating;
 public async Task MoveDataLocationAsync(string target)
 {
  if(!CanChangeDataLocation)throw new InvalidOperationException("Stop the queue before changing its storage location.");
  IsRelocating=true;try {await SaveSettingsAsync();await SaveQueueAsync(true);await Storage.MoveDataDirectoryAsync(target);Changed(nameof(DataDirectory));Message=LocalizationService.Instance.T("Settings location changed");}finally{IsRelocating=false;}
 }
 public string CurrentLanguageName=>LocalizationService.Instance.CurrentLanguageName;
 public string[] ScaleModes=>["Auto","Small","Medium","Large","Custom"];
 private string uiScaleMode="Auto";
 public string UiScaleMode {get=>uiScaleMode;set {if(!ScaleModes.Contains(value))value="Auto";if(Set(ref uiScaleMode,value)){Changed(nameof(EffectiveScale));Changed(nameof(IsCustomScale));Changed(nameof(ScaleLabel));}}}
 private double customUiScale=100;
 public double CustomUiScale {get=>customUiScale;set {if(!double.IsFinite(value))return;if(Set(ref customUiScale,Math.Clamp(value,100,200))){Changed(nameof(EffectiveScale));Changed(nameof(ScaleLabel));}}}
 public double EffectiveScale=>UiScaleMode switch {"Small"=>0.90,"Medium"=>1,"Large"=>1.25,"Custom"=>CustomUiScale/100,_=>AutoScale};
 /// <summary>Logical (DPI-independent) workspace size at which Auto renders at 100%.</summary>
 public const double AutoReferenceWidth=UiScaling.ReferenceWidth,AutoReferenceHeight=UiScaling.ReferenceHeight,AutoMinimumScale=UiScaling.MinimumScale;
 private double autoScale=1;
 /// <summary>Auto zoom: 100% when the window offers the reference workspace, otherwise reduced in 5% steps (minimum 70%) so compact 11–14" displays fit without vertical scrolling.</summary>
 public double AutoScale=>autoScale;
 public static double ComputeAutoScale(double width,double height)=>UiScaling.ComputeAutoScale(width,height);
 /// <summary>Called by the window with its logical client size (Windows DPI is already applied by Avalonia).</summary>
 public void UpdateViewport(double width,double height)
 {
  if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)return;
  var next=ComputeAutoScale(width,height);if(Math.Abs(next-autoScale)<0.001)return;
  autoScale=next;Changed(nameof(AutoScale));
  if(UiScaleMode=="Auto"){Changed(nameof(EffectiveScale));Changed(nameof(ScaleLabel));}
 }
 public bool IsCustomScale=>UiScaleMode=="Custom";
 public string ScaleLabel=>$"{EffectiveScale*100:0}%";
 private bool detailsOuterScroll=true;
 public bool DetailsOuterScroll {get=>detailsOuterScroll;set=>Set(ref detailsOuterScroll,value);}
 public bool HasSelectedJob=>!string.IsNullOrWhiteSpace(SelectedJob?.Name);
 public string DetailsChevron=>DetailsExpanded?"ChevronUp":"ChevronDown";
 public string TestLabel=>LocalizationService.Instance.T(TestState);
 public string ProcessingFeedback
 {
  get
  {
   var job=Jobs.FirstOrDefault(j=>j.IsRunning);if(job==null)return "";
   var label=LocalizationService.Instance.T(job.Progress>0?"Processing image":"Analyzing / preparing image…");
   var elapsed=job.StartedAt is {} start?(DateTimeOffset.UtcNow-start).ToString(@"hh\:mm\:ss"):"00:00:00";
   return $"{label} · {elapsed}";
  }
 }
 private void RefreshLanguage()
 {
  foreach(var name in new[]{nameof(ContentStatus),nameof(SelectedLog),nameof(CurrentLanguageName),nameof(Modes),nameof(Platforms),nameof(Profiles),nameof(Formats),nameof(ScrubModes),nameof(ScaleModes),nameof(AddLabel),nameof(CompletedCount),nameof(WaitingCount),nameof(FailedCount),nameof(TotalCount),nameof(TotalProgressText),nameof(TestLabel),nameof(XisoDetectedText),nameof(XgdDetectedText),nameof(ProcessingFeedback),
   nameof(OutputTargets),nameof(StatusFilters),nameof(SortModes),nameof(ThemeModes),nameof(ReportModes),nameof(AfterQueueOptions),nameof(TitleFields),nameof(PreviewText),nameof(PreviewSpaceText),nameof(ReportSummary),nameof(PowerCountdownText)})Changed(name);
  foreach(var language in Languages)language.Refresh();
  foreach(var job in Jobs)job.RefreshLanguage();
 }
}
