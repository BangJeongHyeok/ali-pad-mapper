using System.Reflection;
using System.Text.Json.Nodes;
using MacroPad.Core.Editing;
using MacroPad.Core.Keys;
using MacroPad.Core.Protocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PadAgent.Editor;

/// <summary>
/// 편집기 창. 화면(ui/ 의 HTML/CSS/JS)은 WebView2(Windows 기본 Edge 엔진)로 그리고,
/// 화면에서 오는 요청은 EditorSession 이 처리.
///
/// 화면 파일은 exe 안에 들어 있어서(EmbeddedResource) https://macropad.local/ 주소로 가로채서 내려줌.
/// 인터넷 연결은 쓰지 않음.
/// </summary>
internal sealed class WebEditorForm : Form
{
    private const string Origin = "https://macropad.local/";

    private readonly EditorSession _session;
    private readonly WebView2 _web;
    private readonly System.Windows.Forms.Timer _deviceTimer;
    private bool _keyTestActive;

    /// <summary>화면에서 '키 테스트'를 켜거나 껐을 때 (켜져 있는 동안 TrayApp 이 키를 이 창으로 넘기고 PC 동작은 실행 안 함)</summary>
    public event Action<bool>? KeyTestChanged;

    /// <summary>이 PC에 WebView2 런타임이 있는지 (Windows 11 기본, Windows 10 대부분 설치돼 있음)</summary>
    public static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    /// <exception cref="FormatException">설정 파일을 읽을 수 없을 때</exception>
    public WebEditorForm(string configPath, Icon? icon)
    {
        _session = new EditorSession(configPath, new WindowsEditorPlatform(this));

        Text = "MacroPad";
        if (icon is not null) Icon = icon;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1320, 860);
        MinimumSize = new Size(1040, 680);
        BackColor = Color.FromArgb(245, 246, 248); // 화면이 뜨기 전 깜빡임 방지 (ui 배경색과 같게)

        _web = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = BackColor,
        };
        Controls.Add(_web);

        // 패드를 꽂고 뽑는 걸 화면에 바로 보이게
        _deviceTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _deviceTimer.Tick += (_, _) =>
        {
            if (_session.RefreshDevice()) PushState();
        };

        // 트레이 메뉴로 패드에 쓴 경우, 설정 파일을 직접 고친 경우 등
        Activated += (_, _) =>
        {
            if (_session.RefreshExternal()) PushState();
        };

        Load += async (_, _) => await InitializeWebAsync();
    }

    private async Task InitializeWebAsync()
    {
        try
        {
            var dataDir = Path.Combine(PadState.DefaultDirectory, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            AgentLog.Write("편집기 화면 초기화 실패: " + ex);
            MessageBox.Show(this, "편집기 화면을 띄우지 못했습니다:\n" + ex.Message + "\n\n트레이 메뉴의 '기본 편집기 열기'를 써 주십시오.",
                "MacroPad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        var settings = core.Settings;
#if DEBUG
        settings.AreDevToolsEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
#endif
        settings.AreBrowserAcceleratorKeysEnabled = false; // F5 새로 고침, Ctrl+P 인쇄 등 브라우저 단축키 끔 (복사/붙여넣기는 됨)
        settings.IsZoomControlEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;

        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };

        core.Navigate(Origin + "index.html");
        _deviceTimer.Start();
    }

    // ─────────────────────────── 화면 파일 ───────────────────────────

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        if (path.Length == 0) path = "index.html";

        var stream = OpenUiResource(path);
        var env = _web.CoreWebView2.Environment;
        e.Response = stream is null
            ? env.CreateWebResourceResponse(null, 404, "Not Found", "")
            : env.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {ContentType(path)}\r\nCache-Control: no-store");
    }

    private static Stream? OpenUiResource(string path)
    {
        var asm = Assembly.GetExecutingAssembly();
        // 빌드 환경에 따라 하위 폴더 구분자가 \ 로 들어갈 수 있어서 둘 다 시도
        return asm.GetManifestResourceStream("ui/" + path)
               ?? asm.GetManifestResourceStream("ui/" + path.Replace('/', '\\'));
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    // ─────────────────────────── 화면 ↔ C# ───────────────────────────

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.WebMessageAsJson;
        // 이벤트 처리기 안에서 대화상자(파일 고르기 등)를 띄우지 않도록 한 박자 뒤에 처리
        BeginInvoke(async () =>
        {
            var reply = await _session.HandleAsync(json);
            if (!IsDisposed && _web.CoreWebView2 is not null)
                _web.CoreWebView2.PostWebMessageAsJson(reply);
            if (_session.KeyTestActive != _keyTestActive)
            {
                _keyTestActive = _session.KeyTestActive;
                KeyTestChanged?.Invoke(_keyTestActive);
            }
        });
    }

    /// <summary>키 테스트 중에 패드에서 온 키 (UI 스레드에서 부름)</summary>
    public void OnPadKey(ushort vk, ModifierFlags mods, bool down, DateTimeOffset at)
    {
        if (IsDisposed || _web.CoreWebView2 is null) return;
        if (_session.HandleKeyTest(vk, mods, down, at) is { } json)
            _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void PushState()
    {
        if (_web.CoreWebView2 is null) return;
        var msg = new JsonObject { ["type"] = "state", ["state"] = _session.BuildState() };
        _web.CoreWebView2.PostWebMessageAsJson(msg.ToJsonString(EditorSession.JsonOptions));
    }

    // ─────────────────────────── 창 ───────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_session.IsBusy)
        {
            e.Cancel = true;
            MessageBox.Show(this, "패드에 쓰는 중입니다. 끝나면 닫아 주십시오.", "MacroPad", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deviceTimer.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
