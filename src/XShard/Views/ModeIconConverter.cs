using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using XShard.Models;
namespace XShard.Views;
public sealed class ModeIconConverter : IValueConverter
{
 public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value switch {XisoMode.Extract=>"Extract",XisoMode.Create=>"Create",XisoMode.List=>"List",XisoMode.Rewrite=>"Retry",XisoMode.Convert=>"Convert",_=>"Info"};
 public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class EnumLabelConverter : IValueConverter
{
 public static string Label(object? value)=>XShard.Services.LocalizationService.Instance.T(value switch
 {
  ConsolePlatform.Xbox=>"Xbox Classic",ConsolePlatform.Xbox360=>"Xbox 360",
  DeviceProfile.Custom=>"Custom",DeviceProfile.OriginalXbox=>"Xbox Classic",DeviceProfile.Xbox360=>"Xbox 360",DeviceProfile.Xemu=>"Xbox Classic · Xemu Emulator",DeviceProfile.Xenia=>"Xbox 360 · Xenia Emulator",
  OutputFormat.ConsoleDefault=>"Console preset",
  OutputTarget.Custom=>"Custom folder",OutputTarget.XemuLibrary=>"Xemu library",OutputTarget.XeniaLibrary=>"Xenia library",OutputTarget.RetroBat=>"RetroBat",OutputTarget.Batocera=>"Batocera",
  "Txt"=>"TXT report","Html"=>"HTML report","Off"=>"No automatic report","Nothing"=>"Do nothing","Hibernate"=>"Hibernate","Shutdown"=>"Shut down",
  "All"=>"All jobs","Added"=>"Order added","Name"=>"Name","Status"=>"Status","Dark"=>"Dark","Xbox"=>"Xbox","Light"=>"Light","System"=>"System","Windows"=>"Windows",
  _=>value?.ToString()??""
 });
 public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>Label(value);
 public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
}
/// <summary>PNG bytes (title icons) to a bitmap; cached per byte array.</summary>
public sealed class PngBytesConverter : IValueConverter
{
 private static readonly ConditionalWeakTable<byte[],Bitmap> Cache=new();
 public object? Convert(object? value,Type targetType,object? parameter,CultureInfo culture)
 {
  if(value is not byte[] {Length:>0} bytes)return null;
  if(Cache.TryGetValue(bytes,out var cached))return cached;
  try{using var stream=new MemoryStream(bytes);var bitmap=new Bitmap(stream);Cache.AddOrUpdate(bytes,bitmap);return bitmap;}
  catch(Exception){return null;}
 }
 public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
}
