using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using XShard.ViewModels;
namespace XShard.Views;
public partial class DetailsPanel:UserControl
{
 public static readonly StyledProperty<bool> IsModalProperty=AvaloniaProperty.Register<DetailsPanel,bool>(nameof(IsModal));
 public bool IsModal {get=>GetValue(IsModalProperty);set=>SetValue(IsModalProperty,value);}
 public event EventHandler<RoutedEventArgs>? ExportRequested;
 public event EventHandler<RoutedEventArgs>? RefreshRequested;
 public event EventHandler<RoutedEventArgs>? CloseRequested;
 public event EventHandler<RoutedEventArgs>? LaunchRequested;
 public DetailsPanel(){InitializeComponent();SizeChanged+=(_,_)=>ResizeBody();DataContextChanged+=(_,_)=>{if(DataContext is MainViewModel vm)vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.DetailsOuterScroll))ResizeBody();};ResizeBody();};}
 private void ResizeBody(){var scroll=this.FindControl<ScrollViewer>("OuterScroll")!;var tabs=this.FindControl<TabControl>("DetailTabs")!;var enabled=(DataContext as MainViewModel)?.DetailsOuterScroll??true;scroll.VerticalScrollBarVisibility=enabled?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;tabs.Height=Math.Max(180,Bounds.Height-68);}
 private void Export_Click(object? sender,RoutedEventArgs e)=>ExportRequested?.Invoke(this,e);
 private void Refresh_Click(object? sender,RoutedEventArgs e)=>RefreshRequested?.Invoke(this,e);
 private void Close_Click(object? sender,RoutedEventArgs e)=>CloseRequested?.Invoke(this,e);
 private void Launch_Click(object? sender,RoutedEventArgs e)=>LaunchRequested?.Invoke(this,e);
}
