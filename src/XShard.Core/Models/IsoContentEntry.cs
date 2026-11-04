namespace XShard.Models;
public sealed record IsoContentEntry(string Path, long Size, bool IsDirectory)
{
 public string SizeText => IsDirectory ? "Folder" : $"{Size:N0} bytes";
}
