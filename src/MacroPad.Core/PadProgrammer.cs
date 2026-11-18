using System.Runtime.Versioning;
using MacroPad.Core.Config;
using MacroPad.Core.Hid;
using MacroPad.Core.Protocol;

namespace MacroPad.Core;

/// <summary>설정 파일 내용을 실제 패드에 써 넣는 상위 기능. (PadFlasher / PadAgent 공통)</summary>
[SupportedOSPlatform("windows")]
public static class PadProgrammer
{
    public static HidDeviceInfo FindDevice(PadConfig config)
    {
        var vid = config.Device.VendorIdValue;
        var pid = config.Device.ProductIdValue;
        return HidDevices.FindConfigInterface(vid, pid)
               ?? throw new InvalidOperationException(
                   $"패드(VID {vid:X4} / PID {pid:X4})의 설정 통로를 못 찾았어. USB가 꽂혀 있는지 확인해줘.");
    }

    /// <summary>설정을 패드에 씀. 반환값: 쓴 바인딩 개수.</summary>
    public static int Upload(PadConfig config, Action<string> log, bool dryRun = false)
    {
        var plan = UploadPlan.Create(config);
        if (!plan.IsValid)
            throw new FormatException("설정 파일에 문제가 있어:\n - " + string.Join("\n - ", plan.Errors));

        if (plan.Bindings.Count == 0)
        {
            log("쓸 내용이 없어. (모든 칸이 비어 있음)");
            return 0;
        }

        if (dryRun)
        {
            log("[미리보기] 실제로 보내지 않고 패킷만 출력해.");
            foreach (var (desc, packet) in plan.Packets())
                log($"{desc}\n    {K8850Protocol.ToHex(packet)}");
            return plan.Bindings.Count;
        }

        var device = FindDevice(config);
        log($"장치: {device.Describe()}");

        using var conn = HidConnection.Open(device);
        var mode = config.Advanced.ReportIdMode;
        var interval = Math.Max(0, config.Advanced.PacketIntervalMs);

        foreach (var (desc, packet) in plan.Packets())
        {
            log($"{desc}\n    {K8850Protocol.ToHex(packet)}");
            conn.Write(packet, mode);
            if (interval > 0) Thread.Sleep(interval);
        }

        log($"완료: {plan.Bindings.Count}개 슬롯을 썼어.");
        return plan.Bindings.Count;
    }

    /// <summary>장치 정보 질의 패킷을 보내고 응답을 받음 (통신 확인용). 응답 없으면 null.</summary>
    public static byte[]? Query(PadConfig config, Action<string> log, int timeoutMs = 1000)
    {
        var device = FindDevice(config);
        log($"장치: {device.Describe()}");
        using var conn = HidConnection.Open(device);
        var packet = K8850Protocol.QueryPacket();
        log($"보냄: {K8850Protocol.ToHex(conn.BuildReport(packet, config.Advanced.ReportIdMode))}");
        conn.Write(packet, config.Advanced.ReportIdMode);
        return conn.Read(timeoutMs);
    }

    /// <summary>16진수 문자열 패킷 하나를 그대로 전송 (실험용)</summary>
    public static void SendRaw(PadConfig config, byte[] packet, Action<string> log)
    {
        var device = FindDevice(config);
        using var conn = HidConnection.Open(device);
        var full = new byte[K8850Protocol.PacketSize];
        Array.Copy(packet, full, Math.Min(packet.Length, full.Length));
        log($"보냄: {K8850Protocol.ToHex(conn.BuildReport(full, config.Advanced.ReportIdMode))}");
        conn.Write(full, config.Advanced.ReportIdMode);
        var reply = conn.Read(500);
        log(reply is null ? "응답 없음 (정상일 수 있음)" : $"응답: {K8850Protocol.ToHex(reply)}");
    }
}
