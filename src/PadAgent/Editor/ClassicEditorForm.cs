using System.Text;
using MacroPad.Core;
using MacroPad.Core.Config;
using MacroPad.Core.Editing;
using MacroPad.Core.Keys;
using MacroPad.Core.Protocol;

namespace PadAgent.Editor;

/// <summary>
/// 기본 설정 편집기 창 (WinForms). WebView2 런타임이 없는 PC에서 대신 쓰임.
///  왼쪽: 패드 모양 (버튼 12개 + 노브 2개 × 왼쪽/누름/오른쪽) — 클릭해서 칸 선택
///  오른쪽: 선택한 칸에 넣을 동작 (비워두기 / 미디어 키 / 키·단축키 / PC 동작)
///  아래: 저장, 패드에 쓰기
///
/// 입력하는 즉시 메모리 안의 설정(ConfigEditor)에 반영되고, [저장]해야 macropad.json 에 써짐.
/// 저장하면 PadAgent 가 파일 변경을 감지해서 PC 동작을 바로 다시 불러옴.
/// </summary>
internal sealed class ClassicEditorForm : Form
{
    private static readonly Color AssignedColor = Color.FromArgb(225, 236, 255);
    private static readonly Color PendingColor = Color.FromArgb(255, 226, 180);
    private static readonly Color InvalidColor = Color.FromArgb(255, 205, 205);
    private static readonly Color EmptyColor = Color.FromArgb(243, 243, 243);

    private readonly string _configPath;
    private ConfigEditor _editor = null!;
    private string _statePath = "";
    private PadState? _padState;
    private bool _fileIsEditorFormat;

    private int _layer;
    private SlotRef _selected = SlotRef.Button(0, 0);
    private bool _loading;
    private bool _busy;

    // 패드 칸 버튼 (키: 레이어 0 기준 SlotRef)
    private readonly Dictionary<SlotRef, Button> _slotButtons = new();
    private readonly List<RadioButton> _layerRadios = new();
    private readonly List<Label> _wrapLabels = new();
    private readonly ToolTip _toolTip = new();

    private Control _main = null!;
    private Label _slotTitle = null!;
    private RadioButton _kindEmpty = null!, _kindMedia = null!, _kindKeys = null!, _kindPc = null!;
    private Label _kindInfo = null!, _errorLabel = null!, _uploadInfo = null!;
    private Control _mediaPanel = null!, _keysPanel = null!, _pcPanel = null!;
    private ComboBox _mediaCombo = null!;
    private TextBox _keysBox = null!, _runBox = null!, _argsBox = null!, _pcKeysBox = null!, _textBox = null!;
    private Label _statusLabel = null!;
    private Button _saveButton = null!, _uploadChangedButton = null!, _uploadAllButton = null!, _reloadButton = null!;

    /// <exception cref="FormatException">설정 파일을 읽을 수 없을 때</exception>
    public ClassicEditorForm(string configPath, Icon? icon)
    {
        _configPath = configPath;

        Text = "MacroPad 편집기";
        if (icon is not null) Icon = icon;
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleDimensions = new SizeF(96F, 96F); // 아래 크기 값은 100% 배율 기준 → 화면 배율에 맞춰 자동 확대
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 580);
        Size = new Size(1080, 680);
        KeyPreview = true;

        LoadFromFile();
        BuildUi();

        SelectSlot(SlotRef.Button(0, 0));
        UpdateTitle();
        UpdateStatus();

