using Microsoft.Win32;

namespace PadAgent.Native;

/// <summary>
/// Windows 시작 시 자동 실행 등록.
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run 에 등록 → 관리자 권한 불필요, 현재 사용자에게만 적용.
/// (작업 관리자 → 시작 앱 탭에서도 켜고 끌 수 있음)
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MacroPadAgent";

    /// <summary>자동 실행 때는 트레이에만 뜨고 편집기 창은 안 염</summary>
    public const string TrayArgument = "--tray";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value
               && value.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{ExePath}\" {TrayArgument}");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>예전 형식(인자 없음)으로 등록돼 있으면 --tray 를 붙여서 다시 등록</summary>
    public static void MigrateIfNeeded()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(ValueName) is string value
            && value.Contains(ExePath, StringComparison.OrdinalIgnoreCase)
            && !value.Contains(TrayArgument, StringComparison.OrdinalIgnoreCase))
        {
            SetEnabled(true);
        }
    }
}
