using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using XShard.Services;

/// <summary>
/// v0.2.2 checks without any real network access: release tags, GitHub release parsing, update decision, host
/// allow-list for redirects, SHA-256 verification, asset selection, zip-slip protection and the emulator install
/// (user files carried over, previous version kept). All HTTP goes through <see cref="FakeGitHub"/>.
/// </summary>
internal static class OnlineChecks
{
 private sealed class FakeGitHub:HttpMessageHandler
 {
  public readonly Dictionary<string,Func<HttpResponseMessage>> Routes=new(StringComparer.OrdinalIgnoreCase);
  public readonly List<string> Requested=[];
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
  {
   var url=request.RequestUri!.AbsoluteUri;lock(Requested)Requested.Add(url);
   return Task.FromResult(Routes.TryGetValue(url,out var route)?route():new HttpResponseMessage(HttpStatusCode.NotFound));
  }
 }
 private static HttpResponseMessage Json(string text)=>new(HttpStatusCode.OK){Content=new StringContent(text,Encoding.UTF8,"application/json")};
 private static HttpResponseMessage Bytes(byte[] data)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
 private static HttpResponseMessage Redirect(string to){var r=new HttpResponseMessage(HttpStatusCode.Found);r.Headers.Location=new Uri(to);return r;}
 private static string Release(string tag,params (string Name,string Url,long Size,string? Sha)[] assets)=>
  "{\"tag_name\":\""+tag+"\",\"name\":\"X\",\"html_url\":\"https://github.com/o/r/releases/tag/"+tag+"\",\"body\":\"Notes\",\"prerelease\":false,\"published_at\":\"2026-11-15T10:00:00Z\",\"assets\":["+
  string.Join(",",assets.Select(a=>"{\"name\":\""+a.Name+"\",\"browser_download_url\":\""+a.Url+"\",\"size\":"+a.Size+(a.Sha==null?"":",\"digest\":\"sha256:"+a.Sha+"\"")+"}"))+"]}";

