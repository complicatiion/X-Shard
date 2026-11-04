using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using XShard.Services;
namespace XShard.Views;
public sealed class TrExtension:MarkupExtension
{
 public string Key {get;set;}="";
 public override object ProvideValue(IServiceProvider serviceProvider)=>new Binding {Path=nameof(LocalizationService.Revision),Source=LocalizationService.Instance,Mode=BindingMode.OneWay,Converter=new TranslationConverter(Key)};
 private sealed class TranslationConverter(string key):IValueConverter
 {
  public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>LocalizationService.Instance.T(key);
  public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
 }
}
