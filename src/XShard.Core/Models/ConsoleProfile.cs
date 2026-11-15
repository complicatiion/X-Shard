namespace XShard.Models;
/// <summary>FTP connection to a modded Xbox 360 (RGH/JTAG) running Aurora, FSD or another dashboard with an FTP server.</summary>
public sealed class ConsoleProfile
{
 public string Host { get; set; } = "";
 public int Port { get; set; } = 21;
 /// <summary>Aurora default login: xboxftp / xboxftp.</summary>
 public string User { get; set; } = "xboxftp";
 /// <summary>Password protected with Windows DPAPI for the current user (Base64). Empty = Aurora default "xboxftp".</summary>
 public string ProtectedPassword { get; set; } = "";
 /// <summary>Hdd1 (internal hard drive) or Usb0/Usb1 (USB storage).</summary>
 public string Drive { get; set; } = "Hdd1";
 /// <summary>Folder for extracted games below the drive (must be a scan path in Aurora).</summary>
 public string GamesFolder { get; set; } = "Games";
 /// <summary>Delete X-Shard's local transfer copy after a verified upload.</summary>
 public bool DeleteAfterUpload { get; set; } = true;
 public int ParallelUploads { get; set; } = 3;
}
