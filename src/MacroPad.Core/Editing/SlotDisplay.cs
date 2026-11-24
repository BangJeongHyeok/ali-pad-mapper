using MacroPad.Core.Config;
using MacroPad.Core.Keys;
using MacroPad.Core.Media;
using MacroPad.Core.Protocol;

namespace MacroPad.Core.Editing;

/// <summary>편집기 화면에 보일 칸 설명 글자.</summary>
public static class SlotDisplay
{
    /// <summary>버튼 위에 쓸 짧은 글자. 비어 있으면 빈 문자열.</summary>
    public static string Short(SlotAssignment assignment, int maxLength = 24) => Truncate(assignment switch
    {
        EmptySlot => "",
        PadKeysSlot { Binding: MediaBinding m } => KeyNames.Media(m.Name),
        PadKeysSlot { Binding: KeySequenceBinding k } => KeyNames.Sequence(k.Chords),
        PadKeysSlot p => p.Text,
        PcActionSlot pc => Action(pc.Action),
        InvalidSlot bad => "[오류] " + bad.Text,
        _ => "",
    }, maxLength);

    /// <summary>PC 동작 요약. 예: "[실행] notepad", "[단축키] Win+Shift+S", "[입력] 확인했습니다…"</summary>
    public static string Action(ActionConfig action)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(action.Run))
            parts.Add("[실행] " + RunName(action.Run));

        var keys = (action.Keys ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        if (keys.Count > 0)
        {
            string keyText;
            try
            {
                keyText = KeyNames.Sequence(keys.SelectMany(Chord.ParseSequence));
            }
            catch (FormatException)
            {
                keyText = string.Join(", ", keys);
            }
            parts.Add("[단축키] " + keyText);
        }

        if (!string.IsNullOrEmpty(action.Text))
            parts.Add("[입력] " + action.Text.ReplaceLineEndings(" "));

        if (action.Seek is { } seek)
            parts.Add("[이동] " + SeekTracker.Describe(seek));

        if (action.Cue is { } cue)
            parts.Add($"[큐] {cue}");

        if (action.IsCueSet)
            parts.Add("[큐 지정 키]");

        return parts.Count == 0 ? "[PC 동작]" : string.Join(" ", parts);
    }

    /// <summary>실행 대상의 짧은 이름: URL 이면 호스트, 경로면 파일/폴더 이름 (확장자 제외)</summary>
    public static string RunName(string run)
    {
        var s = run.Trim().Trim('"');
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.Host;

        // 윈도우 경로 구분자를 리눅스(테스트)에서도 똑같이 처리
        s = s.TrimEnd('\\', '/');
        var cut = s.LastIndexOfAny(['\\', '/']);
        var name = cut >= 0 ? s[(cut + 1)..] : s;
        var dot = name.LastIndexOf('.');
        if (dot > 0 && name.Length - dot <= 5) name = name[..dot];
        return name.Length == 0 ? s : name;
    }

    /// <summary>칸 상태 설명 (편집기 아래쪽 안내용)</summary>
    public static string Kind(SlotAssignment assignment) => assignment switch
    {
        EmptySlot => "비어 있음: 패드에 쓸 때 이 칸은 건드리지 않아 (패드에 원래 있던 값 유지).",
        PadKeysSlot { IsMedia: true } => "미디어 키: 패드에 저장돼서 PC 프로그램 없이도 동작해.",
        PadKeysSlot => "키 입력: 패드에 저장돼서 PC 프로그램 없이도 동작해.",
        PcActionSlot pc => $"PC 동작: 패드는 '{pc.Trigger}' 키를 보내고, PadAgent 가 실행 중일 때 동작해.",
        InvalidSlot bad => "설정 파일 값이 잘못됨: " + bad.Error,
        _ => "",
    };

    private static string Truncate(string s, int max) =>
        max <= 1 || s.Length <= max ? s : s[..(max - 1)] + "…";
}
