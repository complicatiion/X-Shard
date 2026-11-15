using System.IO.Compression;
namespace XShard.Services;

/// <summary>An emulator that can be downloaded on request from its official GitHub releases (never bundled with X-Shard).</summary>
public sealed record EmulatorSource(string Id,string DisplayName,string Owner,string Repository,string License,string ExeName,string ProjectUrl,IReadOnlyList<string> PreservedEntries)
{
 /// <summary>Where the releases come from.</summary>
 public string RepositoryUrl=>$"https://github.com/{Owner}/{Repository}";
}

public static class EmulatorCatalog
{
 /// <summary>Xenia Canary (Xbox 360), BSD-3-Clause. Release tags are commit hashes.</summary>
 public static readonly EmulatorSource Xenia=new("xenia","Xenia Canary","xenia-canary","xenia-canary-releases","BSD-3-Clause","xenia_canary.exe","https://github.com/xenia-canary/xenia-canary",
  ["content","cache","cache_host","patches","portable.txt","xenia-canary.config.toml"]);
 /// <summary>xemu (Xbox Classic), GPL-2.0. Needs MCPX boot ROM, flash BIOS and HDD image, which are never provided.</summary>
 public static readonly EmulatorSource Xemu=new("xemu","xemu","xemu-project","xemu","GPL-2.0","xemu.exe","https://github.com/xemu-project/xemu",
  ["system","xemu.toml","portable.txt"]);
 public static IReadOnlyList<EmulatorSource> All=>[Xenia,Xemu];

 /// <summary>The Windows x64 ZIP among the release assets (names change between releases, so this is a pattern).</summary>
 public static ReleaseAsset? PickAsset(EmulatorSource source,ReleaseInfo release)
 {
  bool Excluded(string n)=>n.Contains("dbg")||n.Contains("debug")||n.Contains("pdb")||n.Contains("symbols")||n.Contains("arm64")||n.Contains("aarch64");
  // Only files published in the releases of this very repository.
  var prefix=$"/{source.Owner}/{source.Repository}/releases/download/";
  bool FromRepository(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&GitHubReleaseClient.IsAllowed(uri)&&uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)&&uri.AbsolutePath.StartsWith(prefix,StringComparison.OrdinalIgnoreCase);
  var zips=release.Assets.Where(a=>a.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)&&FromRepository(a.DownloadUrl)).Select(a=>(Asset:a,Name:a.Name.ToLowerInvariant())).Where(a=>!Excluded(a.Name)).ToList();
  var windows=source.Id=="xenia"
   ?zips.Where(a=>a.Name.Contains("windows")||a.Name.Contains("win"))
   :zips.Where(a=>a.Name.Contains("win")&&(a.Name.Contains("x86_64")||a.Name.Contains("x64")||a.Name.Contains("amd64")));
  return windows.OrderByDescending(a=>a.Name.Contains("release")).ThenBy(a=>a.Name.Length).Select(a=>a.Asset).FirstOrDefault();
 }
}

