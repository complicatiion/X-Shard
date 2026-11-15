using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
namespace XShard.Services;

public sealed record ReleaseAsset(string Name,string DownloadUrl,long Size,string? Sha256);
public sealed record ReleaseInfo(string Tag,string Name,string HtmlUrl,string Body,bool Prerelease,DateTimeOffset? Published,IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// Minimal, read-only GitHub Releases client (update check, emulator downloads). Only used after the user agreed.
/// HTTPS only; redirects are followed manually and only to GitHub hosts; downloads are size-limited and verified
/// against the SHA-256 digest GitHub publishes for release assets (when present). No credentials, no personal data:
/// requests carry only the User-Agent "X-Shard/&lt;version&gt;".
/// </summary>
public sealed class GitHubReleaseClient(HttpMessageHandler? handler=null)
{
 public static readonly IReadOnlyList<string> AllowedHosts=["api.github.com","github.com","objects.githubusercontent.com","release-assets.githubusercontent.com","codeload.github.com"];
 public const long MaxDownloadBytes=1_500_000_000;
 public static string UserAgent=>"X-Shard/"+(typeof(GitHubReleaseClient).Assembly.GetName().Version?.ToString(3)??"0");

 private HttpClient CreateClient()
 {
  var inner=handler??new SocketsHttpHandler{AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(15),AutomaticDecompression=DecompressionMethods.All};
  var client=new HttpClient(inner,disposeHandler:handler==null){Timeout=Timeout.InfiniteTimeSpan};
  client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
  return client;
 }
 /// <summary>True for https URLs on a GitHub host.</summary>
 public static bool IsAllowed(Uri uri)=>uri.Scheme==Uri.UriSchemeHttps&&AllowedHosts.Contains(uri.Host,StringComparer.OrdinalIgnoreCase);

 private static async Task<HttpResponseMessage> SendAsync(HttpClient client,Uri uri,string accept,CancellationToken token)
 {
  for(var hop=0;hop<6;hop++)
  {
   if(!IsAllowed(uri))throw new IOException("Blocked: "+uri.Host+" is not an allowed GitHub address.");
   using var request=new HttpRequestMessage(HttpMethod.Get,uri);
   request.Headers.Accept.ParseAdd(accept);
   if(uri.Host.Equals("api.github.com",StringComparison.OrdinalIgnoreCase))request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version","2022-11-28");
   var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
   if((int)response.StatusCode is >=300 and <400&&response.Headers.Location is {} location)
   {
    uri=location.IsAbsoluteUri?location:new Uri(uri,location);response.Dispose();continue;
   }
   if(response.StatusCode==HttpStatusCode.NotFound){response.Dispose();throw new IOException("Not found on GitHub (no published release?).");}
   if(response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429){response.Dispose();throw new IOException("GitHub refused the request (rate limit). Try again later.");}
   if(!response.IsSuccessStatusCode){var code=(int)response.StatusCode;response.Dispose();throw new IOException($"GitHub answered HTTP {code}.");}
   return response;
  }
  throw new IOException("Too many redirects.");
 }

 /// <summary>Latest published release (GitHub excludes drafts and pre-releases from "latest").</summary>
 public async Task<ReleaseInfo> GetLatestAsync(string owner,string repo,CancellationToken token)
 {
  using var client=CreateClient();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
  using var response=await SendAsync(client,new Uri($"https://api.github.com/repos/{owner}/{repo}/releases/latest"),"application/vnd.github+json",timeout.Token);
  await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);
  using var json=await JsonDocument.ParseAsync(stream,cancellationToken:timeout.Token);
  return Parse(json.RootElement);
 }
 public static ReleaseInfo Parse(JsonElement root)
 {
  if(root.ValueKind!=JsonValueKind.Object)throw new IOException("Unexpected answer from GitHub.");
  string Text(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
  var assets=new List<ReleaseAsset>();
  if(root.TryGetProperty("assets",out var list)&&list.ValueKind==JsonValueKind.Array)
   foreach(var asset in list.EnumerateArray())
   {
    var digest=Text(asset,"digest");
    if(asset.ValueKind!=JsonValueKind.Object)continue;
    assets.Add(new(Text(asset,"name"),Text(asset,"browser_download_url"),asset.TryGetProperty("size",out var size)&&size.ValueKind==JsonValueKind.Number&&size.TryGetInt64(out var bytes)?bytes:0,
     digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)?digest[7..].ToLowerInvariant():null));
   }
  DateTimeOffset? published=DateTimeOffset.TryParse(Text(root,"published_at"),out var date)?date:null;
  var prerelease=root.TryGetProperty("prerelease",out var flag)&&flag.ValueKind==JsonValueKind.True;
  return new(Text(root,"tag_name"),Text(root,"name"),Text(root,"html_url"),Text(root,"body"),prerelease,published,assets);
 }

 /// <summary>Downloads an asset to <paramref name="target"/>, enforcing the size limit and the published SHA-256 digest.</summary>
 public async Task<bool> DownloadAsync(ReleaseAsset asset,string target,Action<double>? progress,CancellationToken token)
 {
  if(!Uri.TryCreate(asset.DownloadUrl,UriKind.Absolute,out var uri))throw new IOException("Invalid download address.");
  if(asset.Size>MaxDownloadBytes)throw new IOException("The download is larger than allowed.");
  using var client=CreateClient();
  using var response=await SendAsync(client,uri,"application/octet-stream",token);
  var expected=response.Content.Headers.ContentLength??asset.Size;
  if(expected>MaxDownloadBytes)throw new IOException("The download is larger than allowed.");
  Directory.CreateDirectory(Path.GetDirectoryName(target)!);
  using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  try
  {
   await using(var input=await response.Content.ReadAsStreamAsync(token))
   await using(var output=new FileStream(target,FileMode.Create,FileAccess.Write,FileShare.None,1<<20,FileOptions.Asynchronous))
   {
    var buffer=new byte[1<<20];long total=0;
    // A stalled connection must not hang forever: every read has to deliver data within 60 seconds.
    using var stall=CancellationTokenSource.CreateLinkedTokenSource(token);
    while(true)
    {
     stall.CancelAfter(TimeSpan.FromSeconds(60));
     int read;
     try{read=await input.ReadAsync(buffer,stall.Token);}
     catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new IOException("The download stalled (no data for 60 seconds).");}
     if(read==0)break;
     total+=read;if(total>MaxDownloadBytes)throw new IOException("The download is larger than allowed.");
     sha.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),token);
     if(expected>0)progress?.Invoke(Math.Min(100,total*100.0/expected));
    }
   }
   var actual=Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
   if(asset.Sha256 is {} published&&published!=actual)throw new IOException("Checksum mismatch: the download does not match the SHA-256 digest published on GitHub. The file was discarded.");
   return asset.Sha256!=null; // true = verified against the published digest
  }
  catch{try{File.Delete(target);}catch(IOException){}throw;}
 }
}

