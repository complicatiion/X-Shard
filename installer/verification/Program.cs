using System.Security.Cryptography;
using System.Diagnostics;
using System.Collections.Concurrent;
using XShard.Models;
using XShard.Services;

// Isolated subprocess fixture: exercises the XGDTool command contract without game data.
if(args.Length>0&&args[0].StartsWith("--"))
{
 if(args[0]=="--version"){Console.WriteLine("1.0.0");return 0;}
 if(args.Any(arg=>Path.GetFileName(arg)=="fail.iso")){Console.Error.WriteLine("Failed to process input: fail.iso");return 0;}
 if(args[0]=="--list"){Console.Error.WriteLine("Files in image:");Console.Error.WriteLine("folder/sample.bin (8192 bytes)");return 0;}
 var target=args[^1];Directory.CreateDirectory(Path.Combine(target,"title-id","00007000"));
 File.WriteAllText(Path.Combine(target,"title-id","00007000","fixture.data"),"isolated CLI fixture");return 0;
}
if(args.Length!=1) {Console.Error.WriteLine("Usage: XShard.Verification <extract-xiso executable>");return 2;}
var backend=Path.GetFullPath(args[0]);var temp=Path.Combine(Path.GetTempPath(),"XShardVerification-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
int checks=0;
void Check(bool condition,string name) {if(!condition) throw new Exception("FAILED: "+name);Console.WriteLine("PASS: "+name);checks++;}
async Task Reject(Func<Task> action,string name)
{try {await action();}catch(IOException) {Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
var logs=new ConcurrentQueue<string>();
void Log(string line,bool error)=>logs.Enqueue((error?"ERROR ":"")+line);
var process=new XisoProcessService();var executor=new JobExecutor(process);
try
{
 Check((await process.ProbeAsync(backend,CancellationToken.None)).Contains("extract-xiso"),"backend probe");
 var entry=XisoOutputParser.ParseListEntry(@"\folder\file with spaces.bin (123 bytes)");
 Check(entry is {Size:123,IsDirectory:false},"list parser accepts spaced filenames");
 Check(XisoOutputParser.ParseListEntry(@"\folder\ (0 bytes)") is {IsDirectory:true},"list parser detects folders");
 Check(XisoOutputParser.ParseListEntry("2 files in image total 123 bytes")==null,"list parser ignores summaries");
 var source=Path.Combine(temp,"source with spaces");Directory.CreateDirectory(Path.Combine(source,"nested"));
 var content=Enumerable.Range(0,8192).Select(i=>(byte)(i%251)).ToArray();await File.WriteAllBytesAsync(Path.Combine(source,"nested","sample.bin"),content);await File.WriteAllTextAsync(Path.Combine(source,"readme.txt"),"X-Shard dummy verification data");
 var iso=Path.Combine(temp,"sample image.iso");
 var create=new XisoJobOptions(XisoMode.Create,source,iso,false,false);
 Check(XisoProcessService.BuildArguments(create with {DisableMediaPatching=true},source,iso)[0]=="-m","patching flag is before mode");
 await executor.ExecuteAsync(create,backend,Log,CancellationToken.None);Check(File.Exists(iso),"create image");
 await Reject(()=>executor.ExecuteAsync(create,backend,Log,CancellationToken.None),"existing output rejected");
 await Reject(()=>executor.ExecuteAsync(create with {Destination=Path.Combine(source,"bad.iso")},backend,Log,CancellationToken.None),"output inside source rejected");
 var entries=new ConcurrentQueue<IsoContentEntry>();
 await executor.ExecuteAsync(new(XisoMode.List,iso,"",false,false),backend,(line,error)=>{Log(line,error);if(XisoOutputParser.ParseListEntry(line) is {} item) entries.Enqueue(item);},CancellationToken.None);
 Check(entries.Any(e=>e.Path.Contains("sample.bin")&&e.Size==content.Length),"list actual image contents");
 var extracted=Path.Combine(temp,"extracted");await executor.ExecuteAsync(new(XisoMode.Extract,iso,extracted,false,false),backend,Log,CancellationToken.None);
 Check((await File.ReadAllBytesAsync(Path.Combine(extracted,"nested","sample.bin"))).SequenceEqual(content),"create/extract byte-for-byte round-trip");
 var originalHash=SHA256.HashData(await File.ReadAllBytesAsync(iso));
 var rewritten=Path.Combine(temp,"rewritten.iso");await executor.ExecuteAsync(new(XisoMode.Rewrite,iso,rewritten,false,false),backend,Log,CancellationToken.None);
 Check(File.Exists(rewritten)&&SHA256.HashData(await File.ReadAllBytesAsync(iso)).SequenceEqual(originalHash),"already-optimized rewrite retains source");
 // Remove only the optimization marker from a dummy image to exercise actual rewrite.
 var unoptimized=Path.Combine(temp,"unoptimized.iso");File.Copy(iso,unoptimized);
 await using(var stream=new FileStream(unoptimized,FileMode.Open,FileAccess.Write)) {stream.Position=31337;await stream.WriteAsync(new byte[32]);}
 var unoptimizedHash=SHA256.HashData(await File.ReadAllBytesAsync(unoptimized));
 var actualRewrite=Path.Combine(temp,"actual-rewrite.iso");await executor.ExecuteAsync(new(XisoMode.Rewrite,unoptimized,actualRewrite,false,false),backend,Log,CancellationToken.None);
 Check(File.Exists(actualRewrite)&&SHA256.HashData(await File.ReadAllBytesAsync(unoptimized)).SequenceEqual(unoptimizedHash),"actual rewrite retains unoptimized source");
 await executor.ExecuteAsync(new(XisoMode.List,actualRewrite,"",false,false),backend,Log,CancellationToken.None);Check(true,"rewritten image can be listed");
 var corrupt=Path.Combine(temp,"corrupt.iso");await File.WriteAllBytesAsync(corrupt,new byte[4096]);
 var corruptTarget=Path.Combine(temp,"corrupt-output");await Reject(()=>executor.ExecuteAsync(new(XisoMode.Extract,corrupt,corruptTarget,false,false),backend,Log,CancellationToken.None),"invalid image reports failure");Check(!Directory.Exists(corruptTarget),"failure does not publish partial result");
 using(var cancelled=new CancellationTokenSource()) {cancelled.Cancel();try {await executor.ExecuteAsync(create with {Destination=Path.Combine(temp,"cancelled.iso")},backend,Log,cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException) {Check(true,"pre-cancelled operation stops");}}
 var clock=Stopwatch.StartNew();using(var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
 {
  try
  {
   await process.RunAsync(OperatingSystem.IsWindows()?"powershell.exe":"/bin/sh",OperatingSystem.IsWindows()?["-NoProfile","-Command","Start-Sleep -Seconds 30"]:["-c","sleep 30"],temp,Log,cancel.Token);
   throw new Exception("Process cancellation ignored");
  }
  catch(OperationCanceledException) {Check(clock.Elapsed<TimeSpan.FromSeconds(8),"running process tree cancellation");}
 }
 var queue=new JobQueueService(executor);queue.Jobs.Add(new(){Options=new(XisoMode.List,corrupt,"",false,false)});queue.Jobs.Add(new(){Options=new(XisoMode.List,iso,"",false,false)});
 await queue.RunAsync(backend,true,(_,line,error)=>Log(line,error),()=>Task.CompletedTask);
 Check(queue.Jobs[0].Status==JobStatus.Failed&&queue.Jobs[1].Status==JobStatus.Succeeded,"queue continues after failure");
 Check(!queue.IsRunning,"queue returns to idle");
 var stopQueue=new JobQueueService(executor);stopQueue.Jobs.Add(new(){Options=new(XisoMode.List,iso,"",false,false)});stopQueue.Jobs.Add(new(){Options=new(XisoMode.List,iso,"",false,false)});
 stopQueue.Changed+=()=>{if(stopQueue.Jobs.Any(j=>j.IsRunning))stopQueue.StopAfterCurrent=true;};
 await stopQueue.RunAsync(backend,true,(_,line,error)=>Log(line,error),()=>Task.CompletedTask);
 Check(stopQueue.Jobs[0].Status==JobStatus.Succeeded&&stopQueue.Jobs[1].Status==JobStatus.Waiting,"stop after current preserves pending jobs");
 stopQueue.CancelPending();Check(stopQueue.Jobs[1].Status==JobStatus.Cancelled,"cancel pending jobs");
 Check(new AppSettings().SkipSystemUpdate,"skip system update enabled by default");
 Check(UiScaling.ComputeAutoScale(1920,1040)==1&&UiScaling.ComputeAutoScale(1366,728)==0.85&&UiScaling.ComputeAutoScale(1280,640)==0.75&&UiScaling.ComputeAutoScale(1093,575)==0.70,"auto scale steps for compact displays");
 var batch=Path.Combine(temp,"batch");Directory.CreateDirectory(Path.Combine(batch,"nested"));
 foreach(var name in new[]{"first.ISO","split.1.iso","split.2.iso","ignore.txt"})await File.WriteAllTextAsync(Path.Combine(batch,name),"fixture");
 await File.WriteAllTextAsync(Path.Combine(batch,"nested","deep.xiso"),"fixture");await File.WriteAllTextAsync(Path.Combine(batch,"nested","archive.cci"),"fixture");
 Check(FolderBatchService.EnumerateImages(batch,false,false,CancellationToken.None).Count==3,"folder batch top-level ISO discovery");
 Check(FolderBatchService.EnumerateImages(batch,true,false,CancellationToken.None).Count==4,"recursive folder batch");
 var advanced=FolderBatchService.EnumerateImages(batch,true,true,CancellationToken.None);
 Check(advanced.Count==4&&!advanced.Any(f=>f.EndsWith("split.2.iso")),"advanced batch deduplicates split parts and finds compressed images");
 var option=new XisoJobOptions(XisoMode.Convert,corrupt,Path.Combine(temp,"god-output"),false,false,ConsolePlatform.Xbox360,BackendEngine.XgdTool,OutputFormat.GoD);
 foreach(var format in Enum.GetValues<OutputFormat>().Where(f=>f is not (OutputFormat.CCI or OutputFormat.CSO)))
 {
  var mapped=XgdToolService.BuildArguments(option with {Format=format},"output");
  Check(mapped.Contains("--offline")&&mapped.Count(flag=>new[]{"--god","--xiso","--cci","--cso","--zar","--xbox360","--extract"}.Contains(flag))==1,"XGDTool offline and exclusive output: "+format);
 }
 foreach(var scrub in Enum.GetValues<ScrubMode>())
 {
  var mapped=XgdToolService.BuildArguments(option with {Format=OutputFormat.XISO,Scrub=scrub,Split=true},"output");
  Check(mapped.Contains("--split")&&mapped.Contains("--offline")&&mapped.Contains("--full-scrub")== (scrub==ScrubMode.Full)&&mapped.Contains("--partial-scrub")== (scrub==ScrubMode.Partial),"scrub and split flags: "+scrub);
 }
 var og=option with {Platform=ConsolePlatform.Xbox,Mode=XisoMode.Extract,Format=OutputFormat.Extract,Advanced=true,PatchMedia=true,RenameXbe=true};
 Check(new[]{"--am-patch","--rename"}.All(XgdToolService.BuildArguments(og,"output").Contains),"original Xbox XBE flags");
 Check(!XgdToolService.BuildArguments(option with {Offline=false},"output").Contains("--offline"),"online mode explicit opt-in");
 Check(BackendPolicy.Normalize(og).Engine==BackendEngine.XgdTool,"advanced Xbox routes to XGD");
 foreach(var format in new[]{OutputFormat.CCI,OutputFormat.CSO})
 {
  try {XgdToolService.BuildArguments(option with {Format=format},"output");throw new Exception("Unsupported Xbox 360 format accepted");}catch(ArgumentException){Check(true,"Xbox 360 rejects "+format);}
 }
 Check(XgdToolService.BuildArguments(option with {Profile=DeviceProfile.Xenia,Format=OutputFormat.XISO,Scrub=ScrubMode.Full},"output").Contains("--offline"),"profile keeps offline flag without upstream auto-reset");
 Check(XgdToolService.BuildArguments(option with {Mode=XisoMode.Rewrite},"output").Contains("--full-scrub"),"XGDTool rewrite uses full scrub");
 var fake=Environment.ProcessPath!;var xgd=new XgdToolService(process);
 Check((await xgd.ProbeAsync(fake,CancellationToken.None)).Contains("1.0.0"),"XGDTool probe contract");
 await executor.ExecuteAsync(option,fake,Log,CancellationToken.None);
 Check(File.Exists(Path.Combine(option.Destination,"title-id","00007000","fixture.data")),"XGDTool output container preserves backend directory names");
 var failInput=Path.Combine(temp,"fail.iso");await File.WriteAllTextAsync(failInput,"fixture");var failOutput=Path.Combine(temp,"failed-xgd");
 await Reject(()=>executor.ExecuteAsync(option with {Source=failInput,Destination=failOutput},fake,Log,CancellationToken.None),"XGDTool detects error despite exit code zero");
 Check(!Directory.Exists(failOutput),"XGDTool failure does not publish partial output");
 var listLines=new List<string>();await executor.ExecuteAsync(option with {Mode=XisoMode.List,Destination=""},fake,(line,error)=>listLines.Add(line),CancellationToken.None);
 Check(listLines.Any(line=>XisoOutputParser.ParseListEntry(line) is {Size:8192}),"XGDTool listing on stderr is retained and parseable");
 var mixed=new JobQueueService(executor);
 mixed.Jobs.Add(new(){Options=new(XisoMode.List,iso,"",false,false)});mixed.Jobs.Add(new(){Options=option with {Destination=Path.Combine(temp,"mixed-output")}});
 await mixed.RunAsync(options=>options.Engine==BackendEngine.XgdTool?fake:backend,true,(_,line,error)=>Log(line,error),()=>Task.CompletedTask);
 Check(mixed.Jobs.All(job=>job.Status==JobStatus.Succeeded),"mixed queue resolves backend per job");
 var state=new SettingsService(Path.Combine(temp,"legal-state"));var legalPath=Path.Combine(temp,"legal");Directory.CreateDirectory(legalPath);
 var missing=new LegalService(state,legalPath);Check(!missing.DocumentsReady&&!missing.IsAccepted,"missing legal documents block acceptance");
 try{await missing.AcceptAsync();throw new Exception("Missing legal documents were accepted");}catch(InvalidOperationException){Check(true,"legal acceptance cannot bypass missing documents");}
 foreach(var name in LegalService.RequiredFiles)await File.WriteAllTextAsync(Path.Combine(legalPath,name),"# Test fixture\n\nNot a legal document: "+name);
 var legal=new LegalService(state,legalPath);Check(legal.DocumentsReady&&!legal.IsAccepted,"complete legal fixture requires initial acceptance");
 await legal.AcceptAsync();Check(new LegalService(state,legalPath).IsAccepted,"legal acceptance persists for exact document content");
 await File.AppendAllTextAsync(Path.Combine(legalPath,"EULA.md"),"\nChanged test fixture");Check(!new LegalService(state,legalPath).IsAccepted,"changed legal documents require acceptance again");
 foreach(var mode in Enum.GetValues<XisoMode>())
 {
  Check(BackendPolicy.For(ConsolePlatform.Xbox360,mode)==BackendEngine.XgdTool,"Xbox 360 routes to XGDTool: "+mode);
  Check(BackendPolicy.For(ConsolePlatform.Xbox,mode)==(mode==XisoMode.Convert?BackendEngine.XgdTool:BackendEngine.ExtractXiso),"Xbox backend routing: "+mode);
 }
 var stale=option with {Engine=BackendEngine.ExtractXiso,Destination=Path.Combine(temp,"stale-engine-output")};
 var routed=new JobQueueService(executor);routed.Jobs.Add(new(){Options=stale});bool resolvedXgd=false;
 await routed.RunAsync(options=>{resolvedXgd=options.Engine==BackendEngine.XgdTool;return fake;},true,(_,line,error)=>Log(line,error),()=>Task.CompletedTask);
 Check(resolvedXgd&&routed.Jobs[0].Status==JobStatus.Succeeded,"stale serialized engine cannot route Xbox 360 to extract-xiso");
 var publishedLegal=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../..","docs","legal"));
 if(Directory.Exists(publishedLegal))
 {
  var actual=new LegalService(new SettingsService(Path.Combine(temp,"actual-legal-state")),publishedLegal);
  Check(actual.DocumentsReady,"all supplied publisher documents and GPL are ready");
  Check(actual.CurrentMarkdown.Contains("GNU GENERAL PUBLIC LICENSE")&&actual.CurrentMarkdown.Contains("Sven Klocke")&&actual.CurrentMarkdown.Contains("Privacy Policy"),"actual publisher documents and GPL appear in the first-start content");
  await actual.AcceptAsync();Check(new LegalService(new SettingsService(Path.Combine(temp,"actual-legal-state")),publishedLegal).IsAccepted,"actual documents can be accepted and restored");
 }
 var movable=new SettingsService(Path.Combine(temp,"movable"));await movable.SaveAsync("settings.json",new AppSettings{OutputDirectory="saved-output"});await movable.SaveAsync("queue.json",new[]{"saved-job"});await movable.SaveAsync("legal-acceptance.json",new LegalAcceptance("fixture",DateTimeOffset.UtcNow));
 var moved=Path.Combine(temp,"moved");await movable.MoveDataDirectoryAsync(moved);var restarted=new SettingsService(movable.LocatorDirectory);
 Check(restarted.DataDirectory==moved&&restarted.LoadSettings().OutputDirectory=="saved-output"&&restarted.Load("queue.json",()=>Array.Empty<string>()).Single()=="saved-job","settings and queue migration survives restart");
 Check(restarted.Load<LegalAcceptance?>("legal-acceptance.json",()=>null)?.Digest=="fixture","migration retains acceptance");
 await Reject(()=>movable.MoveDataDirectoryAsync(movable.LocatorDirectory),"migration rejects existing state without overwriting");
 Check(File.Exists(Path.Combine(movable.LocatorDirectory,"settings.json")),"migration retains previous data backup");
 // ---------- v0.1.8 ----------
 // Job planning shared by GUI and CLI.
 var planOut=Path.Combine(temp,"plan-output");var request=new JobRequest(XisoMode.Extract,ConsolePlatform.Xbox,planOut);
 var planned=JobPlanner.Plan(request,iso,[]);Check(planned.Destination==Path.Combine(planOut,"sample image")&&planned.SkipSystemUpdate&&planned.Engine==BackendEngine.ExtractXiso,"planner names Xbox Classic extraction and keeps Skip $SystemUpdate");
 try{JobPlanner.Plan(request,iso,[planned]);throw new Exception("Duplicate accepted");}catch(IOException){Check(true,"planner rejects a duplicate source");}
 Directory.CreateDirectory(Path.Combine(planOut,"sample image"));Check(JobPlanner.Plan(request,iso,[]).Destination.EndsWith("sample image (2)"),"planner avoids existing destinations");
 var preset=JobPlanner.ApplyProfile(new JobRequest(XisoMode.Extract,ConsolePlatform.Xbox360,planOut,Profile:DeviceProfile.Xenia));
 Check(preset.Mode==XisoMode.Convert&&preset.Format==OutputFormat.XISO&&preset.Scrub==ScrubMode.Full,"Xenia profile preset");
 Check(JobPlanner.Plan(new JobRequest(XisoMode.Convert,ConsolePlatform.Xbox360,planOut,OutputFormat.GoD,FlattenOutput:true),iso,[]).FlattenOutput,"library targets request flattened output");

 // Title information: synthetic XBE (with DXT1 title image) packed into a real XISO by extract-xiso.
 static void U32(byte[] b,int o,uint v)=>System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o),v);
 static void B32(byte[] b,int o,uint v)=>System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o),v);
 var xpr=new byte[32+4*8];System.Text.Encoding.ASCII.GetBytes("XPR0").CopyTo(xpr,0);U32(xpr,4,(uint)xpr.Length);U32(xpr,8,32);U32(xpr,24,(0x0Cu<<8)|(3u<<20)|(3u<<24));
 for(var block=0;block<4;block++){xpr[32+block*8]=0x00;xpr[32+block*8+1]=0xF8;}
 var xbe=new byte[0x1000+xpr.Length];System.Text.Encoding.ASCII.GetBytes("XBEH").CopyTo(xbe,0);
 const uint xbeBase=0x10000;U32(xbe,0x104,xbeBase);U32(xbe,0x108,0x1000);U32(xbe,0x118,xbeBase+0x200);U32(xbe,0x11C,1);U32(xbe,0x120,xbeBase+0x300);
 U32(xbe,0x200+0x08,0x4D530004);System.Text.Encoding.Unicode.GetBytes("Fixture Game").CopyTo(xbe,0x200+0x0C);U32(xbe,0x200+0xA0,1|4);U32(xbe,0x200+0xAC,0x102);
 U32(xbe,0x300+0x0C,0x1000);U32(xbe,0x300+0x10,(uint)xpr.Length);U32(xbe,0x300+0x14,xbeBase+0x400);System.Text.Encoding.ASCII.GetBytes("$$XTIMAGE\0").CopyTo(xbe,0x400);xpr.CopyTo(xbe,0x1000);
 var game=Path.Combine(temp,"title game");Directory.CreateDirectory(game);await File.WriteAllBytesAsync(Path.Combine(game,"default.xbe"),xbe);
 var classic=TitleInfoReader.TryRead(game);
 Check(classic is {Name:"Fixture Game",TitleId:0x4D530004,TitleIdCode:"MS-004"}&&classic.Region=="NTSC-U / PAL","XBE certificate: name, Title ID and region");
 Check(classic!.IconPng is {Length:>8} png&&png.AsSpan(0,8).SequenceEqual(new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A}),"XBE title image decoded to PNG");
 var titleIso=Path.Combine(temp,"title game.iso");await executor.ExecuteAsync(new(XisoMode.Create,game,titleIso,false,true),backend,Log,CancellationToken.None);
 Check(TitleInfoReader.TryRead(titleIso) is {Name:"Fixture Game",TitleId:0x4D530004},"title info read from XDVDFS image without extraction");
 // Xbox 360: synthetic XEX2 header and GoD/STFS content header.
 var xex=new byte[0x400];System.Text.Encoding.ASCII.GetBytes("XEX2").CopyTo(xex,0);B32(xex,0x08,0x400);B32(xex,0x10,0x200);B32(xex,0x14,1);B32(xex,0x18,0x00040006);B32(xex,0x1C,0x100);
 B32(xex,0x100,0x11223344);B32(xex,0x104,0x20000400);B32(xex,0x10C,0x4D5307E6);xex[0x112]=1;xex[0x113]=1;B32(xex,0x200+0x178,0x000000FF);
 var xexFile=Path.Combine(temp,"default.xex");await File.WriteAllBytesAsync(xexFile,xex);
 Check(TitleInfoReader.TryRead(xexFile) is {TitleIdHex:"4D5307E6",MediaIdHex:"11223344",Region:"NTSC-U",Name:null},"XEX header: Title ID, Media ID, region (no name without GoD)");
 var god=Path.Combine(temp,"god","4D5307E6","00007000");Directory.CreateDirectory(god);var stfs=new byte[0x971A];System.Text.Encoding.ASCII.GetBytes("LIVE").CopyTo(stfs,0);
 B32(stfs,0x360,0x4D5307E6);B32(stfs,0x354,0x11223344);System.Text.Encoding.BigEndianUnicode.GetBytes("Fixture 360").CopyTo(stfs,0x1691);
 var thumb=PngWriter.Encode(new byte[4*4*4],4,4);B32(stfs,0x1716,(uint)thumb.Length);thumb.CopyTo(stfs,0x571A);await File.WriteAllBytesAsync(Path.Combine(god,"ABCDEF0123"),stfs);
 Check(TitleInfoReader.TryRead(Path.Combine(temp,"god")) is {Name:"Fixture 360",TitleIdHex:"4D5307E6",HasIcon:true},"GoD header: name, Title ID and icon");
 Check(TitleInfoReader.TryRead(corrupt)==null,"unknown image yields no title info");

 // Target preview, flattened XGD output, reports, crash log and JSON metadata.
 var previewInfo=TargetPreview.Describe(new XisoJobOptions(XisoMode.Extract,iso,Path.Combine(temp,"preview-out"),true,false));
 Check(previewInfo.Layout[0].StartsWith(Path.Combine(temp,"preview-out"))&&previewInfo.EstimatedBytes==new FileInfo(iso).Length&&previewInfo.FreeBytes is >0,"target preview: path, estimate and free space");
 var flatDir=Path.Combine(temp,"flat");Directory.CreateDirectory(flatDir);
 var flatJob=new XisoJob{Options=option with {Destination=Path.Combine(flatDir,"game.god-output"),FlattenOutput=true},Status=JobStatus.Waiting};
 var flatQueue=new JobQueueService(executor);flatQueue.Jobs.Add(flatJob);await flatQueue.RunAsync(_=>fake,true,(_,line,error)=>Log(line,error),()=>Task.CompletedTask);
 Check(flatJob.Status==JobStatus.Succeeded&&flatJob.PublishedPath==Path.Combine(flatDir,"fixture.data")&&File.Exists(flatJob.PublishedPath)&&!Directory.Exists(flatJob.Options.Destination),"single-file XGD result is flattened into the library folder");
 var report=RunReportService.Build(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow,queue.Jobs.Concat(flatQueue.Jobs),"test");
 Check(report.Entries.Count==3&&report.Failed==1&&RunReportService.ToText(report).Contains("corrupt")&&RunReportService.ToHtml(report).Contains("<table"),"run report TXT and HTML");
 var reportFile=Path.Combine(temp,"reports","run.html");await RunReportService.WriteAsync(report,reportFile);Check(File.ReadAllText(reportFile).StartsWith("<!doctype html>"),"HTML report written");
 var crash=CrashLog.Write(temp,new InvalidOperationException("fixture crash"),"verification");
 Check(crash!=null&&File.ReadAllText(crash).Contains("fixture crash")&&CrashLog.TakePendingCrash(temp)==crash&&CrashLog.TakePendingCrash(temp)==null,"crash log written once and reported once");
 var themed=new SettingsService(Path.Combine(temp,"themed"));await themed.SaveAsync("settings.json",new AppSettings{Theme="Light",Accent="Blue",ReportMode="Html",OutputTarget=OutputTarget.RetroBat,RetroBatRoot=@"C:\RetroBat"});
 var loadedSettings=themed.LoadSettings();Check(loadedSettings is {Theme:"Light",Accent:"Blue",ReportMode:"Html",OutputTarget:OutputTarget.RetroBat}&&EmulatorService.LibraryFolder(OutputTarget.RetroBat,ConsolePlatform.Xbox360,loadedSettings)==Path.Combine(@"C:\RetroBat","roms","xbox360"),"v0.1.8 settings persist (source-generated JSON) and library folders resolve");
 Check(EmulatorService.LibraryFolder(OutputTarget.XemuLibrary,ConsolePlatform.Xbox360,loadedSettings)==null,"Xemu library is not offered for Xbox 360");
 var launch=EmulatorService.BuildLaunch(ConsolePlatform.Xbox,"xemu.exe",iso);Check(launch.ArgumentList.SequenceEqual(new[]{"-dvd_path",iso}),"Xemu launch arguments");
 Console.WriteLine($"SUCCESS: {checks} checks passed.");return 0;
}
catch(Exception ex) {Console.Error.WriteLine(ex);foreach(var line in logs.TakeLast(30))Console.Error.WriteLine(line);return 1;}
finally {try {Directory.Delete(temp,true);}catch(IOException) {}}
