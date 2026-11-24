using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PadAgent.Native;

/// <summary>
/// 전역 저수준 키보드 훅 (WH_KEYBOARD_LL).
/// 모든 키 입력이 앱들에게 가기 전에 여기를 먼저 지나감 → 트리거 키면 가로채서(삼켜서) 동작 실행.
///
/// 비유: 우체국 분류실. 편지(키 입력)가 각 집(앱)에 배달되기 전에 한 번 훑어보고,
/// "F13 앞으로" 같은 우리 편지만 빼서 처리하고 나머지는 그대로 배달 보냄.
///
/// 훅은 전용 스레드에서 돌림. Windows 는 훅이 정해진 시간(기본 약 300ms) 안에 답하지 않으면
/// 그 키를 우리를 거치지 않고 그냥 넘기고, 여러 번 그러면 훅을 말없이 떼어 버림.
/// UI 스레드는 편집기 화면·미디어 제어 호출 등으로 잠깐씩 막힐 수 있어서(PC가 바쁠 때 특히),
/// 같은 스레드에 훅을 두면 큐 지정 키의 '뗌'이나 '누름'을 놓칠 수 있음.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    /// <summary>
    /// (가상 키 코드, 눌림 여부, 키가 들어온 시각) → true 를 돌려주면 그 입력을 삼킴.
    /// 훅 스레드에서 불림 → 빨리 끝내고, 무거운 일은 다른 스레드로 넘겨야 함.
    /// </summary>
    private readonly Func<ushort, bool, DateTimeOffset, bool> _handler;

    // GC가 콜백 델리게이트를 수거하지 않도록 필드로 붙잡아 둠 (안 그러면 크래시)
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private Thread? _thread;
    private uint _threadId;

    public KeyboardHook(Func<ushort, bool, DateTimeOffset, bool> handler)
    {
        _handler = handler;
        _proc = Callback;
    }

    public void Install()
    {
        if (_thread is not null) return;

        using var ready = new ManualResetEventSlim();
        Exception? error = null;

        var thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                error = new Win32Exception(Marshal.GetLastWin32Error(), "키보드 훅 설치 실패");
            ready.Set();
            if (error is not null) return;

            // 저수준 훅 콜백은 이 스레드의 메시지 루프를 통해 불림
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "MacroPad 키보드 훅",
            Priority = ThreadPriority.Highest, // PC가 바빠도 제때 답하도록
        };

        thread.Start();
        ready.Wait();
        if (error is not null) throw error;
        _thread = thread;
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
                    if ((down || up) && _handler((ushort)data.vkCode, down, EventTime(data.time)))
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

    /// <summary>
    /// 키 입력에 찍힌 시각(부팅 후 ms) → 지금 시각 기준으로 환산.
    /// 콜백이 늦게 불려도 실제로 키가 눌린/떼진 순간으로 계산하려고.
    /// </summary>
    private static DateTimeOffset EventTime(uint tick)
    {
        var now = DateTimeOffset.Now;
        var ago = unchecked((uint)Environment.TickCount - tick);
        return ago <= 5000 ? now - TimeSpan.FromMilliseconds(ago) : now; // 이상한 값이면 지금 시각
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
