using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Minimal FTP server for the verification harness (127.0.0.1 only): login, PASV/EPSV, STOR/APPE/REST, SIZE, MKD, CWD,
/// RNFR/RNTO, DELE and LIST. It can drop the control and data connection once in the middle of an upload to simulate a
/// console that goes offline, and it records the order in which files were completed.
/// </summary>
internal sealed class FtpTestServer:IAsyncDisposable
{
 private readonly TcpListener listener=new(IPAddress.Loopback,0);
 private readonly CancellationTokenSource stop=new();
 private readonly string root;private readonly string user,password;
 private int dropped;
 public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
 /// <summary>Drop the connection once after this many bytes of one upload (0 = never).</summary>
 public long DropAfterBytes {get;set;}
 public bool HasDropped=>dropped>0;
 /// <summary>false = behave like a server without APPE/REST (502), to test the send-again fallback.</summary>
 public bool SupportsResume {get;set;}=true;
 /// <summary>Final names in the order files became visible (STOR without temp suffix, or RNTO).</summary>
 public List<string> Completed {get;}=[];
 private readonly object gate=new();

 public FtpTestServer(string root,string user,string password)
 {
  this.root=Path.GetFullPath(root);this.user=user;this.password=password;Directory.CreateDirectory(this.root);
  listener.Start();_=AcceptAsync();
 }
 private async Task AcceptAsync()
 {
  while(!stop.IsCancellationRequested)
  {
   TcpClient client;
   try{client=await listener.AcceptTcpClientAsync(stop.Token);}catch(Exception){return;}
   _=Task.Run(()=>SessionAsync(client));
  }
 }
 private string Map(string cwd,string path)
 {
  var full=path.StartsWith('/')?path:(cwd.TrimEnd('/')+"/"+path);
  var parts=new List<string>();
  foreach(var part in full.Split('/',StringSplitOptions.RemoveEmptyEntries)){if(part==".")continue;if(part==".."){if(parts.Count>0)parts.RemoveAt(parts.Count-1);continue;}parts.Add(part);}
  return "/"+string.Join('/',parts);
 }
 private string Local(string virtualPath)=>Path.Combine([root,..virtualPath.Split('/',StringSplitOptions.RemoveEmptyEntries)]);

