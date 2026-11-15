using XShard.Models;
namespace XShard.Services;
public enum LibraryKind { Image, GoD, Extracted }
/// <summary>One game found in a library folder (local or network share).</summary>
public sealed record LibraryEntry(string Path,LibraryKind Kind,TitleInfo? Title,bool ProbablyScrubbed=false)
{
 public string? Key=>LibraryScanner.KeyOf(Title);
 public string DisplayName=>Title?.Name is {Length:>0} name?name:System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar));
}
/// <summary>
/// Finds disc images, GoD packages and extracted games in a folder tree and identifies them by Title ID, Media ID and
/// disc number. Used to skip sources whose game already exists in the target library (any format).
/// </summary>
public static class LibraryScanner
{
 /// <summary>Identity of a disc: Title ID + Media ID + disc number. Null when the Title ID is unknown.</summary>
 public static string? KeyOf(TitleInfo? title)=>title?.TitleId is {} id?$"{id:X8}:{(title.MediaId is {} media?media.ToString("X8"):"-")}:{title.Disc??1}":null;

 /// <summary>Scans <paramref name="root"/> (default depth 6). GoD packages and extracted games are not searched further inside.</summary>
 public static async Task<IReadOnlyList<LibraryEntry>> ScanAsync(string root,Action<string>? status,CancellationToken token,int maxDepth=6)
 {
  return await Task.Run(()=>
  {
   var found=new List<LibraryEntry>();
   if(!Directory.Exists(root))return found;
   var pending=new Queue<(string Folder,int Depth)>();pending.Enqueue((root,0));
   var visited=0;
   while(pending.TryDequeue(out var item))
   {
    token.ThrowIfCancellationRequested();
    if(++visited%200==0)status?.Invoke($"Scanning library … {visited} folders, {found.Count} games");
    string[] files,folders;
    try{files=Directory.GetFiles(item.Folder);folders=Directory.GetDirectories(item.Folder);}
    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){status?.Invoke("Skipped (not readable): "+item.Folder);continue;}
    // GoD: <TitleID>\00007000\<MediaID> (STFS header) + <MediaID>.data\Data0000…
    if(string.Equals(System.IO.Path.GetFileName(item.Folder),"00007000",StringComparison.OrdinalIgnoreCase))
    {
     foreach(var file in files.Where(f=>System.IO.Path.GetExtension(f)==""))
      if(TitleInfoReader.TryRead(file,ConsolePlatform.Xbox360,token) is {SourceKind:"GoD header"} god)found.Add(new(file,LibraryKind.GoD,god,IsProbablyScrubbed(file)));
     continue;
    }
    // Extracted game: default.xex (Xbox 360) or default.xbe (Xbox Classic) directly in the folder.
    if(files.Any(f=>System.IO.Path.GetFileName(f) is {} n&&(n.Equals("default.xex",StringComparison.OrdinalIgnoreCase)||n.Equals("default.xbe",StringComparison.OrdinalIgnoreCase))))
    {
     found.Add(new(item.Folder,LibraryKind.Extracted,TitleInfoReader.TryRead(item.Folder,ConsolePlatform.Xbox360,token)));
     continue;
    }
    foreach(var file in files)
    {
     var extension=System.IO.Path.GetExtension(file).ToLowerInvariant();
     if(extension is not (".iso" or ".xiso"))continue;
     var stem=System.IO.Path.GetFileNameWithoutExtension(file);var dot=stem.LastIndexOf('.');
     if(dot>=0&&int.TryParse(stem[(dot+1)..],out var part)&&part>1)continue; // later parts of split images
     found.Add(new(file,LibraryKind.Image,TitleInfoReader.TryRead(file,ConsolePlatform.Xbox360,token)));
    }
    if(item.Depth<maxDepth)
     foreach(var folder in folders)
     {
      if(folder.EndsWith(".data",StringComparison.OrdinalIgnoreCase)||System.IO.Path.GetFileName(folder).StartsWith(".xshard-",StringComparison.Ordinal))continue;
      try{if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){continue;}
      pending.Enqueue((folder,item.Depth+1));
     }
   }
   return found;
  },token);
 }

 /// <summary>
 /// Disc games keep the full game partition when converted with padding (about 6.9 GB for XGD1, 7.6 GB for XGD2,
 /// 8.7 GB for XGD3). A GoD whose data is clearly smaller was scrubbed or rebuilt. Only a hint: a scrubbed game that
 /// fills almost the whole disc is not detected.
 /// </summary>
 public const long UnscrubbedGoDMinimum=6_500_000_000;
 public static bool IsProbablyScrubbed(string header)
 {
  try
  {
   var data=header+".data";
   if(!Directory.Exists(data))return false;
   var size=Directory.EnumerateFiles(data).Sum(f=>new FileInfo(f).Length);
   return size>0&&size<UnscrubbedGoDMinimum;
  }
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return false;}
 }
 /// <summary>Keys of all identified games in the entries.</summary>
 public static HashSet<string> Keys(IEnumerable<LibraryEntry> entries)=>entries.Select(e=>e.Key).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
}
