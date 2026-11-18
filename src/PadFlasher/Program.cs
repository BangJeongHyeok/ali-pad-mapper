using System.Globalization;
using System.Text;
using MacroPad.Core;
using MacroPad.Core.Config;
using MacroPad.Core.Hid;
using MacroPad.Core.Keys;
using MacroPad.Core.Protocol;

Console.OutputEncoding = Encoding.UTF8;

try
{
    return Run(args);
}
catch (Exception ex) when (ex is FormatException or InvalidOperationException or IOException or FileNotFoundException)
{
    Error(ex.Message);
    return 1;
}

static int Run(string[] args)
{
    var flags = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).Select(a => a.ToLowerInvariant()).ToHashSet();
    var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();

    if (positional.Count == 0 || flags.Contains("--help"))
    {
        PrintHelp();
        return 0;
    }

    var command = positional[0].ToLowerInvariant();
    var rest = positional.Skip(1).ToList();

    switch (command)
    {
        case "list":
            return List(flags.Contains("--all"));

        case "info":
            return Info(LoadConfig(rest.FirstOrDefault()));

        case "upload":
        {
            var config = LoadConfig(rest.FirstOrDefault());
            return Upload(config, flags.Contains("--dry-run"), flags.Contains("--yes"), flags.Contains("--changed"));
        }

        case "keys":
            PrintKeys();
            return 0;

        case "raw":
        {
            // raw <hex...> : 예) raw 03 FB FB FB
            var config = LoadConfig(null);
            var bytes = ParseHex(string.Join(' ', rest));
            PadProgrammer.SendRaw(config, bytes, Console.WriteLine);
            return 0;
        }

        default:
            Error($"모르는 명령: {command}");
            PrintHelp();
            return 1;
    }
}

static PadConfig LoadConfig(string? path)
{
    var resolved = ConfigLocator.Resolve(path);
    if (!File.Exists(resolved))
        throw new FileNotFoundException($"설정 파일이 없어: {resolved}");
    Console.WriteLine($"설정 파일: {resolved}");
    return PadConfig.Load(resolved);
}

static int List(bool all)
{
    var config = PadConfig.Parse("{}"); // 기본 VID/PID
    var vid = config.Device.VendorIdValue;
    var pid = config.Device.ProductIdValue;

    var devices = all ? HidDevices.Enumerate() : HidDevices.Enumerate(vid, pid);
    if (devices.Count == 0)
    {
        Console.WriteLine(all ? "HID 장치가 하나도 없어." : $"VID {vid:X4} / PID {pid:X4} 장치를 못 찾았어. USB 연결을 확인해줘. (전체 목록: list --all)");
        return 1;
    }

    foreach (var d in devices)
    {
        var mark = d.IsVendorDefined && d.OutputReportLength > 0 ? "★ 설정 통로" : "  ";
        Console.WriteLine($"{mark} {d.Describe()}");
        Console.WriteLine($"     {d.Path}");
    }

    if (!all)
    {
        var target = HidDevices.FindConfigInterface(vid, pid);
        Console.WriteLine();
        Console.WriteLine(target is null
            ? "설정 통로(제조사 정의 컬렉션)를 못 찾았어."
            : "★ 표시된 컬렉션으로 설정을 써.");
    }
    return 0;
}

static int Info(PadConfig config)
{
    var reply = PadProgrammer.Query(config, Console.WriteLine);
    if (reply is null)
    {
        Console.WriteLine("응답이 없어. (응답이 없어도 쓰기는 될 수 있음. advanced.reportIdMode 를 바꿔서 다시 해볼 수도 있어)");
        return 1;
    }

    Console.WriteLine($"응답: {K8850Protocol.ToHex(reply)}");

    // 응답에서 FB 다음 두 바이트 = 키 개수, 노브 개수 (알려진 형식: 03 FB 0C 02 …)
    var idx = Array.IndexOf(reply, (byte)0xFB);
    if (idx >= 0 && idx + 2 < reply.Length)
        Console.WriteLine($"→ 키 {reply[idx + 1]}개, 노브 {reply[idx + 2]}개로 보여.");
    return 0;
}

static int Upload(PadConfig config, bool dryRun, bool yes, bool onlyChanged)
{
    var plan = UploadPlan.Create(config);

    if (!plan.IsValid)
    {
        Error("설정 파일에 문제가 있어:");
        foreach (var e in plan.Errors) Error(" - " + e);
        return 1;
    }

    if (onlyChanged)
        plan = plan.OnlyChanged(PadState.Load(PadState.DefaultPath(config.Device)));

    Console.WriteLine();
    Console.WriteLine($"쓸 내용 ({plan.Bindings.Count}개, 비워둔 칸{(onlyChanged ? "과 패드와 같은 칸" : "")}은 그대로 둠):");
    foreach (var b in plan.Bindings)
        Console.WriteLine("  " + b);
    Console.WriteLine();

    if (plan.Bindings.Count == 0)
    {
        Console.WriteLine("쓸 게 없어.");
        return 0;
    }

    if (!dryRun && !yes)
    {
        Console.Write("패드에 쓸까? (y/N) ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (answer is not ("y" or "yes" or "ㅛ"))
        {
            Console.WriteLine("취소했어.");
            return 1;
        }
    }

    PadProgrammer.Upload(config, Console.WriteLine, dryRun, onlyChanged);
    if (!dryRun)
        Console.WriteLine("패드를 한 번 뽑았다 다시 꽂을 필요는 보통 없어. 바로 눌러/돌려서 확인해봐.");
    return 0;
}

static void PrintKeys()
{
    Console.WriteLine("수식 키: ctrl, shift, alt, win, rctrl, rshift, ralt, rwin");
    Console.WriteLine();
    Console.WriteLine("키 이름 (패드 = 패드에 저장 가능, PC = 에이전트 actions 에서 사용 가능):");
    foreach (var k in KeyTable.AllKeys.OrderBy(k => k.IsMedia).ThenBy(k => k.Name, StringComparer.Ordinal))
    {
        var targets = new List<string>();
        if (k.Hid is not null || k.IsMedia) targets.Add("패드");
        if (k.Vk is not null) targets.Add("PC");
        Console.WriteLine($"  {k.Name,-16} {(k.IsMedia ? "[미디어] " : "")}{string.Join("/", targets)}");
    }
}

static byte[] ParseHex(string text)
{
    var parts = text.Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) throw new FormatException("보낼 바이트를 16진수로 적어줘. 예: raw 03 FB FB FB");
    return parts.Select(p => byte.Parse(p.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? p[2..] : p,
        NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
}

static void Error(string message)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(message);
    Console.ForegroundColor = prev;
}

static void PrintHelp()
{
    Console.WriteLine("""
        PadFlasher - 매크로 패드(VID 514C / PID 8850) 설정 쓰기 도구

        사용법:
          PadFlasher list [--all]                   패드의 HID 컬렉션 목록 (--all: 모든 HID 장치)
          PadFlasher info [설정.json]               패드와 통신되는지 확인 (키/노브 개수 응답)
          PadFlasher upload [설정.json] --dry-run   보낼 패킷만 출력 (실제로 안 보냄)
          PadFlasher upload [설정.json] [--yes]     패드에 설정 쓰기
          PadFlasher upload [설정.json] --changed   지난번에 쓴 것과 달라진 칸만 쓰기
          PadFlasher keys                           쓸 수 있는 키 이름 목록
          PadFlasher raw 03 FB FB FB                패킷 하나 직접 전송 (실험용)

        설정 파일을 생략하면 macropad.json 을 씀 (README 참고).
        """);
}