 private async Task SessionAsync(TcpClient client)
 {
  using var connection=client;
  var stream=client.GetStream();
  var reader=new StreamReader(stream,Encoding.ASCII);var writer=new StreamWriter(stream,Encoding.ASCII){NewLine="\r\n",AutoFlush=true};
  var cwd="/";var loggedIn=false;string? pendingUser=null,renameFrom=null;long rest=0;TcpListener? passive=null;
  async Task Reply(string text){await writer.WriteLineAsync(text);}
  try
  {
   await Reply("220 X-Shard verification FTP");
   while(!stop.IsCancellationRequested&&await reader.ReadLineAsync() is {} line)
   {
    var space=line.IndexOf(' ');var command=(space<0?line:line[..space]).ToUpperInvariant();var argument=space<0?"":line[(space+1)..];
    if(!loggedIn&&command is not ("USER" or "PASS" or "QUIT" or "FEAT" or "SYST" or "AUTH" or "OPTS")){await Reply("530 Not logged in");continue;}
    switch(command)
    {
     case "USER":pendingUser=argument;await Reply("331 Password required");break;
     case "PASS":if(pendingUser==user&&argument==password){loggedIn=true;await Reply("230 Logged in");}else await Reply("530 Login incorrect");break;
     case "SYST":await Reply("215 UNIX Type: L8");break;
     case "FEAT":await writer.WriteAsync(SupportsResume?"211-Features:\r\n SIZE\r\n REST STREAM\r\n211 End\r\n":"211-Features:\r\n SIZE\r\n211 End\r\n");break;
     case "AUTH":await Reply("502 TLS not supported");break;
     case "OPTS" or "TYPE" or "MODE" or "STRU" or "NOOP" or "CLNT":await Reply("200 OK");break;
     case "PWD" or "XPWD":await Reply($"257 \"{cwd}\" is the current directory");break;
     case "CWD":{var target=Map(cwd,argument);if(Directory.Exists(Local(target))){cwd=target;await Reply("250 OK");}else await Reply("550 No such directory");break;}
     case "CDUP":cwd=Map(cwd,"..");await Reply("250 OK");break;
     case "MKD":{var target=Map(cwd,argument);if(Directory.Exists(Local(target))){await Reply("550 Directory already exists");break;}Directory.CreateDirectory(Local(target));await Reply($"257 \"{target}\" created");break;}
     case "RMD":{var target=Local(Map(cwd,argument));if(Directory.Exists(target)){Directory.Delete(target,true);await Reply("250 OK");}else await Reply("550 Not found");break;}
     case "DELE":{var target=Local(Map(cwd,argument));if(File.Exists(target)){File.Delete(target);await Reply("250 OK");}else await Reply("550 Not found");break;}
     case "SIZE":{var target=Local(Map(cwd,argument));if(File.Exists(target))await Reply("213 "+new FileInfo(target).Length);else await Reply("550 Not found");break;}
     case "MDTM":{var target=Local(Map(cwd,argument));if(File.Exists(target))await Reply("213 "+File.GetLastWriteTimeUtc(target).ToString("yyyyMMddHHmmss"));else await Reply("550 Not found");break;}
     case "REST" when !SupportsResume:await Reply("502 Command not implemented");break;
     case "APPE" when !SupportsResume:await Reply("502 Command not implemented");break;
     case "REST":rest=long.Parse(argument);await Reply("350 Restarting at "+rest);break;
     case "PASV" or "EPSV":
     {
      passive?.Stop();passive=new TcpListener(IPAddress.Loopback,0);passive.Start();var port=((IPEndPoint)passive.LocalEndpoint).Port;
      await Reply(command=="PASV"?$"227 Entering Passive Mode (127,0,0,1,{port>>8},{port&255})":$"229 Entering Extended Passive Mode (|||{port}|)");
      break;
     }
     case "STOR" or "APPE":
     {
      if(passive==null){await Reply("425 Use PASV first");break;}
      var virtualPath=Map(cwd,argument);var target=Local(virtualPath);
      await Reply("150 Opening data connection");
      using var data=await passive.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(15));passive.Stop();passive=null;
      bool drop=false;
      await using(var file=new FileStream(target,command=="APPE"||rest>0?FileMode.OpenOrCreate:FileMode.Create,FileAccess.Write))
      {
       if(command=="APPE")file.Seek(0,SeekOrigin.End);else if(rest>0){file.SetLength(rest);file.Seek(rest,SeekOrigin.Begin);}
       rest=0;
       var buffer=new byte[65536];var received=0L;var input=data.GetStream();
       while(await input.ReadAsync(buffer) is var read and >0)
       {
        await file.WriteAsync(buffer.AsMemory(0,read));received+=read;
        if(DropAfterBytes>0&&received>=DropAfterBytes&&Interlocked.CompareExchange(ref dropped,1,0)==0){drop=true;break;}
       }
      }
      if(drop){client.Client.Close();return;} // console "goes offline": control and data connection vanish
      lock(gate)if(!virtualPath.EndsWith(".xstmp"))Completed.Add(virtualPath);
      await Reply("226 Transfer complete");
      break;
     }
     case "RNFR":renameFrom=Map(cwd,argument);await Reply(File.Exists(Local(renameFrom))||Directory.Exists(Local(renameFrom))?"350 Ready for RNTO":"550 Not found");break;
     case "RNTO":
     {
      if(renameFrom==null){await Reply("503 RNFR first");break;}
      var from=Local(renameFrom);var toVirtual=Map(cwd,argument);var to=Local(toVirtual);
      if(File.Exists(to)||Directory.Exists(to)){await Reply("550 Target exists");break;}
      if(File.Exists(from))File.Move(from,to);else Directory.Move(from,to);
      lock(gate)Completed.Add(toVirtual);
      renameFrom=null;await Reply("250 Renamed");
      break;
     }
     case "LIST" or "NLST":
     {
      if(passive==null){await Reply("425 Use PASV first");break;}
      var path=string.Join(' ',argument.Split(' ').SkipWhile(token=>token.StartsWith('-')));var target=Local(Map(cwd,path.Length==0?".":path));
      await Reply("150 Listing");
      using(var data=await passive.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(15)))
      {
       passive.Stop();passive=null;
       var text=new StringBuilder();
       if(Directory.Exists(target))
       {
        foreach(var dir in Directory.GetDirectories(target))text.Append(command=="NLST"?Path.GetFileName(dir):$"drwxr-xr-x 1 owner group 0 Jan 01 00:00 {Path.GetFileName(dir)}").Append("\r\n");
        foreach(var file in Directory.GetFiles(target))text.Append(command=="NLST"?Path.GetFileName(file):$"-rw-r--r-- 1 owner group {new FileInfo(file).Length} Jan 01 00:00 {Path.GetFileName(file)}").Append("\r\n");
       }
       await data.GetStream().WriteAsync(Encoding.ASCII.GetBytes(text.ToString()));
      }
      await Reply("226 Listing complete");
      break;
     }
     case "QUIT":await Reply("221 Bye");return;
     default:await Reply("502 Command not implemented");break;
    }
   }
  }
  catch(Exception){} // client went away or server stopped
  finally{passive?.Stop();}
 }
 public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await Task.Yield();}
}
