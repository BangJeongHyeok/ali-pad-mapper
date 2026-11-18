namespace MacroPad.Core.Keys;

/// <summary>
/// 키 하나에 대한 정보.
/// 같은 이름을 패드(HID 코드)와 PC 에이전트(Windows 가상 키 코드)가 같이 쓰기 위한 표.
/// </summary>
/// <param name="Name">대표 이름 (소문자)</param>
/// <param name="Hid">패드에 써 넣을 때 쓰는 HID Keyboard Usage 코드. 없으면 패드에서 못 씀.</param>
/// <param name="Vk">PC 에이전트가 SendInput / 훅에서 쓰는 Windows 가상 키 코드. 없으면 에이전트에서 못 씀.</param>
/// <param name="Extended">SendInput 시 KEYEVENTF_EXTENDEDKEY가 필요한 키인지</param>
/// <param name="Consumer">미디어 키일 때 HID Consumer 코드 (볼륨 등). 패드는 1바이트 코드만 받음.</param>
public sealed record KeyInfo(string Name, byte? Hid, ushort? Vk, bool Extended, ushort? Consumer = null)
{
    public bool IsMedia => Consumer is not null;
}

/// <summary>수식 키. 패드 펌웨어는 F1~F8 같은 전용 번호로 받음.</summary>
public enum ModifierKey
{
    Ctrl, Shift, Alt, Win,
    RCtrl, RShift, RAlt, RWin,
}

[Flags]
public enum ModifierFlags
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

public static class KeyTable
{
    private static readonly Dictionary<string, KeyInfo> Keys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ModifierKey> Modifiers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<ushort, KeyInfo> ByVk = new();

    static KeyTable()
    {
        BuildKeys();
        BuildModifiers();
    }

    public static bool TryGetKey(string name, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out KeyInfo key)
        => Keys.TryGetValue(name, out key);

    public static bool TryGetModifier(string name, out ModifierKey modifier) => Modifiers.TryGetValue(name, out modifier);

    /// <summary>Windows 가상 키 코드 → 키 (편집기에서 "키 눌러서 입력"용). 같은 VK를 쓰는 키가 여럿이면 먼저 등록된 것.</summary>
    public static bool TryGetByVk(ushort vk, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out KeyInfo key)
        => ByVk.TryGetValue(vk, out key);

    /// <summary>문서/도움말 출력용: 대표 이름 목록</summary>
    public static IEnumerable<KeyInfo> AllKeys => Keys.Values.Distinct();

    /// <summary>미디어 키 목록 (등록 순서)</summary>
    public static IEnumerable<KeyInfo> MediaKeys => AllKeys.Where(k => k.IsMedia);

