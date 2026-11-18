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
            key.SetValue(ValueName, $"\"{ExePath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
