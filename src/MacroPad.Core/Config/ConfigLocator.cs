namespace MacroPad.Core.Config;

/// <summary>
/// 어떤 macropad.json 을 쓸지 결정.
///  1) 명령줄로 직접 지정한 경로
///  2) 실행 파일 위쪽 폴더에 MacroPad.sln 이 있으면 (= 프로젝트 폴더 안에서 실행 중) 그 옆의 macropad.json
///     → Visual Studio에서 실행하든, 프로젝트 안 publish 폴더에서 실행하든 같은 설정 파일 하나를 씀
///  3) 실행 파일 옆의 macropad.json
/// </summary>
public static class ConfigLocator
{
    public const string FileName = "macropad.json";
    public const string SolutionFileName = "MacroPad.sln";

    public static string Resolve(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                var candidate = Path.Combine(dir.FullName, FileName);
                if (File.Exists(candidate))
                    return candidate;
                break;
            }
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, FileName);
    }
}
