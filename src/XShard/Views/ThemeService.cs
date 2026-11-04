using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
namespace XShard.Views;
/// <summary>
/// Themes: Dark (Windows 11 Mica, neutral dark gray; default), Xbox (the former dark green theme), Light and System (follows Windows: Dark or Light).
/// Accent: Green (default), another fixed color or Windows (system accent color); the Xbox theme always uses the Xbox green accent.
/// </summary>
public static class ThemeService
{
 /// <summary>Custom theme variant for the green Xbox palette; inherits Fluent's Dark resources.</summary>
 public static readonly ThemeVariant XboxVariant=new("Xbox",ThemeVariant.Dark);
 public static readonly string[] Themes=["Dark","Xbox","Light","System"];
 public static readonly IReadOnlyDictionary<string,Color> FixedAccents=new Dictionary<string,Color>
 {
  ["Green"]=Color.Parse("#318B3B"),
  ["Teal"]=Color.Parse("#1E8A86"),
  ["Blue"]=Color.Parse("#2F6FD0"),
  ["Purple"]=Color.Parse("#7A4FD1"),
  ["Orange"]=Color.Parse("#C4661C"),
  ["Red"]=Color.Parse("#C23B4A")
 };
 private static readonly Color FallbackWindowsAccent=Color.Parse("#0078D4");
 private static string currentTheme="Dark",currentAccent="Green";
 private static Window? window;
 private static bool hooked;
 private static Bitmap? colorLogo,lightLogo;

 /// <summary>The Windows accent color (Settings › Personalization › Colors), or Windows blue if unavailable.</summary>
 public static Color WindowsAccent()
 {
  try{if(Application.Current?.PlatformSettings?.GetColorValues() is {} values&&values.AccentColor1.A>0)return values.AccentColor1;}
  catch(Exception){}
  return FallbackWindowsAccent;
 }
 public static string WindowsAccentHex(){var c=WindowsAccent();return $"#{c.R:X2}{c.G:X2}{c.B:X2}";}

 public static void Apply(string theme,string accent,Window? target=null)
 {
  if(Application.Current is not {} app)return;
  if(target!=null&&!ReferenceEquals(window,target))
  {
   window=target;
   target.PropertyChanged+=(_,e)=>{if(e.Property==TopLevel.ActualTransparencyLevelProperty)UpdateBackdrop();};
  }
  currentTheme=Themes.Contains(theme)?theme:"Dark";currentAccent=accent;
  app.RequestedThemeVariant=currentTheme switch {"Light"=>ThemeVariant.Light,"Xbox"=>XboxVariant,"System"=>ThemeVariant.Default,_=>ThemeVariant.Dark};
  if(!hooked)
  {
   hooked=true;
   app.ActualThemeVariantChanged+=(_,_)=>{ApplyAccent();UpdateBackdrop();};
   if(app.PlatformSettings is {} platform)platform.ColorValuesChanged+=(_,_)=>Avalonia.Threading.Dispatcher.UIThread.Post(ApplyAccent);
  }
  ApplyAccent();UpdateBackdrop();
 }
 private static bool IsXbox(Application app)=>app.ActualThemeVariant==XboxVariant;
 private static bool IsLight(Application app)=>app.ActualThemeVariant==ThemeVariant.Light;
 /// <summary>Mica for the gray Dark theme on Windows 11; solid background (from XsWindowBg) everywhere else.</summary>
 private static void UpdateBackdrop()
 {
  if(window is null||Application.Current is not {} app)return;
  var wantMica=!IsLight(app)&&!IsXbox(app);
  window.TransparencyLevelHint=wantMica?[WindowTransparencyLevel.Mica,WindowTransparencyLevel.None]:[WindowTransparencyLevel.None];
  if(wantMica&&window.ActualTransparencyLevel==WindowTransparencyLevel.Mica)window.Background=Brushes.Transparent;
  else window.ClearValue(TemplatedControl.BackgroundProperty);
 }
 private static void ApplyAccent()
 {
  if(Application.Current is not {} app)return;
  var light=IsLight(app);var xbox=IsXbox(app);
  var a=xbox?FixedAccents["Green"]:currentAccent=="Windows"?WindowsAccent():FixedAccents.GetValueOrDefault(currentAccent,FixedAccents["Green"]);
  var card=Color.Parse(xbox?"#19221B":"#2B2B2B");
  void Set(string key,Color color)=>app.Resources[key]=new SolidColorBrush(color);
  Set("XsAccent",a);
  Set("XsAccentDim",light?Mix(a,Colors.White,0.65):Mix(a,card,0.70));
  Set("XsAccentSoft",light?Mix(a,Colors.White,0.82):Mix(a,card,0.72));
  Set("XsAccentBorder",light?Mix(a,Colors.White,0.45):Mix(a,card,0.35));
  Set("XsAccentText",light?Mix(a,Colors.Black,0.25):Mix(a,Colors.White,0.45));
  Set("XsAccentProgress",light?a:Mix(a,Colors.White,0.25));
  Set("XsOnAccent",Colors.White);
  // Fluent controls (focus rings, sliders, toggles) follow the same accent.
  app.Resources["SystemAccentColor"]=a;
  app.Resources["SystemAccentColorLight1"]=Mix(a,Colors.White,0.2);app.Resources["SystemAccentColorLight2"]=Mix(a,Colors.White,0.4);app.Resources["SystemAccentColorLight3"]=Mix(a,Colors.White,0.6);
  app.Resources["SystemAccentColorDark1"]=Mix(a,Colors.Black,0.2);app.Resources["SystemAccentColorDark2"]=Mix(a,Colors.Black,0.4);app.Resources["SystemAccentColorDark3"]=Mix(a,Colors.Black,0.6);
  foreach(var fluent in app.Styles.OfType<FluentTheme>())
   foreach(var palette in fluent.Palettes.Values)palette.Accent=light?a:Mix(a,Colors.White,0.2);
  // Logo: the white "X" disappears on light backgrounds, so the light theme uses the all-green logo.
  app.Resources["XsLogo"]=light?(lightLogo??=Load("app-logo-light.png")):(colorLogo??=Load("app-logo-color.png"));
 }
 private static Bitmap Load(string name)=>new(AssetLoader.Open(new Uri("avares://X-Shard/Assets/"+name)));
 /// <summary>Linear mix: amount 0 = a, 1 = b.</summary>
 public static Color Mix(Color a,Color b,double amount)
 {
  byte M(byte x,byte y)=>(byte)Math.Round(x+(y-x)*amount);
  return Color.FromArgb(255,M(a.R,b.R),M(a.G,b.G),M(a.B,b.B));
 }
}
