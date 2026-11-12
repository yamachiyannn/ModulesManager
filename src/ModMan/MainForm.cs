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
    private readonly ComboBox typeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ListBox presetList = new() { IntegralHeight = false };
    private readonly CheckedListBox moduleList = new() { CheckOnClick = true, IntegralHeight = false };
    private readonly FlowLayoutPanel homePresetPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly ComboBox traySinglePresetBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly ComboBox trayDoublePresetBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly Label statusLabel = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Panel homePanel = new() { Dock = DockStyle.Fill };
    private readonly Panel editorPanel = new() { Dock = DockStyle.Fill, Visible = false };
    private PresetInfo? currentPreset;
    private List<ModuleEntry> currentModules = new();
    private bool loading;
    private bool allowExit;
    private bool loadingTrayQuickSettings;
    private bool launchInProgress;
    private long ignoreLeftClickUntilTick;
    private readonly NotifyIcon trayIcon = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly System.Windows.Forms.Timer trayClickTimer = new();

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
        RefreshHomePresets();
        ShowHome();
        InitializeTrayIcon();
        Shown += (_, _) =>
        {
            Hide();
            ShowInTaskbar = false;
        };
        FormClosing += HandleFormClosing;
        FormClosed += (_, _) =>
        {
            trayClickTimer.Stop();
            trayClickTimer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
        };
    }

    private void InitializeTrayIcon()
    {
        trayMenu.ShowImageMargin = false;
        trayMenu.Items.Add("設定", null, (_, _) => ShowSettingsWindow());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Opening += (_, _) => RebuildTrayPresetItems();
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("終了", null, (_, _) => ExitApplication());

        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "ModMan - 左クリックで割り当てプリセットを起動 / 右クリックでメニュー";
        // Windows標準のNotifyIconコンテキストメニューを右クリックで表示する。
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;

        // シングルクリックはOSのダブルクリック判定時間だけ待ち、
        // その間にダブルクリックが成立した場合はシングルクリック処理を取り消す。
        trayClickTimer.Interval = Math.Max(1, SystemInformation.DoubleClickTime);
        trayClickTimer.Tick += (_, _) =>
        {
            trayClickTimer.Stop();
            LaunchTrayAssignedPreset(settings.TraySingleClickPreset);
        };

        trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || string.IsNullOrWhiteSpace(settings.TraySingleClickPreset)) return;
            if (Environment.TickCount64 <= ignoreLeftClickUntilTick) return;
            trayClickTimer.Stop();
            trayClickTimer.Start();
        };
        trayIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || string.IsNullOrWhiteSpace(settings.TrayDoubleClickPreset)) return;
            trayClickTimer.Stop();
            // MouseDoubleClickの後にMouseClickが通知される環境でも二重実行しない。
            ignoreLeftClickUntilTick = Environment.TickCount64 + SystemInformation.DoubleClickTime;
            LaunchTrayAssignedPreset(settings.TrayDoubleClickPreset);
        };
    }

    private void RebuildTrayPresetItems()
    {
        // Keep the fixed Settings item, its separator, and the final separator/Exit item.
        while (trayMenu.Items.Count > 2)
            trayMenu.Items.RemoveAt(2);

        var presets = OrderPresets(EnumeratePresets()).ToList();

        if (presets.Count == 0)
        {
            trayMenu.Items.Add(new ToolStripMenuItem("プリセットなし") { Enabled = false });
        }
        else
        {
            foreach (var preset in presets)
            {
                var item = new ToolStripMenuItem(preset.Name)
                {
                    ToolTipText = preset.Type == PresetType.Json ? "JSONプリセットを適用してth123.exeを起動" : "SWRSToys.iniプリセットを適用してth123.exeを起動",
                    Tag = preset
                };
                item.Click += (_, _) => ApplyAndLaunch((PresetInfo)item.Tag!);
                trayMenu.Items.Add(item);
            }
        }
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("終了", null, (_, _) => ExitApplication());
    }

    private void ShowSettingsWindow()
    {
        if (IsDisposed) return;
        ShowInTaskbar = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        ShowHome();
        BringToFront();
        Activate();
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !allowExit)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
        }
    }

    private void ExitApplication()
    {
        allowExit = true;
        trayIcon.Visible = false;
        Close();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(homePanel, 0, 0);
        root.Controls.Add(editorPanel, 0, 0);
        root.Controls.Add(statusLabel, 0, 1);
        BuildHome();
        BuildEditor();
    }

    private void BuildHome()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        homePanel.Controls.Add(layout);
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        heading.Controls.Add(new Label { Text = "ModMan", AutoSize = true, Font = new Font("Yu Gothic UI", 20F, FontStyle.Bold) });
        heading.Controls.Add(new Label
        {
            Text = "SWRSToys.iniを変更して天則を再起動します\nSokuModLoaderのModLoaderSettings.jsonにも対応",
            AutoSize = true
        });
        layout.Controls.Add(heading, 0, 0);
        homePresetPanel.Padding = new Padding(4, 14, 4, 14);
        layout.Controls.Add(homePresetPanel, 0, 1);

        var quickLaunch = new GroupBox { Text = "タスクトレイの左クリックに割り当てるプリセット", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        var quickRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var singleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        singleRow.Controls.Add(new Label { Text = "シングルクリック：", AutoSize = true, Padding = new Padding(0, 6, 0, 0), Width = 145 });
        singleRow.Controls.Add(traySinglePresetBox);
        var doubleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        doubleRow.Controls.Add(new Label { Text = "ダブルクリック：", AutoSize = true, Padding = new Padding(0, 6, 0, 0), Width = 145 });
        doubleRow.Controls.Add(trayDoublePresetBox);
        quickRow.Controls.Add(singleRow);
        quickRow.Controls.Add(doubleRow);
        quickLaunch.Controls.Add(quickRow);
        layout.Controls.Add(quickLaunch, 0, 2);

        traySinglePresetBox.SelectedIndexChanged += (_, _) =>
        {
            if (loadingTrayQuickSettings) return;
            settings.TraySingleClickPreset = traySinglePresetBox.SelectedIndex <= 0 ? "" : traySinglePresetBox.SelectedItem?.ToString() ?? "";
            SaveSettings();
        };
        trayDoublePresetBox.SelectedIndexChanged += (_, _) =>
        {
            if (loadingTrayQuickSettings) return;
            settings.TrayDoubleClickPreset = trayDoublePresetBox.SelectedIndex <= 0 ? "" : trayDoublePresetBox.SelectedItem?.ToString() ?? "";
            SaveSettings();
        };

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var settingsButton = new Button { Text = "プリセット設定", AutoSize = true, Padding = new Padding(14, 7, 14, 7) };
        settingsButton.Click += (_, _) => ShowEditor();
        footer.Controls.Add(settingsButton);
        layout.Controls.Add(footer, 0, 3);
    }

    private void BuildEditor()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorPanel.Controls.Add(layout);

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var back = new Button { Text = "← プリセット選択に戻る", AutoSize = true };
        back.Click += (_, _) => { SaveSettings(); RefreshHomePresets(); ShowHome(); };
        top.Controls.Add(back);
        top.Controls.Add(new Label { Text = "   プリセット設定", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 7, 0, 0) });
        layout.Controls.Add(top, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 12, 0, 12) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(body, 0, 1);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(new Label { Text = "登録済みプリセット", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        presetList.Dock = DockStyle.Fill;
        presetList.SelectedIndexChanged += (_, _) => LoadSelectedPreset();
        left.Controls.Add(presetList, 0, 1);
        var orderButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        AddButton(orderButtons, "上へ", () => MoveSelectedPreset(-1));
        AddButton(orderButtons, "下へ", () => MoveSelectedPreset(1));
        left.Controls.Add(orderButtons, 0, 2);
        var typeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        typeRow.Controls.Add(new Label { Text = "形式：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        typeBox.Items.AddRange(new object[] { "JSON (ModLoaderSettings.json)", "SWRSToys.ini" });
        typeBox.SelectedIndex = 0;
        typeBox.SelectedIndexChanged += (_, _) => { currentPreset = null; moduleList.Items.Clear(); RefreshEditorPresets(); };
        typeRow.Controls.Add(typeBox);
        left.Controls.Add(typeRow, 0, 3);
        var presetButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        AddButton(presetButtons, "新規作成", CreatePreset);
        AddButton(presetButtons, "名前変更", RenamePreset);
        AddButton(presetButtons, "削除", DeletePreset);
        left.Controls.Add(presetButtons, 0, 4);
        body.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.Controls.Add(new Label { Text = "ゲームフォルダ", AutoSize = true }, 0, 0);
        var gameRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        gameRow.Controls.Add(gameDirBox);
        var browse = new Button { Text = "参照...", AutoSize = true };
        browse.Click += (_, _) => BrowseGameDirectory();
        gameRow.Controls.Add(browse);
        right.Controls.Add(gameRow, 0, 1);
        moduleList.Dock = DockStyle.Fill;
        right.Controls.Add(moduleList, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        AddButton(actions, "変更を保存", SaveCurrentPreset);
        AddButton(actions, "適用して起動", ApplySelectedPresetFromEditor);
        right.Controls.Add(actions, 0, 3);
        body.Controls.Add(right, 1, 0);

        var exeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        exeRow.Controls.Add(new Label { Text = "起動EXE：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        exeRow.Controls.Add(exeBox);
        layout.Controls.Add(exeRow, 0, 2);
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

    private void ShowHome()
    {
        editorPanel.Visible = false;
        homePanel.Visible = true;
        RefreshHomePresets();
    }

    private void ShowEditor()
    {
        homePanel.Visible = false;
        editorPanel.Visible = true;
        RefreshEditorPresets();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(settingsPath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
            settings.PresetOrder ??= new List<string>();
            settings.TraySingleClickPreset ??= "";
            settings.TrayDoubleClickPreset ??= "";
            if (string.IsNullOrWhiteSpace(settings.GameDirectory))
                settings.GameDirectory = Directory.GetParent(managerDir)?.FullName ?? managerDir;
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

    private void RefreshHomePresets()
    {
        homePresetPanel.Controls.Clear();
        Directory.CreateDirectory(presetRoot);
        var presets = OrderPresets(EnumeratePresets()).ToList();
        RefreshTrayQuickLaunchControls(presets);
        if (presets.Count == 0)
        {
            homePresetPanel.Controls.Add(new Label { Text = "プリセットがありません。\n「プリセット設定」から新規作成してください。", AutoSize = true, Padding = new Padding(8) });
            return;
        }
        foreach (var preset in presets)
        {
            var button = new Button
            {
                Text = $"▶  {preset.Name}     ({(preset.Type == PresetType.Json ? "JSON" : "SWRSToys.ini")})",
                Width = Math.Max(500, homePresetPanel.ClientSize.Width - 35), Height = 58,
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold),
                Padding = new Padding(14, 0, 0, 0), Margin = new Padding(4, 5, 4, 5),
                Tag = preset
            };
            button.Click += (_, _) => ApplyAndLaunch((PresetInfo)button.Tag!);
            homePresetPanel.Controls.Add(button);
        }
    }

    private void RefreshTrayQuickLaunchControls(IEnumerable<PresetInfo>? source = null)
    {
        var presets = OrderPresets(source ?? EnumeratePresets()).ToList();
        loadingTrayQuickSettings = true;
        try
        {
            FillQuickPresetCombo(traySinglePresetBox, presets, ref settings.TraySingleClickPreset);
            FillQuickPresetCombo(trayDoublePresetBox, presets, ref settings.TrayDoubleClickPreset);
        }
        finally
        {
            loadingTrayQuickSettings = false;
        }
        SaveSettings();
    }

    private void FillQuickPresetCombo(ComboBox combo, IEnumerable<PresetInfo> presets, ref string selectedPreset)
    {
        combo.BeginUpdate();
        try
        {
            combo.Items.Clear();
            combo.Items.Add("（割り当てなし）");
            foreach (var preset in presets) combo.Items.Add(preset.Name);
            int selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(selectedPreset))
            {
                int found = combo.Items.IndexOf(selectedPreset);
                if (found >= 0) selectedIndex = found;
                else selectedPreset = "";
            }
            combo.SelectedIndex = selectedIndex;
        }
        finally { combo.EndUpdate(); }
    }

    private IEnumerable<PresetInfo> EnumeratePresets()
    {
        if (!Directory.Exists(presetRoot)) yield break;
        foreach (var dir in Directory.GetDirectories(presetRoot))
        {
            string json = Path.Combine(dir, settings.JsonFileName);
            string ini = Path.Combine(dir, settings.IniFileName);
            if (File.Exists(json)) yield return new PresetInfo(Path.GetFileName(dir), PresetType.Json, json);
            else if (File.Exists(ini)) yield return new PresetInfo(Path.GetFileName(dir), PresetType.SwrsToysIni, ini);
        }
    }

    private void RefreshEditorPresets()
    {
        string? selected = currentPreset?.Name;
        presetList.Items.Clear();
        foreach (var preset in OrderPresets(EnumeratePresets().Where(p => p.Type == SelectedType)))
            presetList.Items.Add(preset.Name);
        if (selected != null)
        {
            int index = presetList.Items.IndexOf(selected);
            if (index >= 0) presetList.SelectedIndex = index;
        }
    }

    private IEnumerable<PresetInfo> OrderPresets(IEnumerable<PresetInfo> source)
    {
        var items = source.ToList();
        var rank = settings.PresetOrder
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        return items.OrderBy(p => rank.TryGetValue(p.Name, out int index) ? index : int.MaxValue)
                    .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private void MoveSelectedPreset(int direction)
    {
        if (presetList.SelectedItem is not string selectedName) return;
        var names = presetList.Items.Cast<string>().ToList();
        int oldIndex = names.FindIndex(n => n.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
        int newIndex = oldIndex + direction;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= names.Count) return;

        (names[oldIndex], names[newIndex]) = (names[newIndex], names[oldIndex]);
        var allNames = EnumeratePresets().Select(p => p.Name).ToList();
        var reorderedVisible = names;
        // Keep other-format presets in their existing relative order, while storing a global order.
        var visibleSet = new HashSet<string>(reorderedVisible, StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();
        int visibleIndex = 0;
        foreach (var name in OrderNamesAcrossTypes(allNames))
            merged.Add(visibleSet.Contains(name) ? reorderedVisible[visibleIndex++] : name);
        settings.PresetOrder = merged.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SaveSettings();
        RefreshEditorPresets();
        presetList.SelectedItem = selectedName;
        RefreshHomePresets();
        SetStatus("プリセットの表示順を保存しました。");
    }

    private IEnumerable<string> OrderNamesAcrossTypes(IEnumerable<string> names)
    {
        var rank = settings.PresetOrder.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        return names.OrderBy(n => rank.TryGetValue(n, out int index) ? index : int.MaxValue)
                    .ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase);
    }

    private void CreatePreset()
    {
        try
        {
            SaveSettings();
            string source = SourceFile(SelectedType);
            if (!File.Exists(source)) throw new FileNotFoundException("ゲームフォルダ直下に元ファイルが見つかりません。", source);
            string? input = AskText("新規プリセット", "プリセット名を入力してください。");
            if (string.IsNullOrWhiteSpace(input)) return;
            string name = SanitizeName(input);
            string destinationDir = Path.Combine(presetRoot, name);
            if (Directory.Exists(destinationDir) || EnumeratePresets().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"同じ名前のプリセット「{name}」が既に存在します。別の名前を指定してください。", "名前の重複", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Directory.CreateDirectory(destinationDir);
            if (!settings.PresetOrder.Contains(name, StringComparer.OrdinalIgnoreCase)) settings.PresetOrder.Add(name);
            SaveSettings();
            string destination = Path.Combine(destinationDir, TargetFileName(SelectedType));
            File.Copy(source, destination);
            currentPreset = new PresetInfo(name, SelectedType, destination);
            RefreshEditorPresets();
            presetList.SelectedItem = name;
            LoadPreset(currentPreset);
            SetStatus($"'{name}' を作成しました。元ファイルは変更していません。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RenamePreset()
    {
        if (presetList.SelectedItem is not string oldName) { MessageBox.Show("名前を変更するプリセットを選択してください。"); return; }
        string? input = AskText("プリセット名の変更", "新しいプリセット名を入力してください。");
        if (string.IsNullOrWhiteSpace(input)) return;
        try
        {
            string newName = SanitizeName(input);
            if (newName.Equals(oldName, StringComparison.Ordinal)) return;
            string oldDir = Path.Combine(presetRoot, oldName);
            string newDir = Path.Combine(presetRoot, newName);
            if (Directory.Exists(newDir) || EnumeratePresets().Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"同じ名前のプリセット「{newName}」が既に存在します。別の名前を指定してください。", "名前の重複", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Directory.Move(oldDir, newDir);
            settings.PresetOrder = settings.PresetOrder
                .Select(n => n.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : n)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (settings.TraySingleClickPreset.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                settings.TraySingleClickPreset = newName;
            if (settings.TrayDoubleClickPreset.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                settings.TrayDoubleClickPreset = newName;
            SaveSettings();
            currentPreset = new PresetInfo(newName, SelectedType, Path.Combine(newDir, TargetFileName(SelectedType)));
            RefreshEditorPresets();
            presetList.SelectedItem = newName;
            LoadPreset(currentPreset);
            RefreshHomePresets();
            SetStatus($"'{oldName}' を '{newName}' に変更しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static string? AskText(string title, string prompt)
    {
        using var form = new Form { Text = title, Width = 430, Height = 155, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var label = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
        var box = new TextBox { Left = 12, Top = 40, Width = 390 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 240, Top = 78, Width = 75 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Left = 327, Top = 78, Width = 75 };
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
        if (loading || presetList.SelectedItem is not string name) return;
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
            currentModules = preset.Type == PresetType.Json ? ConfigEditors.ReadJsonModules(preset.FilePath) : ConfigEditors.ReadIniModules(preset.FilePath);
            moduleList.Items.Clear();
            foreach (var module in currentModules) moduleList.Items.Add(new ModuleListItem(module), module.Enabled);
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
            if (moduleList.Items[i] is ModuleListItem item) states[item.Entry.Key] = moduleList.GetItemChecked(i);
        return states;
    }

    private void SaveCurrentPreset()
    {
        if (currentPreset == null) { MessageBox.Show("先にプリセットを選択してください。"); return; }
        try
        {
            string temp = currentPreset.FilePath + ".tmp";
            var states = GetStates();
            if (currentPreset.Type == PresetType.Json) ConfigEditors.SaveJsonWithStates(currentPreset.FilePath, temp, states);
            else ConfigEditors.SaveIniWithStates(currentPreset.FilePath, temp, states);
            File.Move(temp, currentPreset.FilePath, true);
            LoadPreset(currentPreset);
            SetStatus($"'{currentPreset.Name}' を保存しました。");
            RefreshHomePresets();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void DeletePreset()
    {
        if (presetList.SelectedItem is not string name) { MessageBox.Show("削除するプリセットを選択してください。"); return; }
        if (MessageBox.Show($"プリセット '{name}' を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            Directory.Delete(Path.Combine(presetRoot, name), true);
            settings.PresetOrder.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (settings.TraySingleClickPreset.Equals(name, StringComparison.OrdinalIgnoreCase))
                settings.TraySingleClickPreset = "";
            if (settings.TrayDoubleClickPreset.Equals(name, StringComparison.OrdinalIgnoreCase))
                settings.TrayDoubleClickPreset = "";
            SaveSettings();
            currentPreset = null;
            moduleList.Items.Clear();
            RefreshEditorPresets();
            RefreshHomePresets();
            SetStatus($"'{name}' を削除しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LaunchTrayAssignedPreset(string presetName)
    {
        if (launchInProgress) return;
        if (string.IsNullOrWhiteSpace(presetName)) return;

        var preset = EnumeratePresets().FirstOrDefault(p =>
            p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            MessageBox.Show("割り当てられたプリセットが見つかりません。設定ウィンドウで選び直してください。", "ModMan");
            return;
        }
        ApplyAndLaunch(preset);
    }

    private void ApplySelectedPresetFromEditor()
    {
        if (currentPreset == null) { MessageBox.Show("適用するプリセットを選択してください。"); return; }
        ApplyAndLaunch(currentPreset);
    }

    private async void ApplyAndLaunch(PresetInfo preset)
    {
        if (launchInProgress) return;
        launchInProgress = true;
        try
        {
            SaveSettings();
            string gameDir = Path.GetFullPath(settings.GameDirectory);
            if (!Directory.Exists(gameDir)) throw new DirectoryNotFoundException("ゲームフォルダが見つかりません。");
            string target = Path.Combine(gameDir, TargetFileName(preset.Type));
            string launch = Path.IsPathRooted(settings.LaunchExe) ? settings.LaunchExe : Path.Combine(gameDir, settings.LaunchExe);
            if (!File.Exists(preset.FilePath)) throw new FileNotFoundException("プリセットファイルがありません。", preset.FilePath);
            if (!File.Exists(launch)) throw new FileNotFoundException("起動EXEが見つかりません。", launch);

            SetStatus("th123.exe を終了しています...");
            await StopGameProcesses();
            if (settings.BackupEnabled && File.Exists(target))
            {
                string backupDir = Path.Combine(managerDir, "Backups");
                Directory.CreateDirectory(backupDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                File.Copy(target, Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(target)}_{stamp}{Path.GetExtension(target)}"));
            }
            string temp = target + ".modman.tmp";
            File.Copy(preset.FilePath, temp, true);
            File.Move(temp, target, true);
            Process.Start(new ProcessStartInfo { FileName = launch, WorkingDirectory = gameDir, UseShellExecute = true });
            SetStatus($"'{preset.Name}' を適用してゲームを起動しました。");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { launchInProgress = false; }
    }

    private static async Task StopGameProcesses()
    {
        foreach (var process in Process.GetProcessesByName("th123"))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
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
                    throw new InvalidOperationException("th123.exe を終了できません。アクセス権を確認してください。", ex);
                }
            }
        }
    }

    private void SetStatus(string message) => statusLabel.Text = message;
    private static void ShowError(Exception ex) => MessageBox.Show(ex.Message, "ModMan - エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
