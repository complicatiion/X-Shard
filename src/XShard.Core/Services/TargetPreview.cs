using System.Runtime.InteropServices;
using XShard.Models;
namespace XShard.Services;
public sealed record TargetPreviewInfo(string Destination,IReadOnlyList<string> Layout,long? EstimatedBytes,long? FreeBytes,bool Expected)
{
 /// <summary>True when the free space on the target is known and lower than the estimate.</summary>
 public bool InsufficientSpace=>EstimatedBytes is {} need&&FreeBytes is {} free&&need>free;
}
/// <summary>Predicts where and how a job writes its result, estimates the space needed and reads the free space of the target (local drives and UNC shares).</summary>
public static class TargetPreview
{
 public static TargetPreviewInfo Describe(XisoJobOptions o,TitleInfo? title=null)
 {
  if(o.Mode==XisoMode.List)return new("",["No files are written (List)."],0,null,false);
  var d=o.Destination;var name=Path.GetFileNameWithoutExtension(o.Source);var lines=new List<string>();var expected=false;
  var options=BackendPolicy.Normalize(o);
  if(options.Engine==BackendEngine.ExtractXiso)
  {
   switch(o.Mode)
   {
    case XisoMode.Extract:lines.Add(d+Path.DirectorySeparatorChar+"   (folder with the game files)");if(o.SkipSystemUpdate)lines.Add("   $SystemUpdate is skipped");break;
    case XisoMode.Create:lines.Add(d+"   (XISO image)");break;
    default:lines.Add(d+"   (rewritten XISO image)");break;
   }
  }
  else
  {
   expected=true;
   var titleId=title?.TitleIdHex??"<TitleID>";var mediaId=title?.MediaIdHex??"<MediaID>";var titleName=title?.Name??name;
   var format=o.Mode switch {XisoMode.Extract=>OutputFormat.Extract,XisoMode.Create or XisoMode.Rewrite=>OutputFormat.XISO,_=>o.Format==OutputFormat.ConsoleDefault?(o.Platform==ConsolePlatform.Xbox360?OutputFormat.GoD:OutputFormat.Extract):o.Format};
   var file=format switch {OutputFormat.XISO=>titleName+(o.Split?".1.iso / .2.iso":".iso"),OutputFormat.CCI=>titleName+".cci",OutputFormat.CSO=>titleName+".cso",OutputFormat.ZAR=>titleName+".zar",_=>""};
   var flatten=o.FlattenOutput&&file!=""&&!o.Split&&!o.AttachXbe;
   if(flatten)lines.Add(Path.Combine(Path.GetDirectoryName(d)??"",file)+"   (single file placed directly in the output folder)");
   else
   {
    lines.Add(d+Path.DirectorySeparatorChar+"   (job container)");
    switch(format)
    {
     case OutputFormat.GoD:
      lines.Add($"   {titleId}{Path.DirectorySeparatorChar}00007000{Path.DirectorySeparatorChar}{mediaId}   (GoD header)");
      lines.Add($"   {titleId}{Path.DirectorySeparatorChar}00007000{Path.DirectorySeparatorChar}{mediaId}.data{Path.DirectorySeparatorChar}Data0000 …");break;
     case OutputFormat.Extract:lines.Add($"   {titleName}{Path.DirectorySeparatorChar}   (game files)");break;
     default:lines.Add("   "+file);if(o.AttachXbe)lines.Add("   default.xbe   (attach XBE)");break;
    }
   }
  }
  var estimate=RunReportService.MeasureSource(o);
  return new(d,lines,estimate,FreeBytes(Path.GetDirectoryName(d)??d),expected);
 }
 /// <summary>Free bytes available to the user at the path (nearest existing parent), or null if unknown.</summary>
 public static long? FreeBytes(string path)
 {
  try
  {
   var probe=Path.GetFullPath(path);
   while(!Directory.Exists(probe)){var parent=Path.GetDirectoryName(probe);if(string.IsNullOrEmpty(parent)||parent==probe)return null;probe=parent;}
   if(OperatingSystem.IsWindows())return GetDiskFreeSpaceEx(probe,out var available,out _,out _)?(long)available:null;
   return new DriveInfo(probe).AvailableFreeSpace;
  }
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return null;}
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)]
 private static extern bool GetDiskFreeSpaceEx(string directory,out ulong freeBytesAvailable,out ulong totalBytes,out ulong totalFreeBytes);
 /// <summary>Groups estimated space by target volume for all writing jobs; returns volumes that lack space.</summary>
 public static IReadOnlyList<(string Volume,long Needed,long Free)> FindShortages(IEnumerable<XisoJobOptions> jobs)
 {
  var result=new List<(string,long,long)>();
  foreach(var group in jobs.Where(j=>j.Mode!=XisoMode.List&&j.Destination!="").GroupBy(j=>Path.GetPathRoot(Path.GetFullPath(j.Destination))??"",StringComparer.OrdinalIgnoreCase))
  {
   var needed=group.Sum(j=>RunReportService.MeasureSource(j)??0);
   var free=FreeBytes(Path.GetDirectoryName(group.First().Destination)??group.Key);
   if(free is {} available&&needed>available)result.Add((group.Key,needed,available));
  }
  return result;
 }
}
/// <summary>Local crash logs: logs folder in the data directory, newest ten kept, nothing is uploaded.</summary>
public static class CrashLog
{
 public const int Keep=10;
 public static string Folder(string dataDirectory)=>Path.Combine(dataDirectory,"logs");
 public static string? Write(string dataDirectory,Exception exception,string context)
 {
  try
  {
   var folder=Folder(dataDirectory);Directory.CreateDirectory(folder);
   var file=Path.Combine(folder,$"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
   var version=typeof(CrashLog).Assembly.GetName().Version?.ToString(3)??"?";
   File.WriteAllText(file,$"X-Shard {version} crash log{Environment.NewLine}Time: {DateTimeOffset.Now:O}{Environment.NewLine}OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}){Environment.NewLine}Runtime: {RuntimeInformation.FrameworkDescription}{Environment.NewLine}Context: {context}{Environment.NewLine}{Environment.NewLine}{exception}{Environment.NewLine}");
   File.WriteAllText(Path.Combine(folder,"last-crash.txt"),file);
   foreach(var old in new DirectoryInfo(folder).GetFiles("crash-*.log").OrderByDescending(f=>f.Name).Skip(Keep))try{old.Delete();}catch(IOException){}
   return file;
  }
  catch{return null;}
 }
 /// <summary>Returns and clears the crash marker of the previous session.</summary>
 public static string? TakePendingCrash(string dataDirectory)
 {
  try
  {
   var marker=Path.Combine(Folder(dataDirectory),"last-crash.txt");if(!File.Exists(marker))return null;
   var file=File.ReadAllText(marker).Trim();File.Delete(marker);return File.Exists(file)?file:null;
  }
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return null;}
 }
}
