namespace XShard.Models;
public enum XisoMode { Extract, Create, List, Rewrite, Convert }
public enum JobStatus { Waiting, Running, Succeeded, Failed, Cancelled, Interrupted }

public enum ConsolePlatform { Xbox, Xbox360 }
public enum BackendEngine { ExtractXiso, XgdTool }
public enum OutputFormat { XISO, GoD, CCI, CSO, ZAR, ConsoleDefault, Extract }
