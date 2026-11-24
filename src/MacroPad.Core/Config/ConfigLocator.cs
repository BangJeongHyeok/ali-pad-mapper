namespace MacroPad.Core.Config;

/// <summary>
/// 어떤 macropad.json 을 쓸지 결정.
///  1) 명령줄로 직접 지정한 경로
///  2) 실행 파일 위쪽 폴더에 MacroPad.sln 이 있으면 (= 프로젝트 폴더 안에서 실행 중) 그 옆의 macropad.json
///     → Visual Studio에서 실행하든, 프로젝트 안 publish 폴더에서 실행하든 같은 설정 파일 하나를 씀
///  3) 실행 파일 옆의 macropad.json
/// 2·3 에서 macropad.json 이 없으면 같은 폴더의 macropad.example.json 을 복사해서 만듦.
/// macropad.json 은 개인 설정이라 git 에 올리지 않고(.gitignore), 레포에는 예시만 둠.
/// </summary>
public static class ConfigLocator
{
    public const string FileName = "macropad.json";
    public const string ExampleFileName = "macropad.example.json";
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
                if (EnsureExists(candidate))
                    return candidate;
                break;
            }
            dir = dir.Parent;
        }

        var beside = Path.Combine(AppContext.BaseDirectory, FileName);
        EnsureExists(beside);
        return beside;
    }

    /// <summary>
    /// 설정 파일이 없으면 같은 폴더의 예시 파일을 복사해서 만듦. 결과적으로 파일이 있으면 true.
    /// (복사에 실패해도 예외를 내지 않음: 편집기는 빈 설정으로 시작할 수 있음)
    /// </summary>
    public static bool EnsureExists(string path)
    {
        if (File.Exists(path)) return true;
        var example = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", ExampleFileName);
        if (!File.Exists(example)) return false;
        try
        {
            File.Copy(example, path, overwrite: false);
            return true;
        }
        catch (IOException)
        {
            return File.Exists(path); // 다른 실행 파일이 동시에 만든 경우
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
