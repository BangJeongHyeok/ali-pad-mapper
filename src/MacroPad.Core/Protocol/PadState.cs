using System.Text.Encodings.Web;
using System.Text.Json;
using MacroPad.Core.Config;

namespace MacroPad.Core.Protocol;

/// <summary>패드의 칸 하나에 마지막으로 쓴 내용.</summary>
public sealed class PadSlotRecord
{
    /// <summary>설정 파일에 적혀 있던 문자열 (표시용)</summary>
    public string Source { get; set; } = "";

    /// <summary>실제로 보낸 바인딩 패킷 (16진수). 이 값으로 같은지 비교.</summary>
    public string Packet { get; set; } = "";

    public DateTime WrittenAt { get; set; }
}

/// <summary>
/// 패드에 마지막으로 써 넣은 내용의 기록.
/// 패드는 설정을 읽어오는 기능이 없어서, "지금 패드에 뭐가 들어 있는지"를 PC 쪽에서 기억해 둠.
///  → 바뀐 칸만 쓰기, 편집기에서 "아직 패드에 안 쓴 칸" 표시에 사용.
/// 이 PC에서 이 프로그램으로 쓴 것만 알 수 있음 (다른 PC/판매자 프로그램으로 바꾼 건 모름).
/// </summary>
public sealed class PadState
{
    /// <summary>키: "L{레이어 1~3}-S{슬롯 번호}"</summary>
    public Dictionary<string, PadSlotRecord> Slots { get; set; } = new();

    public DateTime? UpdatedAt { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Key(int layerIndex, int padSlot) => $"L{layerIndex + 1}-S{padSlot}";

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroPad");

    /// <summary>%LOCALAPPDATA%\MacroPad\pad-514C-8850.json</summary>
    public static string DefaultPath(DeviceSection device) =>
        Path.Combine(DefaultDirectory, $"pad-{device.VendorIdValue:X4}-{device.ProductIdValue:X4}.json");

    public PadSlotRecord? Get(int layerIndex, int padSlot) =>
        Slots.TryGetValue(Key(layerIndex, padSlot), out var r) ? r : null;

    public bool Matches(PlannedBinding binding) =>
        Get(binding.LayerIndex, binding.Slot) is { } r
        && string.Equals(r.Packet, ToHex(binding.Packet), StringComparison.OrdinalIgnoreCase);

    public void Record(PlannedBinding binding, DateTime? now = null)
    {
        var time = now ?? DateTime.Now;
        Slots[Key(binding.LayerIndex, binding.Slot)] = new PadSlotRecord
        {
            Source = binding.Source,
            Packet = ToHex(binding.Packet),
            WrittenAt = time,
        };
        UpdatedAt = time;
    }

    private static string ToHex(byte[] packet)
    {
        var len = packet.Length;
        while (len > 0 && packet[len - 1] == 0) len--;
        return Convert.ToHexString(packet, 0, len);
    }

    /// <summary>파일이 없거나 깨졌으면 null (= 패드 상태 모름)</summary>
    public static PadState? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<PadState>(File.ReadAllText(path), JsonOptions);
            if (state is null) return null;
            state.Slots ??= new();
            return state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
