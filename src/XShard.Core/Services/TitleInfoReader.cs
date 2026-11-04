using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using XShard.Models;
namespace XShard.Services;
/// <summary>
/// Reads title information offline, without extracting the image and without network access.
/// Xbox Classic: default.xbe certificate (Title ID, name, region, version, disc) and the $$XTIMAGE title icon.
/// Xbox 360: default.xex header (Title ID, Media ID, region, version, disc); name and icon only from GoD/STFS content headers
/// (the XEX resource section holding them is encrypted; no console keys are used or shipped).
/// Compressed containers (CCI, CSO, ZAR) are not inspected.
/// </summary>
public static class TitleInfoReader
{
 private const int Sector=2048;
 private static readonly byte[] XdvdfsMagic=Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
 /// <summary>Plain XISO, Xbox Classic redump (XGD1) and Xbox 360 XGD2/XGD3 game partition offsets.</summary>
 private static readonly long[] PartitionOffsets=[0,0x18300000,0xFD90000,0x2080000];
 private const int MaxHeaderBytes=4*1024*1024;

 public static TitleInfo? TryRead(string source,ConsolePlatform hint=ConsolePlatform.Xbox,CancellationToken token=default)
 {
  try
  {
   token.ThrowIfCancellationRequested();
   if(Directory.Exists(source))return ReadDirectory(source,token);
   if(!File.Exists(source))return null;
   var extension=Path.GetExtension(source).ToLowerInvariant();
   if(extension is ".xbe")return ReadXbeFile(source);
   if(extension is ".xex")return ReadXexFile(source);
   if(extension is ".iso" or ".xiso")return ReadImage(source,token);
   if(extension=="")return IsStfs(source)?ReadStfs(source):null;
   return null;
  }
  catch(OperationCanceledException){throw;}
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or EndOfStreamException or OverflowException or IndexOutOfRangeException){return null;}
 }

 // ---------- folders (extracted games and GoD) ----------
 private static TitleInfo? ReadDirectory(string folder,CancellationToken token)
 {
  foreach(var name in new[]{"default.xbe","default.xex"})
  {
   var file=FindFile(folder,name,1);
   if(file!=null)return name.EndsWith(".xbe")?ReadXbeFile(file):MergeStfs(ReadXexFile(file),FindStfsHeader(folder,token));
  }
  return FindStfsHeader(folder,token) is {} header?ReadStfs(header):null;
 }
 private static string? FindFile(string folder,string name,int depth)
 {
  var options=new EnumerationOptions{MatchCasing=MatchCasing.CaseInsensitive,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint,RecurseSubdirectories=depth>0,MaxRecursionDepth=depth};
  return Directory.EnumerateFiles(folder,name,options).FirstOrDefault();
 }
 /// <summary>Locates a GoD/STFS content header (LIVE, PIRS or "CON ") up to three levels deep, preferring 00007000 folders.</summary>
 public static string? FindStfsHeader(string folder,CancellationToken token=default)
 {
  if(!Directory.Exists(folder))return null;
  var options=new EnumerationOptions{IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint,RecurseSubdirectories=true,MaxRecursionDepth=3};
  string? fallback=null;
  foreach(var file in Directory.EnumerateFiles(folder,"*",options))
  {
   token.ThrowIfCancellationRequested();
   if(Path.GetExtension(file)!=""||!IsStfs(file))continue;
   if(string.Equals(Path.GetFileName(Path.GetDirectoryName(file)),"00007000",StringComparison.OrdinalIgnoreCase))return file;
   fallback??=file;
  }
  return fallback;
 }
 private static bool IsStfs(string file)
 {
  try
  {
   using var stream=File.OpenRead(file);if(stream.Length<0x971A)return false;
   Span<byte> magic=stackalloc byte[4];stream.ReadExactly(magic);
   return magic.SequenceEqual("LIVE"u8)||magic.SequenceEqual("PIRS"u8)||magic.SequenceEqual("CON "u8);
  }
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return false;}
 }
 private static TitleInfo? MergeStfs(TitleInfo? xex,string? stfs)
 {
  if(stfs==null)return xex;var header=ReadStfs(stfs);
  if(xex==null)return header;if(header==null)return xex;
  return xex with {Name=header.Name??xex.Name,IconPng=header.IconPng??xex.IconPng,SourceKind="XEX + GoD header"};
 }

 // ---------- disc images (XDVDFS) ----------
 private static TitleInfo? ReadImage(string path,CancellationToken token)
 {
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,64*1024,FileOptions.RandomAccess);
  foreach(var partition in PartitionOffsets)
  {
   token.ThrowIfCancellationRequested();
   if(stream.Length<partition+33L*Sector)continue;
   var volume=ReadAt(stream,partition+32L*Sector,36);
   if(!volume.AsSpan(0,20).SequenceEqual(XdvdfsMagic))continue;
   var rootSector=BinaryPrimitives.ReadUInt32LittleEndian(volume.AsSpan(20));
   var rootSize=BinaryPrimitives.ReadUInt32LittleEndian(volume.AsSpan(24));
   if(rootSize==0||rootSize>16*1024*1024)continue;
   var root=ReadAt(stream,partition+(long)rootSector*Sector,(int)rootSize);
   if(FindEntry(root,"default.xbe") is {} xbe)return ParseXbe(new SliceReader(stream,partition+(long)xbe.Sector*Sector,xbe.Size));
   if(FindEntry(root,"default.xex") is {} xex)return ParseXex(new SliceReader(stream,partition+(long)xex.Sector*Sector,xex.Size),"XEX in disc image");
  }
  return null;
 }
 /// <summary>Walks the XDVDFS directory binary tree (offsets in 4-byte units) and returns the matching file entry.</summary>
 private static (uint Sector,uint Size)? FindEntry(byte[] table,string name)
 {
  var pending=new Stack<int>();pending.Push(0);var visited=new HashSet<int>();
  while(pending.Count>0&&visited.Count<100000)
  {
   var offset=pending.Pop();
   if(offset<0||offset+14>table.Length||!visited.Add(offset))continue;
   var left=BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(offset));
   var right=BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(offset+2));
   if(left==0xFFFF&&right==0xFFFF)continue;
   var sector=BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(offset+4));
   var size=BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(offset+8));
   var attributes=table[offset+12];var length=table[offset+13];
   if(offset+14+length>table.Length)continue;
   var entryName=Encoding.ASCII.GetString(table,offset+14,length);
   if((attributes&0x10)==0&&entryName.Equals(name,StringComparison.OrdinalIgnoreCase))return (sector,size);
   if(left!=0&&left!=0xFFFF)pending.Push(left*4);
   if(right!=0&&right!=0xFFFF)pending.Push(right*4);
  }
  return null;
 }

 // ---------- Xbox Classic: XBE ----------
 private static TitleInfo? ReadXbeFile(string path)
 {
  using var stream=File.OpenRead(path);
  return ParseXbe(new SliceReader(stream,0,stream.Length));
 }
 private static TitleInfo? ParseXbe(SliceReader file)
 {
  var probe=file.Read(0,0x180);
  if(probe.Length<0x124||!probe.AsSpan(0,4).SequenceEqual("XBEH"u8))return null;
  uint U(byte[] b,int o)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
  var baseAddress=U(probe,0x104);var headerSize=U(probe,0x108);
  var header=file.Read(0,(int)Math.Min(Math.Max(headerSize,0x180u),(uint)MaxHeaderBytes));
  var certificate=(int)(U(probe,0x118)-baseAddress);
  if(certificate<0||certificate+0xB0>header.Length)return null;
  var titleId=U(header,certificate+0x08);
  var name=Encoding.Unicode.GetString(header,certificate+0x0C,80).TrimEnd('\0').Trim();
  var region=U(header,certificate+0xA0);var disc=U(header,certificate+0xA8);var version=U(header,certificate+0xAC);
  byte[]? icon=null;
  var sections=(int)U(probe,0x11C);var sectionTable=(int)(U(probe,0x120)-baseAddress);
  if(sections is >0 and <256&&sectionTable>0&&sectionTable+sections*0x38<=header.Length)
  {
   foreach(var wanted in new[]{"$$XTIMAGE","$$XSIMAGE"})
   {
    for(var i=0;i<sections&&icon==null;i++)
    {
     var h=sectionTable+i*0x38;
     var nameOffset=(int)(U(header,h+0x14)-baseAddress);
     if(nameOffset<0||nameOffset>=header.Length)continue;
     var end=Array.IndexOf(header,(byte)0,nameOffset);if(end<0)continue;
     if(Encoding.ASCII.GetString(header,nameOffset,end-nameOffset)!=wanted)continue;
     var raw=U(header,h+0x0C);var size=U(header,h+0x10);
     if(size is >16 and <=MaxHeaderBytes)icon=XprDecoder.ToPng(file.Read(raw,(int)size));
    }
    if(icon!=null)break;
   }
  }
  var regions=new List<string>();
  if((region&1)!=0)regions.Add("NTSC-U");if((region&2)!=0)regions.Add("NTSC-J");if((region&4)!=0)regions.Add("PAL");if((region&0x80000000)!=0)regions.Add("Debug");
  return new TitleInfo(ConsolePlatform.Xbox,"XBE",name.Length>0?name:null,titleId,null,regions.Count>0?string.Join(" / ",regions):null,
   $"{version:X8}",(int)disc+1,null,icon);
 }

 // ---------- Xbox 360: XEX2 (big-endian) ----------
 private static TitleInfo? ReadXexFile(string path)
 {
  using var stream=File.OpenRead(path);
  return ParseXex(new SliceReader(stream,0,stream.Length),"XEX");
 }
 private static TitleInfo? ParseXex(SliceReader file,string kind)
 {
  var probe=file.Read(0,0x18);
  if(probe.Length<0x18||!probe.AsSpan(0,4).SequenceEqual("XEX2"u8))return null;
  uint U(byte[] b,int o)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
  var headerLength=(int)Math.Min(U(probe,0x08),(uint)MaxHeaderBytes);
  var header=file.Read(0,headerLength);
  var security=(int)U(header,0x10);var count=(int)U(header,0x14);
  uint? mediaId=null,titleId=null;string? version=null;int? disc=null,discs=null;
  for(var i=0;i<count&&i<256;i++)
  {
   var entry=0x18+i*8;if(entry+8>header.Length)break;
   if(U(header,entry)!=0x00040006)continue;
   var info=(int)U(header,entry+4);if(info<0||info+20>header.Length)break;
   mediaId=U(header,info);var raw=U(header,info+4);titleId=U(header,info+12);disc=header[info+18];discs=header[info+19];
   version=$"{raw>>28}.{(raw>>24)&0xF}.{(raw>>8)&0xFFFF}.{raw&0xFF}";
  }
  string? region=null;
  if(security>0&&security+0x17C<=header.Length)
  {
   var flags=U(header,security+0x178);
   if(flags==0xFFFFFFFF)region="Region free";
   else
   {
    var list=new List<string>();
    if((flags&0x000000FF)!=0)list.Add("NTSC-U");if((flags&0x0000FF00)!=0)list.Add("NTSC-J");if((flags&0x00FF0000)!=0)list.Add("PAL");
    region=list.Count>0?string.Join(" / ",list):null;
   }
  }
  if(titleId==null&&mediaId==null)return null;
  return new TitleInfo(ConsolePlatform.Xbox360,kind,null,titleId,mediaId,region,version,disc,discs,null);
 }

 // ---------- Xbox 360: GoD / STFS content header (big-endian) ----------
 private static TitleInfo? ReadStfs(string path)
 {
  using var stream=File.OpenRead(path);
  var header=ReadAt(stream,0,0x971A);
  uint U(int o)=>BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(o));
  string Text(int o)=>Encoding.BigEndianUnicode.GetString(header,o,0x80).TrimEnd('\0').Trim();
  var name=Text(0x1691);if(name.Length==0)name=Text(0x411);
  byte[]? Png(int sizeOffset,int dataOffset,int max)
  {
   var size=(int)Math.Min(U(sizeOffset),(uint)max);if(size<8)return null;
   var data=header.AsSpan(dataOffset,size).ToArray();
   return data.AsSpan(0,8).SequenceEqual(PngSignature)?data:null;
  }
  var icon=Png(0x1716,0x571A,0x4000)??Png(0x1712,0x171A,0x4000);
  return new TitleInfo(ConsolePlatform.Xbox360,"GoD header",name.Length>0?name:null,U(0x360),U(0x354),null,null,header[0x366]>0?header[0x366]:null,header[0x367]>0?header[0x367]:null,icon);
 }

 // ---------- helpers ----------
 private static byte[] ReadAt(Stream stream,long offset,int count)
 {
  if(offset<0||offset>=stream.Length)return [];
  count=(int)Math.Min(count,stream.Length-offset);
  var buffer=new byte[count];stream.Position=offset;stream.ReadExactly(buffer);return buffer;
 }
 private sealed class SliceReader(Stream stream,long start,long length)
 {
  public byte[] Read(long offset,int count)
  {
   if(offset<0||offset>=length)return [];
   return ReadAt(stream,start+offset,(int)Math.Min(count,length-offset));
  }
 }
 internal static readonly byte[] PngSignature=[0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A];
}

