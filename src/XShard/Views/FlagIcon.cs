using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace XShard.Views;
/// <summary>Flat 2D country flag drawn as vector shapes (no emoji, no bitmap, no external resources).</summary>
public sealed class FlagIcon : Control
{
 public static readonly StyledProperty<string?> CodeProperty=AvaloniaProperty.Register<FlagIcon,string?>(nameof(Code));
 public static readonly StyledProperty<string?> SpecProperty=AvaloniaProperty.Register<FlagIcon,string?>(nameof(Spec));
 /// <summary>Language code, e.g. "en", "de". Selects a built-in flag.</summary>
 public string? Code {get=>GetValue(CodeProperty);set=>SetValue(CodeProperty,value);}
 /// <summary>Optional stripe definition from the language XML: "h:#000000,#DD0000,#FFCE00" (horizontal) or "v:#0055A4,#FFFFFF,#EF4135" (vertical).</summary>
 public string? Spec {get=>GetValue(SpecProperty);set=>SetValue(SpecProperty,value);}
 private static readonly Dictionary<string,string> BuiltIn=new(StringComparer.OrdinalIgnoreCase)
 {
  ["de"]="h:#000000,#DD0000,#FFCE00",
  ["fr"]="v:#0055A4,#FFFFFF,#EF4135",
  ["es"]="h:#AA151B,#F1BF00,#F1BF00,#AA151B",
  ["it"]="v:#009246,#FFFFFF,#CE2B37",
  ["nl"]="h:#AE1C28,#FFFFFF,#21468B",
  ["pl"]="h:#FFFFFF,#DC143C",
  ["ru"]="h:#FFFFFF,#0039A6,#D52B1E",
  ["at"]="h:#C8102E,#FFFFFF,#C8102E",
  ["be"]="v:#000000,#FDDA24,#EF3340",
  ["ie"]="v:#169B62,#FFFFFF,#FF883E"
 };
 private static readonly IPen Frame=new Pen(new SolidColorBrush(Color.Parse("#66FFFFFF")),0.8);
 static FlagIcon() {AffectsRender<FlagIcon>(CodeProperty,SpecProperty);}
 public FlagIcon() {Width=22;Height=15;IsHitTestVisible=false;}
 public override void Render(DrawingContext context)
 {
  base.Render(context);
  var bounds=new Rect(Bounds.Size);if(bounds.Width<=0||bounds.Height<=0)return;
  using(context.PushClip(new RoundedRect(bounds,2.5)))
  {
   var code=(Code??"").Split('-','_')[0];
   if(string.IsNullOrWhiteSpace(Spec)&&code.Equals("en",StringComparison.OrdinalIgnoreCase))DrawUnionFlag(context,bounds);
   else if(string.IsNullOrWhiteSpace(Spec)&&NordicCross.TryGetValue(code,out var cross))DrawNordic(context,bounds,cross.Field,cross.Cross,cross.Inner);
   else if(!DrawStripes(context,bounds,!string.IsNullOrWhiteSpace(Spec)?Spec!:BuiltIn.GetValueOrDefault(code)))DrawNeutral(context,bounds);
  }
  context.DrawRectangle(null,Frame,bounds.Deflate(0.4),2.5,2.5);
 }
 private static bool DrawStripes(DrawingContext context,Rect bounds,string? spec)
 {
  if(string.IsNullOrWhiteSpace(spec)||spec.Length<3||spec[1]!=':')return false;
  var vertical=char.ToLowerInvariant(spec[0])=='v';if(!vertical&&char.ToLowerInvariant(spec[0])!='h')return false;
  var colors=new List<Color>();
  foreach(var part in spec[2..].Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)){if(!Color.TryParse(part,out var color))return false;colors.Add(color);}
  if(colors.Count is 0 or >8)return false;
  var step=(vertical?bounds.Width:bounds.Height)/colors.Count;
  for(var i=0;i<colors.Count;i++)
  {
   // Overlap by a fraction of a pixel to avoid hairline seams between stripes.
   var rect=vertical?new Rect(bounds.X+i*step,bounds.Y,step+0.5,bounds.Height):new Rect(bounds.X,bounds.Y+i*step,bounds.Width,step+0.5);
   context.FillRectangle(new SolidColorBrush(colors[i]),rect);
  }
  return true;
 }
 /// <summary>Scandinavian cross flags: field, cross and optional inner cross color.</summary>
 private static readonly Dictionary<string,(string Field,string Cross,string? Inner)> NordicCross=new(StringComparer.OrdinalIgnoreCase)
 {
  ["da"]=("#C8102E","#FFFFFF",null),["sv"]=("#006AA7","#FECC02",null),["fi"]=("#FFFFFF","#002F6C",null),["nb"]=("#BA0C2F","#FFFFFF","#00205B"),["no"]=("#BA0C2F","#FFFFFF","#00205B"),["is"]=("#02529C","#FFFFFF","#DC1E35")
 };
 private static void DrawNordic(DrawingContext context,Rect b,string field,string cross,string? inner)
 {
  context.FillRectangle(new SolidColorBrush(Color.Parse(field)),b);
  var thickness=b.Height*0.22;var x=b.X+b.Width*0.36;var y=b.Center.Y-thickness/2;
  var crossBrush=new SolidColorBrush(Color.Parse(cross));
  context.FillRectangle(crossBrush,new Rect(x,b.Y,thickness,b.Height));context.FillRectangle(crossBrush,new Rect(b.X,y,b.Width,thickness));
  if(inner==null)return;
  var thin=thickness*0.5;var innerBrush=new SolidColorBrush(Color.Parse(inner));
  context.FillRectangle(innerBrush,new Rect(x+(thickness-thin)/2,b.Y,thin,b.Height));context.FillRectangle(innerBrush,new Rect(b.X,b.Center.Y-thin/2,b.Width,thin));
 }
 private static void DrawUnionFlag(DrawingContext context,Rect b)
 {
  var blue=new SolidColorBrush(Color.Parse("#012169"));var white=Brushes.White;var red=new SolidColorBrush(Color.Parse("#C8102E"));
  context.FillRectangle(blue,b);
  var h=b.Height;
  context.DrawLine(new Pen(white,h*0.22),b.TopLeft,b.BottomRight);context.DrawLine(new Pen(white,h*0.22),b.TopRight,b.BottomLeft);
  context.DrawLine(new Pen(red,h*0.08),b.TopLeft,b.BottomRight);context.DrawLine(new Pen(red,h*0.08),b.TopRight,b.BottomLeft);
  var cx=b.Center.X;var cy=b.Center.Y;
  context.FillRectangle(white,new Rect(cx-h*0.18,b.Y,h*0.36,h));context.FillRectangle(white,new Rect(b.X,cy-h*0.18,b.Width,h*0.36));
  context.FillRectangle(red,new Rect(cx-h*0.11,b.Y,h*0.22,h));context.FillRectangle(red,new Rect(b.X,cy-h*0.11,b.Width,h*0.22));
 }
 private static void DrawNeutral(DrawingContext context,Rect b)
 {
  context.FillRectangle(new SolidColorBrush(Color.Parse("#3B463C")),b);
  context.DrawEllipse(null,new Pen(new SolidColorBrush(Color.Parse("#A5DF8A")),1),b.Center,b.Height*0.3,b.Height*0.3);
 }
}
