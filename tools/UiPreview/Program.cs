using System.Net;
using System.Text;
using MacroPad.Core.Config;
using MacroPad.Core.Editing;
using MacroPad.Core.Protocol;

// 편집기 화면 미리보기 서버.
//   dotnet run --project tools/UiPreview [-- --port 5180] [--disconnected]
// ui/ 폴더를 그대로 서빙하고, 화면이 보내는 요청(POST /api)은 실제 EditorSession 이 처리.
// 실제 macropad.json 은 임시 폴더로 복사해서 쓰고, 패드 쓰기는 기록만 남기는 흉내.

var port = 5180;
var connected = true;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
    if (args[i] == "--disconnected") connected = false;
}

var root = FindRepoRoot();
var uiDir = Path.Combine(root, "ui");
var work = Path.Combine(Path.GetTempPath(), "macropad-ui-preview");
Directory.CreateDirectory(work);
var configPath = Path.Combine(work, "macropad.json");
if (!File.Exists(configPath))
{
    // 내 설정이 있으면 그걸, 없으면 예시를 복사 (원본은 건드리지 않음)
    var mine = Path.Combine(root, "macropad.json");
    File.Copy(File.Exists(mine) ? mine : Path.Combine(root, "macropad.example.json"), configPath);
}

var platform = new PreviewPlatform { Connected = connected };
var session = new EditorSession(configPath, platform, Path.Combine(work, "pad-state.json"));

var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/");
listener.Start();
Console.WriteLine($"미리보기: http://localhost:{port}/   (설정 복사본: {configPath})");
Console.WriteLine("Ctrl+C 로 종료. 복사본을 초기화하려면 위 폴더를 지우고 다시 실행.");

var gate = new SemaphoreSlim(1, 1); // 실제 앱처럼 요청은 하나씩 (UI 스레드 흉내)
while (true)
{
    var ctx = await listener.GetContextAsync();
    _ = Task.Run(async () =>
    {
        try
        {
            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url!.AbsolutePath == "/api")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                string reply;
                await gate.WaitAsync();
                try
                {
                    session.RefreshExternal();
                    reply = await session.HandleAsync(body);
                }
                finally
                {
                    gate.Release();
                }
                await Write(ctx.Response, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(reply));
                return;
            }

            if (ctx.Request.Url!.AbsolutePath == "/__device")
            {
                // 연결/분리 흉내: /__device?connected=0
                platform.Connected = ctx.Request.QueryString["connected"] != "0";
                await Write(ctx.Response, "text/plain", Encoding.UTF8.GetBytes(platform.Connected ? "connected" : "disconnected"));
                return;
            }

            var rel = ctx.Request.Url.AbsolutePath.TrimStart('/');
            if (rel.Length == 0) rel = "index.html";
            var file = Path.GetFullPath(Path.Combine(uiDir, rel));
            if (!file.StartsWith(uiDir, StringComparison.Ordinal) || !File.Exists(file))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            await Write(ctx.Response, ContentType(file), await File.ReadAllBytesAsync(file));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    });
}

static async Task Write(HttpListenerResponse response, string type, byte[] bytes)
{
    response.ContentType = type;
    response.Headers["Cache-Control"] = "no-store";
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes);
    response.Close();
}

static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".html" => "text/html; charset=utf-8",
    ".css" => "text/css; charset=utf-8",
    ".js" => "text/javascript; charset=utf-8",
    ".svg" => "image/svg+xml",
    ".png" => "image/png",
    _ => "application/octet-stream",
};

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MacroPad.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("MacroPad.sln 이 있는 폴더를 찾지 못했습니다.");
}

internal sealed class PreviewPlatform : IEditorPlatform
{
    public bool Connected { get; set; } = true;
    private bool _startup;

    public bool IsDeviceConnected(PadConfig config) => Connected;

    public async Task<int> UploadAsync(PadConfig config, bool onlyChanged, string statePath, Action<string> log)
    {
        var plan = UploadPlan.Create(config);
        var state = PadState.Load(statePath);
        if (onlyChanged) plan = plan.OnlyChanged(state);
        state ??= new PadState();
        foreach (var b in plan.Bindings)
        {
            await Task.Delay(120); // 쓰는 시간 흉내
            state.Record(b);
            log(b.ToString());
        }
        state.Save(statePath);
        return plan.Bindings.Count;
    }

    public string? BrowseFile() => @"C:\Program Files\Notepad++\notepad++.exe";
    public string? BrowseFolder() => @"C:\Users\me\Documents";
    public void OpenPath(string path) => Console.WriteLine($"[열기] {path}");
    public void RevealPath(string path) => Console.WriteLine($"[위치 열기] {path}");
    public string? LogPath => null;
    public bool IsStartupEnabled() => _startup;
    public void SetStartupEnabled(bool enabled) => _startup = enabled;
    public void RunAction(ActionConfig action) => Console.WriteLine($"[실행] {action.Run} {action.Args}");

    private TimeSpan _fakePosition = TimeSpan.FromSeconds(83);

    public Task<string> SeekMediaAsync(double seconds)
    {
        _fakePosition += TimeSpan.FromSeconds(seconds);
        if (_fakePosition < TimeSpan.Zero) _fakePosition = TimeSpan.Zero;
        return Task.FromResult($"YouTube Music · 미리보기 곡 · {MacroPad.Core.Media.SeekTracker.Format(_fakePosition)}");
    }
}
