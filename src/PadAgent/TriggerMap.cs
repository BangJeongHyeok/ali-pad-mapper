using System.Diagnostics.CodeAnalysis;
using MacroPad.Core.Config;
using MacroPad.Core.Keys;

namespace PadAgent;

/// <summary>트리거 키 하나와 그때 할 동작.</summary>
internal sealed record Trigger(string Name, ushort Vk, ModifierFlags Modifiers, ActionConfig Action, IReadOnlyList<Chord> Keys);

/// <summary>설정 파일 "actions" → (가상 키, 수식 키) 로 찾는 표.</summary>
internal sealed class TriggerMap
{
    public static readonly TriggerMap Empty = new(new(), new());

    private readonly Dictionary<(ushort Vk, ModifierFlags Mods), Trigger> _map;

    public IReadOnlyList<string> Problems { get; }
    public int Count => _map.Count;

    private TriggerMap(Dictionary<(ushort, ModifierFlags), Trigger> map, List<string> problems)
    {
        _map = map;
        Problems = problems;
    }

    public bool TryGet(ushort vk, ModifierFlags mods, [MaybeNullWhen(false)] out Trigger trigger) =>
        _map.TryGetValue((vk, mods), out trigger);

    public static TriggerMap Build(PadConfig config)
    {
        var map = new Dictionary<(ushort, ModifierFlags), Trigger>();
        var problems = new List<string>();

        foreach (var (name, action) in config.Actions)
        {
            if (action is null) continue;
            try
            {
                var chord = Chord.Parse(name);
                if (chord.Key is not { Vk: { } vk } key)
                    throw new FormatException("트리거에는 수식 키 외에 키가 하나 있어야 합니다.");
                if (key.IsMedia)
                    throw new FormatException("미디어 키는 트리거로 쓸 수 없습니다. 패드 설정(layers)에 직접 넣어 주십시오.");

                var keys = action.ParseKeys();

                var trigger = new Trigger(name, vk, chord.ModifierFlags, action, keys);
                if (!map.TryAdd((vk, trigger.Modifiers), trigger))
                    throw new FormatException("같은 트리거가 이미 있습니다.");

                // F13~F24 가 아닌 일반 키를 트리거로 쓰면 원래 키보드 입력까지 가로챔
                bool isF13to24 = vk is >= 0x7C and <= 0x87;
                if (action.IsCueSet && chord.Modifiers.Count > 0)
                    problems.Add($"주의: 큐 지정 키 \"{name}\"에 수식 키가 있어서 누르고 있는 동안 동작하지 않을 수 있습니다. f13~f24 하나만 쓰는 것을 권장합니다.");
                if (!isF13to24 && chord.Modifiers.Count == 0)
                    problems.Add($"주의: \"{name}\"은(는) 일반 키보드에도 있는 키라서, 원래 키보드에서 누를 때도 가로챕니다.");
            }
            catch (FormatException ex)
            {
                problems.Add($"actions \"{name}\": {ex.Message}");
            }
        }

        return new TriggerMap(map, problems);
    }
}
