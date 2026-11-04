namespace XShard.Models;
public enum DeviceProfile { Custom, OriginalXbox, Xbox360, Xemu, Xenia }
public enum ScrubMode { None, Partial, Full }
/// <summary>Where new jobs write their output. Library targets resolve to the emulator/frontend game folder for the selected console.</summary>
public enum OutputTarget { Custom, XemuLibrary, XeniaLibrary, RetroBat, Batocera }
/// <param name="FlattenOutput">XGDTool only: when the result is a single file, publish it directly in the output folder instead of a per-job container folder (used for emulator/frontend libraries).</param>
public sealed record XisoJobOptions(XisoMode Mode, string Source, string Destination, bool SkipSystemUpdate, bool DisableMediaPatching, ConsolePlatform Platform=ConsolePlatform.Xbox, BackendEngine Engine=BackendEngine.ExtractXiso, OutputFormat Format=OutputFormat.XISO, DeviceProfile Profile=DeviceProfile.Custom, ScrubMode Scrub=ScrubMode.None, bool Split=false, bool AttachXbe=false, bool PatchMedia=false, bool RenameXbe=false, bool Offline=true, bool Advanced=false, bool FlattenOutput=false);
