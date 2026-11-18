namespace PadAgent;

/// <summary>%LOCALAPPDATA%\MacroPad\agent.log 에 간단한 기록 (문제 확인용)</summary>
internal static class AgentLog
{
    private static readonly object Lock = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroPad");

    public static string FilePath { get; } = Path.Combine(Directory, "agent.log");

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                System.IO.Directory.CreateDirectory(Directory);

                // 1MB 넘으면 한 번 비움
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1024 * 1024)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);

                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로그 실패로 앱이 멈추면 안 되니까 무시
        }
    }
}
