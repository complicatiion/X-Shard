using XShard.Models;
namespace XShard.Services;
/// <summary>Everything the user selected for new jobs. Shared by the GUI and xshard-cli so both create identical jobs.</summary>
public sealed record JobRequest(
 XisoMode Mode,
 ConsolePlatform Platform,
 string OutputDirectory,
 OutputFormat Format=OutputFormat.XISO,
 DeviceProfile Profile=DeviceProfile.Custom,
 ScrubMode Scrub=ScrubMode.None,
 bool Split=false,
 bool AttachXbe=false,
 bool PatchMedia=false,
 bool RenameXbe=false,
 bool Offline=true,
 bool Advanced=false,
 bool SkipSystemUpdate=true,
 bool DisableMediaPatching=false,
 bool FlattenOutput=false);
/// <summary>Capability rules and job planning (destination naming, collision protection, validation).</summary>
public static class JobPlanner
{
 public static bool UsesXgd(XisoMode mode,ConsolePlatform platform,DeviceProfile profile,bool advanced)=>advanced||profile!=DeviceProfile.Custom||BackendPolicy.For(platform,mode)==BackendEngine.XgdTool;
 public static bool UsesXgd(JobRequest r)=>UsesXgd(r.Mode,r.Platform,r.Profile,r.Advanced);
 public static bool IsWriting(XisoMode mode)=>mode!=XisoMode.List;
 public static bool CanSplit(XisoMode mode,OutputFormat format)=>mode is XisoMode.Create or XisoMode.Rewrite||mode==XisoMode.Convert&&format==OutputFormat.XISO;
 public static bool CanAttach(ConsolePlatform platform,XisoMode mode,OutputFormat format)=>platform==ConsolePlatform.Xbox&&(mode is XisoMode.Create or XisoMode.Rewrite||mode==XisoMode.Convert&&format!=OutputFormat.Extract);
 public static bool CanPatchXgd(ConsolePlatform platform,XisoMode mode,OutputFormat format)=>platform==ConsolePlatform.Xbox&&(mode==XisoMode.Extract||mode==XisoMode.Convert&&format==OutputFormat.Extract);
 public static bool CanScrub(XisoMode mode,OutputFormat format)=>mode is XisoMode.Create or XisoMode.Rewrite||mode==XisoMode.Convert&&format is not (OutputFormat.Extract or OutputFormat.ZAR);
 public static bool SupportsPatching(bool usesXgd,XisoMode mode)=>!usesXgd&&mode is XisoMode.Create or XisoMode.Rewrite;
 public static bool SupportsSkip(bool usesXgd,XisoMode mode)=>!usesXgd&&mode!=XisoMode.List;
 public static OutputFormat[] FormatsFor(ConsolePlatform platform)=>Enum.GetValues<OutputFormat>().Where(f=>platform==ConsolePlatform.Xbox||f is not (OutputFormat.CCI or OutputFormat.CSO)).ToArray();
 public static DeviceProfile[] ProfilesFor(ConsolePlatform platform)=>platform==ConsolePlatform.Xbox?[DeviceProfile.Custom,DeviceProfile.OriginalXbox,DeviceProfile.Xemu]:[DeviceProfile.Custom,DeviceProfile.Xbox360,DeviceProfile.Xenia];
 /// <summary>Editable preset values for a device profile (format, scrub, XBE options).</summary>
 public static JobRequest ApplyProfile(JobRequest r)
 {
  if(r.Profile==DeviceProfile.Custom)return r;
  var patch=r.Profile==DeviceProfile.OriginalXbox;
  return r with {Advanced=true,Mode=XisoMode.Convert,Split=false,AttachXbe=false,PatchMedia=patch,RenameXbe=patch,
   Scrub=r.Profile==DeviceProfile.OriginalXbox?ScrubMode.None:ScrubMode.Full,
   Format=r.Profile switch {DeviceProfile.OriginalXbox=>OutputFormat.Extract,DeviceProfile.Xbox360=>OutputFormat.GoD,_=>OutputFormat.XISO}};
 }
 /// <summary>Creates the options for one source. Throws IOException/ArgumentException with a user-readable message.</summary>
 /// <param name="active">Options of waiting/running jobs, used for duplicate and destination collision checks.</param>
 public static XisoJobOptions Plan(JobRequest r,string rawSource,IReadOnlyCollection<XisoJobOptions> active)
 {
  var source=Path.GetFullPath(rawSource);
  var usesXgd=UsesXgd(r);
  if(IsWriting(r.Mode)&&string.IsNullOrWhiteSpace(r.OutputDirectory))throw new IOException("Choose an output folder first.");
  var name=r.Mode==XisoMode.Create?new DirectoryInfo(source).Name:Path.GetFileNameWithoutExtension(source);
  if(string.IsNullOrWhiteSpace(name))throw new IOException("Choose a named source folder.");
  var destination=r.Mode==XisoMode.List?"":Path.Combine(Path.GetFullPath(r.OutputDirectory),usesXgd
    ?name+"."+(r.Mode==XisoMode.Convert?r.Format.ToString().ToLowerInvariant():r.Mode.ToString().ToLowerInvariant())+"-output"
    :r.Mode switch {XisoMode.Extract=>name,XisoMode.Create=>name+".iso",_=>name+".rewritten.iso"});
  bool Active(XisoJobOptions o,string path)=>o.Destination!=""&&o.Destination.Equals(path,StringComparison.OrdinalIgnoreCase);
  if(active.Any(o=>o.Source.Equals(source,StringComparison.OrdinalIgnoreCase)&&o.Mode==r.Mode&&o.Platform==r.Platform&&o.Format==r.Format))throw new IOException("This source is already queued: "+name);
  if(destination!="")
  {
   var basis=destination;var index=2;
   while(File.Exists(destination)||Directory.Exists(destination)||active.Any(o=>Active(o,destination)))
    destination=usesXgd||r.Mode==XisoMode.Extract?basis+" ("+(index++)+")":Path.Combine(Path.GetDirectoryName(basis)!,Path.GetFileNameWithoutExtension(basis)+" ("+(index++)+")"+Path.GetExtension(basis));
  }
  var options=new XisoJobOptions(r.Mode,source,destination,SupportsSkip(usesXgd,r.Mode)&&r.SkipSystemUpdate,SupportsPatching(usesXgd,r.Mode)&&r.DisableMediaPatching,r.Platform,
   usesXgd?BackendEngine.XgdTool:BackendEngine.ExtractXiso,r.Format,r.Profile,r.Scrub,r.Split&&CanSplit(r.Mode,r.Format),r.AttachXbe&&CanAttach(r.Platform,r.Mode,r.Format),
   r.PatchMedia&&CanPatchXgd(r.Platform,r.Mode,r.Format),r.RenameXbe&&CanPatchXgd(r.Platform,r.Mode,r.Format),r.Offline,r.Advanced,usesXgd&&r.FlattenOutput&&IsWriting(r.Mode));
  if(usesXgd)XgdToolService.BuildArguments(options,"validation-output");
  JobExecutor.Validate(options);
  if(active.Any(o=>o==options||Active(o,destination)))throw new IOException("This source or destination is already queued: "+name);
  return options;
 }
}
/// <summary>Auto UI zoom computation (logical, DPI-independent client size).</summary>
public static class UiScaling
{
 public const double ReferenceWidth=1280,ReferenceHeight=820,MinimumScale=0.70;
 public static double ComputeAutoScale(double width,double height)
 {
  if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)return 1;
  var fit=Math.Min(width/ReferenceWidth,height/ReferenceHeight);
  return Math.Clamp(Math.Floor(fit*20+1e-6)/20,MinimumScale,1);
 }
}
