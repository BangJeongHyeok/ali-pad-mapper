using MacroPad.Core.Config;

namespace MacroPad.Core.Protocol;

/// <summary>패드에 쓸 바인딩 하나 (사람이 읽을 설명 포함).</summary>
public sealed record PlannedBinding(int LayerIndex, int Slot, string Control, string Source, PadBinding Binding)
{
    public override string ToString() => $"레이어{LayerIndex + 1} {Control} (슬롯 {Slot}): \"{Source}\" → {Binding}";
}

/// <summary>설정 파일 → 패드에 보낼 바인딩 목록 / 패킷 목록.</summary>
public sealed class UploadPlan
{
    public required IReadOnlyList<PlannedBinding> Bindings { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required ProtocolOptions Options { get; init; }
    public bool SendInitPacket { get; init; }

    public bool IsValid => Errors.Count == 0;

    public static UploadPlan Create(PadConfig config)
    {
        var adv = config.Advanced;
        var options = ProtocolOptions.From(adv);
        var bindings = new List<PlannedBinding>();
        var errors = new List<string>();

        if (config.Layers.Count > adv.LayerCount)
            errors.Add($"레이어는 최대 {adv.LayerCount}개인데 {config.Layers.Count}개가 있어.");

        for (int layer = 0; layer < Math.Min(config.Layers.Count, adv.LayerCount); layer++)
        {
            var lc = config.Layers[layer];
            if (lc is null) continue;

            var buttons = lc.Buttons ?? new();
            if (buttons.Count > adv.ButtonCount)
                errors.Add($"레이어{layer + 1}: 버튼은 {adv.ButtonCount}개인데 {buttons.Count}개가 적혀 있어.");

            for (int i = 0; i < Math.Min(buttons.Count, adv.ButtonCount); i++)
            {
                var cols = Math.Max(1, adv.ButtonColumns);
                var name = $"버튼{i + 1}({i / cols + 1}행 {i % cols + 1}열)";
                TryAdd(layer, K8850Protocol.ButtonSlot(i), name, buttons[i]);
            }

            var knobs = lc.Knobs ?? new();
            if (knobs.Count > adv.KnobCount)
                errors.Add($"레이어{layer + 1}: 노브는 {adv.KnobCount}개인데 {knobs.Count}개가 적혀 있어.");

            for (int k = 0; k < Math.Min(knobs.Count, adv.KnobCount); k++)
            {
                var knob = knobs[k];
                if (knob is null) continue;
                var knobName = k switch { 0 => "위 노브", 1 => "아래 노브", _ => $"노브{k + 1}" };
                TryAdd(layer, K8850Protocol.KnobSlot(k, KnobAction.Ccw, options), knobName + " 왼쪽", knob.Ccw);
                TryAdd(layer, K8850Protocol.KnobSlot(k, KnobAction.Press, options), knobName + " 누름", knob.Press);
                TryAdd(layer, K8850Protocol.KnobSlot(k, KnobAction.Cw, options), knobName + " 오른쪽", knob.Cw);
            }
        }

        return new UploadPlan
        {
            Bindings = bindings,
            Errors = errors,
            Options = options,
            SendInitPacket = adv.SendInitPacket,
        };

        void TryAdd(int layer, int slot, string control, string? source)
        {
            try
            {
                var binding = PadBinding.Parse(source);
                if (binding is null) return; // 비워둔 칸은 건드리지 않음
                // 패킷 생성까지 미리 해봐서 길이 초과 같은 문제를 여기서 잡음
                _ = K8850Protocol.BindingPacket(layer, slot, binding, options);
                bindings.Add(new PlannedBinding(layer, slot, control, source!.Trim(), binding));
            }
            catch (FormatException ex)
            {
                errors.Add($"레이어{layer + 1} {control}: {ex.Message}");
            }
        }
    }

    /// <summary>실제로 보낼 64바이트 패킷들 (설명 포함).</summary>
    public IEnumerable<(string Description, byte[] Packet)> Packets()
    {
        if (SendInitPacket)
            yield return ("초기화", K8850Protocol.InitPacket());

        foreach (var b in Bindings)
        {
            yield return (b.ToString(), K8850Protocol.BindingPacket(b.LayerIndex, b.Slot, b.Binding, Options));
            yield return ("  확정", K8850Protocol.FinalizePacket());
        }
    }
}
