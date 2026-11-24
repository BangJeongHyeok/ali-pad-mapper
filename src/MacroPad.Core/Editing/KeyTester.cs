using MacroPad.Core.Keys;
using MacroPad.Core.Media;
using MacroPad.Core.Protocol;

namespace MacroPad.Core.Editing;

/// <summary>키 테스트 기록 한 줄.</summary>
/// <param name="Down">누름이면 true, 뗌이면 false</param>
/// <param name="Key">PC가 받은 키 ("F20", "Shift + F13", "볼륨 +")</param>
/// <param name="Slots">이 키가 들어 있는 칸 (없으면 빈 목록 = 설정에 없는 키)</param>
/// <param name="Label">칸에 든 동작 ("큐 3", "볼륨 +")</param>
/// <param name="SincePreviousMs">바로 앞 기록과의 간격 (첫 기록이면 null)</param>
/// <param name="HeldMs">뗌일 때 누르고 있던 시간</param>
/// <param name="Result">샘플 큐 판단 ("저장", "이동" 등). 큐 키가 아니면 null</param>
/// <param name="Hint">이상해 보이는 점 (예: 큐 지정 키가 큐 키 직전에 떼짐)</param>
public sealed record KeyTestEvent(
    DateTimeOffset At,
    bool Down,
    string Key,
    IReadOnlyList<SlotRef> Slots,
    string Label,
    double? SincePreviousMs,
    double? HeldMs,
    string? Result,
    string? Hint);

/// <summary>
/// 편집기의 '키 테스트': 패드에서 들어온 키가 어느 칸인지, 언제 눌리고 떼졌는지, 샘플 큐라면
/// 실제로 저장/이동 중 무엇으로 판단되는지를 보여줌. 테스트 중에는 PC 동작을 실행하지 않음 (PadAgent 가 막음).
///
/// 샘플 큐 판단은 실제 동작과 같은 CueBank 를 따로 하나 둬서 흉내 냄 (재생 위치는 건드리지 않음).
/// 키보드 훅에서 부름 (한 스레드). 시간은 바깥에서 넣어 줌 (테스트 가능하게).
/// </summary>
public sealed class KeyTester
{
    /// <summary>큐 지정 키를 뗀 뒤 이 안에 큐 키가 오면 "같이 누르려다 먼저 떼진 것" 같다고 알려줌</summary>
    public static readonly TimeSpan ReleaseBeforeCueHint = TimeSpan.FromMilliseconds(300);

    private readonly ConfigEditor _editor;
    private readonly Dictionary<(ushort Vk, ModifierFlags Mods), List<SlotRef>> _bySignal = new();
    private readonly Dictionary<ushort, (Entry Entry, DateTimeOffset At)> _held = new();
    private readonly CueBank _cues = new();
    private DateTimeOffset? _lastEventAt;
    private DateTimeOffset? _lastSetUpAt;

    private sealed record Entry(string Key, IReadOnlyList<SlotRef> Slots, string Label, bool IsCueSet, int? Cue);

    public KeyTester(ConfigEditor editor)
    {
        _editor = editor;
        foreach (var slot in editor.AllSlots())
        {
            if (Signal(editor.Config.GetText(slot)) is not { } signal) continue;
            if (!_bySignal.TryGetValue(signal, out var list)) _bySignal[signal] = list = new();
            list.Add(slot);
        }
    }

