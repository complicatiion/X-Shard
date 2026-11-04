using XShard.Models;
namespace XShard.Services;
public sealed class JobExecutor(XisoProcessService process)
{
 public static void Validate(XisoJobOptions options)
 {
  options=BackendPolicy.Normalize(options);
  if(options.Engine==BackendEngine.XgdTool ? (!Directory.Exists(options.Source)&&!File.Exists(options.Source)) : options.Mode==XisoMode.Create ? !Directory.Exists(options.Source) : !File.Exists(options.Source))
   throw new IOException("Source does not exist: "+options.Source);
  if(options.Engine==BackendEngine.XgdTool)XgdToolService.BuildArguments(options,"validation-output");
  if(options.Mode==XisoMode.List) return;
  var source=Path.GetFullPath(options.Source).TrimEnd(Path.DirectorySeparatorChar);
  var target=Path.GetFullPath(options.Destination).TrimEnd(Path.DirectorySeparatorChar);
  if(File.Exists(target)||Directory.Exists(target)) throw new IOException("Destination already exists. Choose a new destination: "+target);
  if(source.Equals(target,StringComparison.OrdinalIgnoreCase)) throw new IOException("Source and destination must be different.");
  if(Directory.Exists(options.Source) && target.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
   throw new IOException("The destination must be outside the source folder.");
 }
 /// <summary>Runs one job. Returns the published result path (file or folder), or an empty string for List.</summary>
 public async Task<string> ExecuteAsync(XisoJobOptions options,string backend,Action<string,bool> output,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  options=BackendPolicy.Normalize(options);
  Validate(options);
  if(options.Engine==BackendEngine.XgdTool) return await new XgdToolService(process).ExecuteAsync(options,backend,output,token);
  if(options.Mode==XisoMode.List)
  {
   var code=await process.RunAsync(backend,XisoProcessService.BuildArguments(options,options.Source,""),AppContext.BaseDirectory,output,token);
   if(code!=0) throw new IOException($"Backend exited with code {code}. See the log.");return "";
  }
  var parent=Path.GetDirectoryName(options.Destination)!;Directory.CreateDirectory(parent);
  var stage=Path.Combine(parent,".xshard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
  bool complete=false;
  try
  {
   string input=options.Source;string outputPath=Path.Combine(stage,"result.iso");
   if(options.Mode==XisoMode.Extract) { outputPath=Path.Combine(stage,"files"); }
   if(options.Mode==XisoMode.Rewrite)
   {
    output("Preparing a working copy. The original ISO is retained.",false);
    input=Path.Combine(stage,Path.GetFileName(options.Source));
    await using(var from=new FileStream(options.Source,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true))
    await using(var to=new FileStream(input,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true))
     await from.CopyToAsync(to,1024*1024,token);
    outputPath=Path.Combine(stage,"rewritten");Directory.CreateDirectory(outputPath);
   }
   var args=XisoProcessService.BuildArguments(options,input,outputPath);
   output("Mode: "+options.Mode+" | source: "+options.Source,false);
   var code=await process.RunAsync(backend,args,stage,output,token);
   if(code!=0) throw new IOException($"Backend exited with code {code}. See the log.");
   token.ThrowIfCancellationRequested();
   if(options.Mode==XisoMode.Extract)
   {
    if(!Directory.Exists(outputPath)) throw new IOException("The backend did not produce an extraction folder.");
    Directory.Move(outputPath,options.Destination);
   }
   else
   {
    if(options.Mode==XisoMode.Rewrite)
    {
     var rewritten=Path.Combine(outputPath,Path.GetFileName(input));
     // An already optimized image is skipped by upstream; publish the untouched working copy.
     outputPath=File.Exists(rewritten)?rewritten:File.Exists(input)?input:throw new IOException("Rewrite did not produce an image.");
    }
    if(!File.Exists(outputPath)||new FileInfo(outputPath).Length==0) throw new IOException("The backend did not produce an ISO.");
    File.Move(outputPath,options.Destination,false);
   }
   complete=true;output("Completed: "+options.Destination,false);
   return options.Destination;
  }
  finally
  {
   // Never remove incomplete work automatically. Only the private, successful staging directory is removed.
   if(complete) { try { Directory.Delete(stage,true); } catch(IOException) {} catch(UnauthorizedAccessException) {} }
   else output("Incomplete working files retained at: "+stage,true);
  }
 }
}
