using MacroPad.Core.Keys;

namespace MacroPad.Core.Protocol;

/// <summary>패드의 한 슬롯(버튼/노브 동작)에 저장되는 동작.</summary>
public abstract record PadBinding
{
    /// <summary>
    /// 설정 파일의 문자열 하나를 패드용 동작으로 변환.
    /// null/빈 문자열이면 null (= 이 슬롯은 건드리지 않음).
    /// </summary>
    public static PadBinding? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var chords = Chord.ParseSequence(text);

        // 미디어 키는 단독으로만 가능 (펌웨어가 다른 키와 섞는 걸 지원 안 함)
        if (chords.Any(c => c.Key?.IsMedia == true))
        {
            if (chords.Count != 1 || chords[0].Modifiers.Count != 0)
                throw new FormatException($"'{text}': 미디어 키(볼륨/재생 등)는 다른 키와 같이 못 써. 단독으로 써줘.");

            var media = chords[0].Key!;
            if (media.Consumer > 0xFF)
                throw new FormatException($"'{text}': 이 패드는 1바이트 미디어 코드만 받아.");

            return new MediaBinding(media.Name, (byte)media.Consumer!.Value);
        }

        foreach (var c in chords)
        {
            if (c.Key is { Hid: null })
                throw new FormatException($"'{text}': '{c.Key.Name}' 키는 패드에 저장할 수 없어.");
        }

        return new KeySequenceBinding(chords);
    }
}

/// <summary>미디어 키 (볼륨 업/다운, 음소거, 재생/정지, 다음/이전 곡 등)</summary>
public sealed record MediaBinding(string Name, byte ConsumerCode) : PadBinding
{
    public override string ToString() => $"미디어 {Name} (0x{ConsumerCode:X2})";
}

/// <summary>키 입력 하나 또는 연속 입력 (단축키 포함)</summary>
public sealed record KeySequenceBinding(IReadOnlyList<Chord> Chords) : PadBinding
{
    public override string ToString() => "키 " + string.Join(", ", Chords);
}
