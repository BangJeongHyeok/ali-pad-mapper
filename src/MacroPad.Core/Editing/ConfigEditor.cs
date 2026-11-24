using MacroPad.Core.Config;
using MacroPad.Core.Keys;
using MacroPad.Core.Protocol;

namespace MacroPad.Core.Editing;

/// <summary>편집기에서 본 칸 하나의 상태.</summary>
public abstract record SlotAssignment;

/// <summary>비어 있음 = 패드에 쓸 때 건드리지 않음 (패드에 원래 있던 값 유지)</summary>
public sealed record EmptySlot : SlotAssignment
{
    public static readonly EmptySlot Instance = new();
}

/// <summary>패드에 직접 저장되는 동작 (미디어 키 / 키 / 단축키 / 연속 입력)</summary>
public sealed record PadKeysSlot(string Text, PadBinding Binding) : SlotAssignment
{
    public bool IsMedia => Binding is MediaBinding;
}

/// <summary>
/// PC 동작: 패드에는 트리거 키(F13~F24 등)를 저장하고, PadAgent 가 그 키를 받아 actions 를 실행.
/// </summary>
public sealed record PcActionSlot(string Trigger, ActionConfig Action) : SlotAssignment;

/// <summary>설정 파일에 잘못된 값이 적혀 있음 (직접 편집한 경우)</summary>
public sealed record InvalidSlot(string Text, string Error) : SlotAssignment;

/// <summary>
/// 설정(PadConfig)을 "칸 단위"로 편집하는 모델. GUI 편집기가 이걸 통해서만 설정을 바꿈.
///
/// PC 동작은 설정 파일에서 두 군데로 나뉘어 있음:
///   layers 의 칸 = "f13" (패드가 보낼 트리거 키)  +  actions["f13"] = { run: ... }
/// 이 클래스가 트리거 키를 자동으로 배정/회수해서, 사용자는 "이 칸 = 메모장 실행"처럼만 다루면 됨.
/// </summary>
public sealed class ConfigEditor
{
    public PadConfig Config { get; }

    /// <summary>마지막 저장 이후 바뀐 게 있는지</summary>
    public bool IsDirty { get; private set; }

    public ConfigEditor(PadConfig config)
    {
        Config = config;
    }

    public AdvancedSection Advanced => Config.Advanced;

    public ProtocolOptions Options => ProtocolOptions.From(Config.Advanced);

    public void MarkSaved() => IsDirty = false;

    // ─────────────────────────── 읽기 ───────────────────────────

    public SlotAssignment Get(SlotRef slot)
    {
        var text = Config.GetText(slot);
        if (string.IsNullOrWhiteSpace(text))
            return EmptySlot.Instance;

        if (FindAction(text) is { } found)
            return new PcActionSlot(found.Key, found.Value);

        try
        {
            var binding = PadBinding.Parse(text)!;
            // 패킷까지 만들어 봐서 길이 초과 등도 확인
            _ = K8850Protocol.BindingPacket(slot.Layer, slot.PadSlot(Options), binding, Options);
            return new PadKeysSlot(text.Trim(), binding);
        }
        catch (FormatException ex)
        {
            return new InvalidSlot(text.Trim(), ex.Message);
        }
    }

