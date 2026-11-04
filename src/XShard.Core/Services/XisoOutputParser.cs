using System.Text.RegularExpressions;
using XShard.Models;
namespace XShard.Services;
public static partial class XisoOutputParser
{
 [GeneratedRegex(@"^(?<path>.+) \((?<size>\d+) bytes\)$",RegexOptions.CultureInvariant)]
 private static partial Regex EntryPattern();
 public static IsoContentEntry? ParseListEntry(string line)
 {
  var match=EntryPattern().Match(line.Trim());
  if(!match.Success || !long.TryParse(match.Groups["size"].Value,out var size)) return null;
  var path=match.Groups["path"].Value;
  return new(path,size,path.EndsWith('/') || path.EndsWith('\\'));
 }
}
