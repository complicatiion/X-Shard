namespace XShard.Models;
/// <summary>Title information read offline from XBE/XEX headers or a GoD/STFS content header. Fields are null when the source does not provide them.</summary>
public sealed record TitleInfo(
 ConsolePlatform Platform,
 string SourceKind,
 string? Name,
 uint? TitleId,
 uint? MediaId,
 string? Region,
 string? Version,
 int? Disc,
 int? DiscCount,
 byte[]? IconPng)
{
 public string? TitleIdHex=>TitleId is {} id?id.ToString("X8"):null;
 public string? MediaIdHex=>MediaId is {} id?id.ToString("X8"):null;
 /// <summary>Xbox Classic publisher notation, e.g. 0x4D530004 → MS-004.</summary>
 public string? TitleIdCode
 {
  get
  {
   if(TitleId is not {} id)return null;
   var a=(char)(id>>24);var b=(char)((id>>16)&0xFF);
   return char.IsAsciiLetterOrDigit(a)&&char.IsAsciiLetterOrDigit(b)?$"{a}{b}-{id&0xFFFF:D3}":null;
  }
 }
 public bool HasIcon=>IconPng is {Length:>0};
 /// <summary>Short line for the queue: "Name · 4D530004" or "Title ID 4D530004".</summary>
 public string Summary=>Name is {Length:>0} name?(TitleIdHex is {} hex?$"{name} · {hex}":name):TitleIdHex is {} only?"Title ID "+only:"";
}
