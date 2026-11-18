using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MacroPad.Core.Config;

/// <summary>
/// PadConfig → macropad.json 저장 (편집기에서 사용).
/// 사람이 계속 직접 고칠 수도 있게 버튼은 한 줄에 4개씩, 노브/동작은 한 줄에 하나씩 씀.
/// 원래 파일의 주석은 보존되지 않음 → 사람이 쓴 파일을 처음 덮어쓸 때 macropad.json.bak 으로 백업.
/// </summary>
public static class ConfigWriter
{
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 한글을 \uXXXX 로 바꾸지 않음
    };

    private const string HeaderFirstLine = "// MacroPad 설정 파일 — MacroPad 편집기가 저장함";

    /// <summary>편집기가 저장한 형식인지 (아니면 사람이 쓴 주석이 있을 수 있음 → 덮어쓰기 전에 확인)</summary>
    public static bool IsEditorFormat(string text) => text.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith(HeaderFirstLine, StringComparison.Ordinal);

    private const string Header = """
        // MacroPad 설정 파일 — MacroPad 편집기가 저장함 (직접 고쳐도 됨, 주석 // 사용 가능)
        //
        //   layers  : 패드 자체에 저장되는 설정 → "패드에 쓰기"를 해야 반영됨
        //             값: "volumeup" / "play" / "a" / "ctrl+c" / "ctrl+a, ctrl+c" / null(건드리지 않음)
        //   actions : PC 에이전트가 처리하는 동작. 패드 칸에 넣은 트리거 키(f13~f24 등)와 연결됨
        //             { "run": 프로그램/파일/URL, "args", "workingDir", "keys": [단축키…], "text": 문자열, "delayMs" }
        //
        // 편집기로 저장하면 주석이 사라져. 이전 파일은 macropad.json.bak 에 백업됨.
        """;

    public static string Serialize(PadConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine("{");

        // device
        sb.AppendLine($"  \"device\": {{ \"vendorId\": {S(config.Device.VendorId)}, \"productId\": {S(config.Device.ProductId)} }},");
        sb.AppendLine();

        // layers
        var cols = Math.Max(1, config.Advanced.ButtonColumns);
        sb.AppendLine("  \"layers\": [");
        for (int l = 0; l < config.Layers.Count; l++)
        {
            var layer = config.Layers[l] ?? new LayerConfig();
            sb.AppendLine("    {");
            sb.AppendLine($"      // 레이어 {l + 1}");

            var buttons = layer.Buttons ?? new();
            var knobs = layer.Knobs ?? new();

            if (buttons.Count == 0)
            {
                sb.Append("      \"buttons\": []");
            }
            else
            {
                sb.AppendLine("      \"buttons\": [");
                for (int i = 0; i < buttons.Count; i += cols)
                {
                    var row = buttons.Skip(i).Take(cols).Select(b => b is null ? "null" : S(b));
                    sb.Append("        ").Append(string.Join(", ", row));
                    sb.AppendLine(i + cols < buttons.Count ? "," : "");
                }
                sb.Append("      ]");
            }
            sb.AppendLine(",");

            if (knobs.Count == 0)
            {
                sb.AppendLine("      \"knobs\": []");
            }
            else
            {
                sb.AppendLine("      \"knobs\": [");
                for (int k = 0; k < knobs.Count; k++)
                {
                    sb.Append("        ").Append(Knob(knobs[k]));
                    sb.AppendLine(k + 1 < knobs.Count ? "," : "");
                }
                sb.AppendLine("      ]");
            }

            sb.Append("    }");
            sb.AppendLine(l + 1 < config.Layers.Count ? "," : "");
        }
        sb.AppendLine("  ],");
        sb.AppendLine();

        // actions (트리거 키 이름 순: f13, f14 … 순서가 자연스럽게 보이도록)
        var actions = config.Actions.Where(kv => kv.Value is not null)
            .OrderBy(kv => kv.Key, TriggerNameComparer.Instance)
            .ToList();
        if (actions.Count == 0)
        {
            sb.AppendLine("  \"actions\": {},");
        }
        else
        {
            sb.AppendLine("  \"actions\": {");
            for (int i = 0; i < actions.Count; i++)
            {
                sb.Append("    ").Append(S(actions[i].Key)).Append(": ").Append(Action(actions[i].Value));
                sb.AppendLine(i + 1 < actions.Count ? "," : "");
            }
            sb.AppendLine("  },");
        }
        sb.AppendLine();

        // advanced (전부 기본값이어도 그대로 씀: 어떤 값으로 썼는지 보이게)
        var a = config.Advanced;
        sb.AppendLine("  // 첫 테스트가 안 될 때만 건드리기 (README 문제 해결 참고)");
        sb.AppendLine("  \"advanced\": {");
        var adv = new List<string>
        {
            $"\"knobSlotBase\": {a.KnobSlotBase}",
            $"\"buttonCount\": {a.ButtonCount}",
            $"\"buttonColumns\": {a.ButtonColumns}",
            $"\"knobCount\": {a.KnobCount}",
            $"\"layerCount\": {a.LayerCount}",
            $"\"keyDelay\": {a.KeyDelay}",
            $"\"mediaModeByte\": {a.MediaModeByte}",
            $"\"packetIntervalMs\": {a.PacketIntervalMs}",
            $"\"sendInitPacket\": {(a.SendInitPacket ? "true" : "false")}",
            $"\"reportIdMode\": {S(a.ReportIdMode)}",
        };
        sb.AppendLine(string.Join(",\n", adv.Select(x => "    " + x)));
        sb.AppendLine("  }");
        sb.AppendLine("}");

        return sb.ToString().ReplaceLineEndings(Environment.NewLine);
    }

    /// <summary>
    /// 파일로 저장. 기존 파일은 같은 폴더의 *.bak 으로 백업하고, 임시 파일에 다 쓴 뒤 바꿔치기
    /// (중간에 꺼져도 설정 파일이 반쯤 쓰인 채로 남지 않게).
    /// </summary>
    public static void Save(PadConfig config, string path)
    {
        var text = Serialize(config);
        PadConfig.Parse(text); // 스스로 다시 읽히는지 확인 (실패하면 저장하지 않음)

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        // 사람이 직접 쓴(주석이 있을 수 있는) 파일만 백업. 편집기 형식끼리는 덮어써도 잃는 게 없음
        // (자동 저장이라 매번 백업하면 원본 백업이 금방 덮여 버림)
        if (File.Exists(path) && !IsEditorFormat(File.ReadAllText(path)))
            File.Copy(path, path + ".bak", overwrite: true);
        File.Move(temp, path, overwrite: true);
    }

    private static string S(string value) => JsonSerializer.Serialize(value, StringOptions);

    private static string Knob(KnobConfig? knob)
    {
        if (knob is null) return "null";
        var parts = new List<string>();
        if (knob.Ccw is not null) parts.Add($"\"ccw\": {S(knob.Ccw)}");
        if (knob.Press is not null) parts.Add($"\"press\": {S(knob.Press)}");
        if (knob.Cw is not null) parts.Add($"\"cw\": {S(knob.Cw)}");
        return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
    }

    private static string Action(ActionConfig action)
    {
        var parts = new List<string>();
        if (action.Run is not null) parts.Add($"\"run\": {S(action.Run)}");
        if (!string.IsNullOrEmpty(action.Args)) parts.Add($"\"args\": {S(action.Args)}");
        if (!string.IsNullOrEmpty(action.WorkingDir)) parts.Add($"\"workingDir\": {S(action.WorkingDir)}");
        if (action.Keys is { Count: > 0 } keys) parts.Add($"\"keys\": [{string.Join(", ", keys.Select(S))}]");
        if (action.Text is not null) parts.Add($"\"text\": {S(action.Text)}");
        if (action.DelayMs is { } d) parts.Add($"\"delayMs\": {d}");
        return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>"f9" &lt; "f13" &lt; "f24" &lt; "shift+f13" 처럼 숫자를 숫자로 비교</summary>
    private sealed class TriggerNameComparer : IComparer<string>
    {
        public static readonly TriggerNameComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var (px, nx) = Split(x ?? "");
            var (py, ny) = Split(y ?? "");
            var c = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : nx.CompareTo(ny);
        }

        private static (string Prefix, int Number) Split(string s)
        {
            int end = s.Length;
            while (end > 0 && char.IsAsciiDigit(s[end - 1])) end--;
            return end < s.Length && int.TryParse(s[end..], out var n) ? (s[..end], n) : (s, -1);
        }
    }
}
