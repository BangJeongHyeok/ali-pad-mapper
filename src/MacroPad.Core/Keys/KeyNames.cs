namespace MacroPad.Core.Keys;

/// <summary>화면 표시용 이름 (편집기 버튼 글자, 목록 등).</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, string> MediaLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["volumeup"] = "볼륨 +",
        ["volumedown"] = "볼륨 -",
        ["mute"] = "음소거",
        ["play"] = "재생/정지",
        ["next"] = "다음 곡",
        ["prev"] = "이전 곡",
        ["stop"] = "정지",
        ["brightnessup"] = "밝기 +",
        ["brightnessdown"] = "밝기 -",
    };

    private static readonly Dictionary<string, string> KeyLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["esc"] = "Esc",
        ["backspace"] = "Backspace",
        ["pageup"] = "PgUp",
        ["pagedown"] = "PgDn",
        ["printscreen"] = "PrtSc",
        ["delete"] = "Del",
        ["insert"] = "Ins",
        ["capslock"] = "CapsLock",
        ["scrolllock"] = "ScrollLock",
        ["numlock"] = "NumLock",
        ["minus"] = "-",
        ["equal"] = "=",
        ["leftbracket"] = "[",
        ["rightbracket"] = "]",
        ["backslash"] = "\\",
        ["semicolon"] = ";",
        ["quote"] = "'",
        ["grave"] = "`",
        ["comma"] = ",",
        ["dot"] = ".",
        ["slash"] = "/",
        ["numpadplus"] = "Num+",
        ["numpadminus"] = "Num-",
        ["numpadasterisk"] = "Num*",
        ["numpadslash"] = "Num/",
        ["numpaddot"] = "Num.",
        ["numpadenter"] = "NumEnter",
        ["hangul"] = "한/영",
        ["hanja"] = "한자",
        ["menu"] = "Menu",
    };

    /// <summary>미디어 키 이름 → "볼륨 +" 같은 한글 이름. 미디어 키가 아니면 원래 이름.</summary>
    public static string Media(string name) => MediaLabels.TryGetValue(name, out var label) ? label : name;

    /// <summary>키 하나의 표시 이름. 예: "f13" → "F13", "pageup" → "PgUp", "a" → "A"</summary>
    public static string Key(KeyInfo key)
    {
        if (key.IsMedia) return Media(key.Name);
        if (KeyLabels.TryGetValue(key.Name, out var label)) return label;
        if (key.Name.StartsWith("numpad", StringComparison.Ordinal) && key.Name.Length == 7)
            return "Num" + key.Name[6..];
        return char.ToUpperInvariant(key.Name[0]) + key.Name[1..];
    }

    public static string Modifier(ModifierKey m) => m switch
    {
        ModifierKey.Ctrl => "Ctrl",
        ModifierKey.Shift => "Shift",
        ModifierKey.Alt => "Alt",
        ModifierKey.Win => "Win",
        ModifierKey.RCtrl => "RCtrl",
        ModifierKey.RShift => "RShift",
        ModifierKey.RAlt => "RAlt",
        ModifierKey.RWin => "RWin",
        _ => m.ToString(),
    };

    /// <summary>"ctrl+shift+esc" → "Ctrl+Shift+Esc"</summary>
    public static string Chord(Chord chord)
    {
        var parts = chord.Modifiers.Select(Modifier).ToList();
        if (chord.Key is not null) parts.Add(Key(chord.Key));
        return string.Join("+", parts);
    }

    /// <summary>"ctrl+a, ctrl+c" → "Ctrl+A, Ctrl+C"</summary>
    public static string Sequence(IEnumerable<Chord> chords) => string.Join(", ", chords.Select(Chord));
}
