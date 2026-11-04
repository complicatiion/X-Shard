using System.Net;
using System.Text;
using XShard.Models;
namespace XShard.Services;
public sealed record RunReportEntry(string Name,string Operation,string Console,string Status,string Result,string Source,string Output,TimeSpan Duration,long? SourceBytes,long? OutputBytes,string? Title)
{
 /// <summary>Source minus output size; positive values are space saved (scrub/compression).</summary>
 public long? SavedBytes=>SourceBytes is {} s&&OutputBytes is {} o?s-o:null;
}
public sealed record RunReport(DateTimeOffset Started,DateTimeOffset Finished,string Version,IReadOnlyList<RunReportEntry> Entries)
{
 public int Succeeded=>Entries.Count(e=>e.Status==nameof(JobStatus.Succeeded));
 public int Failed=>Entries.Count(e=>e.Status==nameof(JobStatus.Failed));
 public int Cancelled=>Entries.Count(e=>e.Status==nameof(JobStatus.Cancelled));
 public long SavedBytes=>Entries.Where(e=>e.Status==nameof(JobStatus.Succeeded)).Sum(e=>Math.Max(0,e.SavedBytes??0));
}
/// <summary>Builds and writes per-run reports (TXT or self-contained HTML without external resources).</summary>
public static class RunReportService
{
 public static RunReport Build(DateTimeOffset started,DateTimeOffset finished,IEnumerable<XisoJob> jobs,string version)
 {
  var entries=jobs.Select(job=>new RunReportEntry(job.Name,job.Options.Mode.ToString(),job.PlatformText,job.Status.ToString(),job.Result,job.Options.Source,
   job.PublishedPath??job.Options.Destination,job.Duration??TimeSpan.Zero,MeasureSource(job.Options),
   job.Status==JobStatus.Succeeded&&job.Options.Mode!=XisoMode.List?Measure(job.PublishedPath??job.Options.Destination):null,
   job.Title?.Summary is {Length:>0} t?t:null)).ToList();
  return new RunReport(started,finished,version,entries);
 }
 /// <summary>Size of the source, including later parts of split images (name.1.iso, name.2.iso, …).</summary>
 public static long? MeasureSource(XisoJobOptions options)
 {
  var source=options.Source;
  if(Directory.Exists(source))return Measure(source);
  if(!File.Exists(source))return null;
  long total=new FileInfo(source).Length;
  var stem=Path.GetFileNameWithoutExtension(source);var dot=stem.LastIndexOf('.');
  if(dot>0&&stem[(dot+1)..]=="1")
  {
   var basis=stem[..dot];var extension=Path.GetExtension(source);var folder=Path.GetDirectoryName(source)!;
   for(var part=2;part<100;part++){var next=Path.Combine(folder,$"{basis}.{part}{extension}");if(!File.Exists(next))break;total+=new FileInfo(next).Length;}
  }
  return total;
 }
 public static long? Measure(string path)
 {
  try
  {
   if(File.Exists(path))return new FileInfo(path).Length;
   if(!Directory.Exists(path))return null;
   var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
   return new DirectoryInfo(path).EnumerateFiles("*",options).Sum(f=>f.Length);
  }
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return null;}
 }
 public static string FormatBytes(long? bytes)
 {
  if(bytes is not {} value)return "—";
  var sign=value<0?"-":"";double v=Math.Abs(value);string[] units=["B","KB","MB","GB","TB"];var unit=0;
  while(v>=1024&&unit<units.Length-1){v/=1024;unit++;}
  return $"{sign}{v:0.##} {units[unit]}";
 }
 public static string ToText(RunReport report)
 {
  var b=new StringBuilder();
  b.AppendLine($"X-Shard {report.Version} - run report");
  b.AppendLine($"Started:   {report.Started.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
  b.AppendLine($"Finished:  {report.Finished.ToLocalTime():yyyy-MM-dd HH:mm:ss}  (duration {report.Finished-report.Started:hh\\:mm\\:ss})");
  b.AppendLine($"Jobs:      {report.Entries.Count}  succeeded {report.Succeeded}  failed {report.Failed}  cancelled {report.Cancelled}");
  b.AppendLine($"Saved:     {FormatBytes(report.SavedBytes)} (source minus output of successful jobs)");
  b.AppendLine(new string('=',78));
  foreach(var e in report.Entries)
  {
   b.AppendLine($"{e.Name}  [{e.Status}]");
   if(e.Title!=null)b.AppendLine($"  Title:     {e.Title}");
   b.AppendLine($"  Operation: {e.Operation} · {e.Console}");
   b.AppendLine($"  Duration:  {e.Duration:hh\\:mm\\:ss}");
   b.AppendLine($"  Size:      source {FormatBytes(e.SourceBytes)}  output {FormatBytes(e.OutputBytes)}  saved {FormatBytes(e.SavedBytes)}");
   b.AppendLine($"  Source:    {e.Source}");
   if(!string.IsNullOrEmpty(e.Output))b.AppendLine($"  Output:    {e.Output}");
   if(e.Status!=nameof(JobStatus.Succeeded))b.AppendLine($"  Result:    {e.Result}");
   b.AppendLine();
  }
  return b.ToString();
 }
 public static string ToHtml(RunReport report)
 {
  string H(string? value)=>WebUtility.HtmlEncode(value??"");
  var b=new StringBuilder();
  b.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>X-Shard run report</title><style>");
  b.Append("body{font-family:'Segoe UI',system-ui,sans-serif;background:#111A14;color:#EEF2ED;margin:24px}h1{margin:0 0 4px;font-size:22px}.muted{color:#9DAA9D}");
  b.Append(".cards{display:flex;gap:12px;flex-wrap:wrap;margin:16px 0}.card{background:#19221B;border:1px solid #334634;border-radius:12px;padding:12px 16px;min-width:140px}.card b{display:block;font-size:20px}");
  b.Append("table{width:100%;border-collapse:collapse;background:#19221B;border-radius:12px;overflow:hidden}th,td{text-align:left;padding:8px 10px;border-bottom:1px solid #2A362B;vertical-align:top;font-size:13px}th{background:#202C22;color:#9BE584}");
  b.Append(".ok{color:#9BE584}.bad{color:#FFA9B0}.warn{color:#FFC173}.path{color:#9DAA9D;font-size:12px;word-break:break-all}</style></head><body>");
  b.Append($"<h1>X-Shard run report</h1><div class=\"muted\">Version {H(report.Version)} · {report.Started.ToLocalTime():yyyy-MM-dd HH:mm:ss} – {report.Finished.ToLocalTime():HH:mm:ss}</div>");
  b.Append("<div class=\"cards\">");
  b.Append($"<div class=\"card\"><span class=\"muted\">Jobs</span><b>{report.Entries.Count}</b></div><div class=\"card\"><span class=\"muted\">Succeeded</span><b class=\"ok\">{report.Succeeded}</b></div>");
  b.Append($"<div class=\"card\"><span class=\"muted\">Failed</span><b class=\"bad\">{report.Failed}</b></div><div class=\"card\"><span class=\"muted\">Cancelled</span><b class=\"warn\">{report.Cancelled}</b></div>");
  b.Append($"<div class=\"card\"><span class=\"muted\">Duration</span><b>{report.Finished-report.Started:hh\\:mm\\:ss}</b></div><div class=\"card\"><span class=\"muted\">Space saved</span><b>{H(FormatBytes(report.SavedBytes))}</b></div></div>");
  b.Append("<table><tr><th>Job</th><th>Operation</th><th>Status</th><th>Duration</th><th>Source</th><th>Output</th><th>Saved</th></tr>");
  foreach(var e in report.Entries)
  {
   var css=e.Status==nameof(JobStatus.Succeeded)?"ok":e.Status==nameof(JobStatus.Failed)?"bad":"warn";
   b.Append($"<tr><td><b>{H(e.Name)}</b>{(e.Title!=null?$"<div class=\"muted\">{H(e.Title)}</div>":"")}<div class=\"path\">{H(e.Source)}</div>{(string.IsNullOrEmpty(e.Output)?"":$"<div class=\"path\">→ {H(e.Output)}</div>")}</td>");
   b.Append($"<td>{H(e.Operation)}<div class=\"muted\">{H(e.Console)}</div></td><td class=\"{css}\">{H(e.Status)}{(e.Status!=nameof(JobStatus.Succeeded)?$"<div class=\"muted\">{H(e.Result)}</div>":"")}</td>");
   b.Append($"<td>{e.Duration:hh\\:mm\\:ss}</td><td>{H(FormatBytes(e.SourceBytes))}</td><td>{H(FormatBytes(e.OutputBytes))}</td><td>{H(FormatBytes(e.SavedBytes))}</td></tr>");
  }
  b.Append("</table></body></html>");
  return b.ToString();
 }
 /// <summary>Writes the report; format by extension (.html/.htm → HTML, otherwise TXT).</summary>
 public static async Task WriteAsync(RunReport report,string path)
 {
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
  var html=Path.GetExtension(path).ToLowerInvariant() is ".html" or ".htm";
  await File.WriteAllTextAsync(path,html?ToHtml(report):ToText(report),new UTF8Encoding(false));
 }
 public static string DefaultFileName(RunReport report,string format)=>$"X-Shard-report-{report.Finished.ToLocalTime():yyyyMMdd-HHmmss}.{(format.Equals("Html",StringComparison.OrdinalIgnoreCase)?"html":"txt")}";
}
