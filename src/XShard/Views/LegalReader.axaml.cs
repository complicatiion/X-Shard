using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Threading;
using XShard.ViewModels;
namespace XShard.Views;
public partial class LegalReader:UserControl
{
 public LegalReader()
 {
  InitializeComponent();
  // Links inside legal documents are routed through the Yes/No consent card instead of opening the browser directly.
  if(this.FindControl<Markdown.Avalonia.MarkdownScrollViewer>("MarkdownBody")?.Engine is global::Markdown.Avalonia.Markdown engine)engine.HyperlinkCommand=new ConsentLinkCommand(this);
  AttachedToVisualTree+=(_,_)=>Dispatcher.UIThread.Post(ConfigureReader);
  DataContextChanged+=(_,_)=>{if(DataContext is MainViewModel vm)vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.SelectedLegalMarkdown)){this.FindControl<ScrollViewer>("ReaderScroll")!.Offset=default;Dispatcher.UIThread.Post(ConfigureReader);}};};
 }
 private void ConfigureReader(){var body=this.FindControl<Markdown.Avalonia.MarkdownScrollViewer>("MarkdownBody")!;foreach(var scroll in body.GetVisualDescendants().OfType<ScrollViewer>()){scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;scroll.ClipToBounds=true;}}
 private sealed class ConsentLinkCommand(LegalReader owner):ICommand
 {
  public event EventHandler? CanExecuteChanged {add{}remove{}}
  public bool CanExecute(object? parameter)=>true;
  public void Execute(object? parameter){if(parameter is string url&&owner.DataContext is MainViewModel vm)_=vm.OpenExternalLinkAsync(url);}
 }
}
