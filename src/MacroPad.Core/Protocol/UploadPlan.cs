using MacroPad.Core.Config;

namespace MacroPad.Core.Protocol;

/// <summary>패드에 쓸 바인딩 하나 (사람이 읽을 설명과 실제 패킷 포함).</summary>
public sealed record PlannedBinding(SlotRef Ref, int Slot, string Control, string Source, PadBinding Binding, byte[] Packet)
{
    public int LayerIndex => Ref.Layer;

    public override string ToString() => $"레이어{LayerIndex + 1} {Control} (슬롯 {Slot}): \"{Source}\" → {Binding}";
}

/// <summary>설정 파일 → 패드에 보낼 바인딩 목록 / 패킷 목록.</summary>
public sealed class UploadPlan
{
    public required IReadOnlyList<PlannedBinding> Bindings { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required ProtocolOptions Options { get; init; }
    public bool SendInitPacket { get; init; }

    /// <summary>OnlyChanged 로 걸러져서 빠진 (패드와 이미 같은) 바인딩 수</summary>
    public int SkippedUnchanged { get; init; }

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

            var buttonCount = lc.Buttons?.Count ?? 0;
            if (buttonCount > adv.ButtonCount)
                errors.Add($"레이어{layer + 1}: 버튼은 {adv.ButtonCount}개인데 {buttonCount}개가 적혀 있어.");

            var knobCount = lc.Knobs?.Count ?? 0;
            if (knobCount > adv.KnobCount)
                errors.Add($"레이어{layer + 1}: 노브는 {adv.KnobCount}개인데 {knobCount}개가 적혀 있어.");

            foreach (var slot in SlotRef.AllInLayer(layer, adv))
            {
                var source = config.GetText(slot);
                try
                {
                    if (TryPlan(slot, source, adv, options) is { } planned)
                        bindings.Add(planned);
                }
                catch (FormatException ex)
                {
                    errors.Add($"{slot.Describe(adv.ButtonColumns)}: {ex.Message}");
                }
            }
        }

        return new UploadPlan
        {
            Bindings = bindings,
            Errors = errors,
            Options = options,
            SendInitPacket = adv.SendInitPacket,
        };
    }

    /// <summary>
    /// 칸 하나를 바인딩으로 변환. 비워둔 칸이면 null (= 건드리지 않음). 잘못된 값이면 FormatException.
    /// 패킷 생성까지 미리 해서 길이 초과 같은 문제도 여기서 잡음.
    /// </summary>
    public static PlannedBinding? TryPlan(SlotRef slot, string? source, AdvancedSection adv, ProtocolOptions options)
    {
        var binding = PadBinding.Parse(source);
        if (binding is null) return null;
        var padSlot = slot.PadSlot(options);
        var packet = K8850Protocol.BindingPacket(slot.Layer, padSlot, binding, options);
        return new PlannedBinding(slot, padSlot, slot.ControlName(adv.ButtonColumns), source!.Trim(), binding, packet);
    }

    /// <summary>
    /// 패드에 마지막으로 쓴 기록(state)과 다른 바인딩만 남긴 계획.
    /// 기록이 없으면 그대로 (= 전부 씀).
    /// </summary>
    public UploadPlan OnlyChanged(PadState? state)
    {
        if (state is null) return this;
        var changed = Bindings.Where(b => !state.Matches(b)).ToList();
        return new UploadPlan
        {
            Bindings = changed,
            Errors = Errors,
            Options = Options,
            SendInitPacket = SendInitPacket,
            SkippedUnchanged = Bindings.Count - changed.Count,
        };
    }

    /// <summary>실제로 보낼 64바이트 패킷들 (설명, 해당 바인딩 포함. 초기화 패킷은 바인딩 null).</summary>
    public IEnumerable<(string Description, byte[] Packet, PlannedBinding? Binding)> PacketsWithBindings()
    {
        if (SendInitPacket)
            yield return ("초기화", K8850Protocol.InitPacket(), null);

        foreach (var b in Bindings)
        {
            yield return (b.ToString(), b.Packet, null);
            yield return ("  확정", K8850Protocol.FinalizePacket(), b); // 확정까지 보내야 그 바인딩이 끝난 것
        }
    }

    /// <summary>실제로 보낼 64바이트 패킷들 (설명 포함).</summary>
    public IEnumerable<(string Description, byte[] Packet)> Packets() =>
        PacketsWithBindings().Select(p => (p.Description, p.Packet));
}
