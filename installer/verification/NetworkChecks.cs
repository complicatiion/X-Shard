using System.Security.Cryptography;
using XShard.Models;
using XShard.Services;

/// <summary>
/// v0.2.0 checks: network path handling, resumable copies with simulated outages, local staging for network extract
/// targets, job restart after an interruption, library scan by Title ID, console transfer plans and a full FTP
/// transfer (GoD + extracted) against a local test server that drops the connection mid-upload.
/// </summary>
internal static class NetworkChecks
{
 private static readonly RetryPolicy Fast=new(TimeSpan.FromSeconds(20),TimeSpan.FromMilliseconds(25),6,TimeSpan.FromSeconds(2));

 public static async Task RunAsync(string temp,string fakeXgd,string godRoot,byte[] xex,Action<bool,string> check,Action<string,bool> log)
 {
  // ---------- paths ----------
  check(NetworkPath.IsUnc(@"\\nas\roms\Xbox 360\game.iso")&&!NetworkPath.IsUnc(@"\\?\C:\games")&&NetworkPath.IsUnc(@"\\?\UNC\nas\roms\x"),"UNC detection (incl. long-path form, excluding \\\\?\\C:)");
  check(NetworkPath.ShareRoot(@"\\nas\roms\Xbox 360\game.iso")==@"\\nas\roms"&&NetworkPath.ShareRoot(@"\\?\UNC\nas\roms\x\y")==@"\\nas\roms","share root of UNC paths");
  check(NetworkPath.IsNetwork(@"\\nas\roms")&&!NetworkPath.IsNetwork(temp)&&NetworkPath.ToUniversal(temp)==temp,"local paths stay local, UNC paths are network");
  check(ResilientIO.IsTransient(new IOException("net name deleted",unchecked((int)0x80070040)))&&ResilientIO.IsTransient(new IOException("timeout",110))&&ResilientIO.IsTransient(new TimeoutException()),"network errors are transient (Win32 64, errno 110, timeout)");
  check(!ResilientIO.IsTransient(new IOException("disk full",unchecked((int)0x80070070)))&&!ResilientIO.IsTransient(new IOException("plain")),"disk full and ordinary I/O errors are not retried");

  // ---------- resumable file copy with an outage ----------
  var outageProbes=0;
  NetworkPath.ReachabilityOverride=_=>Interlocked.Increment(ref outageProbes)>2; // share "returns" on the third probe
  var source=Path.Combine(temp,"net-source.bin");var bytes=RandomNumberGenerator.GetBytes(5*1024*1024+123);await File.WriteAllBytesAsync(source,bytes);
  var copy=Path.Combine(temp,"net-target","copy.bin");var faults=0;
  ResilientIO.FaultInjection=(path,offset)=>offset>=2*1024*1024&&path.EndsWith("copy.bin.partial")&&Interlocked.Increment(ref faults)==1?new IOException("simulated: network name no longer available",unchecked((int)0x80070040)):null;
  var statuses=new List<string>();
  await ResilientIO.CopyFileAsync(source,copy,null,text=>{lock(statuses)statuses.Add(text);},CancellationToken.None,Fast);
  check(faults>=1&&File.ReadAllBytes(copy).AsSpan().SequenceEqual(bytes)&&!File.Exists(copy+".partial"),"copy resumes after a simulated SMB outage and is byte-identical");
  check(statuses.Any(s=>s.StartsWith("Waiting for"))&&statuses.Any(s=>s.Contains("Connection restored")),"outage is reported (waiting / restored)");

  // ---------- parallel folder copy ----------
  NetworkPath.ReachabilityOverride=_=>true;
  var tree=Path.Combine(temp,"net-tree");
  for(var i=0;i<60;i++){var file=Path.Combine(tree,$"dir{i%5}",$"file{i}.bin");Directory.CreateDirectory(Path.GetDirectoryName(file)!);await File.WriteAllBytesAsync(file,RandomNumberGenerator.GetBytes(1000+i*37));}
  Directory.CreateDirectory(Path.Combine(tree,"empty"));
  faults=0;ResilientIO.FaultInjection=(path,_)=>path.EndsWith("file17.bin")&&Interlocked.Increment(ref faults)==1?new IOException("simulated reset",104):null;
  var treeCopy=Path.Combine(temp,"net-tree-copy");
  await ResilientIO.CopyDirectoryAsync(tree,treeCopy,8,null,null,CancellationToken.None,Fast);
  ResilientIO.FaultInjection=null;
  var same=Directory.GetFiles(tree,"*",SearchOption.AllDirectories).All(f=>File.ReadAllBytes(f).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(treeCopy,Path.GetRelativePath(tree,f)))));
  check(same&&Directory.Exists(Path.Combine(treeCopy,"empty"))&&faults==1,"parallel folder copy (8 files in flight) survives an interruption");

  // ---------- network extract target: local staging + parallel publish ----------
  var nas=Path.Combine(temp,"nas");Directory.CreateDirectory(nas);
  NetworkPath.TreatAsNetwork=p=>Path.GetFullPath(p).StartsWith(nas,StringComparison.OrdinalIgnoreCase);
  var executor=new JobExecutor(new XisoProcessService()){WorkDirectory=Path.Combine(temp,"work"),Network=Fast};
  var iso=Path.Combine(nas,"Game 360.iso");await File.WriteAllBytesAsync(iso,RandomNumberGenerator.GetBytes(4096));
  var extractOptions=new XisoJobOptions(XisoMode.Convert,iso,Path.Combine(nas,"library","Game 360.extract-output"),false,false,ConsolePlatform.Xbox360,BackendEngine.XgdTool,OutputFormat.Extract);
  var lines=new List<string>();
  var published=await executor.ExecuteAsync(extractOptions,fakeXgd,(line,error)=>{lock(lines)lines.Add(line);log(line,error);},CancellationToken.None);
  check(File.Exists(Path.Combine(published,"title-id","00007000","fixture.data"))&&lines.Any(l=>l.Contains("prepared locally")),"extract to a network share is staged locally and published");
  check(!Directory.EnumerateDirectories(executor.WorkDirectory,".xshard-*").Any()&&!Directory.EnumerateDirectories(Path.Combine(nas,"library"),".xshard-*").Any(),"no staging folders remain after publishing");

  // ---------- restart after an interruption while the backend runs ----------
  var probes=0;NetworkPath.ReachabilityOverride=_=>Interlocked.Increment(ref probes) is not (2 or 3); // probes 2–3 (during the run) fail
  var failing=Path.Combine(nas,"fail.iso");await File.WriteAllTextAsync(failing,"fixture");
  executor.MaxNetworkRestarts=1;lines.Clear();
  try{await executor.ExecuteAsync(extractOptions with {Source=failing,Destination=Path.Combine(nas,"library","fail.extract-output")},fakeXgd,(line,error)=>{lock(lines)lines.Add(line);},CancellationToken.None);check(false,"failing job must fail");}
  catch(IOException){}
  check(lines.Any(l=>l.StartsWith("Network interruption during the job")),"interrupted network job is restarted once, then fails cleanly");
  check(!Directory.Exists(Path.Combine(nas,"library","fail.extract-output")),"restarted job publishes nothing incomplete");
  NetworkPath.ReachabilityOverride=null;NetworkPath.TreatAsNetwork=null;

  // ---------- library scan ----------
  var library=Path.Combine(temp,"library-scan");
  CopyTree(godRoot,Path.Combine(library,"GoD"));
  var extracted=Path.Combine(library,"Extracted Game");Directory.CreateDirectory(extracted);await File.WriteAllBytesAsync(Path.Combine(extracted,"default.xex"),xex);
  await File.WriteAllBytesAsync(Path.Combine(extracted,"data.bin"),new byte[64]);
  await File.WriteAllBytesAsync(Path.Combine(library,"unknown.iso"),new byte[8192]);await File.WriteAllBytesAsync(Path.Combine(library,"unknown.2.iso"),new byte[16]);
  var entries=await LibraryScanner.ScanAsync(library,null,CancellationToken.None);
  check(entries.Count==3&&entries.Count(e=>e.Kind==LibraryKind.GoD)==1&&entries.Count(e=>e.Kind==LibraryKind.Extracted)==1&&entries.Count(e=>e.Kind==LibraryKind.Image)==1,"library scan finds GoD, extracted game and image (split parts once)");
  var keys=LibraryScanner.Keys(entries);
  check(keys.Count==1&&keys.Contains("4D5307E6:11223344:1"),"GoD and extracted copy of the same disc share one Title ID key");

  // ---------- console transfer plans ----------
  var profile=new ConsoleProfile{Host="127.0.0.1",User="xboxftp",Drive="Hdd1"};
  var godPlan=ConsoleTransferService.Plan(Path.Combine(library,"GoD"),profile);
  check(godPlan.Kind==LibraryKind.GoD&&godPlan.RemoteTarget=="/Hdd1/Content/0000000000000000/4D5307E6/00007000"&&godPlan.CommitLast.Count==1&&godPlan.CommitLast[0].Remote.EndsWith("/00007000/ABCDEF0123"),"GoD plan targets Content/0000000000000000/<TitleID>/00007000, header last");
  var extractPlan=ConsoleTransferService.Plan(library+Path.DirectorySeparatorChar+"Extracted Game",profile);
  check(extractPlan.Kind==LibraryKind.Extracted&&extractPlan.RemoteTarget=="/Hdd1/Games/Extracted Game"&&extractPlan.PartialFolder=="/Hdd1/Games/Extracted Game.xsp","extracted plan targets /Hdd1/Games/<name> via a .xsp folder");
  try{ConsoleTransferService.CheckFatx("A name that is far too long for the FATX file system.bin");check(false,"FATX limit");}catch(IOException){check(true,"names longer than 42 characters are rejected (FATX)");}
  check(ConsoleTransferService.FatxName("Halo: Reach \"GOTY\" Edition with a very long title")is {Length:<=38} name&&!name.Contains(':'),"folder names are made FATX-safe and short enough");

  // ---------- FTP transfer against a local server (drops once) ----------
  var ftpRoot=Path.Combine(temp,"ftp-root");Directory.CreateDirectory(Path.Combine(ftpRoot,"Hdd1"));
  var dataFolder=Path.Combine(library,"GoD","4D5307E6","00007000","ABCDEF0123.data");Directory.CreateDirectory(dataFolder);
  var godData=RandomNumberGenerator.GetBytes(3*1024*1024+17);await File.WriteAllBytesAsync(Path.Combine(dataFolder,"Data0000"),godData);
  await using(var server=new FtpTestServer(ftpRoot,"xboxftp","secret"){DropAfterBytes=1024*1024})
  {
   profile.Port=server.Port;
   var service=new ConsoleTransferService(profile,"secret"){Policy=Fast};
   check((await service.TestAsync(CancellationToken.None)).Contains("Hdd1"),"FTP test lists the console drives");
   var ftpLog=new List<string>();double lastPercent=0;
   var remote=await service.UploadAsync(Path.Combine(library,"GoD"),(line,_)=>{lock(ftpLog)ftpLog.Add(line);},p=>lastPercent=p,CancellationToken.None);
   var onServer=Path.Combine(ftpRoot,"Hdd1","Content","0000000000000000","4D5307E6","00007000");
   check(remote==godPlan.RemoteTarget&&server.HasDropped&&File.ReadAllBytes(Path.Combine(onServer,"ABCDEF0123.data","Data0000")).AsSpan().SequenceEqual(godData)&&File.Exists(Path.Combine(onServer,"ABCDEF0123")),"GoD upload resumes after the console drops the connection and is byte-identical");
   check(server.Completed.Last().EndsWith("/00007000/ABCDEF0123")&&!Directory.GetFiles(onServer,"*.xstmp",SearchOption.AllDirectories).Any()&&lastPercent>=99.9,"STFS header becomes visible last, no temporary files remain");
   check(await service.UploadAsync(Path.Combine(library,"GoD"),(_,_)=>{},null,CancellationToken.None)==null,"a game that is already on the console is skipped");
   var extractedRemote=await service.UploadAsync(extracted,(_,_)=>{},null,CancellationToken.None);
   check(extractedRemote=="/Hdd1/Games/Extracted Game"&&File.Exists(Path.Combine(ftpRoot,"Hdd1","Games","Extracted Game","default.xex"))&&!Directory.Exists(Path.Combine(ftpRoot,"Hdd1","Games","Extracted Game.xsp")),"extracted game is uploaded into .xsp and renamed when complete");
   var wrong=new ConsoleTransferService(profile,"wrong"){Policy=Fast};
   try{await wrong.TestAsync(CancellationToken.None);check(false,"wrong password must fail");}catch(IOException ex){check(ex.Message.Contains("login failed"),"wrong FTP password gives a clear message");}
  }

  // ---------- v0.2.1: console FTP server without APPE/REST ----------
  var plainRoot=Path.Combine(temp,"ftp-root-noresume");Directory.CreateDirectory(Path.Combine(plainRoot,"Hdd1"));
  await using(var plain=new FtpTestServer(plainRoot,"xboxftp","secret"){DropAfterBytes=1024*1024,SupportsResume=false})
  {
   var plainProfile=new ConsoleProfile{Host="127.0.0.1",Port=plain.Port,User="xboxftp",Drive="Hdd1"};
   var service=new ConsoleTransferService(plainProfile,"secret"){Policy=Fast};
   var plainLog=new List<string>();
   var remote=await service.UploadAsync(Path.Combine(library,"GoD"),(line,_)=>{lock(plainLog)plainLog.Add(line);},null,CancellationToken.None);
   var onServer=Path.Combine(plainRoot,"Hdd1","Content","0000000000000000","4D5307E6","00007000");
   check(remote!=null&&plain.HasDropped&&File.ReadAllBytes(Path.Combine(onServer,"ABCDEF0123.data","Data0000")).AsSpan().SequenceEqual(godData),"without APPE/REST an interrupted file is sent again from the start (no endless retries)");
   log("FTP server without resume: fallback "+(service.ResumeUnsupported?"used":"not needed (client restarted the file itself)"),false);
  }

  // ---------- v0.2.1: scrub defaults and warnings ----------
  var consoleRequest=JobPlanner.ApplyProfile(new JobRequest(XisoMode.Convert,ConsolePlatform.Xbox360,temp,OutputFormat.XISO,DeviceProfile.Xbox360));
  check(consoleRequest is {Format:OutputFormat.GoD,Scrub:ScrubMode.None},"Xbox 360 console profile: GoD without scrub (padding kept)");
  check(JobPlanner.ApplyProfile(new JobRequest(XisoMode.Convert,ConsolePlatform.Xbox360,temp,OutputFormat.GoD,DeviceProfile.Xenia)) is {Format:OutputFormat.XISO,Scrub:ScrubMode.Full},"Xenia profile keeps full-scrub XISO");
  var scrubbedGod=new XisoJobOptions(XisoMode.Convert,"a.iso","b",false,false,ConsolePlatform.Xbox360,BackendEngine.XgdTool,OutputFormat.GoD,DeviceProfile.Custom,ScrubMode.Full);
  check(JobPlanner.ConsoleScrubWarning(scrubbedGod)!=null&&JobPlanner.ConsoleScrubWarning(scrubbedGod with {Scrub=ScrubMode.None})==null&&JobPlanner.ConsoleScrubWarning(scrubbedGod with {Format=OutputFormat.XISO})==null,"scrubbed console GoD is flagged, unscrubbed GoD and XISO are not");
  check(JobPlanner.OutputSummary(scrubbedGod with {Scrub=ScrubMode.None})=="GoD · Scrub None"&&JobPlanner.OutputSummary(scrubbedGod with {Mode=XisoMode.Rewrite}).Contains("Scrub Full"),"output summary names format and scrub, also for Rewrite");

  check(LibraryScanner.IsProbablyScrubbed(Path.Combine(library,"GoD","4D5307E6","00007000","ABCDEF0123")),"small GoD data is marked as probably scrubbed");

  // ---------- job queue: convert → console pipeline ----------
  var queue=new JobQueueService(new JobExecutor(new XisoProcessService()){Network=Fast});
  var uploaded=new List<string>();
  queue.Uploader=(job,local,_,progress,_)=>{uploaded.Add(local);progress(100);return Task.FromResult<string?>("/Hdd1/Content/0000000000000000/title-id/00007000");};
  var transfer=Path.Combine(temp,"console-transfer");Directory.CreateDirectory(transfer);
  var queueIso=Path.Combine(temp,"queue 360.iso");await File.WriteAllBytesAsync(queueIso,new byte[2048]);
  var godOptions=new XisoJobOptions(XisoMode.Convert,queueIso,Path.Combine(transfer,"queue 360.god-output"),false,false,ConsolePlatform.Xbox360,BackendEngine.XgdTool,OutputFormat.GoD,UploadToConsole:true,DeleteAfterUpload:true);
  var sendJob=new XisoJob{Options=godOptions,Status=JobStatus.Waiting};queue.Jobs.Add(sendJob);
  var uploadOnly=new XisoJob{Options=JobPlanner.PlanUpload(Path.Combine(library,"GoD"),[]),Status=JobStatus.Waiting};queue.Jobs.Add(uploadOnly);
  await queue.RunAsync(_=>fakeXgd,true,(_,line,error)=>log(line,error),()=>Task.CompletedTask);
  check(sendJob.Status==JobStatus.Succeeded&&sendJob.RemotePath!=null&&!Directory.Exists(godOptions.Destination)&&uploaded.Count==2,"converted result is sent to the console and the transfer copy is deleted");
  check(uploadOnly.Status==JobStatus.Succeeded&&Directory.Exists(Path.Combine(library,"GoD")),"send-only job never deletes the user's folder");
  try{JobPlanner.ValidateConsoleRequest(new JobRequest(XisoMode.Convert,ConsolePlatform.Xbox360,transfer,OutputFormat.XISO));check(false,"XISO to console");}catch(ArgumentException){check(true,"console target accepts GoD or extract only");}
 }
 private static void CopyTree(string from,string to)
 {
  foreach(var dir in Directory.GetDirectories(from,"*",SearchOption.AllDirectories))Directory.CreateDirectory(Path.Combine(to,Path.GetRelativePath(from,dir)));
  Directory.CreateDirectory(to);
  foreach(var file in Directory.GetFiles(from,"*",SearchOption.AllDirectories))File.Copy(file,Path.Combine(to,Path.GetRelativePath(from,file)));
 }
}
