using System.Collections.Concurrent;
using XShard.Models;
using XShard.Services;

// xshard-cli — X-Shard command line. Exit codes: 0 success, 1 at least one job failed, 2 usage error,
// 3 legal documents not accepted/missing, 4 backend not found, 130 cancelled (Ctrl+C).
namespace XShard.Cli;

internal static class Program
{
 private const int Ok=0,JobFailed=1,Usage=2,Legal=3,NoBackend=4,Cancelled=130;
 private static bool quiet;

 private static async Task<int> Main(string[] args)
 {
  Console.OutputEncoding=System.Text.Encoding.UTF8;
  if(args.Length==0||args[0] is "help" or "-h" or "--help" or "/?"){PrintHelp();return args.Length==0?Usage:Ok;}
  if(args[0] is "version" or "--version"){Console.WriteLine("xshard-cli "+Version);return Ok;}
  Options o;
  try{o=Options.Parse(args);}catch(ArgumentException ex){Console.Error.WriteLine("error: "+ex.Message);Console.Error.WriteLine("Run 'xshard-cli help' for usage.");return Usage;}
  quiet=o.Quiet;
  if(o.Command=="info")return Info(o);

  var storage=new SettingsService();
  var legal=new LegalService(storage);
  if(!legal.DocumentsReady){Console.Error.WriteLine("error: the legal documents are missing beside xshard-cli.exe (folder 'legal'). Use a complete X-Shard release.");return Legal;}
  if(!legal.IsAccepted)
  {
   if(!o.AcceptEula)
   {
    Console.Error.WriteLine("error: the X-Shard legal documents have not been accepted yet.");
    Console.Error.WriteLine("Review them in: "+Path.Combine(AppContext.BaseDirectory,"legal"));
    Console.Error.WriteLine("Then accept them once in X-Shard or run again with --accept-eula.");
    return Legal;
   }
   await legal.AcceptAsync();Say("Legal documents accepted (stored locally).");
  }
  var settings=storage.LoadSettings();
  var extractXiso=XisoProcessService.FindBackend(o.ExtractXisoPath??settings.BackendPath);
  var xgdTool=XgdToolService.FindBackend(o.XgdToolPath??settings.XgdBackendPath);

  var request=new JobRequest(o.Mode,o.Platform,o.Output??"",o.Format,o.Profile,o.Scrub,o.Split,o.AttachXbe,o.PatchMedia,o.RenameXbe,!o.Online,o.Advanced,!o.NoSkipSystemUpdate,o.DisableMediaPatching,o.Flatten);
  if(o.Profile!=DeviceProfile.Custom)
  {
   // Profiles are presets: explicit --format/--scrub still win.
   var preset=JobPlanner.ApplyProfile(request);
   request=preset with {Mode=o.Mode==XisoMode.List?XisoMode.List:XisoMode.Convert,Format=o.FormatGiven?o.Format:preset.Format,Scrub=o.ScrubGiven?o.Scrub:preset.Scrub};
  }
  if(o.Online)Say("Online mode: XGDTool may contact Google, GitHub and XboxUnity for title data and artwork (explicitly requested with --online).");

  var queue=new JobQueueService(new JobExecutor(new XisoProcessService()));
  foreach(var source in ExpandSources(o))
  {
   try{queue.Jobs.Add(new XisoJob{Options=JobPlanner.Plan(request,source,queue.Jobs.Select(j=>j.Options).ToList()),Status=JobStatus.Waiting});}
   catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException){Console.Error.WriteLine($"error: {source}: {ex.Message}");if(!o.ContinueOnError)return Usage;}
  }
  if(queue.Jobs.Count==0){Console.Error.WriteLine("error: no sources to process.");return Usage;}
  foreach(var job in queue.Jobs)
  {
   var xgd=BackendPolicy.For(job.Options)==BackendEngine.XgdTool;
   if(xgd?xgdTool==null:extractXiso==null){Console.Error.WriteLine($"error: {(xgd?"XGDTool":"extract-xiso")} backend not found. Use a complete release or pass {(xgd?"--xgdtool":"--extract-xiso")} <path>.");return NoBackend;}
  }

