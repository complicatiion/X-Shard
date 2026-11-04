using XShard.Models;
namespace XShard.Services;
public static class BackendPolicy
{
 public static BackendEngine For(ConsolePlatform platform,XisoMode mode)
 {
  if(!Enum.IsDefined(platform)||!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(platform),"Unsupported console or operation.");
  return platform==ConsolePlatform.Xbox360||mode==XisoMode.Convert?BackendEngine.XgdTool:BackendEngine.ExtractXiso;
 }
 public static BackendEngine For(XisoJobOptions options) {var baseline=For(options.Platform,options.Mode);return options.Advanced||options.Profile!=DeviceProfile.Custom?BackendEngine.XgdTool:baseline;}
 public static XisoJobOptions Normalize(XisoJobOptions options)=>options with {Engine=For(options)};
}
