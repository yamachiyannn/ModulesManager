using System.Diagnostics;
using System.Text.Json;

namespace ModMan;

internal sealed class MainForm : Form
{
    private readonly string managerDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    private readonly string settingsPath;
    private readonly string presetRoot;
    private AppSettings settings = new();

    private readonly TextBox gameDirBox = new() { Width = 430 };
    private readonly TextBox exeBox = new() { Width = 220, Text = "th123.exe" };
    private readonly ComboBox typeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ListBox presetList = new() { Width = 210, IntegralHeight = false };
    private readonly CheckedListBox moduleList = new() { CheckOnClick = true, IntegralHeight = false };
    private readonly Label statusLabel = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button saveButton = new() { Text = "変更を保存", AutoSize = true };
    private PresetInfo? currentPreset;
    private List<ModuleEntry> currentModules = new();
    private bool loading;

    public MainForm()
    {
        settingsPath = Path.Combine(managerDir, "ModManSettings.json");
        presetRoot = Path.Combine(managerDir, "preset");
        Text = "ModMan - Hisoutensoku Module Preset Manager";
        MinimumSize = new Size(900, 620);
        Size = new Size(1080, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9F);
        BuildUi();
        LoadSettings();
        RefreshPresets();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var settingsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        settingsPanel.Controls.Add(new Label { Text = "ゲームフォルダ", AutoSize = true });
        var gameRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        gameRow.Controls.Add(gameDirBox);
        var browse = new Button { Text = "参照...", AutoSize = true };
        browse.Click += (_, _) => BrowseGameDirectory();
        gameRow.Controls.Add(browse);
        settingsPanel.Controls.Add(gameRow);
        settingsPanel.Controls.Add(new Label { Text = "起動するEXE（ゲームフォルダからの相対パス、またはフルパス）", AutoSize = true });
        settingsPanel.Controls.Add(exeBox);
        root.Controls.Add(settingsPanel, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 12, 0, 12) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(new Label { Text = "プリセット", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        presetList.Dock = DockStyle.Fill;
        presetList.SelectedIndexChanged += (_, _) => LoadSelectedPreset();
        left.Controls.Add(presetList, 0, 1);
        var presetButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        AddButton(presetButtons, "新規作成", CreatePreset);
        AddButton(presetButtons, "削除", DeletePreset);
        left.Controls.Add(presetButtons, 0, 2);
        body.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        top.Controls.Add(new Label { Text = "プリセット形式：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        typeBox.Items.AddRange(new object[] { "JSON (ModLoaderSettings.json)", "SWRSToys.ini" });
        typeBox.SelectedIndex = 0;
        typeBox.SelectedIndexChanged += (_, _) => RefreshPresets();
        top.Controls.Add(typeBox);
        right.Controls.Add(top, 0, 0);
        right.Controls.Add(new Label { Text = "モジュール一覧（チェックでON/OFF）", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 8, 0, 6) }, 0, 1);
        moduleList.Dock = DockStyle.Fill;
        right.Controls.Add(moduleList, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        AddButton(actions, "変更を保存", SaveCurrentPreset);
        AddButton(actions, "適用してゲーム起動", ApplyAndLaunch);
        right.Controls.Add(actions, 0, 3);
        body.Controls.Add(right, 1, 0);

        root.Controls.Add(statusLabel, 0, 2);
    }

    private static void AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }

    private PresetType SelectedType => typeBox.SelectedIndex == 1 ? PresetType.SwrsToysIni : PresetType.Json;
    private string TargetFileName(PresetType type) => type == PresetType.Json ? settings.JsonFileName : settings.IniFileName;
    private string SourceFile(PresetType type) => Path.Combine(settings.GameDirectory, TargetFileName(type));

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(settingsPath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.GameDirectory))
                settings.GameDirectory = Directory.GetParent(managerDir)?.FullName ?? managerDir;
            else if (!Path.IsPathRooted(settings.GameDirectory))
                settings.GameDirectory = Path.GetFullPath(Path.Combine(managerDir, settings.GameDirectory));
            gameDirBox.Text = settings.GameDirectory;
            exeBox.Text = settings.LaunchExe;
        }
        catch (Exception ex) { MessageBox.Show("設定ファイルを読み込めませんでした。\n" + ex.Message, "ModMan"); }
    }