/// <summary>Decodes Xbox XPR0 texture resources (DXT1/3/5, swizzled or linear 32-bit ARGB) to PNG.</summary>
public static class XprDecoder
{
 public static byte[]? ToPng(byte[] xpr)
 {
  if(xpr.Length<32||!xpr.AsSpan(0,4).SequenceEqual("XPR0"u8))return null;
  var headerSize=(int)BinaryPrimitives.ReadUInt32LittleEndian(xpr.AsSpan(8));
  var dataOffset=(int)BinaryPrimitives.ReadUInt32LittleEndian(xpr.AsSpan(16));
  var format=BinaryPrimitives.ReadUInt32LittleEndian(xpr.AsSpan(24));
  var code=(int)((format>>8)&0xFF);var width=1<<(int)((format>>20)&0xF);var height=1<<(int)((format>>24)&0xF);
  if(width>1024||height>1024)return null;
  var start=headerSize+dataOffset;if(start<0||start>=xpr.Length)return null;
  var data=xpr.AsSpan(start);
  var rgba=code switch
  {
   0x0C=>DecodeDxt(data,width,height,1),
   0x0E=>DecodeDxt(data,width,height,3),
   0x0F=>DecodeDxt(data,width,height,5),
   0x06 or 0x07=>DecodeArgb(data,width,height,true,code==0x07),
   0x12 or 0x1E=>DecodeArgb(data,width,height,false,code==0x1E),
   _=>null
  };
  return rgba==null?null:PngWriter.Encode(rgba,width,height);
 }
 private static byte[]? DecodeArgb(ReadOnlySpan<byte> data,int width,int height,bool swizzled,bool opaque)
 {
  if(data.Length<width*height*4)return null;
  var rgba=new byte[width*height*4];
  for(var y=0;y<height;y++)for(var x=0;x<width;x++)
  {
   var source=(swizzled?Swizzle(x,y,width,height):y*width+x)*4;var target=(y*width+x)*4;
   rgba[target]=data[source+2];rgba[target+1]=data[source+1];rgba[target+2]=data[source];rgba[target+3]=opaque?(byte)255:data[source+3];
  }
  return rgba;
 }
 /// <summary>Morton (Z-order) index used by Xbox swizzled textures.</summary>
 private static int Swizzle(int x,int y,int width,int height)
 {
  int result=0,bit=1;
  for(int i=1;i<width||i<height;i<<=1)
  {
   if(i<width){if((x&i)!=0)result|=bit;bit<<=1;}
   if(i<height){if((y&i)!=0)result|=bit;bit<<=1;}
  }
  return result;
 }
 private static byte[]? DecodeDxt(ReadOnlySpan<byte> data,int width,int height,int kind)
 {
  var blockSize=kind==1?8:16;var blocksX=Math.Max(1,(width+3)/4);var blocksY=Math.Max(1,(height+3)/4);
  if(data.Length<blocksX*blocksY*blockSize)return null;
  var rgba=new byte[width*height*4];Span<byte> colors=stackalloc byte[16];Span<byte> alpha=stackalloc byte[16];
  for(var by=0;by<blocksY;by++)for(var bx=0;bx<blocksX;bx++)
  {
   var block=data.Slice((by*blocksX+bx)*blockSize,blockSize);
   alpha.Fill(255);
   if(kind==3)for(var i=0;i<16;i++){var nibble=(block[i/2]>>((i%2)*4))&0xF;alpha[i]=(byte)(nibble*17);}
   if(kind==5)
   {
    int a0=block[0],a1=block[1];ulong bits=0;for(var i=0;i<6;i++)bits|=(ulong)block[2+i]<<(8*i);
    for(var i=0;i<16;i++)
    {
     var index=(int)((bits>>(3*i))&7);
     alpha[i]=(byte)(index switch
     {
      0=>a0,1=>a1,
      _ when a0>a1=>((8-index)*a0+(index-1)*a1)/7,
      6=>0,7=>255,
      _=>((6-index)*a0+(index-1)*a1)/5
     });
    }
   }
   var colorBlock=kind==1?block:block[8..];
   var c0=BinaryPrimitives.ReadUInt16LittleEndian(colorBlock);var c1=BinaryPrimitives.ReadUInt16LittleEndian(colorBlock[2..]);
   Unpack(c0,colors[..4]);Unpack(c1,colors.Slice(4,4));
   var fourColor=c0>c1||kind!=1;
   for(var channel=0;channel<3;channel++)
   {
    colors[8+channel]=(byte)(fourColor?(2*colors[channel]+colors[4+channel])/3:(colors[channel]+colors[4+channel])/2);
    colors[12+channel]=(byte)(fourColor?(colors[channel]+2*colors[4+channel])/3:0);
   }
   colors[3]=colors[7]=colors[11]=255;colors[15]=(byte)(fourColor?255:0);
   var indices=BinaryPrimitives.ReadUInt32LittleEndian(colorBlock[4..]);
   for(var i=0;i<16;i++)
   {
    var x=bx*4+i%4;var y=by*4+i/4;if(x>=width||y>=height)continue;
    var index=(int)((indices>>(2*i))&3);var target=(y*width+x)*4;
    rgba[target]=colors[index*4];rgba[target+1]=colors[index*4+1];rgba[target+2]=colors[index*4+2];
    rgba[target+3]=(byte)(colors[index*4+3]*alpha[i]/255);
   }
  }
  return rgba;
 }
 private static void Unpack(ushort color,Span<byte> rgba)
 {
  var r=(color>>11)&0x1F;var g=(color>>5)&0x3F;var b=color&0x1F;
  rgba[0]=(byte)((r<<3)|(r>>2));rgba[1]=(byte)((g<<2)|(g>>4));rgba[2]=(byte)((b<<3)|(b>>2));rgba[3]=255;
 }
}

