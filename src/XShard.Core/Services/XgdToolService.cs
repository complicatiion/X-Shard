using XShard.Models;
namespace XShard.Services;
public sealed class XgdToolService(XisoProcessService runner)
{
 public static string? FindBackend(string configured)
 {
  if(!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured)?Path.GetFullPath(configured):null;
  var names=OperatingSystem.IsWindows()?new[]{"XGDTool.exe","XGDTool-v1.0.0-win-x64-CLI.exe"}:new[]{"XGDTool"};
  return new[]{Path.Combine(AppContext.BaseDirectory,"backends","XGDTool"),AppContext.BaseDirectory,Path.Combine(AppContext.BaseDirectory,"tools"),Path.Combine(AppContext.BaseDirectory,"bin")}
   .SelectMany(folder=>names.Select(name=>Path.Combine(folder,name))).FirstOrDefault(File.Exists);
 }
 public static List<string> BuildArguments(XisoJobOptions options,string outputDirectory)
 {
  if(!Enum.IsDefined(options.Format)||!Enum.IsDefined(options.Scrub)||!Enum.IsDefined(options.Profile)) throw new ArgumentException("Unsupported XGD option.");
  if(options.Profile is DeviceProfile.Xemu or DeviceProfile.OriginalXbox && options.Platform!=ConsolePlatform.Xbox || options.Profile is DeviceProfile.Xenia or DeviceProfile.Xbox360 && options.Platform!=ConsolePlatform.Xbox360) throw new ArgumentException("Profile does not match console.");
  var format=options.Format==OutputFormat.ConsoleDefault?(options.Platform==ConsolePlatform.Xbox360?OutputFormat.GoD:OutputFormat.Extract):options.Format;
  if(options.Platform==ConsolePlatform.Xbox360 && (format is OutputFormat.CCI or OutputFormat.CSO || options.AttachXbe||options.PatchMedia||options.RenameXbe)) throw new ArgumentException("CCI, CSO and XBE options are for original Xbox only.");
  var flag=options.Mode switch
  {
   XisoMode.Extract=>"--extract",XisoMode.List=>"--list",XisoMode.Create or XisoMode.Rewrite=>"--xiso",
   _=>format switch {OutputFormat.GoD=>"--god",OutputFormat.CCI=>"--cci",OutputFormat.CSO=>"--cso",OutputFormat.ZAR=>"--zar",OutputFormat.Extract=>"--extract",_=>"--xiso"}
  };
  if(options.Mode!=XisoMode.List)
  {
   if((options.PatchMedia||options.RenameXbe)&&(flag!="--extract"||Path.GetExtension(options.Source).Equals(".zar",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("XBE media/title patching requires extracting an original Xbox disc image (not ZAR).");
   if(options.AttachXbe&&flag=="--extract")throw new ArgumentException("Attach XBE generation requires image output.");
  }
  // Upstream auto-profile flags discard all settings, including --offline.
  // Profiles are expanded by X-Shard into explicit format/settings snapshots.
  var args=new List<string>{flag};
  if(options.Offline)args.Add("--offline");
  if(options.Mode!=XisoMode.List)
  {
   var scrub=options.Mode==XisoMode.Rewrite?ScrubMode.Full:options.Scrub;
   if(scrub==ScrubMode.Partial)args.Add("--partial-scrub");
   if(scrub==ScrubMode.Full)args.Add("--full-scrub");
   if(options.Split) {if(flag!="--xiso")throw new ArgumentException("Split requires XISO output.");args.Add("--split");}
   if(options.AttachXbe)args.Add("--attach-xbe");
   if(options.PatchMedia)args.Add("--am-patch");
   if(options.RenameXbe)args.Add("--rename");
  }
  args.Add(options.Source);
  if(options.Mode!=XisoMode.List) args.Add(outputDirectory);
  return args;
 }
 public static bool IsGodDirectory(string path,CancellationToken token=default)
 {
  if(!Directory.Exists(path))return false;
  bool Scan(string folder,int depth)
  {
   foreach(var entry in Directory.EnumerateFileSystemEntries(folder))
   {
    token.ThrowIfCancellationRequested();var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)continue;
    if((attributes&FileAttributes.Directory)!=0) {if(depth<2&&Scan(entry,depth+1))return true;}
    else if(Path.GetExtension(folder).Equals(".data",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(entry).StartsWith("Data",StringComparison.Ordinal))return true;
   }
   return false;
  }
  return Scan(path,0);
 }
 public static bool IsFailureLine(string line) => line.Contains("Failed to process input:",StringComparison.OrdinalIgnoreCase)
  || line.Contains("[ERROR]",StringComparison.OrdinalIgnoreCase) || line.Contains("[Error]",StringComparison.OrdinalIgnoreCase);
 public async Task<string> ProbeAsync(string path,CancellationToken token)
 {
  var lines=new List<string>();var gate=new object();
  var code=await runner.RunAsync(path,["--version"],AppContext.BaseDirectory,(line,_)=>{lock(gate)lines.Add(line);},token);
  var text=string.Join(" ",lines).Trim();
  if(code!=0||!text.Any(char.IsDigit)) throw new IOException("Selected file is not a working XGDTool CLI backend.");
  return "XGDTool "+text;
 }
 /// <summary>Runs XGDTool in a private staging folder and publishes the result. Returns the published path (container folder, or the single file when FlattenOutput applies).</summary>
 public async Task<string> ExecuteAsync(XisoJobOptions options,string path,Action<string,bool> output,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  string? stage=null;bool committed=false;int failures=0;int listed=0;
  try
  {
   if(options.Mode!=XisoMode.List)
   {
    var parent=Path.GetDirectoryName(options.Destination)!;Directory.CreateDirectory(parent);
    stage=Path.Combine(parent,".xshard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   }
   var code=await runner.RunAsync(path,BuildArguments(options,stage??""),stage??AppContext.BaseDirectory,(line,error)=>
   {if(IsFailureLine(line))Interlocked.Increment(ref failures);if(line.Contains("Files in image:"))Interlocked.Increment(ref listed);output(line,error);},token);
   if(code!=0||failures>0||(options.Mode==XisoMode.List&&listed==0))throw new IOException($"XGDTool reported a failure (exit {code}). See the job log.");
   token.ThrowIfCancellationRequested();
   if(stage==null)return "";
   if(!Directory.EnumerateFileSystemEntries(stage).Any())throw new IOException("XGDTool did not produce output.");
   if(options.FlattenOutput&&Directory.GetFiles(stage,"*",SearchOption.AllDirectories) is [var single])
   {
    // Library targets (emulators/frontends) expect the image directly in the game folder.
    var target=UniqueFilePath(Path.Combine(Path.GetDirectoryName(options.Destination)!,Path.GetFileName(single)));
    File.Move(single,target,false);committed=true;
    try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    output("Completed: "+target,false);return target;
   }
   Directory.Move(stage,options.Destination);committed=true;
   output("Completed: "+options.Destination,false);return options.Destination;
  }
  finally {if(stage!=null&&!committed)output("Incomplete working files retained at: "+stage,true);}
 }
 private static string UniqueFilePath(string path)
 {
  if(!File.Exists(path)&&!Directory.Exists(path))return path;
  var folder=Path.GetDirectoryName(path)!;var stem=Path.GetFileNameWithoutExtension(path);var extension=Path.GetExtension(path);
  for(var index=2;;index++){var candidate=Path.Combine(folder,$"{stem} ({index}){extension}");if(!File.Exists(candidate)&&!Directory.Exists(candidate))return candidate;}
 }
}