    private void SaveSettings()
    {
        settings.GameDirectory = gameDirBox.Text.Trim();
        settings.LaunchExe = string.IsNullOrWhiteSpace(exeBox.Text) ? "th123.exe" : exeBox.Text.Trim();
        Directory.CreateDirectory(managerDir);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void BrowseGameDirectory()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = gameDirBox.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) gameDirBox.Text = dialog.SelectedPath;
    }

    private void RefreshPresets()
    {
        if (!IsHandleCreated) return;
        string? selected = currentPreset?.Name;
        presetList.Items.Clear();
        if (!Directory.Exists(presetRoot)) Directory.CreateDirectory(presetRoot);
        foreach (var dir in Directory.GetDirectories(presetRoot).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            string file = Path.Combine(dir, TargetFileName(SelectedType));
            if (File.Exists(file)) presetList.Items.Add(Path.GetFileName(dir));
        }
        if (selected != null)
        {
            int index = presetList.Items.IndexOf(selected);
            if (index >= 0) presetList.SelectedIndex = index;
        }
    }

    private void CreatePreset()
    {
        try
        {
            SaveSettings();
            var source = SourceFile(SelectedType);
            if (!File.Exists(source)) throw new FileNotFoundException("ゲームフォルダに元ファイルが見つかりません。", source);
            string? name = AskText("新規プリセット", "プリセット名を入力してください。");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = SanitizeName(name);
            string dir = Path.Combine(presetRoot, name);
            if (Directory.Exists(dir)) throw new IOException("同じ名前のプリセットが既にあります。");
            Directory.CreateDirectory(dir);
            string destination = Path.Combine(dir, TargetFileName(SelectedType));
            File.Copy(source, destination);
            currentPreset = new PresetInfo(name, SelectedType, destination);
            RefreshPresets();
            presetList.SelectedItem = name;
            LoadPreset(currentPreset);
            SetStatus($"'{name}' を作成しました。元ファイルは変更していません。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static string? AskText(string title, string prompt)
    {
        using var form = new Form { Text = title, Width = 420, Height = 150, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var label = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
        var box = new TextBox { Left = 12, Top = 40, Width = 380 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 230, Top = 75, Width = 75 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Left = 317, Top = 75, Width = 75 };
        form.Controls.AddRange(new Control[] { label, box, ok, cancel });
        form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? box.Text : null;
    }

    private static string SanitizeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().Trim('.');
        if (name.Length == 0) throw new InvalidDataException("使用できるプリセット名を入力してください。");
        return name;
    }

    private void LoadSelectedPreset()
    {
        if (presetList.SelectedItem is not string name) return;
        string path = Path.Combine(presetRoot, name, TargetFileName(SelectedType));
        if (!File.Exists(path)) return;
        currentPreset = new PresetInfo(name, SelectedType, path);
        settings.SelectedPreset = name;
        LoadPreset(currentPreset);
    }

    private void LoadPreset(PresetInfo preset)
    {
        try
        {
            loading = true;
            currentModules = preset.Type == PresetType.Json
                ? ConfigEditors.ReadJsonModules(preset.FilePath)
                : ConfigEditors.ReadIniModules(preset.FilePath);
            moduleList.Items.Clear();
            foreach (var module in currentModules)
                moduleList.Items.Add(new ModuleListItem(module), module.Enabled);
            SetStatus($"{preset.Name}：{currentModules.Count} 個のモジュールを読み込みました。");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { loading = false; }
    }

    private sealed class ModuleListItem
    {
        public ModuleEntry Entry { get; }
        public ModuleListItem(ModuleEntry entry) => Entry = entry;
        public override string ToString() => $"{Entry.DisplayName}    [{Entry.Key}]";
    }

    private Dictionary<string, bool> GetStates()
    {
        var states = new Dictionary<string, bool>();
        for (int i = 0; i < moduleList.Items.Count; i++)
            if (moduleList.Items[i] is ModuleListItem item)
                states[item.Entry.Key] = moduleList.GetItemChecked(i);
        return states;
    }

    private void SaveCurrentPreset()
    {
        if (currentPreset == null) { MessageBox.Show("先にプリセットを選択してください。"); return; }
        try
        {
            var states = GetStates();
            string temp = currentPreset.FilePath + ".tmp";
            if (currentPreset.Type == PresetType.Json)
                ConfigEditors.SaveJsonWithStates(currentPreset.FilePath, temp, states);
            else
                ConfigEditors.SaveIniWithStates(currentPreset.FilePath, temp, states);
            File.Move(temp, currentPreset.FilePath, true);
            LoadPreset(currentPreset);
            SetStatus($"'{currentPreset.Name}' を保存しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void DeletePreset()
    {
        if (presetList.SelectedItem is not string name) return;
        if (MessageBox.Show($"プリセット '{name}' を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            Directory.Delete(Path.Combine(presetRoot, name), true);
            currentPreset = null;
            moduleList.Items.Clear();
            RefreshPresets();
            SetStatus($"'{name}' を削除しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void ApplyAndLaunch()
    {
        if (currentPreset == null) { MessageBox.Show("適用するプリセットを選択してください。"); return; }
        try
        {
            SaveSettings();
            string gameDir = Path.GetFullPath(settings.GameDirectory);
            if (!Directory.Exists(gameDir)) throw new DirectoryNotFoundException("ゲームフォルダが見つかりません。");
            string target = Path.Combine(gameDir, TargetFileName(currentPreset.Type));
            string launch = Path.IsPathRooted(settings.LaunchExe) ? settings.LaunchExe : Path.Combine(gameDir, settings.LaunchExe);
            if (!File.Exists(currentPreset.FilePath)) throw new FileNotFoundException("プリセットファイルがありません。", currentPreset.FilePath);
            if (!File.Exists(launch)) throw new FileNotFoundException("起動EXEが見つかりません。", launch);

            SetStatus("th123.exe の終了を待っています...");
            await StopGameProcesses();
            if (settings.BackupEnabled && File.Exists(target))
            {
                string backupDir = Path.Combine(managerDir, "Backups");
                Directory.CreateDirectory(backupDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                File.Copy(target, Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(target)}_{stamp}{Path.GetExtension(target)}"));
            }

            string temp = target + ".modman.tmp";
            File.Copy(currentPreset.FilePath, temp, true);
            File.Move(temp, target, true);
            Process.Start(new ProcessStartInfo { FileName = launch, WorkingDirectory = gameDir, UseShellExecute = true });
            SetStatus($"'{currentPreset.Name}' を適用してゲームを起動しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static async Task StopGameProcesses()
    {
        foreach (var process in Process.GetProcessesByName("th123"))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                        process.CloseMainWindow();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited) process.Kill(true);
                        await process.WaitForExitAsync();
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new InvalidOperationException("th123.exe を終了できません。管理者権限やアクセス権を確認してください。", ex);
                }
            }
        }
    }

    private void SetStatus(string message) => statusLabel.Text = message;
    private static void ShowError(Exception ex) => MessageBox.Show(ex.Message, "ModMan - エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
