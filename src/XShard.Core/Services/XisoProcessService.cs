using System.Diagnostics;
using XShard.Models;
namespace XShard.Services;
public sealed class XisoProcessService
{
 public static string? FindBackend(string configuredPath)
 {
  if(!string.IsNullOrWhiteSpace(configuredPath)) return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
  string name=OperatingSystem.IsWindows()?"extract-xiso.exe":"extract-xiso";
  return new[]{Path.Combine(AppContext.BaseDirectory,"backends","extract-xiso"),AppContext.BaseDirectory,Path.Combine(AppContext.BaseDirectory,"tools"),Path.Combine(AppContext.BaseDirectory,"bin")}
   .Select(dir=>Path.Combine(dir,name)).FirstOrDefault(File.Exists);
 }
 public static List<string> BuildArguments(XisoJobOptions options,string source,string destination)
 {
  var args=new List<string>();
  // Upstream parses -m before -x/-l; keep optional flags before the mode.
  if(options.DisableMediaPatching && options.Mode is XisoMode.Create or XisoMode.Rewrite) args.Add("-m");
  if(options.SkipSystemUpdate && options.Mode!=XisoMode.List) args.Add("-s");
  switch(options.Mode)
  {
   case XisoMode.Extract: args.AddRange(["-d",destination,"-x",source]);break;
   case XisoMode.Create: args.AddRange(["-c",source,destination]);break;
   case XisoMode.List: args.AddRange(["-l",source]);break;
   case XisoMode.Rewrite: args.AddRange(["-d",destination,"-r",source]);break;
  }
  return args;
 }
 public async Task<int> RunAsync(string backend,IEnumerable<string> args,string workingDirectory,Action<string,bool> output,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  var start=new ProcessStartInfo(backend) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=workingDirectory };
  foreach(var arg in args) start.ArgumentList.Add(arg);
  using var process=new Process { StartInfo=start };
  if(!process.Start()) throw new IOException("The backend could not be started.");
  void StopProcess()
  {
   try { if(!process.HasExited) process.Kill(true); }
   catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
   // Fall back to terminating the root if process-tree enumeration is unavailable.
   try { if(!process.HasExited) process.Kill(); }
   catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
  }
  using var registration=token.Register(StopProcess);
  async Task Pump(StreamReader reader,bool error)
  {
   try { while(await reader.ReadLineAsync(token) is {} line) output(line,error); }
   catch(OperationCanceledException) when(token.IsCancellationRequested) {}
   catch(IOException) when(token.IsCancellationRequested) {}
  }
  var stdout=Pump(process.StandardOutput,false);var stderr=Pump(process.StandardError,true);
  try { await process.WaitForExitAsync(token); await Task.WhenAll(stdout,stderr); }
  catch(OperationCanceledException)
  {
   StopProcess();
   using var shutdown=new CancellationTokenSource(TimeSpan.FromSeconds(3));
   try { await process.WaitForExitAsync(shutdown.Token); } catch(OperationCanceledException) {}
   await Task.WhenAll(stdout,stderr);
   throw;
  }
  finally { if(!process.HasExited) StopProcess(); }

  token.ThrowIfCancellationRequested();
  return process.ExitCode;
 }
 public async Task<string> ProbeAsync(string backend,CancellationToken token)
 {
  var lines=new List<string>();var gate=new object();
  var exit=await RunAsync(backend,["-v"],AppContext.BaseDirectory,(line,_)=> { lock(gate) lines.Add(line); },token);
  var text=string.Join(" ",lines);
  if(exit!=0 || !text.Contains("extract-xiso",StringComparison.OrdinalIgnoreCase)) throw new IOException("The selected file is not a working extract-xiso backend.");
  return text.Trim();
 }
}
