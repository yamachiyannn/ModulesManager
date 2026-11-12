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

    // SWRSToys: module entry "Name=path.dll" is enabled; "; Name=path.dll" is disabled.
    // Other comments (including # comments), section headers, spacing and unrelated lines are preserved.
    private static readonly Regex IniModuleLine =
        new(@"^(?<indent>\s*)(?<comment>;+\s*)?(?<key>[A-Za-z0-9_.+\-]+)\s*=\s*(?<value>[^;#]+?\\?\.dll)\s*(?<tail>;.*)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<ModuleEntry> ReadIniModules(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        bool inModule = false;
        var result = new List<ModuleEntry>();
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
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();
            var identity = key + "=" + value;
            var display = key;
            result.Add(new ModuleEntry(identity, display, !match.Groups["comment"].Success));
        }
        return result;
    }

    public static void SaveIniWithStates(string sourcePath, string destinationPath, IReadOnlyDictionary<string, bool> states)
    {
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

            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value.Trim();
            string identity = key + "=" + value;
            if (!states.TryGetValue(identity, out bool enabled)) continue;

            string indent = match.Groups["indent"].Value;
            string tail = match.Groups["tail"].Value;
            string assignment = key + "=" + value;
            lines[i] = enabled ? indent + assignment + tail : indent + "; " + assignment + tail;
        }
        WriteUtf8(destinationPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    public static void WriteUtf8(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