/// <summary>Release tags of X-Shard: "0.2.2" (also accepted: "v0.2.2"). Tags with a suffix ("0.3.0-beta") are pre-releases.</summary>
public static class ReleaseVersion
{
 public static bool TryParse(string? tag,out Version version,out bool prerelease)
 {
  version=new Version(0,0,0);prerelease=false;
  if(string.IsNullOrWhiteSpace(tag))return false;
  var text=tag.Trim();if(text.StartsWith('v')||text.StartsWith('V'))text=text[1..];
  var cut=text.IndexOfAny(['-','+']);if(cut>=0){prerelease=text[cut]=='-';text=text[..cut];}
  var parts=text.Split('.');
  if(parts.Length is <2 or >4||!parts.All(p=>p.Length>0&&p.All(char.IsAsciiDigit)))return false;
  var numbers=new int[3];
  for(var i=0;i<Math.Min(3,parts.Length);i++)if(!int.TryParse(parts[i],out numbers[i]))return false;
  version=new Version(numbers[0],numbers[1],numbers[2]);return true;
 }
 public static Version Current=>typeof(ReleaseVersion).Assembly.GetName().Version is {} v?new Version(v.Major,v.Minor,Math.Max(0,v.Build)):new Version(0,0,0);
}

public sealed record UpdateResult(bool Available,string Latest,string Current,string Url,string Notes,DateTimeOffset CheckedAt);
/// <summary>Checks github.com/complicatiion/X-Shard for a newer release. Never downloads or installs anything.</summary>
public static class UpdateService
{
 public const string Owner="complicatiion",Repository="X-Shard";
 public static string ReleasesPage=>$"https://github.com/{Owner}/{Repository}/releases";
 public static async Task<UpdateResult> CheckAsync(GitHubReleaseClient client,Version current,CancellationToken token)
 {
  var release=await client.GetLatestAsync(Owner,Repository,token);
  var newer=ReleaseVersion.TryParse(release.Tag,out var latest,out var prerelease)&&!prerelease&&!release.Prerelease&&latest>current;
  var notes=release.Body.Length>1500?release.Body[..1500]+" …":release.Body;
  var url=Uri.TryCreate(release.HtmlUrl,UriKind.Absolute,out var page)&&GitHubReleaseClient.IsAllowed(page)?page.AbsoluteUri:ReleasesPage;
  return new(newer,release.Tag.TrimStart('v','V'),current.ToString(3),url,notes,DateTimeOffset.Now);
 }
}
