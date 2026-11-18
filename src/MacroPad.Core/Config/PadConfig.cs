using System.Globalization;
using System.Text.Json;

namespace MacroPad.Core.Config;

/// <summary>macropad.json 전체 구조.</summary>
public sealed class PadConfig
{
    public DeviceSection Device { get; set; } = new();

    /// <summary>패드에 직접 저장되는 설정 (레이어별). 첫 번째가 레이어 1.</summary>
    public List<LayerConfig> Layers { get; set; } = new();

    /// <summary>
    /// PC 에이전트(트레이 앱)가 처리할 동작. 키: 트리거 키 이름 (예: "f13"), 값: 동작.
    /// </summary>
    public Dictionary<string, ActionConfig> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AdvancedSection Advanced { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static PadConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return Parse(json);
    }

    public static PadConfig Parse(string json)
    {
        PadConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<PadConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" ({line + 1}번째 줄 근처)" : "";
            throw new FormatException($"설정 파일 JSON 형식 오류{where}: {ex.Message}", ex);
        }

        if (config is null)
            throw new FormatException("설정 파일이 비어 있어.");

        // 대소문자 구분 없이 찾을 수 있게 다시 담기 (JSON 역직렬화는 기본 비교자를 씀)
        config.Actions = new Dictionary<string, ActionConfig>(config.Actions ?? new(), StringComparer.OrdinalIgnoreCase);
        config.Layers ??= new();
        config.Device ??= new();
        config.Advanced ??= new();
        return config;
    }
}

public sealed class DeviceSection
{
    public string VendorId { get; set; } = "0x514C";
    public string ProductId { get; set; } = "0x8850";

    public ushort VendorIdValue => ParseHex(VendorId, nameof(VendorId));
    public ushort ProductIdValue => ParseHex(ProductId, nameof(ProductId));

    private static ushort ParseHex(string text, string name)
    {
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (!ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"device.{name} 값 '{text}' 을 16진수로 읽을 수 없어. (예: \"0x514C\")");
        return v;
    }
}

public sealed class LayerConfig
{
    /// <summary>버튼 순서: 왼쪽 위부터 가로 방향 (1행 1~4열, 2행 1~4열, 3행 1~4열)</summary>
    public List<string?> Buttons { get; set; } = new();

    /// <summary>노브 순서: 위 노브, 아래 노브</summary>
    public List<KnobConfig?> Knobs { get; set; } = new();
}

public sealed class KnobConfig
{
    /// <summary>왼쪽(반시계)으로 돌릴 때</summary>
    public string? Ccw { get; set; }

    /// <summary>꾹 누를 때</summary>
    public string? Press { get; set; }

    /// <summary>오른쪽(시계)으로 돌릴 때</summary>
    public string? Cw { get; set; }
}

/// <summary>PC 에이전트 동작. run / keys / text 중 하나를 씀 (여러 개 쓰면 run → keys → text 순서로 실행).</summary>
public sealed class ActionConfig
{
    /// <summary>실행할 프로그램 / 파일 / URL. 환경 변수(%USERPROFILE% 등) 사용 가능.</summary>
    public string? Run { get; set; }

    /// <summary>run 에 넘길 인자</summary>
    public string? Args { get; set; }

    /// <summary>run 의 작업 폴더 (생략하면 프로그램 위치)</summary>
    public string? WorkingDir { get; set; }

    /// <summary>순서대로 보낼 단축키 목록. 예: ["ctrl+c", "alt+tab", "ctrl+v"]</summary>
    public List<string>? Keys { get; set; }

    /// <summary>입력할 문자열 (한글 가능)</summary>
    public string? Text { get; set; }

    /// <summary>keys 각 단계 사이 대기 시간(ms)</summary>
    public int DelayMs { get; set; } = 40;
}

/// <summary>프로토콜 세부값. 첫 테스트가 안 될 때만 건드리면 됨 (README 문제 해결 참고).</summary>
public sealed class AdvancedSection
{
    /// <summary>위 노브의 첫 슬롯 번호. 12키+노브2 모델은 16 (위 노브 16~18, 아래 노브 19~21).</summary>
    public int KnobSlotBase { get; set; } = 16;

    /// <summary>버튼 개수 (설정 검증용)</summary>
    public int ButtonCount { get; set; } = 12;

    /// <summary>한 줄의 버튼 개수 (메시지 표시용)</summary>
    public int ButtonColumns { get; set; } = 4;

    /// <summary>노브 개수 (설정 검증용)</summary>
    public int KnobCount { get; set; } = 2;

    /// <summary>패드 레이어 개수</summary>
    public int LayerCount { get; set; } = 3;

    /// <summary>패드가 연속 입력할 때 키 사이 지연. 판매자 프로그램은 50(0x32)을 씀.</summary>
    public int KeyDelay { get; set; } = 50;

    /// <summary>미디어 키 패킷의 7번째 바이트. 판매자 프로그램 캡처는 2. 안 되면 1로 바꿔볼 것.</summary>
    public int MediaModeByte { get; set; } = 2;

    /// <summary>패킷 사이 대기(ms)</summary>
    public int PacketIntervalMs { get; set; } = 15;

    /// <summary>쓰기 전에 0으로 채운 초기화 패킷을 먼저 보낼지</summary>
    public bool SendInitPacket { get; set; } = true;

    /// <summary>
    /// HID 리포트 ID 처리: "auto"(장치 정보로 판단) / "zero"(항상 0 + 패킷 전체) / "payload"(패킷 첫 바이트 03을 리포트 ID로).
    /// </summary>
    public string ReportIdMode { get; set; } = "auto";
}
