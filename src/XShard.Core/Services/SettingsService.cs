using System.Text.Json;
using XShard.Models;
namespace XShard.Services;
public sealed class SettingsService
{
 private static JsonSerializerOptions Json=>XShardJson.Options;
 /// <summary>Typed metadata from the source-generated context (trim-safe overloads).</summary>
 private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> Info<T>()=>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)Json.GetTypeInfo(typeof(T));
 private readonly SemaphoreSlim writeGate=new(1,1);
 public string DataDirectory {get;private set;}
 public string LocatorDirectory {get;}
 public string Warning {get;private set;}="";
 public SettingsService(string? dataDirectory=null)
 {
  LocatorDirectory=Path.GetFullPath(dataDirectory??(File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.flag"))?Path.Combine(AppContext.BaseDirectory,"data"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"X-Shard")));
  DataDirectory=LocatorDirectory;
  try
  {
   var locator=Path.Combine(LocatorDirectory,"storage-location.json");
   if(File.Exists(locator))
   {
    var target=JsonSerializer.Deserialize(File.ReadAllText(locator),Info<StorageLocation>());
    if(target!=null&&Path.IsPathFullyQualified(target.Directory)&&Directory.Exists(target.Directory))DataDirectory=Path.GetFullPath(target.Directory);
    else Warning="Custom settings location is unavailable; the default location is in use.";
   }
  }
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){Warning=ex.Message;}
 }
 public T Load<T>(string name,Func<T> fallback)
 {
  try{var path=Path.Combine(DataDirectory,name);if(!File.Exists(path))return fallback();return JsonSerializer.Deserialize(File.ReadAllText(path),Info<T>())??fallback();}
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){Warning=$"Could not read {name}: {ex.Message}";return fallback();}
 }
 private static async Task AtomicWriteAsync<T>(string directory,string name,T value)
 {
  Directory.CreateDirectory(directory);var path=Path.Combine(directory,name);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(value,Info<T>()));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public async Task SaveAsync<T>(string name,T value)
 {
  await writeGate.WaitAsync();try{await AtomicWriteAsync(DataDirectory,name,value);}finally{writeGate.Release();}
 }
 public async Task MoveDataDirectoryAsync(string directory)
 {
  var target=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
  await writeGate.WaitAsync();var created=new List<string>();
  try
  {
   if(target.Equals(Path.TrimEndingDirectorySeparator(DataDirectory),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return;
   Directory.CreateDirectory(target);
   var names=new[]{"settings.json","queue.json","legal-acceptance.json"};
   foreach(var name in names)if(File.Exists(Path.Combine(target,name)))throw new IOException("The selected location already contains X-Shard data. Choose an empty application-data folder.");
   foreach(var name in names)
   {
    var from=Path.Combine(DataDirectory,name);if(!File.Exists(from))continue;
    var to=Path.Combine(target,name);await using var input=new FileStream(from,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);await using var output=new FileStream(to,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);created.Add(to);await input.CopyToAsync(output);await output.FlushAsync();
   }
   // Commit the stable bootstrap pointer only after every owned file was copied.
   await AtomicWriteAsync(LocatorDirectory,"storage-location.json",new StorageLocation(target));
   DataDirectory=target;
  }
  catch{foreach(var file in created)try{File.Delete(file);}catch(IOException){}throw;}
  finally{writeGate.Release();}
 }
 public AppSettings LoadSettings()
 {
  var settings=Load("settings.json",()=>new AppSettings());if(settings.SchemaVersion!=1){Warning="Settings schema is newer than this application. Defaults loaded.";return new();}return settings;
 }
}
