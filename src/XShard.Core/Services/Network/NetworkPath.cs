using System.Runtime.InteropServices;
namespace XShard.Services;
/// <summary>
/// Classifies paths as local or network (SMB/UNC, mapped drives, CIFS/NFS mounts on Linux) and normalizes mapped
/// drive letters to UNC paths. Mapped drives exist only in the logon session that created them; elevated processes,
/// the CLI in Task Scheduler and services do not see them, UNC paths work everywhere.
/// </summary>
public static class NetworkPath
{
 /// <summary>Testing only: overrides network detection (e.g. to treat a local temp folder as a share).</summary>
 public static Func<string,bool>? TreatAsNetwork {get;set;}
 /// <summary>Testing only: overrides the reachability probe for a share root.</summary>
 public static Func<string,bool>? ReachabilityOverride {get;set;}

 public static bool IsUnc(string path)=>
  path.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)||
  (path.Length>2&&(path.StartsWith(@"\\")||path.StartsWith("//"))&&path[2] is not ('?' or '.'));

 /// <summary>True for UNC paths, mapped network drives (Windows) and network file systems (Linux /proc/mounts).</summary>
 public static bool IsNetwork(string? path)
 {
  if(string.IsNullOrWhiteSpace(path))return false;
  if(TreatAsNetwork is {} hook)return hook(path);
  if(IsUnc(path))return true;
  try
  {
   if(OperatingSystem.IsWindows())
   {
    var root=Path.GetPathRoot(Path.GetFullPath(path));
    return !string.IsNullOrEmpty(root)&&root.Length<=3&&new DriveInfo(root).DriveType==DriveType.Network;
   }
   if(OperatingSystem.IsLinux())return LinuxMount(Path.GetFullPath(path)) is {IsNetwork:true};
  }
  catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException){}
  return false;
 }

 /// <summary>Replaces a mapped drive letter (Z:\Games) with its UNC path (\\nas\roms\Games). Other paths are returned unchanged.</summary>
 public static string ToUniversal(string path)
 {
  if(string.IsNullOrWhiteSpace(path)||!OperatingSystem.IsWindows()||IsUnc(path))return path;
  try
  {
   var full=Path.GetFullPath(path);
   if(full.Length<2||full[1]!=':')return path;
   var drive=full[..2];
   if(new DriveInfo(drive+"\\").DriveType!=DriveType.Network)return path;
   var buffer=new char[1024];var length=buffer.Length;
   if(WNetGetConnection(drive,buffer,ref length)!=0)return path;
   var end=Array.IndexOf(buffer,'\0');
   var remote=new string(buffer,0,end>=0?end:buffer.Length).TrimEnd('\\');
   if(!IsUnc(remote))return path;
   var rest=full.Length>2?full[2..].TrimStart('\\'):"";
   return rest.Length==0?remote:remote+"\\"+rest;
  }
  catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException or EntryPointNotFoundException or DllNotFoundException){return path;}
 }

 /// <summary>The share or mount that must be reachable for the path: \\server\share, a drive root or a Linux mount point.</summary>
 public static string ShareRoot(string path)
 {
  if(IsUnc(path))
  {
   var trimmed=path.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)?path[8..]:path.TrimStart('\\','/');
   var parts=trimmed.Split(['\\','/'],StringSplitOptions.RemoveEmptyEntries);
   return parts.Length>=2?$@"\\{parts[0]}\{parts[1]}":path;
  }
  try
  {
   var full=Path.GetFullPath(path);
   if(OperatingSystem.IsLinux()&&LinuxMount(full) is {} mount)return mount.Point;
   return Path.GetPathRoot(full) is {Length:>0} root?root:full;
  }
  catch(Exception ex) when(ex is ArgumentException or NotSupportedException or IOException){return path;}
 }

 /// <summary>Checks the share root with a timeout. A dead SMB connection can block file APIs for 30–60 s; the probe never blocks the caller longer than <paramref name="timeout"/>.</summary>
 public static async Task<bool> IsReachableAsync(string path,TimeSpan timeout,CancellationToken token)
 {
  var root=ShareRoot(path);
  if(ReachabilityOverride is {} hook)return hook(root);
  // One probe per share at a time: a probe that hangs on a dead SMB session keeps its worker thread until Windows
  // gives up; later callers wait for that same probe instead of blocking more threads.
  static Task<bool> Start(string key)=>Task.Run(()=>Directory.Exists(key),CancellationToken.None);
  var probe=Probes.GetOrAdd(root,Start);
  if(probe.IsCompleted){Probes.TryRemove(KeyValuePair.Create(root,probe));probe=Probes.GetOrAdd(root,Start);} // stale result of an earlier probe
  try{return await probe.WaitAsync(timeout,token);}
  catch(TimeoutException){return false;}
  finally{if(probe.IsCompleted)Probes.TryRemove(KeyValuePair.Create(root,probe));} // finished probes are never reused
 }
 private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Task<bool>> Probes=new(StringComparer.OrdinalIgnoreCase);

 private sealed record Mount(string Point,string Type,bool IsNetwork);
 private static readonly string[] NetworkFileSystems=["cifs","smb3","smbfs","nfs","nfs4","fuse.sshfs","9p","afs","ceph","glusterfs"];
 private static Mount? LinuxMount(string full)
 {
  if(!File.Exists("/proc/mounts"))return null;
  Mount? best=null;
  foreach(var line in File.ReadLines("/proc/mounts"))
  {
   var fields=line.Split(' ');if(fields.Length<3)continue;
   var point=fields[1].Replace("\\040"," ");
   var inside=full==point||full.StartsWith(point.TrimEnd('/')+"/",StringComparison.Ordinal);
   if(inside&&(best==null||point.Length>best.Point.Length))best=new(point,fields[2],NetworkFileSystems.Contains(fields[2]));
  }
  return best;
 }

 [DllImport("mpr.dll",CharSet=CharSet.Unicode,EntryPoint="WNetGetConnectionW")]
 private static extern int WNetGetConnection(string localName,[Out] char[] remoteName,ref int length);
}
