using MacroPad.Core.Config;

namespace MacroPad.Core.Protocol;

public enum SlotControl
{
    Button,
    Knob,
}

/// <summary>
/// 설정 파일 안의 칸 하나 (레이어 + 버튼 번호, 또는 레이어 + 노브 번호 + 동작).
/// 모든 번호는 0부터.
/// </summary>
public readonly record struct SlotRef(int Layer, SlotControl Control, int Index, KnobAction KnobAction = KnobAction.Ccw)
{
    public static SlotRef Button(int layer, int index) => new(layer, SlotControl.Button, index);

    public static SlotRef Knob(int layer, int knob, KnobAction action) => new(layer, SlotControl.Knob, knob, action);

    public bool IsButton => Control == SlotControl.Button;

    /// <summary>패드 펌웨어의 슬롯 번호</summary>
    public int PadSlot(ProtocolOptions options) => IsButton
        ? K8850Protocol.ButtonSlot(Index)
        : K8850Protocol.KnobSlot(Index, KnobAction, options);

    /// <summary>같은 칸의 다른 레이어</summary>
    public SlotRef WithLayer(int layer) => this with { Layer = layer };

    public static string KnobName(int knob) => knob switch
    {
        0 => "위 노브",
        1 => "아래 노브",
        _ => $"노브{knob + 1}",
    };

    public static string KnobActionName(KnobAction action) => action switch
    {
        KnobAction.Ccw => "왼쪽",
        KnobAction.Press => "누름",
        KnobAction.Cw => "오른쪽",
        _ => action.ToString(),
    };

    /// <summary>레이어 없이 칸 이름만. 예: "버튼3(1행 3열)", "위 노브 왼쪽"</summary>
    public string ControlName(int buttonColumns = 4)
    {
        if (!IsButton)
            return $"{KnobName(Index)} {KnobActionName(KnobAction)}";
        var cols = Math.Max(1, buttonColumns);
        return $"버튼{Index + 1}({Index / cols + 1}행 {Index % cols + 1}열)";
    }

    public string Describe(int buttonColumns = 4) => $"레이어{Layer + 1} {ControlName(buttonColumns)}";

    /// <summary>한 레이어의 모든 칸 (버튼 → 노브 순서)</summary>
    public static IEnumerable<SlotRef> AllInLayer(int layer, AdvancedSection adv)
    {
        for (int i = 0; i < adv.ButtonCount; i++)
            yield return Button(layer, i);
        for (int k = 0; k < adv.KnobCount; k++)
        {
            yield return Knob(layer, k, KnobAction.Ccw);
            yield return Knob(layer, k, KnobAction.Press);
            yield return Knob(layer, k, KnobAction.Cw);
        }
    }

    /// <summary>모든 레이어의 모든 칸</summary>
    public static IEnumerable<SlotRef> All(AdvancedSection adv) =>
        Enumerable.Range(0, Math.Max(0, adv.LayerCount)).SelectMany(l => AllInLayer(l, adv));
}

/// <summary>PadConfig 안의 칸 문자열 읽기/쓰기.</summary>
public static class PadConfigSlots
{
    public static string? GetText(this PadConfig config, SlotRef slot)
    {
        if (slot.Layer < 0 || slot.Layer >= config.Layers.Count) return null;
        var layer = config.Layers[slot.Layer];
        if (layer is null) return null;

        if (slot.IsButton)
        {
            var buttons = layer.Buttons;
            return buttons is not null && slot.Index >= 0 && slot.Index < buttons.Count ? buttons[slot.Index] : null;
        }

        var knobs = layer.Knobs;
        if (knobs is null || slot.Index < 0 || slot.Index >= knobs.Count) return null;
        var knob = knobs[slot.Index];
        return knob is null ? null : slot.KnobAction switch
        {
            KnobAction.Ccw => knob.Ccw,
            KnobAction.Press => knob.Press,
            KnobAction.Cw => knob.Cw,
            _ => null,
        };
    }

    /// <summary>칸에 문자열을 넣음. null/빈 문자열이면 비움(= 패드에 쓸 때 건드리지 않음). 목록은 필요한 만큼 늘어나고, 끝의 빈 칸은 정리됨.</summary>
    public static void SetText(this PadConfig config, SlotRef slot, string? text)
    {
        if (slot.Layer < 0 || slot.Index < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));

        var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        if (value is null && config.GetText(slot) is null)
            return;

        while (config.Layers.Count <= slot.Layer)
            config.Layers.Add(new LayerConfig());
        var layer = config.Layers[slot.Layer] ??= new LayerConfig();

        if (slot.IsButton)
        {
            var buttons = layer.Buttons ??= new();
            while (buttons.Count <= slot.Index) buttons.Add(null);
            buttons[slot.Index] = value;
            while (buttons.Count > 0 && buttons[^1] is null) buttons.RemoveAt(buttons.Count - 1);
        }
        else
        {
            var knobs = layer.Knobs ??= new();
            while (knobs.Count <= slot.Index) knobs.Add(null);
            var knob = knobs[slot.Index] ??= new KnobConfig();
            switch (slot.KnobAction)
            {
                case KnobAction.Ccw: knob.Ccw = value; break;
                case KnobAction.Press: knob.Press = value; break;
                case KnobAction.Cw: knob.Cw = value; break;
            }
            while (knobs.Count > 0 && IsEmpty(knobs[^1])) knobs.RemoveAt(knobs.Count - 1);
        }

        // 끝쪽의 완전히 빈 레이어 정리
        while (config.Layers.Count > 0 && IsEmpty(config.Layers[^1]))
            config.Layers.RemoveAt(config.Layers.Count - 1);
    }

    private static bool IsEmpty(KnobConfig? k) =>
        k is null || (string.IsNullOrWhiteSpace(k.Ccw) && string.IsNullOrWhiteSpace(k.Press) && string.IsNullOrWhiteSpace(k.Cw));

    private static bool IsEmpty(LayerConfig? l) =>
        l is null
        || ((l.Buttons is null || l.Buttons.All(string.IsNullOrWhiteSpace))
            && (l.Knobs is null || l.Knobs.All(IsEmpty)));
}
