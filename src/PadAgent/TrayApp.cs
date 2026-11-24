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
    // 누르고 있는 트리거 (뗄 때도 삼키려고). 훅 스레드에서만 씀
    private readonly Dictionary<ushort, HeldKey> _heldTriggers = new();
    private volatile WebEditorForm? _keyTestForm; // 키 테스트 중인 편집기 (UI 스레드가 쓰고 훅 스레드가 읽음)
    private FileSystemWatcher? _watcher;
    private int _reloadRetries;
    private Form? _editorForm;

    private PadConfig? _config;
    private volatile TriggerMap _triggers = TriggerMap.Empty; // UI 스레드가 바꾸고 훅 스레드가 읽음

    /// <param name="TestOnly">키 테스트 중에 눌린 키 (동작을 실행하지 않았으니 뗄 때도 알리지 않음)</param>
    /// <param name="LastSeen">마지막으로 누름(자동 반복 포함)이 들어온 시각</param>
    private sealed record HeldKey(Trigger Trigger, bool TestOnly, DateTimeOffset LastSeen);

    /// <summary>
    /// 누르고 있는 키는 자동 반복으로 0.5초 안팎마다 누름이 다시 들어옴.
    /// 이보다 오래 소식이 없다가 다시 누름이 오면 그 사이 '뗌'을 놓친 것으로 보고 새로 누른 걸로 처리.
    /// </summary>
    private static readonly TimeSpan MissedKeyUpGap = TimeSpan.FromSeconds(2);

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
        _menu.Items.Add("기본 편집기 열기", null, (_, _) => OpenEditor(classic: true));
        _menu.Items.Add("설정 파일 열기 (JSON)", null, (_, _) => OpenConfig());
        _menu.Items.Add("설정 다시 불러오기", null, (_, _) => Reload(showResult: true));
        _menu.Items.Add("패드에 설정 쓰기…", null, (_, _) => UploadToPad());
        _menu.Items.Add("샘플 큐 모두 지우기", null, (_, _) => ClearCues());
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

        _runner = new ActionRunner(ShowError, ShowInfo);

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

    /// <param name="classic">true 면 WinForms 기본 편집기. 아니면 WebView2 편집기 (런타임이 없으면 기본 편집기)</param>
    private void OpenEditor(bool classic = false)
    {
        var wantClassic = classic || !WebEditorForm.IsRuntimeAvailable();

        if (_editorForm is { IsDisposed: false } open)
        {
            if ((open is ClassicEditorForm) == wantClassic)
            {
                if (open.WindowState == FormWindowState.Minimized)
                    open.WindowState = FormWindowState.Normal;
                open.Activate();
                return;
            }

            // 다른 종류의 편집기가 열려 있으면 닫고 새로 (저장 안 한 내용이 있으면 물어보고, 취소하면 그대로)
            open.Close();
            if (!open.IsDisposed) return;
        }

        try
        {
            if (wantClassic && !classic)
                AgentLog.Write("WebView2 런타임이 없어서 기본 편집기로 열어요.");
            Form form = wantClassic
                ? new ClassicEditorForm(_configPath, _icon.Icon)
                : new WebEditorForm(_configPath, _icon.Icon);
            if (form is WebEditorForm web)
                web.KeyTestChanged += active => _keyTestForm = active ? web : null;
            form.FormClosed += (_, _) =>
            {
                if (_editorForm == form) _editorForm = null;
                if (_keyTestForm == form) _keyTestForm = null;
                form.Dispose();
            };
            _editorForm = form;
            form.Show();
            form.Activate();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            AgentLog.Write("편집기 열기 실패: " + ex);
            MessageBox.Show("설정 파일을 읽을 수 없어서 편집기를 열지 못했어요:\n" + ex.Message +
                            "\n\n[설정 파일 열기 (JSON)]로 직접 고쳐 주세요.", "MacroPad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    /// <summary>
    /// 훅 스레드에서 호출됨. true 면 입력을 삼킴. 여기서는 빨리 끝내야 함 (실제 동작은 UI 스레드로 Post).
    /// at = 키가 실제로 눌린/떼진 시각 (샘플 큐는 이 순간이 중요).
    /// </summary>
    private bool OnKey(ushort vk, bool down, DateTimeOffset at)
    {
        var mods = InputSender.CurrentModifiers();

        // 키 테스트 중: 화면에 알려 주고, 트리거는 삼키기만 하고 실행하지 않음
        var tester = _keyTestForm;
        if (tester is not null)
            _ui.Post(_ => tester.OnPadKey(vk, mods, down, at), null);

        if (!down)
        {
            // 우리가 삼킨 키의 '뗌'도 삼킴. 큐 지정 키는 뗀 것도 알려줘야 함
            if (!_heldTriggers.Remove(vk, out var released)) return false;
            if (!released.TestOnly && released.Trigger.Action.IsCueSet)
                _ui.Post(_ => _runner.Release(released.Trigger, at), null);
            return true;
        }

        if (_heldTriggers.TryGetValue(vk, out var held))
        {
            // 누르고 있으면 자동 반복으로 계속 들어오는데, 처음 한 번만 실행
            if (at - held.LastSeen < MissedKeyUpGap)
            {
                _heldTriggers[vk] = held with { LastSeen = at };
                return true;
            }

            // 한참 만에 다시 누름 → 그 사이 '뗌'을 놓쳤음. 뗀 것으로 처리하고 새로 누른 걸로
            _heldTriggers.Remove(vk);
            _ui.Post(_ => AgentLog.Write($"[{held.Trigger.Name}] 뗀 신호를 놓쳐서 뗀 것으로 처리"), null);
            if (!held.TestOnly && held.Trigger.Action.IsCueSet)
                _ui.Post(_ => _runner.Release(held.Trigger, at), null);
        }

        if (!_triggers.TryGet(vk, mods, out var trigger))
            return false;

        var testOnly = tester is not null;
        _heldTriggers[vk] = new HeldKey(trigger, testOnly, at);
        if (!testOnly)
            _ui.Post(_ => _runner.Run(trigger, at), null);
        return true;
    }

    private void ClearCues()
    {
        var count = _runner.CueCount;
        _runner.ClearCues();
        ShowInfo(count > 0 ? $"샘플 큐 {count}개를 지웠어." : "저장한 샘플 큐가 없어.");
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
            _triggers = triggers; // 누르고 있던 키는 그대로 둠: 뗄 때 예전 트리거로 처리됨
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
