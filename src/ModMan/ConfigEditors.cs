using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModMan;

internal static class ConfigEditors
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<ModuleEntry> ReadJsonModules(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))
            ?? throw new InvalidDataException("JSONが空です。");
        var modules = root["modules"] as JsonObject
            ?? throw new InvalidDataException("JSON内に modules オブジェクトがありません。");

        var result = new List<ModuleEntry>();
        foreach (var pair in modules)
        {
            if (pair.Value is not JsonObject item || item["enabled"] is not JsonValue enabledNode ||
                !enabledNode.TryGetValue<bool>(out bool enabled))
                throw new InvalidDataException($"'{pair.Key}' の enabled が true/false ではありません。");
            result.Add(new ModuleEntry(pair.Key, Path.GetFileNameWithoutExtension(pair.Key.Replace('\\', '/')), enabled));
        }
        return result;
    }

    public static void SaveJsonWithStates(string sourcePath, string destinationPath, IReadOnlyDictionary<string, bool> states)
    {
        var root = JsonNode.Parse(File.ReadAllText(sourcePath, Encoding.UTF8))
            ?? throw new InvalidDataException("JSONが空です。");
        var modules = root["modules"] as JsonObject
            ?? throw new InvalidDataException("JSON内に modules オブジェクトがありません。");

        foreach (var pair in states)
        {
            if (modules[pair.Key] is not JsonObject item)
                throw new InvalidDataException($"モジュール '{pair.Key}' が元JSONにありません。");
            item["enabled"] = pair.Value;
        }
        WriteUtf8(destinationPath, root.ToJsonString(JsonOptions));
    }

    // Supports both "Name=path.dll" and plain "path.dll" module rows.
    // A leading semicolon disables a row. Other comments/sections are untouched.
    private static readonly Regex IniModuleLine =
        new(@"^(?<indent>\s*)(?<comment>;+\s*)?(?<body>(?:[A-Za-z0-9_.+\-]+\s*=\s*)?[^;#\s][^;#]*?\.dll)(?<tail>\s*;.*)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<ModuleEntry> ReadIniModules(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        bool inModule = false;
        var result = new List<ModuleEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                inModule = trimmed.Equals("[Module]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inModule) continue;
            var match = IniModuleLine.Match(line);
            if (!match.Success) continue;
            string identity = match.Groups["body"].Value.Trim();
            if (!seen.Add(identity)) continue;
            string display = identity.Contains('=')
                ? identity[..identity.IndexOf('=')].Trim()
                : Path.GetFileNameWithoutExtension(identity.Replace('\\', '/'));
            result.Add(new ModuleEntry(identity, display, !match.Groups["comment"].Success));
        }
        return result;
    }

    public static void SaveIniWithStates(string sourcePath, string destinationPath, IReadOnlyDictionary<string, bool> states)
    {
        var original = File.ReadAllText(sourcePath, Encoding.UTF8);
        var lines = File.ReadAllLines(sourcePath, Encoding.UTF8).ToList();
        bool inModule = false;
        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                inModule = trimmed.Equals("[Module]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inModule) continue;
            var match = IniModuleLine.Match(lines[i]);
            if (!match.Success) continue;

            string identity = match.Groups["body"].Value.Trim();
            if (!states.TryGetValue(identity, out bool enabled)) continue;

            string indent = match.Groups["indent"].Value;
            string body = match.Groups["body"].Value;
            string tail = match.Groups["tail"].Value;
            lines[i] = enabled ? indent + body + tail : indent + "; " + body + tail;
        }

        string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" :
            original.Contains('\r') ? "\r" : "\n";
        bool hadFinalNewline = original.EndsWith("\n", StringComparison.Ordinal) || original.EndsWith("\r", StringComparison.Ordinal);
        string output = string.Join(newline, lines);
        if (hadFinalNewline && lines.Count > 0) output += newline;
        WriteUtf8(destinationPath, output);
    }


    // Creates the missing-format companion from the current game configuration.
    // Matching modules inherit only their enabled/disabled state; unmatched modules
    // retain the state already present in the destination-format template.
    public static int CreateCompanionPreset(string sourcePresetPath, PresetType sourceType,
        string targetTemplatePath, PresetType targetType, string destinationPath)
    {
        var sourceModules = sourceType == PresetType.Json
            ? ReadJsonModules(sourcePresetPath)
            : ReadIniModules(sourcePresetPath);
        var targetModules = targetType == PresetType.Json
            ? ReadJsonModules(targetTemplatePath)
            : ReadIniModules(targetTemplatePath);

        var sourceStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in sourceModules)
            foreach (var identity in ComparableNames(module))
                sourceStates[identity] = module.Enabled;

        var targetStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        int matched = 0;
        foreach (var module in targetModules)
        {
            bool? inherited = null;
            foreach (var identity in ComparableNames(module))
            {
                if (sourceStates.TryGetValue(identity, out bool enabled))
                {
                    inherited = enabled;
                    break;
                }
            }
            targetStates[module.Key] = inherited ?? module.Enabled;
            if (inherited.HasValue) matched++;
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = destinationPath + ".creating";
        try
        {
            File.Copy(targetTemplatePath, temporary, true);
            if (targetType == PresetType.Json)
                SaveJsonWithStates(temporary, destinationPath, targetStates);
            else
                SaveIniWithStates(temporary, destinationPath, targetStates);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return matched;
    }

    private static IEnumerable<string> ComparableNames(ModuleEntry module)
    {
        string key = module.Key.Trim();
        int equals = key.IndexOf('=');
        if (equals >= 0) key = key[(equals + 1)..].Trim();
        key = key.TrimStart(';').Trim().Replace('\\', '/');
        string fileName = Path.GetFileNameWithoutExtension(key);
        if (!string.IsNullOrWhiteSpace(fileName)) yield return fileName;
        string display = module.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(display)) yield return display;
    }

    public static void WriteUtf8(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
