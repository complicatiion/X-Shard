using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace XShard.Views;
public sealed class AppIcon : Control
{
 public static readonly StyledProperty<string> KindProperty=AvaloniaProperty.Register<AppIcon,string>(nameof(Kind),"Info");
 public static readonly StyledProperty<IBrush> StrokeProperty=AvaloniaProperty.Register<AppIcon,IBrush>(nameof(Stroke),Brushes.White);
 public string Kind {get=>GetValue(KindProperty);set=>SetValue(KindProperty,value);}
 public IBrush Stroke {get=>GetValue(StrokeProperty);set=>SetValue(StrokeProperty,value);}
 private static readonly Dictionary<string,Geometry> Shapes=new()
 {
  ["ChevronUp"]=Geometry.Parse("M5,16 L12,9 L19,16"),
  ["ClearAll"]=Geometry.Parse("M3,5 H16 M3,10 H12 M3,15 H8 M14,12 L21,19 M21,12 L14,19 M3,20 H8"),
  ["FolderPlus"]=Geometry.Parse("M3,6 H10 L12,8 H21 V20 H3 Z M12,10 V18 M8,14 H16"),
  ["ChevronDown"]=Geometry.Parse("M5,9 L12,16 L19,9"),
  ["Extract"] = Geometry.Parse("M3,4 H21 V20 H3 Z M12,7 V16 M8,12 L12,16 L16,12"),
  ["Create"] = Geometry.Parse("M3,4 H21 V20 H3 Z M12,17 V8 M8,12 L12,8 L16,12"),
  ["List"] = Geometry.Parse("M8,5 H21 M8,12 H21 M8,19 H21 M3,5 H4 M3,12 H4 M3,19 H4"),
  ["Convert"] = Geometry.Parse("M3,7 H20 M16,3 L20,7 L16,11 M21,17 H4 M8,13 L4,17 L8,21"),
  ["Test"] = Geometry.Parse("M4,8 H20 V20 H4 Z M8,4 V8 M16,4 V8 M7,14 H10 L12,10 L15,18 L17,14 H20"),
  ["Play"] = Geometry.Parse("M8,5 L19,12 L8,19 Z"),
  ["Stop"] = Geometry.Parse("M6,6 H18 V18 H6 Z"),
  ["Cancel"] = Geometry.Parse("M6,6 L18,18 M18,6 L6,18"),
  ["Plus"] = Geometry.Parse("M12,5 V19 M5,12 H19"),
  ["Folder"] = Geometry.Parse("M3,6 H10 L12,8 H21 V20 H3 Z"),
  ["Save"] = Geometry.Parse("M4,3 H18 L21,6 V21 H3 V3 Z M7,3 V9 H17 V3 M7,21 V14 H17 V21"),
  ["Trash"] = Geometry.Parse("M3,6 H21 M9,6 V3 H15 V6 M6,6 L7,21 H17 L18,6 M10,10 V17 M14,10 V17"),
  ["Retry"] = Geometry.Parse("M20,7 V12 H15 M20,12 A8,8 0 1 0 19,17"),
  ["Info"] = Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,10 V17 M12,7 V7.1"),
  ["Settings"] = Geometry.Parse("M10.05,4.97 L10.32,2.45 L13.68,2.45 L13.95,4.97 A7.3,7.3 0 0 1 15.59,5.65 L17.56,4.05 L19.95,6.44 L18.35,8.41 A7.3,7.3 0 0 1 19.03,10.05 L21.55,10.32 L21.55,13.68 L19.03,13.95 A7.3,7.3 0 0 1 18.35,15.59 L19.95,17.56 L17.56,19.95 L15.59,18.35 A7.3,7.3 0 0 1 13.95,19.03 L13.68,21.55 L10.32,21.55 L10.05,19.03 A7.3,7.3 0 0 1 8.41,18.35 L6.44,19.95 L4.05,17.56 L5.65,15.59 A7.3,7.3 0 0 1 4.97,13.95 L2.45,13.68 L2.45,10.32 L4.97,10.05 A7.3,7.3 0 0 1 5.65,8.41 L4.05,6.44 L6.44,4.05 L8.41,5.65 A7.3,7.3 0 0 1 10.05,4.97 Z M12,8.6 A3.4,3.4 0 1 0 12,15.4 A3.4,3.4 0 1 0 12,8.6 Z"),
  ["Details"] = Geometry.Parse("M5,3 H14 L19,8 V21 H5 Z M14,3 V8 H19 M8.5,12 H15.5 M8.5,15 H15.5 M8.5,18 H12.5"),
  ["Shield"] = Geometry.Parse("M12,3 L20,6 V11 C20,16 16.5,19.5 12,21 C7.5,19.5 4,16 4,11 V6 Z M8.5,12 L11,14.5 L15.5,9.5"),
  ["Globe"] = Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M3,12 H21 M12,3 C6,8 6,16 12,21 C18,16 18,8 12,3"),
  ["GitHub"] = Geometry.Parse("M12,2 C6.5,2 2,6.5 2,12 C2,16.4 4.9,20.2 8.8,21.5 V18.5 C6.3,19 5.8,17.4 5.8,17.4 C5.4,16.3 4.7,16 4.7,16 C3.8,15.4 4.8,15.4 4.8,15.4 C5.8,15.5 6.3,16.4 6.3,16.4 C7.2,18 8.6,17.5 9.1,17.2 C9.2,16.5 9.5,16 9.8,15.7 C7.8,15.5 5.7,14.7 5.7,11.2 C5.7,10.2 6.1,9.4 6.7,8.8 C6.6,8.6 6.2,7.5 6.8,6.1 C6.8,6.1 7.6,5.8 9.5,7.1 C11.1,6.6 12.9,6.6 14.5,7.1 C16.4,5.8 17.2,6.1 17.2,6.1 C17.8,7.5 17.4,8.6 17.3,8.8 C17.9,9.4 18.3,10.2 18.3,11.2 C18.3,14.7 16.2,15.5 14.2,15.7 C14.6,16.1 14.9,16.8 14.9,17.8 V21.5 C18.9,20.2 22,16.4 22,12 C22,6.5 17.5,2 12,2 Z"),
  ["Search"] = Geometry.Parse("M10.5,3 A7.5,7.5 0 1 0 10.5,18 A7.5,7.5 0 1 0 10.5,3 M16,16 L21,21"),
  ["Sort"] = Geometry.Parse("M7,4 V20 M3,16 L7,20 L11,16 M17,20 V4 M13,8 L17,4 L21,8"),
  ["Report"] = Geometry.Parse("M5,3 H15 L19,7 V21 H5 Z M15,3 V7 H19 M8,17 V13 M12,17 V10 M16,17 V14"),
  ["Power"] = Geometry.Parse("M12,3 V12 M7.5,6 A8,8 0 1 0 16.5,6"),
  ["Gamepad"] = Geometry.Parse("M7,8 H17 C20,8 22,11 22,15 C22,18 20,19 18.5,18 L16,15.5 H8 L5.5,18 C4,19 2,18 2,15 C2,11 4,8 7,8 Z M7,11 V14 M5.5,12.5 H8.5 M16,11.5 V11.6 M18,13.5 V13.6"),
  ["Palette"] = Geometry.Parse("M12,3 C7,3 3,7 3,12 C3,17 7,21 12,21 C13.5,21 14,20 13.5,18.8 C13,17.5 14,16.5 15.3,16.5 H17 C19.5,16.5 21,14.8 21,12 C21,7 17,3 12,3 Z M7.5,12 V12.1 M9.5,7.8 V7.9 M14.5,7.8 V7.9 M17,11.5 V11.6"),
  ["Bell"] = Geometry.Parse("M6,17 V11 A6,6 0 0 1 18,11 V17 L20,19 H4 Z M10,21 H14 M12,3 V5"),
  ["Disc"] = Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,9.5 A2.5,2.5 0 1 0 12,14.5 A2.5,2.5 0 1 0 12,9.5 M7,8 A6,6 0 0 1 10,6.3"),
  ["Target"] = Geometry.Parse("M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 M12,8.5 A3.5,3.5 0 1 0 12,15.5 A3.5,3.5 0 1 0 12,8.5 M12,1.5 V5 M12,19 V22.5 M1.5,12 H5 M19,12 H22.5"),
  ["Warning"] = Geometry.Parse("M12,3 L22,20 H2 Z M12,9 V14 M12,17 V17.1"),
  ["Export"] = Geometry.Parse("M12,15 V3 M7,8 L12,3 L17,8 M4,14 V21 H20 V14"),
  ["Check"] = Geometry.Parse("M4,12 L9,17 L20,6"),
  ["Pause"] = Geometry.Parse("M8,5 V19 M16,5 V19"),
  ["Chevron"] = Geometry.Parse("M8,5 L15,12 L8,19")
 };
 static AppIcon() { AffectsRender<AppIcon>(KindProperty,StrokeProperty); }
 public AppIcon() { Width=20;Height=20;IsHitTestVisible=false; }
 public override void Render(DrawingContext context)
 {
  base.Render(context);
  var shape=Shapes.GetValueOrDefault(Kind,Shapes["Info"]);
  using(context.PushTransform(Matrix.CreateScale(Bounds.Width/24,Bounds.Height/24)))
   context.DrawGeometry(Kind is "GitHub" or "Play" or "Stop"?Stroke:null,new Pen(Stroke,1.7){LineCap=PenLineCap.Round,LineJoin=Kind is "Settings" or "Shield" or "Palette" or "Gamepad" or "Bell" or "Warning"?PenLineJoin.Round:PenLineJoin.Miter},shape);
 }
}