 public static async Task RunAsync(string temp,Action<bool,string> check)
 {
  // ---------- release tags ----------
  check(ReleaseVersion.TryParse("0.2.2",out var plain,out var pre1)&&!pre1&&plain==new Version(0,2,2)
   &&ReleaseVersion.TryParse("v0.3.0",out var prefixed,out _)&&prefixed==new Version(0,3,0)
   &&ReleaseVersion.TryParse("0.3.0-beta",out _,out var pre2)&&pre2
   &&!ReleaseVersion.TryParse("latest",out _,out _)&&!ReleaseVersion.TryParse("",out _,out _),"release tags: 0.2.2 and v0.2.2 accepted, -suffix is a pre-release, other tags ignored");

  // ---------- update check ----------
  var fake=new FakeGitHub();var client=new GitHubReleaseClient(fake);
  const string latestUrl="https://api.github.com/repos/complicatiion/X-Shard/releases/latest";
  fake.Routes[latestUrl]=()=>Json(Release("0.9.0"));
  var newer=await UpdateService.CheckAsync(client,new Version(0,2,2),CancellationToken.None);
  fake.Routes[latestUrl]=()=>Json(Release("0.2.2"));
  var same=await UpdateService.CheckAsync(client,new Version(0,2,2),CancellationToken.None);
  fake.Routes[latestUrl]=()=>Json(Release("1.0.0-rc1"));
  var rc=await UpdateService.CheckAsync(client,new Version(0,2,2),CancellationToken.None);
  check(newer is {Available:true,Latest:"0.9.0"}&&newer.Url.StartsWith("https://github.com/")&&!same.Available&&!rc.Available,"update check: newer release found, same version and pre-release ignored");
  check(fake.Requested.All(u=>u.StartsWith("https://api.github.com/repos/complicatiion/X-Shard/")),"update check only contacts the X-Shard releases API");

  // ---------- redirects are limited to GitHub ----------
  fake.Routes[latestUrl]=()=>Redirect("https://evil.example.com/latest");
  try{await client.GetLatestAsync("complicatiion","X-Shard",CancellationToken.None);check(false,"redirect to a foreign host must be blocked");}
  catch(IOException ex){check(ex.Message.Contains("not an allowed GitHub address"),"redirect to a non-GitHub host is blocked");}
  check(!GitHubReleaseClient.IsAllowed(new Uri("http://github.com/x"))&&GitHubReleaseClient.IsAllowed(new Uri("https://objects.githubusercontent.com/x")),"only HTTPS GitHub hosts are allowed");

  // ---------- download with digest ----------
  var payload=RandomNumberGenerator.GetBytes(300_000);var sha=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
  fake.Routes["https://github.com/o/r/releases/download/1/file.zip"]=()=>Redirect("https://objects.githubusercontent.com/file.zip");
  fake.Routes["https://objects.githubusercontent.com/file.zip"]=()=>Bytes(payload);
  var target=Path.Combine(temp,"online","file.zip");
  var verified=await client.DownloadAsync(new ReleaseAsset("file.zip","https://github.com/o/r/releases/download/1/file.zip",payload.Length,sha),target,null,CancellationToken.None);
  check(verified&&File.ReadAllBytes(target).AsSpan().SequenceEqual(payload),"download follows the GitHub redirect and verifies SHA-256");
  try{await client.DownloadAsync(new ReleaseAsset("file.zip","https://github.com/o/r/releases/download/1/file.zip",payload.Length,new string('0',64)),target,null,CancellationToken.None);check(false,"wrong digest");}
  catch(IOException ex){check(ex.Message.Contains("Checksum mismatch")&&!File.Exists(target),"checksum mismatch discards the download");}

  // ---------- asset selection ----------
  ReleaseInfo Assets(EmulatorSource from,params string[] names)=>GitHubReleaseClient.Parse(System.Text.Json.JsonDocument.Parse(Release("t",names.Select(n=>(n,$"https://github.com/{from.Owner}/{from.Repository}/releases/download/t/"+n,1L,(string?)null)).ToArray())).RootElement);
  check(EmulatorCatalog.PickAsset(EmulatorCatalog.Xemu,Assets(EmulatorCatalog.Xemu,"xemu-win-x86_64-release-pdb.zip","xemu-win-x86_64-release.zip","xemu-macos-universal-release.zip","xemu-win-aarch64-release.zip"))?.Name=="xemu-win-x86_64-release.zip","xemu: Windows x64 release ZIP is chosen (no debug, ARM or macOS)");
  check(EmulatorCatalog.PickAsset(EmulatorCatalog.Xenia,Assets(EmulatorCatalog.Xenia,"xenia_canary_linux.tar.gz","xenia_canary_windows.zip"))?.Name=="xenia_canary_windows.zip"&&EmulatorCatalog.PickAsset(EmulatorCatalog.Xenia,Assets(EmulatorCatalog.Xenia,"xenia_canary_linux.tar.gz"))==null,"Xenia: Windows ZIP is chosen, none when missing");
  check(EmulatorCatalog.PickAsset(EmulatorCatalog.Xemu,Assets(EmulatorCatalog.Xenia,"xemu-win-x86_64-release.zip"))==null,"assets from another repository are never chosen");

  // ---------- zip slip ----------
  var evil=Path.Combine(temp,"online","evil.zip");
  using(var archive=ZipFile.Open(evil,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("../outside.txt").Open());writer.Write("x");}
  try{EmulatorInstaller.ExtractSafely(evil,Path.Combine(temp,"online","extract"),CancellationToken.None);check(false,"zip slip");}
  catch(IOException ex){check(ex.Message.Contains("Unsafe path")&&!File.Exists(Path.Combine(temp,"online","outside.txt")),"archive entries outside the target folder are rejected");}

  // ---------- emulator install / update ----------
  byte[] Zip(string exe,string version)
  {
   using var memory=new MemoryStream();
   using(var archive=new ZipArchive(memory,ZipArchiveMode.Create,true))
   {
    using(var w=new StreamWriter(archive.CreateEntry("xemu-build/"+exe).Open()))w.Write("exe "+version);
    using(var w=new StreamWriter(archive.CreateEntry("xemu-build/LICENSE.txt").Open()))w.Write("GPL-2.0");
   }
   return memory.ToArray();
  }
  var root=Path.Combine(temp,"online","emulators");var installer=new EmulatorInstaller(client,root);
  var first=Zip("xemu.exe","1");var second=Zip("xemu.exe","2");
  fake.Routes["https://github.com/xemu-project/xemu/releases/download/v1/xemu-win-x86_64-release.zip"]=()=>Bytes(first);
  fake.Routes["https://github.com/xemu-project/xemu/releases/download/v2/xemu-win-x86_64-release.zip"]=()=>Bytes(second);
  ReleaseInfo XemuRelease(string tag,byte[] data)=>GitHubReleaseClient.Parse(System.Text.Json.JsonDocument.Parse(Release(tag,("xemu-win-x86_64-release.zip",$"https://github.com/xemu-project/xemu/releases/download/{tag}/xemu-win-x86_64-release.zip",data.Length,Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()))).RootElement);
  var exe1=await installer.InstallAsync(EmulatorCatalog.Xemu,XemuRelease("v1",first),_=>{},null,CancellationToken.None);
  check(File.ReadAllText(exe1)=="exe 1"&&installer.InstalledTag(EmulatorCatalog.Xemu)=="v1"&&Directory.Exists(installer.XemuSystemFolder)&&File.Exists(Path.Combine(installer.FolderFor(EmulatorCatalog.Xemu),"xemu-build","LICENSE.txt")),"xemu installs with its license file and an empty system files folder");
  await File.WriteAllTextAsync(Path.Combine(installer.XemuSystemFolder,"my-own-bios.bin"),"dump");
  var exe2=await installer.InstallAsync(EmulatorCatalog.Xemu,XemuRelease("v2",second),_=>{},null,CancellationToken.None);
  check(File.ReadAllText(exe2)=="exe 2"&&File.ReadAllText(Path.Combine(installer.XemuSystemFolder,"my-own-bios.bin"))=="dump"&&Directory.Exists(installer.FolderFor(EmulatorCatalog.Xemu)+".previous"),"update keeps the user's system files and the previous version");
  check(!Directory.EnumerateDirectories(root,".xshard-download-*").Any(),"no download working folders remain");
 }
}
