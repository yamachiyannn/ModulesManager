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
    public List<string> PresetOrder { get; set; } = new();
    // Independent quick-launch assignments for left single-click and left double-click.
    public string TraySingleClickPreset { get; set; } = "";
    public string TrayDoubleClickPreset { get; set; } = "";
}

internal sealed record ModuleEntry(string Key, string DisplayName, bool Enabled);
internal sealed record PresetInfo(string Name, PresetType Type, string FilePath);
