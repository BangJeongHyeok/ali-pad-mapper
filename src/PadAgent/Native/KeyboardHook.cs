using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PadAgent.Native;

/// <summary>
/// 전역 저수준 키보드 훅 (WH_KEYBOARD_LL).
/// 모든 키 입력이 앱들에게 가기 전에 여기를 먼저 지나감 → 트리거 키면 가로채서(삼켜서) 동작 실행.
///
/// 비유: 우체국 분류실. 편지(키 입력)가 각 집(앱)에 배달되기 전에 한 번 훑어보고,
/// "F13 앞으로" 같은 우리 편지만 빼서 처리하고 나머지는 그대로 배달 보냄.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    /// <summary>(가상 키 코드, 눌림 여부) → true 를 돌려주면 그 입력을 삼킴</summary>
    private readonly Func<ushort, bool, bool> _handler;

    // GC가 콜백 델리게이트를 수거하지 않도록 필드로 붙잡아 둠 (안 그러면 크래시)
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;

    public KeyboardHook(Func<ushort, bool, bool> handler)
    {
        _handler = handler;
        _proc = Callback;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "키보드 훅 설치 실패");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // 우리가 SendInput 으로 보낸 입력은 건드리지 않음
                if (data.dwExtraInfo != InputSender.Signature)
                {
                    int msg = (int)wParam;
                    bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                    bool up = msg is WM_KEYUP or WM_SYSKEYUP;
                    if ((down || up) && _handler((ushort)data.vkCode, down))
                        return 1; // 삼킴
                }
            }
            catch (Exception ex)
            {
                // 훅 콜백에서 예외가 새어 나가면 앱이 죽으므로 여기서 막음
                AgentLog.Write("훅 처리 중 오류: " + ex);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
