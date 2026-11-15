namespace XShard.Models;
public sealed class AppSettings
{
 public DeviceProfile Profile {get;set;}
 public ScrubMode Scrub {get;set;}
 public bool Split {get;set;}
 public bool AttachXbe {get;set;}
 public bool PatchMedia {get;set;}
 public bool RenameXbe {get;set;}
 public bool Offline {get;set;}=true;
 public bool Advanced {get;set;}
 public string Language {get;set;}="en";
 public string UiScaleMode {get;set;}="Auto";
 public double CustomUiScale {get;set;}=100;
 public bool DetailsOuterScroll {get;set;}=true;
 public int SchemaVersion { get; set; } = 1;
 public string BackendPath { get; set; } = "";
 public string OutputDirectory { get; set; } = "";
 public string XgdBackendPath { get; set; } = "";
 public ConsolePlatform Platform { get; set; }
 public OutputFormat OutputFormat { get; set; }
 public bool RecursiveFolders { get; set; } = true;
 public bool SkipSystemUpdate { get; set; } = true; // default for new settings
 public bool DisableMediaPatching { get; set; }
 public bool ContinueOnError { get; set; } = true;
 /// <summary>Revision of default values last written. Older files get new defaults applied once: 1 = Skip $SystemUpdate on (v0.1.7), 2 = former "Dark" (green) becomes "Xbox" (v0.1.9).</summary>
 public int DefaultsRevision { get; set; }
 public const int CurrentDefaultsRevision = 4;
 // v0.1.8
 /// <summary>Dark (Windows 11 Mica gray), Xbox (green dark), Light or System.</summary>
 public string Theme { get; set; } = "Dark";
 /// <summary>Accent name: Green (default), Teal, Blue, Purple, Orange, Red or Windows (system accent). The Xbox theme always uses Green.</summary>
 public string Accent { get; set; } = "Green";
 public bool NotificationsEnabled { get; set; } = true;
 /// <summary>Off, Txt or Html: run report written automatically to the reports folder after each queue run.</summary>
 public string ReportMode { get; set; } = "Off";
 public string XemuPath { get; set; } = "";
 public string XeniaPath { get; set; } = "";
 public string XemuLibrary { get; set; } = "";
 public string XeniaLibrary { get; set; } = "";
 public string RetroBatRoot { get; set; } = "";
 public string BatoceraShare { get; set; } = "";
 public OutputTarget OutputTarget { get; set; } = OutputTarget.Custom;
 // v0.2.0
 /// <summary>FTP transfer to an Xbox 360 console (Aurora/FSD FTP server). Off by default; must be switched on in Settings.</summary>
 public bool FtpEnabled { get; set; }
 public ConsoleProfile XboxConsole { get; set; } = new();
 // v0.2.2
 /// <summary>Skip sources whose game already exists in the output folder (probably scrubbed GoD packages do not count).</summary>
 public bool SkipExistingGames { get; set; } = true;
 /// <summary>The user agreed once that X-Shard may ask GitHub for the latest X-Shard release.</summary>
 public bool UpdateConsent { get; set; }
 /// <summary>Check for updates at start (at most once per 24 hours). Off by default; requires <see cref="UpdateConsent"/>.</summary>
 public bool UpdateAutoCheck { get; set; }
 /// <summary>Show a Windows notification and a badge when a newer release exists.</summary>
 public bool UpdateNotify { get; set; } = true;
 public DateTimeOffset? LastUpdateCheck { get; set; }
 public string LatestRelease { get; set; } = "";
}
public sealed record SavedJob(Guid Id, XisoJobOptions Options, JobStatus Status, string Result, string? PublishedPath = null, string? RemotePath = null);
