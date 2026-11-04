using System.ComponentModel;
using System.Xml;
using System.Xml.Linq;
namespace XShard.Services;
public sealed class LanguageOption(string code,string name,string? flag=null):INotifyPropertyChanged
{
 public string Code {get;}=code;public string Name {get;}=name;
 /// <summary>Optional flat flag stripes from the XML root attribute Flag (e.g. "h:#000000,#DD0000,#FFCE00"); built-in flags are used when empty.</summary>
 public string? Flag {get;}=flag;
 public bool IsSelected=>LocalizationService.Instance.Language==Code;
 public event PropertyChangedEventHandler? PropertyChanged;
 public void Refresh()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsSelected)));
}
public sealed class LocalizationService:INotifyPropertyChanged
{
 public static LocalizationService Instance {get;}=new();
 private IReadOnlyList<LanguageOption>? languages;
 private Dictionary<string,string> entries=new(StringComparer.Ordinal);
 public event PropertyChangedEventHandler? PropertyChanged;
 public int Revision {get;private set;}
 public string Language {get;private set;}="en";
 public string CurrentLanguageName {get;private set;}="English";
 private string directoryPath=Path.Combine(AppContext.BaseDirectory,"Localization");
 public string DirectoryPath {get=>directoryPath;set {directoryPath=value;languages=null;}}
 public string? Warning {get;private set;}
 public string this[string key]=>T(key);
 public string T(string key)=>entries.GetValueOrDefault(Key(key),key);
 private static string Key(string text)=>System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(),"[^a-z0-9]+","_").Trim('_');
 private static bool ValidCode(string code)=>!string.IsNullOrWhiteSpace(code)&&code.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
 private static XElement Read(Stream input)
 {
  using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000});
  var root=XElement.Load(reader);if(root.Name!="Language")throw new XmlException("Expected Language root element.");return root;
 }
 private XElement? ReadFile(string code)
 {
  var file=Path.Combine(DirectoryPath,code+".xml");if(!File.Exists(file))return null;
  using var input=File.OpenRead(file);return Read(input);
 }
 private static void Merge(Dictionary<string,string> map,XElement? root)
 {
  if(root==null)return;
  foreach(var entry in root.Elements("String")){var key=(string?)entry.Attribute("Key");if(!string.IsNullOrWhiteSpace(key))map[Key(key)]=entry.Value;}
 }
 public IReadOnlyList<LanguageOption> GetLanguages()
 {
  if(languages!=null)return languages;
  var result=new List<LanguageOption>{new("en","English")};
  try
  {
   if(Directory.Exists(DirectoryPath))foreach(var file in Directory.EnumerateFiles(DirectoryPath,"*.xml"))
    try{var code=Path.GetFileNameWithoutExtension(file);if(!ValidCode(code)||code=="en")continue;var root=ReadFile(code);if(root!=null)result.Add(new(code,(string?)root.Attribute("Name")??code,(string?)root.Attribute("Flag")));}
    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or XmlException){}
  }
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
  return languages=result.OrderBy(x=>x.Code=="en"?0:1).ThenBy(x=>x.Name).ToArray();
 }
 public void SetLanguage(string language)
 {
  if(!ValidCode(language))language="en";
  var baseline=new Dictionary<string,string>(StringComparer.Ordinal);Warning=null;
  using(var embedded=typeof(LocalizationService).Assembly.GetManifestResourceStream("XShard.Localization.en.xml"))if(embedded!=null)Merge(baseline,Read(embedded));
  try{Merge(baseline,ReadFile("en"));}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or XmlException){Warning=ex.Message;}
  var merged=new Dictionary<string,string>(baseline,StringComparer.Ordinal);Language="en";CurrentLanguageName="English";
  if(language!="en")try{var root=ReadFile(language);if(root!=null){Merge(merged,root);Language=language;CurrentLanguageName=(string?)root.Attribute("Name")??language;}}
   catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or XmlException){Warning=ex.Message;merged=baseline;}
  entries=merged;Revision++;if(languages!=null)foreach(var option in languages)option.Refresh();
  PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Revision)));
  PropertyChanged?.Invoke(this,new PropertyChangedEventArgs("Item[]"));
  PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Language)));
 }
}