/// <summary>
/// Installs or updates an emulator in X-Shard's "emulators" folder after the user agreed: download (verified), safe
/// extraction into a working folder, the user's own files (xemu "system" folder with BIOS/MCPX/HDD, Xenia content,
/// configuration) are carried over, then the folders are swapped; the previous version is kept as "&lt;id&gt;.previous".
/// Nothing is started automatically.
/// </summary>
public sealed class EmulatorInstaller(GitHubReleaseClient client,string emulatorsRoot)
{
 public const long MaxExtractedBytes=4_000_000_000;
 private const string ReleaseMarker=".xshard-release";
 public string Root=>emulatorsRoot;
 public string FolderFor(EmulatorSource source)=>Path.Combine(emulatorsRoot,source.Id);
 /// <summary>Folder where the user places the xemu system files (MCPX boot ROM, flash BIOS, HDD image). Created on demand.</summary>
 public string XemuSystemFolder=>Path.Combine(FolderFor(EmulatorCatalog.Xemu),"system");
 public string? InstalledTag(EmulatorSource source)
 {
  try{var marker=Path.Combine(FolderFor(source),ReleaseMarker);return File.Exists(marker)?File.ReadAllText(marker).Trim():null;}
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return null;}
 }
 public string? InstalledExe(EmulatorSource source)
 {
  var folder=FolderFor(source);if(!Directory.Exists(folder))return null;
  return Directory.EnumerateFiles(folder,source.ExeName,new EnumerationOptions{RecurseSubdirectories=true,MaxRecursionDepth=3,MatchCasing=MatchCasing.CaseInsensitive}).FirstOrDefault();
 }
 public Task<ReleaseInfo> GetLatestAsync(EmulatorSource source,CancellationToken token)=>client.GetLatestAsync(source.Owner,source.Repository,token);

 /// <summary>Downloads and installs the release. Returns the path of the emulator executable.</summary>
 public async Task<string> InstallAsync(EmulatorSource source,ReleaseInfo release,Action<string> log,Action<double>? progress,CancellationToken token)
 {
  var asset=EmulatorCatalog.PickAsset(source,release)??throw new IOException($"No Windows download found in {source.DisplayName} release {release.Tag}.");
  Directory.CreateDirectory(emulatorsRoot);
  var work=Path.Combine(emulatorsRoot,".xshard-download-"+Guid.NewGuid().ToString("N"));
  try
  {
   var zip=Path.Combine(work,Path.GetFileName(asset.Name));
   log($"Downloading {asset.Name} ({RunReportService.FormatBytes(asset.Size)}) from {source.RepositoryUrl}");
   var verified=await client.DownloadAsync(asset,zip,progress,token);
   log(verified?"SHA-256 checksum verified against GitHub.":"GitHub published no checksum for this file; size and archive were checked.");
   var extracted=Path.Combine(work,"extracted");
   await Task.Run(()=>ExtractSafely(zip,extracted,token),token);
   var exe=Directory.EnumerateFiles(extracted,source.ExeName,new EnumerationOptions{RecurseSubdirectories=true,MaxRecursionDepth=3,MatchCasing=MatchCasing.CaseInsensitive}).FirstOrDefault()
    ??throw new IOException($"{source.ExeName} was not found in the download.");
   // Root entries that came from the archive; everything else in the folder belongs to the user.
   var archiveEntries=Directory.EnumerateFileSystemEntries(extracted).Select(e=>Path.GetFileName(e)).ToList();
   await File.WriteAllTextAsync(Path.Combine(extracted,ReleaseMarker),release.Tag,token);
   await File.WriteAllLinesAsync(Path.Combine(extracted,ManifestFile),archiveEntries,token);
   var target=FolderFor(source);
   await Task.Run(()=>Swap(source,extracted,target),token);
   log($"{source.DisplayName} {release.Tag} installed in {target}.");
   log($"{source.DisplayName} {release.Tag} installed in {target}.");
   return Path.Combine(target,Path.GetRelativePath(extracted,exe));
  }
  finally{ResilientIO.TryDeleteDirectory(work);}
 }

 private const string ManifestFile=".xshard-files";
 /// <summary>
 /// Replaces the installed version without risking the user's files: (1) the installed folder is renamed to
 /// "&lt;id&gt;.previous" – if that fails, nothing was touched; (2) the user's entries (everything that did not come from the
 /// previous archive, plus the known user entries) are moved into the new version; (3) the new version takes the
 /// folder name. Any failure in (2)/(3) moves everything back. User files are never deleted.
 /// </summary>
 private static void Swap(EmulatorSource source,string extracted,string target)
 {
  var previous=target+".previous";
  if(!Directory.Exists(target)){Directory.Move(extracted,target);EnsureSystemFolder(source,target);return;}
  if(Directory.Exists(previous))Directory.Delete(previous,true); // the version before the installed one; contains no user files
  Directory.Move(target,previous);
  var moved=new List<(string From,string To)>();
  try
  {
   var shipped=File.Exists(Path.Combine(previous,ManifestFile))?File.ReadAllLines(Path.Combine(previous,ManifestFile)).ToHashSet(StringComparer.OrdinalIgnoreCase):null;
   foreach(var entry in Directory.EnumerateFileSystemEntries(previous).ToList())
   {
    var name=Path.GetFileName(entry);
    if(name is ReleaseMarker or ManifestFile)continue;
    var userEntry=source.PreservedEntries.Contains(name,StringComparer.OrdinalIgnoreCase)||shipped!=null&&!shipped.Contains(name);
    if(!userEntry)continue;
    var to=Path.Combine(extracted,name);
    if(Directory.Exists(to))Directory.Delete(to,true);else if(File.Exists(to))File.Delete(to); // the user's version wins
    if(Directory.Exists(entry))Directory.Move(entry,to);else File.Move(entry,to);
    moved.Add((entry,to));
   }
   Directory.Move(extracted,target);
  }
  catch
  {
   // Roll back: user entries return to the installed version, which gets its name back.
   foreach(var (from,to) in moved)
   {
    try
    {
     var current=Directory.Exists(target)?Path.Combine(target,Path.GetFileName(to)):to;
     if(Directory.Exists(current))Directory.Move(current,from);else if(File.Exists(current))File.Move(current,from);
    }
    catch(Exception){}
   }
   if(Directory.Exists(target)&&!Directory.Exists(previous+".failed"))try{Directory.Move(target,previous+".failed");}catch(Exception){}
   if(!Directory.Exists(target))try{Directory.Move(previous,target);}catch(Exception){}
   throw;
  }
  EnsureSystemFolder(source,target);
 }
 private static void EnsureSystemFolder(EmulatorSource source,string target)
 {
  if(source.Id==EmulatorCatalog.Xemu.Id)Directory.CreateDirectory(Path.Combine(target,"system"));
 }

 /// <summary>Extracts a ZIP; entries that would leave the target folder ("..", absolute paths) are rejected, total size is limited.</summary>
 public static void ExtractSafely(string zip,string destination,CancellationToken token)
 {
  var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;
  Directory.CreateDirectory(root);
  using var archive=ZipFile.OpenRead(zip);
  long total=0;
  foreach(var entry in archive.Entries)
  {
   token.ThrowIfCancellationRequested();
   var path=Path.GetFullPath(Path.Combine(root,entry.FullName));
   if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe path in the archive was rejected: "+entry.FullName);
   if(entry.FullName.EndsWith('/')||entry.FullName.EndsWith('\\')){Directory.CreateDirectory(path);continue;}
   total+=entry.Length;if(total>MaxExtractedBytes)throw new IOException("The archive is larger than allowed when extracted.");
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   entry.ExtractToFile(path,true);
  }
 }
}
