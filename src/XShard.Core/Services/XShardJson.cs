using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using XShard.Models;
namespace XShard.Services;
/// <summary>Source-generated JSON metadata. Keeps persistence working in the trimmed CLI where reflection-based serialization is disabled.</summary>
[JsonSourceGenerationOptions(WriteIndented=true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<SavedJob>))]
[JsonSerializable(typeof(LegalAcceptance))]
[JsonSerializable(typeof(StorageLocation))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class XShardJsonContext:JsonSerializerContext {}
internal static class XShardJson
{
 public static JsonSerializerOptions Options {get;}=Create();
 private static JsonSerializerOptions Create()
 {
  var options=new JsonSerializerOptions(XShardJsonContext.Default.Options)
  {
   // Reflection fallback only where the runtime permits it (GUI, tests); the trimmed CLI uses generated metadata exclusively.
   TypeInfoResolver=JsonSerializer.IsReflectionEnabledByDefault?JsonTypeInfoResolver.Combine(XShardJsonContext.Default,new DefaultJsonTypeInfoResolver()):XShardJsonContext.Default
  };
  options.MakeReadOnly();return options;
 }
}
/// <summary>Bootstrap pointer to a relocated data folder.</summary>
public sealed record StorageLocation(string Directory);
