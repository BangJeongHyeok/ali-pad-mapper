using MacroPad.Core.Config;

namespace PadAgent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 중복 실행 방지
        using var mutex = new Mutex(true, @"Local\MacroPad.PadAgent", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("MacroPad Agent가 이미 실행 중이야.\n(작업 표시줄 오른쪽 숨겨진 아이콘 확인)",
                "MacroPad", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        AgentLog.Write($"시작 (설정: {configPath})");

        Application.Run(new TrayApp(configPath));

        AgentLog.Write("종료");
    }
}
