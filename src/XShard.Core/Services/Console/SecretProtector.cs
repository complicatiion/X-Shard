using System.Security.Cryptography;
using System.Text;
namespace XShard.Services;
/// <summary>Stores secrets (FTP password) encrypted with Windows DPAPI for the current user. Never written in plain text.</summary>
public static class SecretProtector
{
 private static readonly byte[] Entropy="X-Shard console FTP"u8.ToArray();
 public static string Protect(string secret)
 {
  if(string.IsNullOrEmpty(secret))return "";
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Saving the FTP password requires Windows (DPAPI).");
  return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret),Entropy,DataProtectionScope.CurrentUser));
 }
 /// <summary>Returns the secret, or null if it cannot be decrypted (other user/PC, damaged value, non-Windows).</summary>
 public static string? Unprotect(string protectedValue)
 {
  if(string.IsNullOrEmpty(protectedValue))return "";
  if(!OperatingSystem.IsWindows())return null;
  try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue),Entropy,DataProtectionScope.CurrentUser));}
  catch(Exception ex) when(ex is CryptographicException or FormatException){return null;}
 }
}