        // 트레이 메뉴로 패드에 쓴 경우 등: 창으로 돌아올 때마다 패드 기록 다시 읽기
        Activated += (_, _) =>
        {
            if (_busy) return;
            _padState = PadState.Load(_statePath);
            RefreshSlotButtons();
            UpdateSlotInfo();
            UpdateStatus();
        };
    }

    // ─────────────────────────── 파일 ───────────────────────────

    private void LoadFromFile()
    {
        var exists = File.Exists(_configPath);
        var text = exists ? File.ReadAllText(_configPath) : "{}";
        var config = PadConfig.Parse(text);

        _editor = new ConfigEditor(config);
        _fileIsEditorFormat = !exists || ConfigWriter.IsEditorFormat(text);
        _statePath = PadState.DefaultPath(config.Device);
        _padState = PadState.Load(_statePath);
    }

    private void ReloadFromFile()
    {
        if (_editor.IsDirty &&
            MessageBox.Show(this, "저장하지 않은 변경을 버리고 파일에서 다시 읽을까요?", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        try
        {
            LoadFromFile();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "설정 파일을 읽지 못했습니다 (편집 중인 내용 유지):\n" + ex.Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectSlot(_selected);
        UpdateTitle();
        UpdateStatus();
        SetStatus("파일에서 다시 읽었습니다.");
    }

    private bool Save()
    {
        if (!_fileIsEditorFormat)
        {
            var answer = MessageBox.Show(this,
                "편집기로 저장하면 설정 파일의 주석(// …)과 줄 배치가 편집기 형식으로 바뀝니다.\n" +
                $"원래 파일은 {Path.GetFileName(_configPath)}.bak 으로 백업해 둡니다. 저장할까요?",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (answer != DialogResult.OK) return false;
        }

        try
        {
            ConfigWriter.Save(_editor.Config, _configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            MessageBox.Show(this, "저장 실패: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _editor.MarkSaved();
        _fileIsEditorFormat = true;
        AgentLog.Write("편집기: 설정 저장");
        UpdateTitle();
        SetStatus("저장했습니다. (PC 동작은 바로 반영, 패드 칸은 [패드에 쓰기] 필요)");
        return true;
    }

    // ─────────────────────────── 패드에 쓰기 ───────────────────────────

    private async void Upload(bool onlyChanged)
    {
        if (_busy) return;

        if (_editor.IsDirty || !File.Exists(_configPath))
        {
            if (!Save()) return;
        }

        var config = _editor.Config;
        var plan = UploadPlan.Create(config);
        if (!plan.IsValid)
        {
            MessageBox.Show(this, "설정에 문제가 있어서 쓸 수 없습니다:\n\n" + string.Join("\n", plan.Errors), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (onlyChanged) plan = plan.OnlyChanged(_padState);
        if (plan.Bindings.Count == 0)
        {
            MessageBox.Show(this, onlyChanged ? "패드에 새로 쓸 칸이 없습니다. (패드와 설정이 같습니다)" : "쓸 칸이 없습니다. (모든 칸이 비어 있음)",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        const int maxLines = 30;
        var summary = string.Join("\n", plan.Bindings.Take(maxLines).Select(b => b.ToString()));
        if (plan.Bindings.Count > maxLines) summary += $"\n… 외 {plan.Bindings.Count - maxLines}개";
        var skipped = plan.SkippedUnchanged > 0 ? $"\n(패드와 같은 {plan.SkippedUnchanged}개 칸은 건너뜀)" : "";

        if (MessageBox.Show(this, $"아래 {plan.Bindings.Count}개 칸을 패드에 쓸까요?{skipped}\n\n{summary}", "패드에 쓰기",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetBusy(true);
        SetStatus("패드에 쓰는 중…");
        var log = new StringBuilder();
        string result;
        try
        {
            var count = await Task.Run(() => PadProgrammer.Upload(config, line => log.AppendLine(line), onlyChanged: onlyChanged));
            AgentLog.Write("편집기: 패드에 쓰기 완료\n" + log);
            result = $"패드에 {count}개 칸을 썼습니다. 바로 눌러 보거나 돌려서 확인해 주십시오.";
        }
        catch (Exception ex)
        {
            AgentLog.Write("편집기: 패드에 쓰기 실패\n" + log + ex);
            result = "패드에 쓰기 실패: " + ex.Message;
            SetStatus(result);
            MessageBox.Show(this, result, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            // 중간에 실패해도 거기까지 쓴 칸은 기록돼 있으니 다시 읽어서 표시
            _padState = PadState.Load(_statePath);
            SetBusy(false);
            RefreshSlotButtons();
            UpdateSlotInfo();
        }
        SetStatus(result);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _main.Enabled = !busy;
        foreach (var rb in _layerRadios) rb.Enabled = !busy;
        _saveButton.Enabled = _uploadChangedButton.Enabled = _uploadAllButton.Enabled = _reloadButton.Enabled = !busy;
        UseWaitCursor = busy;
    }

    // ─────────────────────────── 칸 선택 / 표시 ───────────────────────────

    private void SwitchLayer(int layer)
    {
        _layer = layer;
        SelectSlot(_selected.WithLayer(layer));
    }

    private void SelectSlot(SlotRef slot)
    {
        _selected = slot;
        LoadSlotIntoUi();
        RefreshSlotButtons();
    }

    private void LoadSlotIntoUi()
    {
        _loading = true;
        try
        {
            _slotTitle.Text = _selected.Describe(_editor.Advanced.ButtonColumns);
            _errorLabel.Text = "";

            var assignment = _editor.Get(_selected);
            _keysBox.Text = "";
            FillPcFields(null);

            switch (assignment)
            {
                case PadKeysSlot { Binding: MediaBinding media }:
                    _kindMedia.Checked = true;
                    SelectMedia(media.Name);
                    break;
                case PadKeysSlot keys:
                    _kindKeys.Checked = true;
                    _keysBox.Text = keys.Text;
                    break;
                case PcActionSlot pc:
                    _kindPc.Checked = true;
                    FillPcFields(pc.Action);
                    break;
                case InvalidSlot bad:
                    _kindKeys.Checked = true;
                    _keysBox.Text = bad.Text;
                    _errorLabel.Text = "설정 파일 값이 잘못됐습니다: " + bad.Error;
                    break;
                default:
                    _kindEmpty.Checked = true;
                    break;
            }

            UpdateKindPanels();
            UpdateSlotInfo();
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshSlotButtons()
    {
        foreach (var (key, button) in _slotButtons)
        {
            var slot = key.WithLayer(_layer);
            var assignment = _editor.Get(slot);
            var label = SlotDisplay.Short(assignment, 30);
            var head = key.IsButton
                ? $"{key.Index + 1}"
                : key.KnobAction switch
                {
                    KnobAction.Ccw => "◀ 왼쪽",
                    KnobAction.Press => "● 누름",
                    _ => "오른쪽 ▶",
                };

            button.Text = $"{head}\n{(label.Length == 0 ? "(비어 있음)" : label)}";
            button.ForeColor = assignment is EmptySlot ? SystemColors.GrayText : SystemColors.ControlText;
            button.BackColor = assignment switch
            {
                InvalidSlot => InvalidColor,
                EmptySlot => EmptyColor,
                _ when _editor.NeedsUpload(slot, _padState) => PendingColor,
                _ => AssignedColor,
            };

            var selected = slot == _selected;
            button.FlatAppearance.BorderSize = selected ? 3 : 1;
            button.FlatAppearance.BorderColor = selected ? SystemColors.Highlight : SystemColors.ControlDark;
            _toolTip.SetToolTip(button, $"{slot.Describe(_editor.Advanced.ButtonColumns)}\n{SlotDisplay.Short(assignment, 200)}");
        }
    }

    private void UpdateSlotInfo()
    {
        var assignment = _editor.Get(_selected);
        _kindInfo.Text = SlotDisplay.Kind(assignment);

        string upload;
        if (_editor.NeedsUpload(_selected, _padState))
        {
            upload = "이 칸은 아직 패드에 쓰지 않았습니다. [패드에 쓰기]를 눌러야 패드에 반영됩니다.";
        }
        else if (assignment is EmptySlot
                 && _padState?.Get(_selected.Layer, _selected.PadSlot(_editor.Options)) is { } record)
        {
            upload = $"패드에 마지막으로 쓴 값: \"{record.Source}\" ({record.WrittenAt:yyyy-MM-dd HH:mm}) — 비워두면 그대로 남아.";
        }
        else
        {
            upload = "";
        }
        _uploadInfo.Text = upload;
    }

    private void UpdateStatus()
    {
        string text;
        if (_padState is null)
        {
            text = "이 PC에서 패드에 쓴 기록이 없습니다 — 처음 한 번은 [전체 쓰기]를 권장합니다.";
        }
        else
        {
            var pending = _editor.AllSlots().Count(s => _editor.NeedsUpload(s, _padState));
            text = pending > 0 ? $"패드에 아직 안 쓴 칸: {pending}개" : "패드와 설정이 같아.";
        }
        SetStatus(text);
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void UpdateTitle()
    {
        Text = $"MacroPad 편집기 — {Path.GetFileName(_configPath)}{(_editor.IsDirty ? " *" : "")}";
        _toolTip.SetToolTip(_statusLabel, "설정 파일: " + _configPath);
    }

    private void UpdateKindPanels()
    {
        _mediaPanel.Visible = _kindMedia.Checked;
        _keysPanel.Visible = _kindKeys.Checked;
        _pcPanel.Visible = _kindPc.Checked;
    }

    // ─────────────────────────── 입력 → 설정 ───────────────────────────

    private void OnKindChanged(object? sender, EventArgs e)
    {
        if (_loading || sender is not RadioButton { Checked: true }) return;

        if (_kindMedia.Checked && _mediaCombo.SelectedIndex < 0 && _mediaCombo.Items.Count > 0)
        {
            _loading = true;
            _mediaCombo.SelectedIndex = 0;
            _loading = false;
        }

        UpdateKindPanels();
        ApplyFromUi();
    }

    /// <summary>오른쪽 입력 내용을 선택한 칸에 반영. 잘못된 입력이면 설정은 그대로 두고 오류만 표시.</summary>
    private void ApplyFromUi()
    {
        if (_loading) return;

        _errorLabel.Text = "";
        try
        {
            if (_kindEmpty.Checked)
            {
                _editor.Clear(_selected);
            }
            else if (_kindMedia.Checked)
            {
                if (_mediaCombo.SelectedItem is MediaItem item)
                    _editor.SetPadKeys(_selected, item.Key.Name);
            }
            else if (_kindKeys.Checked)
            {
                var text = _keysBox.Text.Trim();
                if (text.Length == 0) _editor.Clear(_selected);
                else _editor.SetPadKeys(_selected, text);
            }
            else if (_kindPc.Checked)
            {
                var action = BuildActionFromUi();
                if (action.IsEmpty) _editor.Clear(_selected);
                else _editor.SetPcAction(_selected, action);
            }
        }
        catch (FormatException ex)
        {
            _errorLabel.Text = "아직 적용 안 됨 — " + ex.Message;
        }

        UpdateSlotInfo();
        RefreshSlotButtons();
        UpdateTitle();
        UpdateStatus();
    }

    private ActionConfig BuildActionFromUi()
    {
        // 화면에 없는 값(workingDir, delayMs)은 기존 값 유지
        var action = _editor.Get(_selected) is PcActionSlot pc ? pc.Action.Clone() : new ActionConfig();
        action.Run = NullIfBlank(_runBox.Text);
        action.Args = NullIfBlank(_argsBox.Text);
        var keys = _pcKeysBox.Text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        action.Keys = keys.Count > 0 ? keys : null;
        action.Text = _textBox.Text.Length > 0 ? _textBox.Text.ReplaceLineEndings("\n") : null;
        return action;
    }

    private void FillPcFields(ActionConfig? action)
    {
        _runBox.Text = action?.Run ?? "";
        _argsBox.Text = action?.Args ?? "";
        _pcKeysBox.Text = string.Join(", ", action?.Keys ?? new());
        _textBox.Text = (action?.Text ?? "").ReplaceLineEndings("\r\n");
    }

    private void SelectMedia(string name)
    {
        for (int i = 0; i < _mediaCombo.Items.Count; i++)
        {
            if (_mediaCombo.Items[i] is MediaItem m && string.Equals(m.Key.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _mediaCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void CaptureKeysInto(TextBox target, string title)
    {
        using var dialog = new KeyCaptureDialog(title);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result.Length > 0)
            target.Text = dialog.Result; // TextChanged → ApplyFromUi
    }

    private void BrowseFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "실행할 프로그램 / 파일",
            Filter = "프로그램 (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|모든 파일 (*.*)|*.*",
            DereferenceLinks = false, // 바로가기(.lnk)는 바로가기 그대로
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _runBox.Text = dialog.FileName;
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "열 폴더", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _runBox.Text = dialog.SelectedPath;
    }

    // ─────────────────────────── 창 이벤트 ───────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            if (!_busy) Save();
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            return;
        }

        if (_editor.IsDirty)
        {
            var answer = MessageBox.Show(this, "저장하지 않은 변경이 있습니다. 저장할까요?", Text,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !Save()))
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }

    // ─────────────────────────── 화면 구성 ───────────────────────────

    private void BuildUi()
    {
        var adv = _editor.Advanced;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildTopBar(adv), 0, 0);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.Controls.Add(BuildPad(adv), 0, 0);
        main.Controls.Add(BuildSlotEditor(), 1, 0);
        root.Controls.Add(main, 0, 1);
        _main = main;

        root.Controls.Add(BuildBottomBar(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildTopBar(AdvancedSection adv)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        bar.Controls.Add(new Label { Text = "레이어", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });

        for (int l = 0; l < Math.Max(1, adv.LayerCount); l++)
        {
            int layer = l;
            var rb = new RadioButton
            {
                Text = (l + 1).ToString(),
                Appearance = Appearance.Button,
                AutoSize = true,
                MinimumSize = new Size(40, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Checked = l == 0,
            };
            rb.CheckedChanged += (_, _) =>
            {
                if (rb.Checked) SwitchLayer(layer);
            };
            _layerRadios.Add(rb);
            bar.Controls.Add(rb);
        }

        bar.Controls.Add(new Label
        {
            Text = "레이어 2·3 전환 방법은 아직 확인 전이라, 보통은 레이어 1만 쓰면 됩니다.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(12, 7, 0, 0),
        });
        return bar;
    }

    private Control BuildPad(AdvancedSection adv)
    {
        var cols = Math.Max(1, adv.ButtonColumns);
        var rows = Math.Max(1, (adv.ButtonCount + cols - 1) / cols);

        var wrapper = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 12, 0) };
        wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols + 1, RowCount = rows, Margin = new Padding(0) };
        for (int c = 0; c < cols; c++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / (cols + 1.4f)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 140f / (cols + 1.4f)));
        for (int r = 0; r < rows; r++)
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

        for (int i = 0; i < adv.ButtonCount; i++)
            grid.Controls.Add(CreateSlotButton(SlotRef.Button(0, i)), i % cols, i / cols);

        if (adv.KnobCount > 0)
        {
            var knobs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = adv.KnobCount, Margin = new Padding(8, 0, 0, 0) };
            knobs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int k = 0; k < adv.KnobCount; k++)
            {
                knobs.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / adv.KnobCount));

                var group = new GroupBox { Text = SlotRef.KnobName(k), Dock = DockStyle.Fill, Padding = new Padding(4) };
                var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
                inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                foreach (var action in new[] { KnobAction.Ccw, KnobAction.Press, KnobAction.Cw })
                {
                    inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
                    inner.Controls.Add(CreateSlotButton(SlotRef.Knob(0, k, action)));
                }
                group.Controls.Add(inner);
                knobs.Controls.Add(group, 0, k);
            }
            grid.Controls.Add(knobs, cols, 0);
            grid.SetRowSpan(knobs, rows);
        }

        wrapper.Controls.Add(grid, 0, 0);

        var legend = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
        legend.Controls.Add(LegendItem(AssignedColor, "지정됨"));
        legend.Controls.Add(LegendItem(PendingColor, "패드에 아직 안 씀"));
        legend.Controls.Add(LegendItem(EmptyColor, "비어 있음 (패드 값 유지)"));
        legend.Controls.Add(LegendItem(InvalidColor, "잘못된 값"));
        wrapper.Controls.Add(legend, 0, 1);

        return wrapper;
    }

    private static Control LegendItem(Color color, string text)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 14, 0) };
        panel.Controls.Add(new Label
        {
            BackColor = color,
            BorderStyle = BorderStyle.FixedSingle,
            Size = new Size(14, 14),
            Margin = new Padding(0, 3, 4, 0),
        });
        panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 0) });
        return panel;
    }

    private Button CreateSlotButton(SlotRef slot)
    {
        var button = new Button
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            UseMnemonic = false, // '&' 가 들어간 동작 이름도 그대로 보이게
        };
        button.Click += (_, _) => SelectSlot(slot.WithLayer(_layer));
        _slotButtons[slot] = button;
        return button;
    }

    private Control BuildSlotEditor()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(4, 0, 4, 0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.SizeChanged += (_, _) => UpdateWrapWidths(panel);

        _slotTitle = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        };
        panel.Controls.Add(_slotTitle);

        var kinds = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 0, 4) };
        _kindEmpty = KindRadio("비워두기");
        _kindMedia = KindRadio("미디어 키");
        _kindKeys = KindRadio("키 / 단축키");
        _kindPc = KindRadio("PC 동작");
        kinds.Controls.AddRange([_kindEmpty, _kindMedia, _kindKeys, _kindPc]);
        _toolTip.SetToolTip(_kindEmpty, "패드에 쓸 때 이 칸은 건드리지 않음");
        _toolTip.SetToolTip(_kindMedia, "볼륨, 재생/정지 등. 패드에 저장돼서 PC 프로그램 없이 동작");
        _toolTip.SetToolTip(_kindKeys, "키 하나, 단축키, 연속 입력. 패드에 저장돼서 PC 프로그램 없이 동작");
        _toolTip.SetToolTip(_kindPc, "프로그램 실행, 긴 단축키 묶음, 문자열 입력. 이 프로그램이 켜져 있을 때 동작");
        panel.Controls.Add(kinds);

        _kindInfo = WrapLabel(SystemColors.GrayText);
        panel.Controls.Add(_kindInfo);

        _mediaPanel = BuildMediaPanel();
        _keysPanel = BuildKeysPanel();
        _pcPanel = BuildPcPanel();
        panel.Controls.Add(_mediaPanel);
        panel.Controls.Add(_keysPanel);
        panel.Controls.Add(_pcPanel);

        _errorLabel = WrapLabel(Color.Firebrick);
        _uploadInfo = WrapLabel(Color.FromArgb(176, 96, 0));
        panel.Controls.Add(_errorLabel);
        panel.Controls.Add(_uploadInfo);

        return panel;
    }

    private RadioButton KindRadio(string text)
    {
        var rb = new RadioButton { Text = text, AutoSize = true, Margin = new Padding(0, 0, 14, 0) };
        rb.CheckedChanged += OnKindChanged;
        return rb;
    }

    private Control BuildMediaPanel()
    {
        var p = Stack();
        p.Controls.Add(Caption("패드가 보낼 미디어 키"));

        _mediaCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        foreach (var key in KeyTable.MediaKeys)
            _mediaCombo.Items.Add(new MediaItem(key));
        _mediaCombo.SelectedIndexChanged += (_, _) => ApplyFromUi();
        p.Controls.Add(_mediaCombo);

        p.Controls.Add(Hint("미디어 키는 다른 키와 섞을 수 없습니다. 노브 왼쪽/오른쪽에 볼륨 -/+ 를 넣는 것이 일반적입니다."));
        return p;
    }

    private Control BuildKeysPanel()
    {
        var p = Stack();
        p.Controls.Add(Caption("패드가 보낼 키 (동시에 누르기는 +, 차례대로는 쉼표)"));

        _keysBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _keysBox.TextChanged += (_, _) => ApplyFromUi();
        var capture = new Button { Text = "키 눌러서 입력…", AutoSize = true };
        capture.Click += (_, _) => CaptureKeysInto(_keysBox, "패드가 보낼 키");
        p.Controls.Add(Row(_keysBox, capture));

        p.Controls.Add(Hint("예: f5 · ctrl+c · ctrl+shift+esc · ctrl+a, ctrl+c (수식 키 포함 최대 18개)\n" +
                            "키 이름 전체 목록: PadFlasher keys"));
        return p;
    }

    private Control BuildPcPanel()
    {
        var p = Stack();

        p.Controls.Add(Caption("실행할 프로그램 / 파일 / 폴더 / URL"));
        _runBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _runBox.TextChanged += (_, _) => ApplyFromUi();
        var browseFile = new Button { Text = "파일…", AutoSize = true };
        browseFile.Click += (_, _) => BrowseFile();
        var browseFolder = new Button { Text = "폴더…", AutoSize = true };
        browseFolder.Click += (_, _) => BrowseFolder();
        p.Controls.Add(Row(_runBox, browseFile, browseFolder));

        p.Controls.Add(Caption("실행 인자 (선택)"));
        _argsBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _argsBox.TextChanged += (_, _) => ApplyFromUi();
        p.Controls.Add(_argsBox);

        p.Controls.Add(Caption("보낼 단축키 (선택, 쉼표로 차례대로)"));
        _pcKeysBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _pcKeysBox.TextChanged += (_, _) => ApplyFromUi();
        var capture = new Button { Text = "키 눌러서 입력…", AutoSize = true };
        capture.Click += (_, _) => CaptureKeysInto(_pcKeysBox, "PC가 보낼 단축키");
        p.Controls.Add(Row(_pcKeysBox, capture));

        p.Controls.Add(Caption("입력할 문자열 (선택, 한글 가능)"));
        _textBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 64,
        };
        _textBox.TextChanged += (_, _) => ApplyFromUi();
        p.Controls.Add(_textBox);

        p.Controls.Add(Hint("적힌 것을 실행 → 단축키 → 문자열 순서로 처리합니다. 환경 변수(%USERPROFILE% 등) 사용 가능.\n" +
                            "트리거 키(F13~F24 등)는 자동으로 배정됩니다."));
        return p;
    }

    private Control BuildBottomBar()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _statusLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, AutoEllipsis = true };
        bar.Controls.Add(_statusLabel, 0, 0);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        _reloadButton = BottomButton("다시 읽기", "파일에서 다시 읽기 (저장 안 한 변경은 버림)", ReloadFromFile);
        _saveButton = BottomButton("저장 (Ctrl+S)", "macropad.json 에 저장. PC 동작은 바로 반영됨", () => Save());
        _uploadChangedButton = BottomButton("패드에 쓰기", "저장하고, 패드에 아직 안 쓴 칸만 씀", () => Upload(onlyChanged: true));
        _uploadAllButton = BottomButton("전체 쓰기", "저장하고, 비어 있지 않은 모든 칸을 다시 씀 (처음 한 번, 또는 패드 상태가 의심될 때)", () => Upload(onlyChanged: false));
        _uploadChangedButton.Font = new Font(Font, FontStyle.Bold);
        buttons.Controls.AddRange([_reloadButton, _saveButton, _uploadChangedButton, _uploadAllButton]);
        bar.Controls.Add(buttons, 1, 0);

        return bar;
    }

    private Button BottomButton(string text, string tooltip, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(6, 0, 0, 0) };
        b.Click += (_, _) => onClick();
        _toolTip.SetToolTip(b, tooltip);
        return b;
    }

    private static TableLayoutPanel Stack()
    {
        var p = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(0, 6, 0, 6),
        };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return p;
    }

    private static Control Row(Control fill, params Control[] buttons)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 1 + buttons.Length,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var _ in buttons) row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        fill.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        row.Controls.Add(fill, 0, 0);
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Margin = new Padding(4, 0, 0, 0);
            row.Controls.Add(buttons[i], i + 1, 0);
        }
        return row;
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 2) };

    private Label Hint(string text)
    {
        var label = WrapLabel(SystemColors.GrayText);
        label.Text = text;
        return label;
    }

    /// <summary>패널 폭에 맞춰 줄바꿈되는 라벨 (UpdateWrapWidths 에서 최대 폭 갱신)</summary>
    private Label WrapLabel(Color color)
    {
        var label = new Label { AutoSize = true, ForeColor = color, Margin = new Padding(0, 4, 0, 0) };
        _wrapLabels.Add(label);
        return label;
    }

    private void UpdateWrapWidths(Control panel)
    {
        var width = Math.Max(120, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
        foreach (var label in _wrapLabels)
            label.MaximumSize = new Size(width, 0);
    }

    private sealed record MediaItem(KeyInfo Key)
    {
        public override string ToString() => $"{KeyNames.Media(Key.Name)}   ({Key.Name})";
    }
}