    /// <summary>패드에 쓸 바인딩 (비었거나 잘못된 칸이면 null)</summary>
    public PlannedBinding? Plan(SlotRef slot)
    {
        try
        {
            return UploadPlan.TryPlan(slot, Config.GetText(slot), Advanced, Options);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>패드에 아직 안 쓴 칸인지 (state 가 없으면 = 기록이 없으면, 비어 있지 않은 칸은 전부 true)</summary>
    public bool NeedsUpload(SlotRef slot, PadState? state) =>
        Plan(slot) is { } planned && (state is null || !state.Matches(planned));

    public IEnumerable<SlotRef> AllSlots() => SlotRef.All(Advanced);

    /// <summary>어느 칸에서도 쓰지 않는 actions 항목 (트리거 키 이름)</summary>
    public IReadOnlyList<string> UnreferencedActions()
    {
        var used = AllSlots().Select(s => TriggerId(Config.GetText(s))).Where(id => id is not null).ToHashSet();
        return Config.Actions.Keys.Where(k => !used.Contains(TriggerId(k))).ToList();
    }

    // ─────────────────────────── 쓰기 ───────────────────────────

    /// <summary>칸 비우기 (패드에 쓸 때 건드리지 않음)</summary>
    public void Clear(SlotRef slot) => Replace(slot, null);

    /// <summary>패드에 직접 저장할 동작 지정. 잘못된 값이면 FormatException (설정은 안 바뀜).</summary>
    public void SetPadKeys(SlotRef slot, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("입력할 키가 없습니다.");

        var binding = PadBinding.Parse(text)!;
        _ = K8850Protocol.BindingPacket(slot.Layer, slot.PadSlot(Options), binding, Options);

        if (FindAction(text) is not null)
            throw new FormatException($"'{text.Trim()}' 은(는) PC 동작의 트리거 키로 이미 쓰이고 있습니다. 다른 키를 고르거나 'PC 동작'으로 지정해 주십시오.");

        Replace(slot, text.Trim());
    }

    /// <summary>
    /// PC 동작 지정. 이 칸이 이미 자기 전용 트리거를 갖고 있으면 그대로 두고 동작만 바꿈
    /// (→ 패드에 다시 쓸 필요 없음). 아니면 빈 트리거 키를 새로 배정.
    /// 잘못된 동작이면 FormatException (설정은 안 바뀜).
    /// </summary>
    /// <returns>배정된 트리거 키 이름</returns>
    public string SetPcAction(SlotRef slot, ActionConfig action)
    {
        _ = action.ParseKeys(); // 검사만

        // 큐 지정 키는 누르고 있는 동안 다른 키를 받아야 해서 수식 키 없는 F13~F24 만 씀
        // (수식 키가 붙으면 패드가 연속 입력으로 보내서 바로 떼 버리고, 같이 누른 큐 키에도 수식 키가 섞임)
        var plainOnly = action.IsCueSet;

        if (Get(slot) is PcActionSlot existing && CountSlotsUsing(existing.Trigger) == 1
            && (!plainOnly || IsPlainTrigger(existing.Trigger)))
        {
            Config.Actions[existing.Trigger] = action;
            IsDirty = true;
            return existing.Trigger;
        }

        var trigger = AllocateTrigger(plainOnly);
        Config.Actions[trigger] = action;
        Replace(slot, trigger);
        return trigger;
    }

    /// <summary>칸의 문자열을 바꾸고, 그 칸이 쓰던 트리거가 더 이상 안 쓰이면 actions 에서도 지움.</summary>
    private void Replace(SlotRef slot, string? text)
    {
        var before = Config.GetText(slot);
        if (string.Equals(before?.Trim(), text?.Trim(), StringComparison.Ordinal))
            return;

        var oldAction = before is null ? null : FindAction(before);
        Config.SetText(slot, text);
        IsDirty = true;

        if (oldAction is { } old && CountSlotsUsing(old.Key) == 0)
            Config.Actions.Remove(old.Key);
    }

    // ─────────────────────────── 트리거 키 ───────────────────────────

    /// <summary>
    /// 자동 배정 순서: f13~f24 → shift+ → ctrl+ → ctrl+shift+ (각 12개, 총 48개).
    /// alt/win 조합은 안 씀: 패드가 alt/win 을 눌렀다 떼면 메뉴 막대/시작 메뉴가 열릴 수 있음.
    /// </summary>
    public static IEnumerable<string> TriggerCandidates()
    {
        string[] prefixes = ["", "shift+", "ctrl+", "ctrl+shift+"];
        foreach (var p in prefixes)
            for (int f = 13; f <= 24; f++)
                yield return $"{p}f{f}";
    }

    private string AllocateTrigger(bool plainOnly = false)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in Config.Actions.Keys)
            if (TriggerId(k) is { } id) used.Add(id);
        foreach (var s in AllSlots())
            if (TriggerId(Config.GetText(s)) is { } id) used.Add(id);

        foreach (var candidate in TriggerCandidates())
            if ((!plainOnly || IsPlainTrigger(candidate)) && !used.Contains(TriggerId(candidate)!))
                return candidate;

        throw new FormatException(plainOnly
            ? "큐 지정 키에 쓸 F13~F24 가 다 쓰였습니다. 다른 PC 동작 하나를 지우고 다시 해 주십시오."
            : "자동으로 배정할 트리거 키가 다 떨어졌습니다. (최대 48개)");
    }

    /// <summary>수식 키 없는 키 하나인지 ("f13" 은 true, "shift+f13" 은 false)</summary>
    public static bool IsPlainTrigger(string trigger) => TriggerId(trigger) is { } id && id.StartsWith("0:", StringComparison.Ordinal);

    private int CountSlotsUsing(string trigger)
    {
        var id = TriggerId(trigger);
        return id is null ? 0 : AllSlots().Count(s => TriggerId(Config.GetText(s)) == id);
    }

    private KeyValuePair<string, ActionConfig>? FindAction(string slotText)
    {
        var id = TriggerId(slotText);
        if (id is null) return null;
        foreach (var kv in Config.Actions)
        {
            if (kv.Value is not null && TriggerId(kv.Key) == id)
                return kv;
        }
        return null;
    }

    /// <summary>
    /// 트리거 비교용 정규화: 키 조합 하나면 "수식키플래그:키이름", 아니면 null.
    /// "Shift+F13" 과 "shift+f13", "lshift+f13" 이 같은 값이 됨 (PadAgent 도 왼/오 구분 없이 비교).
    /// </summary>
    public static string? TriggerId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains(',')) return null;
        try
        {
            var chord = Chord.Parse(text);
            return chord.Key is null ? null : $"{(int)chord.ModifierFlags}:{chord.Key.Name}";
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
