using MacroPad.Core.Config;
using PadAgent.Native;

namespace PadAgent;

internal static class Program
{
    /// <summary>이미 실행 중인 PadAgent 에게 "편집기 열어줘"를 알리는 신호</summary>
    private const string ShowEditorEventName = @"Local\MacroPad.PadAgent.ShowEditor";

    /// <summary>
    /// 사용법:
    ///   PadAgent.exe                 트레이 + 편집기 창 열기 (이미 실행 중이면 그쪽 편집기를 띄움)
    ///   PadAgent.exe --tray          트레이에만 상주 (Windows 시작 시 자동 실행용)
    ///   PadAgent.exe --config 경로   설정 파일 직접 지정
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        var trayOnly = args.Any(a => string.Equals(a, StartupRegistration.TrayArgument, StringComparison.OrdinalIgnoreCase));

        // 중복 실행 방지: 두 번째로 실행되면 먼저 떠 있는 쪽에 편집기를 열라고 알리고 끝냄
        using var mutex = new Mutex(true, @"Local\MacroPad.PadAgent", out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (!trayOnly)
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(ShowEditorEventName);
                    signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    MessageBox.Show("MacroPad Agent가 이미 실행 중이야.\n(작업 표시줄 오른쪽 숨겨진 아이콘 확인)",
                        "MacroPad", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            return;
        }

        ApplicationConfiguration.Initialize();

        // --config <경로> 로 설정 파일 직접 지정 가능
        string? explicitConfig = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
                explicitConfig = args[i + 1];
        }

        var configPath = ConfigLocator.Resolve(explicitConfig);
        AgentLog.Write($"시작 (설정: {configPath}{(trayOnly ? ", 트레이만" : "")})");

        using var showEditor = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEditorEventName);
        var app = new TrayApp(configPath, openEditor: !trayOnly);
        var registration = ThreadPool.RegisterWaitForSingleObject(
            showEditor, (_, _) => app.OpenEditorFromAnyThread(), null, Timeout.Infinite, executeOnlyOnce: false);

        try
        {
            Application.Run(app);
        }
        finally
        {
            registration.Unregister(null);
        }

        AgentLog.Write("종료");
    }
}
