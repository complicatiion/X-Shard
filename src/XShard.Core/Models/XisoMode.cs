namespace XShard.Models;
/// <summary>Upload (v0.2.0): sends an existing GoD or extracted game folder to the console via FTP; not offered in the operation list.</summary>
public enum XisoMode { Extract, Create, List, Rewrite, Convert, Upload }
public enum JobStatus { Waiting, Running, Succeeded, Failed, Cancelled, Interrupted }

public enum ConsolePlatform { Xbox, Xbox360 }
public enum BackendEngine { ExtractXiso, XgdTool }
public enum OutputFormat { XISO, GoD, CCI, CSO, ZAR, ConsoleDefault, Extract }
