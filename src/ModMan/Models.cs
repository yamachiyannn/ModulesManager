namespace ModMan;

internal enum PresetType { Json, SwrsToysIni }

internal sealed class AppSettings
{
    public string GameDirectory { get; set; } = "";
    public string LaunchExe { get; set; } = "th123.exe";
    public string JsonFileName { get; set; } = "ModLoaderSettings.json";
    public string IniFileName { get; set; } = "SWRSToys.ini";
    public string SelectedPreset { get; set; } = "";
    public bool BackupEnabled { get; set; } = true;
}

internal sealed record ModuleEntry(string Key, string DisplayName, bool Enabled);
internal sealed record PresetInfo(string Name, PresetType Type, string FilePath);