  using var cancel=new CancellationTokenSource();
  Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;if(!cancel.IsCancellationRequested){cancel.Cancel();Console.Error.WriteLine("Cancelling current job …");queue.StopAfterCurrent=true;queue.CancelCurrent();}};
  var started=DateTimeOffset.UtcNow;var gate=new object();
  queue.Changed+=()=>
  {
   foreach(var job in queue.Jobs.Where(j=>j.IsRunning).ToList())if(announced.TryAdd(job.Id,true))Say($"> {job.Name}: {job.Options.Mode} ({job.PlatformText})");
  };
  await queue.RunAsync(options=>BackendPolicy.For(options)==BackendEngine.XgdTool?xgdTool!:extractXiso!,o.ContinueOnError,(job,line,error)=>
  {
   if(XisoOutputParser.ParseListEntry(line) is {} entry&&job.Options.Mode==XisoMode.List){lock(gate)Console.WriteLine(entry.Path+(entry.IsDirectory?"":"\t"+entry.Size));return;}
   if(!quiet||error)lock(gate)(error?Console.Error:Console.Out).WriteLine("  "+line);
  },()=>Task.CompletedTask);

  foreach(var job in queue.Jobs)
   Console.WriteLine($"{(job.Status==JobStatus.Succeeded?"OK  ":job.Status==JobStatus.Waiting?"SKIP":"FAIL")} {job.Name} — {job.Result}{(job.PublishedPath is {Length:>0} p?" → "+p:"")}");
  if(o.Report is {Length:>0} reportPath)
  {
   var report=RunReportService.Build(started,DateTimeOffset.UtcNow,queue.Jobs.Where(j=>j.Status!=JobStatus.Waiting),Version);
   await RunReportService.WriteAsync(report,reportPath);Say("Report: "+Path.GetFullPath(reportPath));
  }
  if(cancel.IsCancellationRequested)return Cancelled;
  return queue.Jobs.Any(j=>j.Status is JobStatus.Failed or JobStatus.Cancelled)?JobFailed:Ok;
 }
 private static readonly ConcurrentDictionary<Guid,bool> announced=new();
 private static string Version=>typeof(Program).Assembly.GetName().Version?.ToString(3)??"?";
 private static void Say(string text){if(!quiet)Console.WriteLine(text);}

 private static IEnumerable<string> ExpandSources(Options o)
 {
  foreach(var raw in o.Sources)
  {
   var full=Path.GetFullPath(raw);
   if(Directory.Exists(full)&&o.Mode!=XisoMode.Create&&!o.GameFolder)
   {
    var images=FolderBatchService.EnumerateImages(full,o.Recursive,JobPlanner.UsesXgd(o.Mode,o.Platform,o.Profile,o.Advanced),CancellationToken.None);
    if(images.Count>0){foreach(var image in images)yield return image;continue;}
   }
   yield return full;
  }
 }

 private static int Info(Options o)
 {
  var exit=Ok;
  foreach(var source in o.Sources)
  {
   var info=TitleInfoReader.TryRead(Path.GetFullPath(source),o.Platform);
   Console.WriteLine(source);
   if(info==null){Console.WriteLine("  No title information found (supported: XISO/ISO, XBE, XEX, extracted game folder, GoD folder or header).");exit=JobFailed;continue;}
   void Field(string name,string? value){if(!string.IsNullOrEmpty(value))Console.WriteLine($"  {name,-10} {value}");}
   Field("Console",info.Platform==ConsolePlatform.Xbox360?"Xbox 360":"Xbox Classic");Field("Read from",info.SourceKind);Field("Name",info.Name);
   Field("Title ID",info.TitleIdHex+(info.TitleIdCode is {} code?$" ({code})":""));Field("Media ID",info.MediaIdHex);Field("Region",info.Region);Field("Version",info.Version);
   Field("Disc",info.Disc is {} d?(info.DiscCount is {} c and >0?$"{d} / {c}":d.ToString()):null);
   if(info.HasIcon&&o.IconFolder is {Length:>0} folder)
   {
    Directory.CreateDirectory(folder);var file=Path.Combine(folder,(info.TitleIdHex??Path.GetFileNameWithoutExtension(source))+".png");
    File.WriteAllBytes(file,info.IconPng!);Field("Icon",file);
   }
   else Field("Icon",info.HasIcon?"available (use --icon-dir <folder> to save)":null);
  }
  return exit;
 }

 private static void PrintHelp()
 {
  Console.WriteLine($"""
  xshard-cli {Version} — X-Shard is an Xbox 360 and Xbox Classic ISO tool for the community.

  Usage: xshard-cli <command> [options] <source> [source …]

  Commands
    extract     Extract images into new folders
    create      Build an XISO from a game folder
    list        List image contents (no files written)
    rewrite     Rewrite a working copy (the original is kept)
    convert     Convert to another format (--format)
    info        Show title information (Title ID, name, region, …)
    help | version

  Options
    -c, --console <classic|xbox360>   Console (default: classic)
    -o, --output <folder>             Output folder (required for writing commands)
    -f, --format <xiso|god|cci|cso|zar|extract>   Convert target format
        --profile <custom|classic|xemu|xbox360|xenia>   Device profile preset
        --scrub <none|partial|full>   Scrub mode (XGDTool)
        --split  --attach-xbe  --patch-media  --rename-xbe   XGDTool options
        --advanced                    Use XGDTool for Xbox Classic operations
        --no-skip-systemupdate        Keep $SystemUpdate (skipped by default)
        --disable-media-patching      extract-xiso: do not patch XBE media flags
        --flatten                     Place single-file results directly in the output folder
        --online                      Allow XGDTool online title/artwork lookups (offline by default)
    -r, --recursive                   Scan source folders recursively for images
        --game-folder                 Treat source folders as game/GoD folders, not image folders
        --continue-on-error           Continue with the next job after an error
        --report <file.txt|file.html> Write a run report
        --icon-dir <folder>           info: save title icons as PNG
        --extract-xiso <path>  --xgdtool <path>   Backend overrides
        --accept-eula                 Accept the X-Shard legal documents (folder 'legal')
    -q, --quiet                       Only results and errors

  Exit codes: 0 ok, 1 job failed, 2 usage, 3 legal not accepted, 4 backend missing, 130 cancelled
  """);
 }

 private sealed class Options
 {
  public string Command="";public XisoMode Mode;public ConsolePlatform Platform=ConsolePlatform.Xbox;public string? Output;
  public OutputFormat Format=OutputFormat.XISO;public bool FormatGiven;public DeviceProfile Profile=DeviceProfile.Custom;public ScrubMode Scrub=ScrubMode.None;public bool ScrubGiven;
  public bool Split,AttachXbe,PatchMedia,RenameXbe,Advanced,NoSkipSystemUpdate,DisableMediaPatching,Flatten,Online,Recursive,GameFolder,ContinueOnError,AcceptEula,Quiet;
  public string? Report,IconFolder,ExtractXisoPath,XgdToolPath;public List<string> Sources=[];
  public static Options Parse(string[] args)
  {
   var o=new Options{Command=args[0].ToLowerInvariant()};
   o.Mode=o.Command switch {"extract"=>XisoMode.Extract,"create"=>XisoMode.Create,"list"=>XisoMode.List,"rewrite"=>XisoMode.Rewrite,"convert"=>XisoMode.Convert,"info"=>XisoMode.List,_=>throw new ArgumentException("unknown command: "+args[0])};
   for(var i=1;i<args.Length;i++)
   {
    var a=args[i];
    string Next(){if(++i>=args.Length)throw new ArgumentException("missing value for "+a);return args[i];}
    switch(a)
    {
     case "-c" or "--console":o.Platform=Next().ToLowerInvariant() switch {"classic" or "xbox" or "og"=>ConsolePlatform.Xbox,"xbox360" or "360"=>ConsolePlatform.Xbox360,var v=>throw new ArgumentException("unknown console: "+v)};break;
     case "-o" or "--output":o.Output=Next();break;
     case "-f" or "--format":o.FormatGiven=true;o.Format=Next().ToLowerInvariant() switch {"xiso" or "iso"=>OutputFormat.XISO,"god"=>OutputFormat.GoD,"cci"=>OutputFormat.CCI,"cso"=>OutputFormat.CSO,"zar"=>OutputFormat.ZAR,"extract"=>OutputFormat.Extract,var v=>throw new ArgumentException("unknown format: "+v)};break;
     case "--profile":o.Profile=Next().ToLowerInvariant() switch {"custom"=>DeviceProfile.Custom,"classic" or "xbox"=>DeviceProfile.OriginalXbox,"xemu"=>DeviceProfile.Xemu,"xbox360" or "360"=>DeviceProfile.Xbox360,"xenia"=>DeviceProfile.Xenia,var v=>throw new ArgumentException("unknown profile: "+v)};break;
     case "--scrub":o.ScrubGiven=true;o.Scrub=Next().ToLowerInvariant() switch {"none"=>ScrubMode.None,"partial"=>ScrubMode.Partial,"full"=>ScrubMode.Full,var v=>throw new ArgumentException("unknown scrub mode: "+v)};break;
     case "--split":o.Split=true;break;
     case "--attach-xbe":o.AttachXbe=true;break;
     case "--patch-media":o.PatchMedia=true;break;
     case "--rename-xbe":o.RenameXbe=true;break;
     case "--advanced":o.Advanced=true;break;
     case "--no-skip-systemupdate":o.NoSkipSystemUpdate=true;break;
     case "--disable-media-patching":o.DisableMediaPatching=true;break;
     case "--flatten":o.Flatten=true;break;
     case "--online":o.Online=true;break;
     case "-r" or "--recursive":o.Recursive=true;break;
     case "--game-folder":o.GameFolder=true;break;
     case "--continue-on-error":o.ContinueOnError=true;break;
     case "--report":o.Report=Next();break;
     case "--icon-dir":o.IconFolder=Next();break;
     case "--extract-xiso":o.ExtractXisoPath=Next();break;
     case "--xgdtool":o.XgdToolPath=Next();break;
     case "--accept-eula":o.AcceptEula=true;break;
     case "-q" or "--quiet":o.Quiet=true;break;
     default:
      if(a.StartsWith('-')&&a.Length>1)throw new ArgumentException("unknown option: "+a);
      o.Sources.Add(a);break;
    }
   }
   if(o.Sources.Count==0)throw new ArgumentException("no source given.");
   if(o.Profile is DeviceProfile.OriginalXbox or DeviceProfile.Xemu&&o.Platform!=ConsolePlatform.Xbox||o.Profile is DeviceProfile.Xbox360 or DeviceProfile.Xenia&&o.Platform!=ConsolePlatform.Xbox360)
    throw new ArgumentException("the profile does not match the console (use --console).");
   if(o.Command!="info"&&o.Mode!=XisoMode.List&&string.IsNullOrWhiteSpace(o.Output))throw new ArgumentException("--output is required for "+o.Command+".");
   if(o.Mode==XisoMode.Convert&&!o.FormatGiven&&o.Profile==DeviceProfile.Custom)throw new ArgumentException("--format is required for convert.");
   return o;
  }
 }
}
