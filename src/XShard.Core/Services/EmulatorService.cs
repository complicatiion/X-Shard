using System.Diagnostics;
using XShard.Models;
namespace XShard.Services;
/// <summary>Emulator/frontend integration: library folders per output target and launching a result in Xemu or Xenia.</summary>
public static class EmulatorService
{
 /// <summary>Resolves the output folder for a library target, or null if it is not configured or not valid for the console.</summary>
 public static string? LibraryFolder(OutputTarget target,ConsolePlatform platform,AppSettings settings)
 {
  var system=platform==ConsolePlatform.Xbox360?"xbox360":"xbox";
  string? Join(string root)=>string.IsNullOrWhiteSpace(root)?null:Path.Combine(root.Trim(),"roms",system);
  return target switch
  {
   OutputTarget.XemuLibrary when platform==ConsolePlatform.Xbox=>Blank(settings.XemuLibrary),
   OutputTarget.XeniaLibrary when platform==ConsolePlatform.Xbox360=>Blank(settings.XeniaLibrary),
   OutputTarget.RetroBat=>Join(settings.RetroBatRoot),
   OutputTarget.Batocera=>Join(settings.BatoceraShare),
   _=>null
  };
 }
 private static string? Blank(string value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
 public static bool IsValidFor(OutputTarget target,ConsolePlatform platform)=>target switch {OutputTarget.XemuLibrary=>platform==ConsolePlatform.Xbox,OutputTarget.XeniaLibrary=>platform==ConsolePlatform.Xbox360,_=>true};
 /// <summary>Finds a bootable file for the emulator: the published result if available, otherwise the source.</summary>
 public static string? FindLaunchTarget(XisoJob job)
 {
  var classic=job.Options.Platform==ConsolePlatform.Xbox;
  foreach(var candidate in new[]{job.Status==JobStatus.Succeeded?job.PublishedPath??job.Options.Destination:null,job.Options.Source})
  {
   if(string.IsNullOrEmpty(candidate))continue;
   if(File.Exists(candidate)&&IsLaunchableFile(candidate,classic))return candidate;
   if(!Directory.Exists(candidate))continue;
   var options=new EnumerationOptions{RecurseSubdirectories=true,MaxRecursionDepth=2,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
   var image=Directory.EnumerateFiles(candidate,"*",options).Where(f=>Path.GetExtension(f).ToLowerInvariant() is ".iso" or ".xiso")
    .Where(f=>!Path.GetFileNameWithoutExtension(f).EndsWith(".2")).OrderBy(f=>f.Length).FirstOrDefault();
   if(image!=null)return image;
   if(!classic)
   {
    if(TitleInfoReader.FindStfsHeader(candidate) is {} god)return god;
    if(Directory.EnumerateFiles(candidate,"default.xex",new EnumerationOptions{RecurseSubdirectories=true,MaxRecursionDepth=2,MatchCasing=MatchCasing.CaseInsensitive}).FirstOrDefault() is {} xex)return xex;
   }
  }
  return null;
 }
 private static bool IsLaunchableFile(string file,bool classic)
 {
  var extension=Path.GetExtension(file).ToLowerInvariant();
  return classic?extension is ".iso" or ".xiso":extension is ".iso" or ".xex" or ".zar"||extension==""&&TitleInfoReader.FindStfsHeader(Path.GetDirectoryName(file)!)==file;
 }
 /// <summary>Xemu boots a disc image with -dvd_path; Xenia takes the image, XEX or GoD header as its argument.</summary>
 public static ProcessStartInfo BuildLaunch(ConsolePlatform platform,string emulator,string target)
 {
  var start=new ProcessStartInfo(emulator){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(emulator)??""};
  if(platform==ConsolePlatform.Xbox){start.ArgumentList.Add("-dvd_path");start.ArgumentList.Add(target);}
  else start.ArgumentList.Add(target);
  return start;
 }
}