/// <summary>Minimal RGBA PNG encoder (no external libraries).</summary>
public static class PngWriter
{
 private static readonly uint[] CrcTable=BuildTable();
 public static byte[] Encode(byte[] rgba,int width,int height)
 {
  using var output=new MemoryStream();
  output.Write(TitleInfoReader.PngSignature);
  Span<byte> ihdr=stackalloc byte[13];
  BinaryPrimitives.WriteInt32BigEndian(ihdr,width);BinaryPrimitives.WriteInt32BigEndian(ihdr[4..],height);
  ihdr[8]=8;ihdr[9]=6;ihdr[10]=0;ihdr[11]=0;ihdr[12]=0;
  Chunk(output,"IHDR",ihdr);
  using(var compressed=new MemoryStream())
  {
   using(var zlib=new ZLibStream(compressed,CompressionLevel.Optimal,true))
    for(var y=0;y<height;y++){zlib.WriteByte(0);zlib.Write(rgba,y*width*4,width*4);}
   Chunk(output,"IDAT",compressed.ToArray());
  }
  Chunk(output,"IEND",[]);
  return output.ToArray();
 }
 private static void Chunk(Stream output,string type,ReadOnlySpan<byte> data)
 {
  Span<byte> length=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);output.Write(length);
  var typeBytes=Encoding.ASCII.GetBytes(type);output.Write(typeBytes);output.Write(data);
  var crc=0xFFFFFFFFu;foreach(var b in typeBytes)crc=CrcTable[(crc^b)&0xFF]^(crc>>8);foreach(var b in data)crc=CrcTable[(crc^b)&0xFF]^(crc>>8);
  Span<byte> crcBytes=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(crcBytes,crc^0xFFFFFFFFu);output.Write(crcBytes);
 }
 private static uint[] BuildTable()
 {
  var table=new uint[256];
  for(uint n=0;n<256;n++){var c=n;for(var k=0;k<8;k++)c=(c&1)!=0?0xEDB88320u^(c>>1):c>>1;table[n]=c;}
  return table;
 }
}
