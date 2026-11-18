using System.Runtime.InteropServices;
using MacroPad.Core.Keys;

namespace PadAgent.Editor;

/// <summary>
/// "키 눌러서 입력" 창. 누른 키 조합을 순서대로 기록해서 "ctrl+c, ctrl+v" 형식 문자열로 돌려줌.
/// Enter / Esc / Tab 도 기록 대상이라, 확인/취소는 마우스로 누름.
/// </summary>
internal sealed class KeyCaptureDialog : Form
{
    private readonly List<string> _chords = new();
    private readonly Label _display;
    private readonly Label _message;
    private readonly Button _ok;

    /// <summary>기록된 키 조합들. 예: "ctrl+a, ctrl+c"</summary>
    public string Result => string.Join(", ", _chords);

    public KeyCaptureDialog(string title)
    {
        Text = title;
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleDimensions = new SizeF(96F, 96F); // 아래 크기 값은 100% 배율 기준 → 화면 배율에 맞춰 자동 확대
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(460, 220);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Text = "넣고 싶은 키 조합을 눌러줘. 여러 번 누르면 순서대로 기록돼 (연속 입력).\n" +
                   "다 됐으면 [확인]을 마우스로 눌러. (Win 조합은 Windows가 먼저 가로챌 수 있어)",
        });

        _display = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Font = new Font(Font.FontFamily, Font.Size + 4, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 4),
        };
        layout.Controls.Add(_display);

        _message = new Label { AutoSize = true, ForeColor = Color.Firebrick };
        layout.Controls.Add(_message);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 6, 0, 0) };
        var cancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel, TabStop = false };
        _ok = new Button { Text = "확인", AutoSize = true, DialogResult = DialogResult.OK, TabStop = false, Enabled = false };
        var clear = new Button { Text = "지우기", AutoSize = true, TabStop = false };
        clear.Click += (_, _) =>
        {
            _chords.Clear();
            UpdateDisplay();
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(clear);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        UpdateDisplay();
    }

    // 모든 키(Enter, Tab, 방향키, Alt 조합 포함)를 버튼 동작보다 먼저 가로챔
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        if (msg.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            OnCapturedKey(keyData);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnCapturedKey(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;

        // 수식 키만 누른 상태: 아직 기록하지 않음
        if (code is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin)
        {
            _message.Text = "";
            return;
        }

        if (!KeyTable.TryGetByVk((ushort)code, out var key))
        {
            _message.Text = $"'{code}' 키는 지원하지 않아.";
            return;
        }

        var parts = new List<string>();
        if ((keyData & Keys.Control) != 0) parts.Add("ctrl");
        if ((keyData & Keys.Shift) != 0) parts.Add("shift");
        if ((keyData & Keys.Alt) != 0) parts.Add("alt");
        if (IsDown(0x5B) || IsDown(0x5C)) parts.Add("win");

        if (key.IsMedia && parts.Count > 0)
        {
            _message.Text = "미디어 키는 다른 키와 같이 못 써.";
            return;
        }

        parts.Add(key.Name);
        _chords.Add(string.Join("+", parts));
        _message.Text = "";
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_chords.Count == 0)
        {
            _display.Text = "(키를 눌러줘)";
            _display.ForeColor = SystemColors.GrayText;
        }
        else
        {
            _display.Text = KeyNames.Sequence(_chords.Select(Chord.Parse));
            _display.ForeColor = SystemColors.ControlText;
        }
        _ok.Enabled = _chords.Count > 0;
    }

    private static bool IsDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
