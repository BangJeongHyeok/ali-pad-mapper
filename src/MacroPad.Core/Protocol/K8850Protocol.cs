using MacroPad.Core.Config;
using MacroPad.Core.Keys;

namespace MacroPad.Core.Protocol;

public enum KnobAction
{
    Ccw = 0,   // 왼쪽(반시계)
    Press = 1, // 누르기
    Cw = 2,    // 오른쪽(시계)
}

public sealed record ProtocolOptions
{
    public int KnobSlotBase { get; init; } = 16;
    public byte KeyDelay { get; init; } = 0x32;
    public byte MediaModeByte { get; init; } = 0x02;

    public static ProtocolOptions From(AdvancedSection a) => new()
    {
        KnobSlotBase = a.KnobSlotBase,
        KeyDelay = (byte)Math.Clamp(a.KeyDelay, 0, 255),
        MediaModeByte = (byte)Math.Clamp(a.MediaModeByte, 0, 255),
    };
}

/// <summary>
/// VID 514C / PID 8850 매크로 패드(12키 + 노브 2개)의 설정 쓰기 프로토콜.
///
/// 패킷은 64바이트, 형식:
///   [03 FD slot layer type 00 | 내용 ...]  ← 바인딩 하나
///   [03 FD FE FF]                          ← 바인딩마다 뒤에 붙는 확정(finalize) 패킷
///
///   slot : 버튼 1~12, 위 노브 16(왼쪽)/17(누름)/18(오른쪽), 아래 노브 19/20/21
///   layer: 1~3
///   type : 1 = 키보드, 2 = 미디어
///
///   키 하나(수식 키 없음): [01 00 00 code 00]
///   그 외(단축키/연속 입력): [count 00 00] + 항목마다 [code 00 delay]
///       수식 키 code: Ctrl F1, Shift F2, Alt F3, Win F4 (오른쪽 F5~F8)
///   미디어: [02 00 00 consumer]
///
/// 참고: kriomant/ch57x-keyboard-tool 의 이슈 #89, PR #154/#174 에 공개된 분석 내용
/// (판매자 프로그램의 HID 통신 캡처)을 바탕으로 작성.
/// </summary>
public static class K8850Protocol
{
    public const int PacketSize = 64;
    public const int MaxMacroEntries = 18;

    public static int ButtonSlot(int buttonIndex) => buttonIndex + 1;

    public static int KnobSlot(int knobIndex, KnobAction action, ProtocolOptions options) =>
        options.KnobSlotBase + 3 * knobIndex + (int)action;

    /// <summary>쓰기 시작 전에 보내는 0으로 채운 패킷</summary>
    public static byte[] InitPacket() => new byte[PacketSize];

    /// <summary>바인딩 하나를 확정하는 패킷</summary>
    public static byte[] FinalizePacket() => Pad(0x03, 0xFD, 0xFE, 0xFF);

    /// <summary>장치 정보 질의 (응답 예: 03 FB 0C 02 ... = 키 12개, 노브 2개)</summary>
    public static byte[] QueryPacket() => Pad(0x03, 0xFB, 0xFB, 0xFB);

    public static byte[] BindingPacket(int layerIndex, int slot, PadBinding binding, ProtocolOptions options)
    {
        if (layerIndex is < 0 or > 15)
            throw new ArgumentOutOfRangeException(nameof(layerIndex), "레이어 번호가 범위를 벗어났어.");
        if (slot is < 1 or > 0xFD)
            throw new ArgumentOutOfRangeException(nameof(slot), "슬롯 번호가 범위를 벗어났어.");

        var msg = new List<byte>(PacketSize) { 0x03, 0xFD, (byte)slot, (byte)(layerIndex + 1) };

        switch (binding)
        {
            case MediaBinding media:
                msg.AddRange([0x02, 0x00, options.MediaModeByte, 0x00, 0x00, media.ConsumerCode]);
                break;

            case KeySequenceBinding keys:
                msg.AddRange([0x01, 0x00]);
                AppendKeys(msg, keys.Chords, options);
                break;

            default:
                throw new NotSupportedException($"지원하지 않는 동작: {binding}");
        }

        return Pad(msg.ToArray());
    }

    private static void AppendKeys(List<byte> msg, IReadOnlyList<Chord> chords, ProtocolOptions options)
    {
        // 수식 키 없는 키 하나: 간단 형식
        if (chords is [{ Modifiers.Count: 0, Key: { Hid: { } single } }])
        {
            msg.AddRange([0x01, 0x00, 0x00, single, 0x00]);
            return;
        }

        // 그 외: 항목 목록 형식. "ctrl+c" → [F1 00 d] [06 00 d]
        var entries = new List<byte>();
        foreach (var chord in chords)
        {
            foreach (var m in chord.Modifiers)
                entries.Add(KeyTable.PadCode(m));
            if (chord.Key is { } key)
                entries.Add(key.Hid ?? throw new FormatException($"'{key.Name}' 키는 패드에 저장할 수 없어."));
        }

        if (entries.Count == 0)
            throw new FormatException("입력할 키가 없어.");
        if (entries.Count > MaxMacroEntries)
            throw new FormatException($"연속 입력이 너무 길어. 수식 키 포함 최대 {MaxMacroEntries}개인데 {entries.Count}개야.");

        msg.AddRange([(byte)entries.Count, 0x00, 0x00]);
        foreach (var e in entries)
            msg.AddRange([e, 0x00, options.KeyDelay]);
    }

    private static byte[] Pad(params byte[] bytes)
    {
        if (bytes.Length > PacketSize)
            throw new InvalidOperationException("패킷이 64바이트를 넘었어.");
        var packet = new byte[PacketSize];
        bytes.CopyTo(packet, 0);
        return packet;
    }

    public static string ToHex(byte[] packet, bool trimTrailingZeros = true)
    {
        var len = packet.Length;
        if (trimTrailingZeros)
        {
            while (len > 0 && packet[len - 1] == 0) len--;
            len = Math.Max(len, Math.Min(4, packet.Length));
        }
        var hex = Convert.ToHexString(packet, 0, len);
        var spaced = string.Join(' ', Enumerable.Range(0, len).Select(i => hex.Substring(i * 2, 2)));
        return len < packet.Length ? spaced + " …(0)" : spaced;
    }
}
