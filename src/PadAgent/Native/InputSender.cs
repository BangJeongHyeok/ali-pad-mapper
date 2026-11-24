using System.ComponentModel;
using System.Runtime.InteropServices;
using MacroPad.Core.Keys;

namespace PadAgent.Native;

/// <summary>SendInput 으로 키 입력을 흉내 냄.</summary>
internal static class InputSender
{
    /// <summary>우리가 보낸 입력이라는 표시 ('MPAD'). 훅에서 이 값이면 무시.</summary>
    public static readonly IntPtr Signature = new(0x4D504144);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint MAPVK_VK_TO_VSC = 0;

    /// <summary>지금 눌려 있는 수식 키 (왼/오 구분 없음)</summary>
    public static ModifierFlags CurrentModifiers()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        var flags = ModifierFlags.None;
        if (Down(0x11)) flags |= ModifierFlags.Ctrl;   // VK_CONTROL
        if (Down(0x10)) flags |= ModifierFlags.Shift;  // VK_SHIFT
        if (Down(0x12)) flags |= ModifierFlags.Alt;    // VK_MENU
        if (Down(0x5B) || Down(0x5C)) flags |= ModifierFlags.Win;
        return flags;
    }

    /// <summary>"ctrl+shift+esc" 같은 조합 하나: 수식 키 누름 → 키 누름/뗌 → 수식 키 뗌</summary>
    public static void SendChord(Chord chord)
    {
        var inputs = new List<INPUT>();

        foreach (var m in chord.Modifiers)
            inputs.Add(Key(KeyTable.Vk(m), KeyTable.IsExtended(m), up: false));

        if (chord.Key is { Vk: { } vk } key)
        {
            inputs.Add(Key(vk, key.Extended, up: false));
            inputs.Add(Key(vk, key.Extended, up: true));
        }

        for (int i = chord.Modifiers.Count - 1; i >= 0; i--)
        {
            var m = chord.Modifiers[i];
            inputs.Add(Key(KeyTable.Vk(m), KeyTable.IsExtended(m), up: true));
        }

        Send(inputs);
    }

    /// <summary>문자열 입력 (한글 포함, IME 상태와 무관하게 유니코드로 입력)</summary>
    public static void SendText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n')
            {
                inputs.Add(Key(0x0D, false, up: false));
                inputs.Add(Key(0x0D, false, up: true));
                continue;
            }
            inputs.Add(Unicode(ch, up: false));
            inputs.Add(Unicode(ch, up: true));
        }
        Send(inputs);
    }

    private static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        var arr = inputs.ToArray();
        var sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        if (sent != arr.Length)
        {
            // 관리자 권한으로 실행 중인 창에는 일반 권한 앱이 입력을 못 보냄 (Windows 보안 정책)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "키 입력 전송 실패 (관리자 권한 창이 앞에 있으면 막힐 수 있습니다)");
        }
    }

    private static INPUT Key(ushort vk, bool extended, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0),
                dwExtraInfo = Signature,
            },
        },
    };

    private static INPUT Unicode(char ch, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                dwExtraInfo = Signature,
            },
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // 공용체 크기를 Windows와 맞추려면 가장 큰 MOUSEINPUT 도 넣어야 함
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