    /// <summary>
    /// 키 하나 처리. 기록할 게 없으면 null (자동 반복, 수식 키, 설정과 상관없는 키).
    /// 일반 키보드 입력을 화면에 보이지 않도록 설정에 있는 키와 F13~F24 만 기록함.
    /// </summary>
    public KeyTestEvent? Handle(ushort vk, ModifierFlags mods, bool down, DateTimeOffset at)
    {
        if (IsModifierVk(vk)) return null;

        if (!down)
        {
            if (!_held.Remove(vk, out var held)) return null;
            var e = held.Entry;
            string? result = null;
            if (e.IsCueSet)
            {
                _cues.SetKeyUp(at);
                _lastSetUpAt = at;
                if (_cues.IsArmed(at)) result = "짧게 톡 → 3초 안의 큐 키 한 번 저장";
            }
            return Record(at, false, e, held: at - held.At, result, hint: null);
        }

        if (_held.ContainsKey(vk)) return null; // 자동 반복

        if (!Find(vk, mods, out var entry)) return null;
        _held[vk] = (entry, at);

        string? cueResult = null, hint = null;
        if (entry.IsCueSet)
        {
            _cues.SetKeyDown(at);
        }
        else if (entry.Cue is { } cue)
        {
            var holding = _cues.IsSetHeld;
            var mode = _cues.Press(at, cue);
            if (mode != CuePress.Jump) _cues.Store(cue, new CuePoint(TimeSpan.Zero, "", ""));
            cueResult = mode switch
            {
                CuePress.Set when holding => "저장 (큐 지정 키 누르는 중)",
                CuePress.Set => "저장 (톡 다음 한 번)",
                CuePress.SetEmpty => "빈 큐라 저장",
                _ => "이동",
            };

            if (mode != CuePress.Set && _lastSetUpAt is { } up && at - up >= TimeSpan.Zero && at - up <= ReleaseBeforeCueHint)
                hint = $"큐 지정 키가 이 키보다 {(at - up).TotalMilliseconds:0}ms 먼저 떼졌어요. 패드가 두 키를 같이 보내지 못하는 것 같아요.";
        }

        return Record(at, true, entry, held: null, cueResult, hint);
    }

    private KeyTestEvent Record(DateTimeOffset at, bool down, Entry e, TimeSpan? held, string? result, string? hint)
    {
        double? since = _lastEventAt is { } last ? Math.Max(0, (at - last).TotalMilliseconds) : null;
        _lastEventAt = at;
        return new KeyTestEvent(at, down, e.Key, e.Slots, e.Label, since, held?.TotalMilliseconds, result, hint);
    }

    private bool Find(ushort vk, ModifierFlags mods, out Entry entry)
    {
        var key = KeyTable.TryGetByVk(vk, out var info)
            ? KeyNames.Chord(new Chord(ModifierList(mods), info))
            : $"VK {vk:X2}";

        if (_bySignal.TryGetValue((vk, mods), out var slots))
        {
            var first = _editor.Get(slots[0]);
            var action = first is PcActionSlot pc ? pc.Action : null;
            entry = new Entry(key, slots, SlotDisplay.Short(first, 30), action?.IsCueSet == true, action?.Cue);
            return true;
        }

        // 설정에는 없지만 패드 신호 키로 쓰는 F13~F24 (패드에 옛 설정이 남아 있는 경우 등)
        if (vk is >= 0x7C and <= 0x87)
        {
            entry = new Entry(key, Array.Empty<SlotRef>(), "설정에 없는 키", false, null);
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>칸 문자열 → 패드가 보내는 (가상 키, 수식 키). 키 조합 하나가 아니면(연속 입력 등) null</summary>
    private static (ushort, ModifierFlags)? Signal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains(',')) return null;
        try
        {
            var chord = Chord.Parse(text);
            return chord.Key is { Vk: { } vk } ? (vk, chord.ModifierFlags) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IReadOnlyList<ModifierKey> ModifierList(ModifierFlags mods)
    {
        var list = new List<ModifierKey>();
        if (mods.HasFlag(ModifierFlags.Ctrl)) list.Add(ModifierKey.Ctrl);
        if (mods.HasFlag(ModifierFlags.Shift)) list.Add(ModifierKey.Shift);
        if (mods.HasFlag(ModifierFlags.Alt)) list.Add(ModifierKey.Alt);
        if (mods.HasFlag(ModifierFlags.Win)) list.Add(ModifierKey.Win);
        return list;
    }

    private static bool IsModifierVk(ushort vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);
}
