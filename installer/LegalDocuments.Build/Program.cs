using System.Text;
using System.Text.RegularExpressions;
if(args.Length!=1){Console.Error.WriteLine("Usage: LegalDocuments.Build <repository>");return 2;}
var repo=Path.GetFullPath(args[0]);var output=Path.Combine(repo,"installer","legal");Directory.CreateDirectory(output);
string Escape(string value){var result=new StringBuilder();foreach(var c in value){if(c is '\\' or '{' or '}')result.Append('\\').Append(c);else if(c>127)result.Append("\\u").Append(unchecked((short)c)).Append('?');else result.Append(c);}return result.ToString();}
string Inline(string value)
{
 var parts=Regex.Split(value,@"(\[[^\]]+\]\([^)]+\)|\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*)");var b=new StringBuilder();
 foreach(var part in parts)
 {
  if(part.StartsWith("**")&&part.EndsWith("**"))b.Append("{\\b ").Append(Escape(part[2..^2])).Append('}');
  else if(part.StartsWith('`')&&part.EndsWith('`'))b.Append("{\\f1 ").Append(Escape(part[1..^1])).Append('}');
  else if(part.StartsWith('*')&&part.EndsWith('*'))b.Append("{\\i ").Append(Escape(part[1..^1])).Append('}');
  else if(Regex.Match(part,@"^\[([^\]]+)\]\(([^)]+)\)$") is {Success:true} link)b.Append(Escape(link.Groups[1].Value+" ("+link.Groups[2].Value+")"));
  else b.Append(Escape(part));
 }
 return b.ToString();
}
int count=0;
foreach(var file in Directory.EnumerateFiles(Path.Combine(repo,"docs","legal"),"*.md").Where(f=>Path.GetFileName(f)!="README.md").Order())
{
 var rtf=new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}{\f1 Consolas;}}{\colortbl ;\red49\green139\blue59;}\f0\fs20 ");bool code=false;
 foreach(var line in File.ReadAllLines(file))
 {
  if(line.StartsWith("```")){code=!code;continue;}
  var heading=Regex.Match(line,@"^(#{1,6})\s+(.+)$");
  if(heading.Success)rtf.Append("\\pard\\sb140\\sa80\\b\\cf1\\fs").Append(heading.Groups[1].Length==1?30:24).Append(' ').Append(Inline(heading.Groups[2].Value)).Append("\\b0\\cf0\\fs20\\par ");
  else if(line.Trim()=="---")rtf.Append("\\pard\\sa100\\par ");
  else if(code)rtf.Append("\\pard\\f1 ").Append(Escape(line)).Append("\\f0\\par ");
  else rtf.Append("\\pard\\sa60 ").Append(Inline(Regex.IsMatch(line.TrimStart(),@"^[-*+]\s+")?"• "+line.TrimStart()[2..]:line)).Append("\\par ");
 }
 rtf.Append('}');File.WriteAllText(Path.Combine(output,Path.GetFileNameWithoutExtension(file)+".rtf"),rtf.ToString(),Encoding.ASCII);count++;
}
Console.WriteLine($"Generated {count} legal RTF documents.");return 0;
