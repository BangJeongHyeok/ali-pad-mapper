namespace MacroPad.Core.Keys;

/// <summary>
/// 동시에 누르는 키 묶음 하나. 예: "ctrl+shift+esc" → 수식키 [Ctrl, Shift] + 키 esc
/// 키 없이 수식 키만 있는 것도 허용 (예: "ctrl").
/// </summary>
public sealed record Chord(IReadOnlyList<ModifierKey> Modifiers, KeyInfo? Key)
{
    public ModifierFlags ModifierFlags =>
        Modifiers.Aggregate(ModifierFlags.None, (acc, m) => acc | KeyTable.Flag(m));

    public override string ToString()
    {
        var parts = Modifiers.Select(m => m.ToString().ToLowerInvariant()).ToList();
        if (Key is not null) parts.Add(Key.Name);
        return string.Join("+", parts);
    }

    /// <summary>
    /// "ctrl+shift+esc" 형식의 문자열을 해석.
    /// 구분자는 '+' (그래서 '+' 키 자체는 numpadplus / equal 등 이름으로 씀).
    /// </summary>
    public static Chord Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("빈 키 조합이야.");

        var modifiers = new List<ModifierKey>();
        KeyInfo? key = null;

        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0)
                throw new FormatException($"'{text}': '+' 사이에 빈 칸이 있어. ('+' 키는 numpadplus 또는 shift+equal 로 써줘)");

            if (KeyTable.TryGetModifier(part, out var mod))
            {
                if (!modifiers.Contains(mod)) modifiers.Add(mod);
                continue;
            }

            if (!KeyTable.TryGetKey(part, out var k))
                throw new FormatException($"'{text}': '{part}' 라는 키 이름을 모르겠어.");

            if (key is not null)
                throw new FormatException($"'{text}': 수식 키가 아닌 키가 두 개 이상이야 ('{key.Name}', '{k.Name}'). 연속 입력은 쉼표로 구분해줘.");

            key = k;
        }

        return new Chord(modifiers, key);
    }

    /// <summary>"ctrl+c, ctrl+v" 처럼 쉼표로 구분된 연속 입력을 해석.</summary>
    public static IReadOnlyList<Chord> ParseSequence(string text)
    {
        var chords = text.Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Select(Parse)
            .ToList();

        if (chords.Count == 0)
            throw new FormatException("빈 키 입력이야.");

        return chords;
    }
}
