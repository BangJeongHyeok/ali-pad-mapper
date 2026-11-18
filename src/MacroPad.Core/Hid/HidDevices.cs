using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static MacroPad.Core.Hid.HidNative;

namespace MacroPad.Core.Hid;

/// <summary>HID 컬렉션 하나의 정보. (USB 장치 하나가 여러 컬렉션 = 여러 HID 장치로 보임)</summary>
public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    int OutputReportId,
    string? Product)
{
    /// <summary>제조사 정의(Vendor-defined) 컬렉션 = 설정 쓰기용 통로</summary>
    public bool IsVendorDefined => UsagePage >= 0xFF00;

    public string Describe() =>
        $"VID {VendorId:X4} PID {ProductId:X4} | UsagePage 0x{UsagePage:X4} Usage 0x{Usage:X2} | " +
        $"입력 {InputReportLength}B 출력 {OutputReportLength}B 출력ReportID {(OutputReportId < 0 ? "?" : OutputReportId.ToString())}" +
        (Product is null ? "" : $" | \"{Product}\"");
}

[SupportedOSPlatform("windows")]
public static class HidDevices
{
    /// <summary>연결된 모든 HID 컬렉션 목록 (vid/pid 지정 시 해당 장치만)</summary>
    public static List<HidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null)
    {
        var result = new List<HidDeviceInfo>();
        foreach (var path in GetHidInterfacePaths())
        {
            // 경로에 vid_xxxx&pid_xxxx 가 들어 있어서 미리 걸러냄 (키보드/마우스를 굳이 열지 않으려고)
            if (vendorId is { } v && !path.Contains($"vid_{v:x4}", StringComparison.OrdinalIgnoreCase)) continue;
            if (productId is { } p && !path.Contains($"pid_{p:x4}", StringComparison.OrdinalIgnoreCase)) continue;

            var info = TryQuery(path);
            if (info is null) continue;
            if (vendorId is { } v2 && info.VendorId != v2) continue;
            if (productId is { } p2 && info.ProductId != p2) continue;
            result.Add(info);
        }
        return result;
    }

    /// <summary>설정 쓰기에 쓸 컬렉션(제조사 정의 + 출력 리포트 있음)을 찾음</summary>
    public static HidDeviceInfo? FindConfigInterface(ushort vendorId, ushort productId) =>
        Enumerate(vendorId, productId)
            .Where(d => d.IsVendorDefined && d.OutputReportLength > 0)
            .OrderByDescending(d => d.UsagePage == 0xFF00)
            .FirstOrDefault();

    private static List<string> GetHidInterfacePaths()
    {
        HidD_GetHidGuid(out var guid);

        while (true)
        {
            int cr = CM_Get_Device_Interface_List_Size(out var len, ref guid, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            if (cr != CR_SUCCESS)
                throw new InvalidOperationException($"HID 장치 목록 크기를 못 가져왔어 (CONFIGRET {cr}).");

            var buffer = new char[len];
            cr = CM_Get_Device_Interface_List(ref guid, null, buffer, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            if (cr == CR_BUFFER_SMALL) continue; // 그 사이에 장치가 늘어남 → 다시
            if (cr != CR_SUCCESS)
                throw new InvalidOperationException($"HID 장치 목록을 못 가져왔어 (CONFIGRET {cr}).");

            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
    }

    private static HidDeviceInfo? TryQuery(string path)
    {
        // 권한 0으로 열면 키보드/마우스처럼 시스템이 독점한 장치도 정보 조회는 가능
        using var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(handle, ref attrs)) return null;

        if (!HidD_GetPreparsedData(handle, out var preparsed)) return null;
        try
        {
            if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS) return null;

            return new HidDeviceInfo(
                path,
                attrs.VendorID,
                attrs.ProductID,
                caps.UsagePage,
                caps.Usage,
                caps.InputReportByteLength,
                caps.OutputReportByteLength,
                DetectOutputReportId(preparsed, caps),
                GetProduct(handle));
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>출력 리포트의 Report ID (리포트 ID를 안 쓰는 장치면 0, 모르면 -1)</summary>
    private static int DetectOutputReportId(IntPtr preparsed, HIDP_CAPS caps)
    {
        int Read(Func<IntPtr, ushort, (int status, ushort count)> query, ushort count)
        {
            if (count == 0) return -1;
            var buf = Marshal.AllocHGlobal(CapsStructSize * count);
            try
            {
                var (status, got) = query(buf, count);
                if (status != HIDP_STATUS_SUCCESS || got == 0) return -1;
                return Marshal.ReadByte(buf, CapsReportIdOffset);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        var id = Read((buf, n) => { var len = n; var s = HidP_GetValueCaps(HidP_Output, buf, ref len, preparsed); return (s, len); },
                      caps.NumberOutputValueCaps);
        if (id >= 0) return id;

        return Read((buf, n) => { var len = n; var s = HidP_GetButtonCaps(HidP_Output, buf, ref len, preparsed); return (s, len); },
                    caps.NumberOutputButtonCaps);
    }

    private static string? GetProduct(SafeFileHandle handle)
    {
        var buf = new byte[256];
        if (!HidD_GetProductString(handle, buf, buf.Length)) return null;
        var s = Encoding.Unicode.GetString(buf);
        var end = s.IndexOf('\0');
        s = end >= 0 ? s[..end] : s;
        return s.Length == 0 ? null : s;
    }
}

/// <summary>설정 쓰기용 HID 연결 (읽기/쓰기).</summary>
[SupportedOSPlatform("windows")]
public sealed class HidConnection : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;

    public HidDeviceInfo Info { get; }

    private HidConnection(HidDeviceInfo info, SafeFileHandle handle)
    {
        Info = info;
        _handle = handle;
        _stream = new FileStream(handle, FileAccess.ReadWrite, 0, isAsync: true);
    }

    public static HidConnection Open(HidDeviceInfo info)
    {
        var handle = CreateFile(info.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException($"패드의 설정 통로를 열 수 없어: {new Win32Exception(err).Message} (판매자 프로그램이 켜져 있으면 끄고 다시 해봐)");
        }
        return new HidConnection(info, handle);
    }

    /// <summary>
    /// 64바이트 패킷을 HID 출력 리포트로 만들어서 전송.
    ///  - 리포트 ID 0 (안 씀): [00][패킷 64바이트]
    ///  - 리포트 ID 3       : [03][패킷의 2번째 바이트부터]
    /// </summary>
    public void Write(byte[] packet, string reportIdMode = "auto")
    {
        var report = BuildReport(packet, reportIdMode);
        try
        {
            // 장치가 응답을 안 하면 영원히 멈추지 않도록 시간 제한
            using var cts = new CancellationTokenSource(2000);
            _stream.WriteAsync(report.AsMemory(), cts.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // 인터럽트 OUT 전송이 실패하면 컨트롤 전송(SET_REPORT)으로 한 번 더 시도
            if (!HidNative.HidD_SetOutputReport(_handle, report, report.Length))
                throw new IOException($"패킷 전송 실패: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }
    }

    public byte[] BuildReport(byte[] packet, string reportIdMode)
    {
        int length = Info.OutputReportLength;
        if (length <= 1)
            throw new InvalidOperationException("이 HID 컬렉션은 출력 리포트가 없어.");

        int reportId = reportIdMode.Trim().ToLowerInvariant() switch
        {
            "zero" => 0,
            "payload" => packet[0],
            _ => Info.OutputReportId > 0 ? Info.OutputReportId : 0,
        };

        var report = new byte[length];
        if (reportId == 0)
        {
            report[0] = 0;
            Array.Copy(packet, 0, report, 1, Math.Min(packet.Length, length - 1));
        }
        else
        {
            report[0] = (byte)reportId;
            Array.Copy(packet, 1, report, 1, Math.Min(packet.Length - 1, length - 1));
        }
        return report;
    }

    /// <summary>입력 리포트 하나를 읽음. 시간 안에 안 오면 null.</summary>
    public byte[]? Read(int timeoutMs)
    {
        if (Info.InputReportLength <= 0) return null;
        var buf = new byte[Info.InputReportLength];
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            int n = _stream.ReadAsync(buf.AsMemory(), cts.Token).AsTask().GetAwaiter().GetResult();
            return n > 0 ? buf[..n] : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _handle.Dispose();
    }
}
