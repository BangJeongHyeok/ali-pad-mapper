using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using MacroPad.Core;
using MacroPad.Core.Config;
using MacroPad.Core.Protocol;
using PadAgent.Editor;
using PadAgent.Native;

namespace PadAgent;

/// <summary>
/// 트레이 아이콘 + 메뉴 + 키보드 훅을 묶은 앱 본체.
/// 창 없이 작업 표시줄 오른쪽 알림 영역에만 아이콘으로 떠 있음.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly string _configPath;
    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly KeyboardHook _hook;
    private readonly ActionRunner _runner;
    private readonly System.Windows.Forms.Timer _reloadTimer;
    private readonly HashSet<ushort> _heldTriggers = new();
    private FileSystemWatcher? _watcher;
    private int _reloadRetries;
    private EditorForm? _editorForm;

    private PadConfig? _config;
    private TriggerMap _triggers = TriggerMap.Empty;

    public TrayApp(string configPath, bool openEditor)
    {
        _configPath = configPath;

        // 메뉴(컨트롤)를 먼저 만들어야 UI 스레드 동기화 컨텍스트가 설치됨
        _menu = new ContextMenuStrip();
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _statusItem = new ToolStripMenuItem("MacroPad Agent") { Enabled = false };
        _startupItem = new ToolStripMenuItem("Windows 시작 시 자동 실행") { CheckOnClick = true };
        _startupItem.Checked = SafeIsStartupEnabled();
        _startupItem.CheckedChanged += (_, _) => ToggleStartup(_startupItem.Checked);

        var editorItem = new ToolStripMenuItem("편집기 열기…", null, (_, _) => OpenEditor());
        editorItem.Font = new Font(editorItem.Font, FontStyle.Bold); // 더블클릭 = 이 메뉴

        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(editorItem);
        _menu.Items.Add("설정 파일 열기 (JSON)", null, (_, _) => OpenConfig());
        _menu.Items.Add("설정 다시 불러오기", null, (_, _) => Reload(showResult: true));
        _menu.Items.Add("패드에 설정 쓰기…", null, (_, _) => UploadToPad());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startupItem);
        _menu.Items.Add("로그 보기", null, (_, _) => OpenPath(AgentLog.FilePath));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("종료", null, (_, _) => Exit());

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "MacroPad Agent",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenEditor();

        _runner = new ActionRunner(ShowError);

        _reloadTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            Reload(showResult: false); // 파일 저장마다 알림이 뜨면 귀찮으니 성공은 조용히 (오류는 알림)
        };

        Reload(showResult: false);
        WatchConfig();

        _hook = new KeyboardHook(OnKey);
        try
        {
            _hook.Install();
        }
        catch (Win32Exception ex)
        {
            ShowError("키보드 훅을 설치하지 못했어: " + ex.Message);
        }

        // 예전 버전이 등록한 자동 실행 항목에 --tray 붙이기 (로그인할 때 편집기 창이 뜨지 않게)
        try { StartupRegistration.MigrateIfNeeded(); }
        catch (Exception ex) { AgentLog.Write("자동 실행 항목 갱신 실패: " + ex.Message); }

        if (openEditor)
            _ui.Post(_ => OpenEditor(), null);
    }

    // ─────────────────────────── 편집기 ───────────────────────────

    /// <summary>편집기 창 열기 (이미 열려 있으면 앞으로). 다른 스레드에서 불러도 됨.</summary>
    public void OpenEditorFromAnyThread() => _ui.Post(_ => OpenEditor(), null);

    private void OpenEditor()
    {
        if (_editorForm is { IsDisposed: false } existing)
        {
            if (existing.WindowState == FormWindowState.Minimized)
                existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            return;
        }

        try
        {
            var form = new EditorForm(_configPath, _icon.Icon);
            form.FormClosed += (_, _) =>
            {
                _editorForm = null;
                form.Dispose();
            };
            _editorForm = form;
            form.Show();
            form.Activate();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            AgentLog.Write("편집기 열기 실패: " + ex);
            MessageBox.Show("설정 파일을 읽을 수 없어서 편집기를 못 열었어:\n" + ex.Message +
                            "\n\n[설정 파일 열기 (JSON)]로 직접 고쳐줘.", "MacroPad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Exit()
    {
        // 편집기에 저장 안 한 내용이 있으면 먼저 물어봄 (취소하면 종료도 취소)
        if (_editorForm is { IsDisposed: false } form)
        {
            form.Close();
            if (_editorForm is not null) return;
        }
        ExitThread();
    }

    // ─────────────────────────── 키 처리 ───────────────────────────

    /// <summary>훅에서 호출됨. true 면 입력을 삼킴. 여기서는 빨리 끝내야 함 (무거운 일은 Post 로 넘김).</summary>
    private bool OnKey(ushort vk, bool down)
    {
        if (!down)
            return _heldTriggers.Remove(vk); // 우리가 삼킨 키의 '뗌'도 삼킴

        // 누르고 있으면 자동 반복으로 계속 들어오는데, 처음 한 번만 실행
        if (_heldTriggers.Contains(vk))
            return true;

        if (!_triggers.TryGet(vk, InputSender.CurrentModifiers(), out var trigger))
            return false;

        _heldTriggers.Add(vk);
        _ui.Post(_ => _runner.Run(trigger), null);
        return true;
    }

    // ─────────────────────────── 설정 ───────────────────────────

    private void Reload(bool showResult)
    {
        try
        {
            if (!File.Exists(_configPath))
                throw new FileNotFoundException($"설정 파일이 없어: {_configPath}");

            var config = PadConfig.Load(_configPath);
            var triggers = TriggerMap.Build(config);

            _config = config;
            _triggers = triggers;
            _heldTriggers.Clear();
            _reloadRetries = 0;

            _statusItem.Text = $"MacroPad Agent · PC 동작 {triggers.Count}개";
            _icon.Text = Truncate($"MacroPad Agent ({triggers.Count}개)\n{Path.GetFileName(_configPath)}", 120);
            AgentLog.Write($"설정 불러옴: PC 동작 {triggers.Count}개");

            if (triggers.Problems.Count > 0)
            {
                foreach (var p in triggers.Problems) AgentLog.Write(p);
                ShowError(string.Join("\n", triggers.Problems.Take(4)));
            }
            else if (showResult)
            {
                ShowInfo($"설정을 다시 불러왔어. (PC 동작 {triggers.Count}개)");
            }
        }
        catch (IOException) when (_reloadRetries++ < 3)
        {
            // 편집기가 아직 저장 중일 수 있음 → 잠깐 뒤 다시
            _reloadTimer.Start();
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            _reloadRetries = 0;
            AgentLog.Write("설정 불러오기 실패: " + ex.Message);
            ShowError("설정을 못 불러왔어 (이전 설정 유지): " + ex.Message);
        }
    }

    private void WatchConfig()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir is null || !Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_configPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler changed = (_, _) => _ui.Post(_ => RestartReloadTimer(), null);
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (_, _) => _ui.Post(_ => RestartReloadTimer(), null);
        _watcher.EnableRaisingEvents = true;
    }

    private void RestartReloadTimer()
    {
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    private void OpenConfig() => OpenPath(_configPath);

    private void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                ShowError($"파일이 없어: {path}");
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            Process.Start("notepad.exe", $"\"{path}\"")?.Dispose();
        }
    }

    // ─────────────────────────── 패드에 쓰기 ───────────────────────────

    private async void UploadToPad()
    {
        if (_config is null)
        {
            ShowError("설정을 먼저 제대로 불러와야 해.");
            return;
        }

        var config = _config;
        var plan = UploadPlan.Create(config);
        if (!plan.IsValid)
        {
            MessageBox.Show("설정 파일에 문제가 있어:\n\n" + string.Join("\n", plan.Errors),
                "패드에 설정 쓰기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var summary = string.Join("\n", plan.Bindings.Take(40).Select(b => b.ToString()));
        if (plan.Bindings.Count > 40) summary += $"\n… 외 {plan.Bindings.Count - 40}개";

        var answer = MessageBox.Show(
            $"아래 {plan.Bindings.Count}개 슬롯을 패드에 쓸까? (비워둔 칸은 그대로 둠)\n\n{summary}",
            "패드에 설정 쓰기", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        var log = new StringBuilder();
        try
        {
            var count = await Task.Run(() => PadProgrammer.Upload(config, line => log.AppendLine(line)));
            AgentLog.Write("패드에 쓰기 완료\n" + log);
            ShowInfo($"패드에 {count}개 슬롯을 썼어. 바로 눌러/돌려서 확인해봐.");
        }
        catch (Exception ex)
        {
            AgentLog.Write("패드에 쓰기 실패\n" + log + ex);
            ShowError("패드에 쓰기 실패: " + ex.Message);
        }
    }

    // ─────────────────────────── 시작 프로그램 ───────────────────────────

    private static bool SafeIsStartupEnabled()
    {
        try { return StartupRegistration.IsEnabled(); }
        catch { return false; }
    }

    private void ToggleStartup(bool enabled)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled);
            ShowInfo(enabled ? "Windows 시작 시 자동으로 실행할게." : "자동 실행을 껐어.");
        }
        catch (Exception ex)
        {
            ShowError("자동 실행 설정 실패: " + ex.Message);
        }
    }

    // ─────────────────────────── 알림 ───────────────────────────

    private void ShowInfo(string message) => Notify(message, ToolTipIcon.Info);

    /// <summary>어느 스레드에서 불러도 됨</summary>
    private void ShowError(string message) => Notify(message, ToolTipIcon.Warning);

    private void Notify(string message, ToolTipIcon icon) =>
        _ui.Post(_ => _icon.ShowBalloonTip(4000, "MacroPad", Truncate(message, 250), icon), null);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static Icon CreateIcon()
    {
        // 노브 모양 아이콘을 코드로 그림 (리소스 파일 없이)
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(52, 120, 220));
            g.FillEllipse(body, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, 16, 16, 16, 6);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _editorForm?.Dispose();
        _hook.Dispose();
        _watcher?.Dispose();
        _reloadTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        base.ExitThreadCore();
    }
}
