using System.Security.Cryptography;
using System.Text;
namespace XShard.Services;
public sealed record LegalAcceptance(string Digest,DateTimeOffset AcceptedAtUtc);
public sealed record LegalDocument(string FileName,string Title,string Markdown);
public sealed class LegalService
{
 public static readonly string[] RequiredFiles=["THIRD_PARTY_LICENSES.md","LICENSE_DECLARATION.md","EULA.md","PRIVACY_POLICY.md","LEGAL_NOTICE.md","COPYRIGHT.md","GPL-3.0.md"];
 private readonly SettingsService storage;
 public List<LegalDocument> Documents {get;}=[];
 public bool DocumentsReady {get;}
 public bool IsAccepted {get;private set;}
 public string CurrentMarkdown {get;}
 public string Digest {get;}
 public LegalService(SettingsService settings,string? directory=null)
 {
  storage=settings;directory??=Path.Combine(AppContext.BaseDirectory,"legal");
  var docs=new List<string>();var missing=new List<string>();
  foreach(var name in RequiredFiles)
  {
   var file=Path.Combine(directory,name);
   try {if(!File.Exists(file)||string.IsNullOrWhiteSpace(File.ReadAllText(file))){missing.Add(name);continue;}var text=File.ReadAllText(file);docs.Add(text);Documents.Add(new(name,name switch {"THIRD_PARTY_LICENSES.md"=>"Components","LICENSE_DECLARATION.md"=>"X-Shard GPLv3","EULA.md"=>"EULA","PRIVACY_POLICY.md"=>"Privacy","LEGAL_NOTICE.md"=>"Legal Notice","COPYRIGHT.md"=>"Copyright",_=>"GNU GPL v3 — Full text"},text));}
   catch(IOException) {missing.Add(name);}catch(UnauthorizedAccessException){missing.Add(name);}
  }
  DocumentsReady=missing.Count==0;
  CurrentMarkdown=DocumentsReady?string.Join("\n\n---\n\n",docs):"# Legal documents pending\n\nThe publisher's final legal documents have not been supplied yet. Acceptance is unavailable until all documents are included.\n\nMissing files:\n\n"+string.Join("\n",missing.Select(name=>"- `"+name+"`"));
  Digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CurrentMarkdown)));
  IsAccepted=DocumentsReady&&storage.Load<LegalAcceptance?>("legal-acceptance.json",()=>null)?.Digest==Digest;
 }
 public void ResetAcceptance() {File.Delete(Path.Combine(storage.DataDirectory,"legal-acceptance.json"));IsAccepted=false;}
 public async Task AcceptAsync()
 {
  if(!DocumentsReady)throw new InvalidOperationException("Final legal documents are missing.");
  await storage.SaveAsync("legal-acceptance.json",new LegalAcceptance(Digest,DateTimeOffset.UtcNow));IsAccepted=true;
 }
}