    /// <summary>패드 펌웨어에서 쓰는 수식 키 번호 (vendor 프로그램 캡처 기준: Ctrl=F1, Shift=F2, Alt=F3, Win=F4, 오른쪽은 F5~F8)</summary>
    public static byte PadCode(ModifierKey m) => m switch
    {
        ModifierKey.Ctrl => 0xF1,
        ModifierKey.Shift => 0xF2,
        ModifierKey.Alt => 0xF3,
        ModifierKey.Win => 0xF4,
        ModifierKey.RCtrl => 0xF5,
        ModifierKey.RShift => 0xF6,
        ModifierKey.RAlt => 0xF7,
        ModifierKey.RWin => 0xF8,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>Windows 가상 키 코드</summary>
    public static ushort Vk(ModifierKey m) => m switch
    {
        ModifierKey.Ctrl => 0xA2,   // VK_LCONTROL
        ModifierKey.Shift => 0xA0,  // VK_LSHIFT
        ModifierKey.Alt => 0xA4,    // VK_LMENU
        ModifierKey.Win => 0x5B,    // VK_LWIN
        ModifierKey.RCtrl => 0xA3,
        ModifierKey.RShift => 0xA1,
        ModifierKey.RAlt => 0xA5,
        ModifierKey.RWin => 0x5C,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    public static bool IsExtended(ModifierKey m) =>
        m is ModifierKey.Win or ModifierKey.RWin or ModifierKey.RCtrl or ModifierKey.RAlt;

    /// <summary>왼쪽/오른쪽 구분 없이 묶은 플래그 (훅에서 수식 키 상태 비교용)</summary>
    public static ModifierFlags Flag(ModifierKey m) => m switch
    {
        ModifierKey.Ctrl or ModifierKey.RCtrl => ModifierFlags.Ctrl,
        ModifierKey.Shift or ModifierKey.RShift => ModifierFlags.Shift,
        ModifierKey.Alt or ModifierKey.RAlt => ModifierFlags.Alt,
        ModifierKey.Win or ModifierKey.RWin => ModifierFlags.Win,
        _ => ModifierFlags.None,
    };

    private static void Add(byte? hid, ushort? vk, bool extended, params string[] names)
    {
        var info = new KeyInfo(names[0], hid, vk, extended);
        foreach (var n in names) Keys[n] = info;
        if (vk is { } v) ByVk.TryAdd(v, info);
    }

    private static void AddMedia(ushort consumer, ushort? vk, params string[] names)
    {
        var info = new KeyInfo(names[0], null, vk, true, consumer);
        foreach (var n in names) Keys[n] = info;
        if (vk is { } v) ByVk.TryAdd(v, info);
    }

    private static void BuildKeys()
    {
        // 알파벳: HID 0x04~0x1D, VK 'A'~'Z'
        for (int i = 0; i < 26; i++)
            Add((byte)(0x04 + i), (ushort)('A' + i), false, ((char)('a' + i)).ToString());

        // 숫자 1~9, 0: HID 0x1E~0x27, VK '0'~'9'
        for (int i = 1; i <= 9; i++)
            Add((byte)(0x1D + i), (ushort)('0' + i), false, i.ToString());
        Add(0x27, '0', false, "0");

        Add(0x28, 0x0D, false, "enter", "return");
        Add(0x29, 0x1B, false, "esc", "escape");
        Add(0x2A, 0x08, false, "backspace");
        Add(0x2B, 0x09, false, "tab");
        Add(0x2C, 0x20, false, "space");
        Add(0x2D, 0xBD, false, "minus");
        Add(0x2E, 0xBB, false, "equal", "equals");
        Add(0x2F, 0xDB, false, "leftbracket", "lbracket");
        Add(0x30, 0xDD, false, "rightbracket", "rbracket");
        Add(0x31, 0xDC, false, "backslash");
        Add(0x33, 0xBA, false, "semicolon");
        Add(0x34, 0xDE, false, "quote", "apostrophe");
        Add(0x35, 0xC0, false, "grave", "backtick");
        Add(0x36, 0xBC, false, "comma");
        Add(0x37, 0xBE, false, "dot", "period");
        Add(0x38, 0xBF, false, "slash");
        Add(0x39, 0x14, false, "capslock");

        // F1~F12: HID 0x3A~0x45, VK 0x70~0x7B
        for (int i = 1; i <= 12; i++)
            Add((byte)(0x39 + i), (ushort)(0x6F + i), false, "f" + i);

        Add(0x46, 0x2C, true, "printscreen", "prtsc");
        Add(0x47, 0x91, false, "scrolllock");
        Add(0x48, 0x13, false, "pause");
        Add(0x49, 0x2D, true, "insert");
        Add(0x4A, 0x24, true, "home");
        Add(0x4B, 0x21, true, "pageup", "pgup");
        Add(0x4C, 0x2E, true, "delete", "del");
        Add(0x4D, 0x23, true, "end");
        Add(0x4E, 0x22, true, "pagedown", "pgdn");
        Add(0x4F, 0x27, true, "right");
        Add(0x50, 0x25, true, "left");
        Add(0x51, 0x28, true, "down");
        Add(0x52, 0x26, true, "up");
        Add(0x53, 0x90, true, "numlock");
        Add(0x54, 0x6F, true, "numpadslash");
        Add(0x55, 0x6A, false, "numpadasterisk");
        Add(0x56, 0x6D, false, "numpadminus");
        Add(0x57, 0x6B, false, "numpadplus");
        Add(0x58, 0x0D, true, "numpadenter");
        for (int i = 1; i <= 9; i++)
            Add((byte)(0x58 + i), (ushort)(0x60 + i), false, "numpad" + i);
        Add(0x62, 0x60, false, "numpad0");
        Add(0x63, 0x6E, false, "numpaddot");
        Add(0x65, 0x5D, true, "menu", "application", "apps");

        // F13~F24: HID 0x68~0x73, VK 0x7C~0x87
        // 일반 키보드에는 없는 키라서 "패드 → PC 에이전트" 신호용으로 쓰기 좋음
        for (int i = 13; i <= 24; i++)
            Add((byte)(0x68 + i - 13), (ushort)(0x7C + i - 13), false, "f" + i);

        // 한/영, 한자 (HID LANG1 / LANG2)
        Add(0x90, 0x15, false, "hangul", "hanyeong");
        Add(0x91, 0x19, false, "hanja");

        // 미디어 키: 패드는 Consumer 코드(1바이트)로, 에이전트는 VK로 보냄
        AddMedia(0xE9, 0xAF, "volumeup", "volup");
        AddMedia(0xEA, 0xAE, "volumedown", "voldown");
        AddMedia(0xE2, 0xAD, "mute");
        AddMedia(0xCD, 0xB3, "play", "playpause");
        AddMedia(0xB5, 0xB0, "next");
        AddMedia(0xB6, 0xB1, "prev", "previous");
        AddMedia(0xB7, 0xB2, "stop");
        AddMedia(0x6F, null, "brightnessup");
        AddMedia(0x70, null, "brightnessdown");
    }

    private static void BuildModifiers()
    {
        void M(ModifierKey m, params string[] names)
        {
            foreach (var n in names) Modifiers[n] = m;
        }

        M(ModifierKey.Ctrl, "ctrl", "control", "lctrl");
        M(ModifierKey.Shift, "shift", "lshift");
        M(ModifierKey.Alt, "alt", "lalt");
        M(ModifierKey.Win, "win", "lwin", "windows");
        M(ModifierKey.RCtrl, "rctrl");
        M(ModifierKey.RShift, "rshift");
        M(ModifierKey.RAlt, "ralt");
        M(ModifierKey.RWin, "rwin");
    }
}
