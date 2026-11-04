namespace XShard.Services;
public static class FolderBatchService
{
 public static IReadOnlyList<string> EnumerateImages(string folder,bool recursive,bool advanced,CancellationToken token)
 {
  var options=new EnumerationOptions {RecurseSubdirectories=recursive,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint,ReturnSpecialDirectories=false};
  var extensions=advanced?new[]{".iso",".xiso",".cci",".cso",".zar"}:new[]{".iso",".xiso"};
  var files=new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var file in Directory.EnumerateFiles(folder,"*",options))
  {
   token.ThrowIfCancellationRequested();
   if(!extensions.Contains(Path.GetExtension(file),StringComparer.OrdinalIgnoreCase))continue;
   // XGDTool automatically discovers split parts from the first one.
   var stem=Path.GetFileNameWithoutExtension(file);var part=stem.LastIndexOf('.');
   if(advanced&&part>=0&&int.TryParse(stem[(part+1)..],out var number)&&number>1)continue;
   files.Add(Path.GetFullPath(file));
  }
  return files.ToArray();
 }
}
