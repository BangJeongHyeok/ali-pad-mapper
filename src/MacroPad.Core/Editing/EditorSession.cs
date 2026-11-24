using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroPad.Core.Config;
using MacroPad.Core.Keys;
using MacroPad.Core.Protocol;

namespace MacroPad.Core.Editing;

/// <summary>
/// 편집기 화면(HTML) ↔ C# 사이의 창구.
///
/// 화면은 JSON 메시지 { id, type, ... } 를 보내고, 여기서 처리한 뒤
/// { id, ok, error?, result?, state } 로 답함. state 는 화면을 그리는 데 필요한 전체 상태.
///
/// 바뀐 설정은 바로 macropad.json 에 저장(자동 저장)되고, 패드에는 "패드에 적용"을 해야 써짐.
/// OS 기능(패드 쓰기, 파일 고르기 등)은 IEditorPlatform 으로 위임 → 이 클래스는 OS 와 무관해서 테스트 가능.
/// </summary>
public sealed class EditorSession
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IEditorPlatform _platform;
    private readonly string? _statePathOverride;

    private ConfigEditor _editor = null!;
    private PadState? _padState;
    private string _statePath = "";
    private bool _handWritten;
    private DateTime _lastWriteUtc;
    private bool _connected;
    private bool _uploading;
    private KeyTester? _keyTester;

    public string ConfigPath { get; }

    public PadConfig Config => _editor.Config;

    /// <summary>패드에 쓰는 중인지 (이때 창을 닫으면 안 됨)</summary>
    public bool IsBusy => _uploading;

    /// <summary>화면이 '키 테스트' 중인지 (이때 PadAgent 는 PC 동작을 실행하지 않고 여기로 키를 넘김)</summary>
    public bool KeyTestActive => _keyTester is not null;

    /// <exception cref="FormatException">설정 파일을 읽을 수 없을 때</exception>
    public EditorSession(string configPath, IEditorPlatform platform, string? statePath = null)
    {
        ConfigPath = Path.GetFullPath(configPath);
        _platform = platform;
        _statePathOverride = statePath;
        Load();
        _connected = SafeIsConnected();
    }

    private void Load()
    {
        var exists = File.Exists(ConfigPath);
        var text = exists ? File.ReadAllText(ConfigPath) : "{}";
        var config = PadConfig.Parse(text);

        _editor = new ConfigEditor(config);
        _handWritten = exists && !ConfigWriter.IsEditorFormat(text);
        _lastWriteUtc = exists ? File.GetLastWriteTimeUtc(ConfigPath) : default;
        _statePath = _statePathOverride ?? PadState.DefaultPath(config.Device);
        _padState = PadState.Load(_statePath);
        if (_keyTester is not null) _keyTester = new KeyTester(_editor); // 바뀐 설정으로 다시 맞춤
    }

    /// <summary>
    /// 키 테스트 중에 패드(키보드 훅)에서 온 키. 화면에 보낼 메시지 { type: "keyTest", event } 또는 null.
    /// </summary>
    public string? HandleKeyTest(ushort vk, ModifierFlags mods, bool down, DateTimeOffset at)
    {
        if (_keyTester?.Handle(vk, mods, down, at) is not { } e) return null;
        return new JsonObject { ["type"] = "keyTest", ["event"] = KeyTestJson(e) }.ToJsonString(JsonOptions);
    }

    private static JsonObject KeyTestJson(KeyTestEvent e) => new()
    {
        ["at"] = e.At.ToLocalTime().ToString("HH:mm:ss.fff"),
        ["down"] = e.Down,
        ["key"] = e.Key,
        ["slots"] = new JsonArray(e.Slots.Select(s => (JsonNode)SlotId(s)).ToArray()),
        ["names"] = new JsonArray(e.Slots.Select(s => (JsonNode)$"레이어 {s.Layer + 1} {SlotTitle(s)}{(s.IsButton ? "" : " " + KnobActionName(s.KnobAction))}").ToArray()),
        ["label"] = e.Label,
        ["sinceMs"] = e.SincePreviousMs is { } since ? Math.Round(since) : null,
        ["heldMs"] = e.HeldMs is { } held ? Math.Round(held) : null,
        ["result"] = e.Result,
        ["hint"] = e.Hint,
    };

    private static string KnobActionName(KnobAction a) => a switch
    {
        KnobAction.Ccw => "왼쪽",
        KnobAction.Press => "누르기",
        _ => "오른쪽",
    };

    // ─────────────────────────── 주기적 확인 (창이 활성화될 때, 타이머) ───────────────────────────

    /// <summary>패드 연결 상태 다시 확인. 바뀌었으면 true.</summary>
    public bool RefreshDevice()
    {
        var now = SafeIsConnected();
        if (now == _connected) return false;
        _connected = now;
        return true;
    }

    /// <summary>
    /// 밖에서 바뀐 것 반영: 패드 기록(트레이 메뉴로 쓴 경우), 설정 파일(직접 편집한 경우).
    /// 화면을 다시 그려야 하면 true.
    /// </summary>
    public bool RefreshExternal()
    {
        if (_uploading) return false;
        bool changed = false;

        if (File.Exists(ConfigPath) && File.GetLastWriteTimeUtc(ConfigPath) != _lastWriteUtc)
        {
            try
            {
                Load();
                changed = true;
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                // 편집 중이라 잠깐 깨진 상태일 수 있음 → 지금 내용 유지
            }
        }

        var state = PadState.Load(_statePath);
        if (!SameState(state, _padState))
        {
            _padState = state;
            changed = true;
        }

        return RefreshDevice() || changed;
    }

    private bool SafeIsConnected()
    {
        try { return _platform.IsDeviceConnected(_editor.Config); }
        catch { return false; }
    }

    private static bool SameState(PadState? a, PadState? b) =>
        (a is null && b is null) || (a is not null && b is not null && a.UpdatedAt == b.UpdatedAt && a.Slots.Count == b.Slots.Count);

    // ─────────────────────────── 메시지 처리 ───────────────────────────

    /// <summary>화면에서 온 JSON 메시지 하나 처리 → 답장 JSON</summary>
    public async Task<string> HandleAsync(string json)
    {
        JsonNode? id = null;
        var reply = new JsonObject();
        try
        {
            var msg = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("빈 메시지입니다.");
            id = msg["id"]?.DeepClone();
            var type = msg["type"]?.GetValue<string>() ?? throw new FormatException("메시지 종류가 없습니다.");
            var result = await HandleCoreAsync(type, msg);
            reply["ok"] = true;
            if (result is not null) reply["result"] = result;
        }
        catch (Exception ex) // 화면 쪽 요청 하나가 실패해도 앱은 계속 돌아야 함 → 전부 오류 답장으로
        {
            reply["ok"] = false;
            reply["error"] = ex.Message;
        }

        reply["id"] = id;
        reply["state"] = BuildState();
        return reply.ToJsonString(JsonOptions);
    }

    private async Task<JsonNode?> HandleCoreAsync(string type, JsonObject msg)
    {
        switch (type)
        {
            case "getState":
                return null;

            case "setSlot":
            {
                var slot = ParseSlotId(Str(msg, "slot") ?? throw new FormatException("칸이 지정되지 않았습니다."));
                var value = msg["value"]?.AsObject() ?? throw new FormatException("값이 없습니다.");
                EnsureNotUploading();
                ApplyValue(slot, value);
                return SaveIfDirty();
            }

            case "copySlot":
            {
                // 같은 동작을 다른 칸(다른 레이어 포함)에 복사
                var from = ParseSlotId(Str(msg, "from") ?? throw new FormatException("원본 칸이 없습니다."));
                var to = ParseSlotId(Str(msg, "to") ?? throw new FormatException("대상 칸이 없습니다."));
                EnsureNotUploading();
                switch (_editor.Get(from))
                {
                    case EmptySlot: _editor.Clear(to); break;
                    case PadKeysSlot p: _editor.SetPadKeys(to, p.Text); break;
                    case PcActionSlot pc: _editor.SetPcAction(to, pc.Action.Clone()); break;
                    case InvalidSlot: throw new FormatException("잘못된 값은 복사할 수 없습니다.");
                }
                return SaveIfDirty();
            }

            case "reload":
                Load();
                return null;

            case "keyTest":
                _keyTester = Bool(msg, "enabled") ? new KeyTester(_editor) : null;
                return null;

            case "keyTestInject":
            {
                // 미리보기(tools/UiPreview)용: 패드 없이 화면에서 칸을 눌러 키 테스트를 흉내 냄
                var slot = ParseSlotId(Str(msg, "slot") ?? throw new FormatException("칸이 지정되지 않았습니다."));
                var text = _editor.Config.GetText(slot);
                if (_keyTester is null || string.IsNullOrWhiteSpace(text) || text.Contains(','))
                    return null;
                var chord = Chord.Parse(text);
                if (chord.Key is not { Vk: { } vk }) return null;
                var e = _keyTester.Handle(vk, chord.ModifierFlags, Bool(msg, "down"), DateTimeOffset.Now);
                return e is null ? null : new JsonObject { ["event"] = KeyTestJson(e) };
            }

            case "previewUpload":
            {
                var plan = CreatePlan(Bool(msg, "onlyChanged"));
                return new JsonObject
                {
                    ["items"] = new JsonArray(plan.Bindings.Select(b => (JsonNode)new JsonObject
                    {
                        ["slot"] = SlotId(b.Ref),
                        ["name"] = SlotTitle(b.Ref),
                        ["layer"] = b.Ref.Layer + 1,
                        ["label"] = SlotDisplay.Short(_editor.Get(b.Ref), 60),
                        ["source"] = b.Source,
                    }).ToArray()),
                    ["skipped"] = plan.SkippedUnchanged,
                };
            }

            case "upload":
            {
                var onlyChanged = Bool(msg, "onlyChanged");
                CreatePlan(onlyChanged); // 검증
                RefreshDevice();
                if (!_connected)
                    throw new InvalidOperationException("패드가 연결돼 있지 않습니다. USB를 확인해 주십시오.");

                EnsureNotUploading();
                _uploading = true;
                var log = new List<string>();
                try
                {
                    var written = await _platform.UploadAsync(_editor.Config, onlyChanged, _statePath, log.Add);
                    return new JsonObject { ["written"] = written, ["log"] = string.Join("\n", log) };
                }
                finally
                {
                    _uploading = false;
                    _padState = PadState.Load(_statePath);
                }
            }

            case "browseFile":
                return new JsonObject { ["path"] = _platform.BrowseFile() };

            case "browseFolder":
                return new JsonObject { ["path"] = _platform.BrowseFolder() };

            case "openConfig":
                if (!File.Exists(ConfigPath)) SaveNow();
                _platform.OpenPath(ConfigPath);
                return null;

            case "revealConfig":
                _platform.RevealPath(ConfigPath);
                return null;

            case "openLog":
                if (_platform.LogPath is not { } log2 || !File.Exists(log2))
                    throw new FileNotFoundException("아직 로그가 없습니다.");
                _platform.OpenPath(log2);
                return null;

            case "setStartup":
                _platform.SetStartupEnabled(Bool(msg, "enabled"));
                return null;

            case "testAction":
            {
                var slot = ParseSlotId(Str(msg, "slot") ?? throw new FormatException("칸이 지정되지 않았습니다."));
                if (_editor.Get(slot) is PcActionSlot { Action.Seek: { } seek })
                    return new JsonObject { ["message"] = await _platform.SeekMediaAsync(seek) };
                if (_editor.Get(slot) is not PcActionSlot { Action.Run: { Length: > 0 } } pc)
                    throw new FormatException("미리 실행은 프로그램/웹사이트 열기와 재생 위치 이동만 할 수 있습니다.");
                _platform.RunAction(new ActionConfig { Run = pc.Action.Run, Args = pc.Action.Args, WorkingDir = pc.Action.WorkingDir });
                return null;
            }

            default:
                throw new FormatException($"알 수 없는 요청입니다: {type}");
        }
    }

    private void EnsureNotUploading()
    {
        if (_uploading) throw new InvalidOperationException("패드에 쓰는 중입니다. 잠시 후 다시 해 주십시오.");
    }

    private UploadPlan CreatePlan(bool onlyChanged)
    {
        var plan = UploadPlan.Create(_editor.Config);
        if (!plan.IsValid)
            throw new FormatException("설정에 문제가 있습니다:\n" + string.Join("\n", plan.Errors));
        return onlyChanged ? plan.OnlyChanged(_padState) : plan;
    }

    // ─────────────────────────── 칸 값 적용 ───────────────────────────

    private void ApplyValue(SlotRef slot, JsonObject value)
    {
        var type = Str(value, "type") ?? throw new FormatException("값 종류가 없습니다.");
        switch (type)
        {
            case "empty":
                _editor.Clear(slot);
                break;

            case "pad":
                _editor.SetPadKeys(slot, Str(value, "text") ?? "");
                break;

            case "pc":
            {
                var mode = Str(value, "mode") ?? throw new FormatException("PC 동작 종류가 없습니다.");
                // 같은 종류를 고치는 거면 화면에 없는 값(workingDir, delayMs 등)은 유지
                var action = _editor.Get(slot) is PcActionSlot pc && PcMode(pc.Action) == mode
                    ? pc.Action.Clone()
                    : new ActionConfig();

                switch (mode)
                {
                    case "app":
                    case "web":
                        action.Run = Str(value, "run")?.Trim();
                        action.Args = mode == "app" ? NullIfBlank(Str(value, "args")) : null;
                        if (string.IsNullOrWhiteSpace(action.Run))
                            throw new FormatException(mode == "web" ? "웹사이트 주소를 입력해 주십시오." : "열 프로그램이나 파일을 골라 주십시오.");
                        if (mode == "web" && !action.Run.Contains("://", StringComparison.Ordinal))
                            action.Run = "https://" + action.Run;
                        break;
                    case "text":
                        action.Text = Str(value, "text")?.ReplaceLineEndings("\n");
                        if (string.IsNullOrEmpty(action.Text))
                            throw new FormatException("입력할 문장을 적어 주십시오.");
                        break;
                    case "pckeys":
                        var keys = value["keys"]?.AsArray().Select(k => k?.GetValue<string>()?.Trim())
                            .Where(k => !string.IsNullOrEmpty(k)).Select(k => k!).ToList() ?? new();
                        if (keys.Count == 0)
                            throw new FormatException("보낼 키를 입력해 주십시오.");
                        action.Keys = keys;
                        break;
                    case "seek":
                    {
                        var seconds = Num(value, "seconds");
                        if (seconds is not { } s || !double.IsFinite(s) || s <= 0 || s > ActionConfig.MaxSeekSeconds)
                            throw new FormatException($"이동할 시간은 0초보다 크고 {ActionConfig.MaxSeekSeconds:0}초 이하로 입력해 주십시오.");
                        s = Math.Round(s, 3);
                        action.Seek = Str(value, "direction") == "back" ? -s : s;
                        // 기본값(켬)이면 파일에 안 씀
                        action.SeekAccel = value["accel"] is null || Bool(value, "accel") ? null : false;
                        break;
                    }
                    case "cue":
                    {
                        var n = Num(value, "cue");
                        if (n is not { } c || c != Math.Floor(c) || c < 1 || c > ActionConfig.MaxCue)
                            throw new FormatException($"큐 번호는 1~{ActionConfig.MaxCue} 사이 정수로 입력해 주십시오.");
                        action.Cue = (int)c;
                        break;
                    }
                    case "cueset":
                        action.CueSet = true;
                        break;
                    default:
                        throw new FormatException($"알 수 없는 PC 동작 종류입니다: {mode}");
                }

                _editor.SetPcAction(slot, action);
                break;
            }

            default:
                throw new FormatException($"알 수 없는 값 종류입니다: {type}");
        }
    }

    /// <summary>바뀐 게 있으면 저장. 손으로 쓴 설정 파일을 처음 덮어쓴 거면 결과에 backup 경로를 넣음.</summary>
    private JsonNode? SaveIfDirty()
    {
        if (!_editor.IsDirty) return null;
        return SaveNow();
    }

    private JsonNode? SaveNow()
    {
        var wasHandWritten = _handWritten;
        ConfigWriter.Save(_editor.Config, ConfigPath);
        _editor.MarkSaved();
        _handWritten = false;
        _lastWriteUtc = File.GetLastWriteTimeUtc(ConfigPath);
        return wasHandWritten ? new JsonObject { ["backup"] = ConfigPath + ".bak" } : new JsonObject { ["saved"] = true };
    }

    // ─────────────────────────── 화면 상태 ───────────────────────────

    public JsonObject BuildState()
    {
        var config = _editor.Config;
        var adv = config.Advanced;
        var pending = 0;

        var slots = new JsonArray();
        foreach (var slot in _editor.AllSlots())
        {
            var vm = SlotState(slot);
            if (vm["pending"]?.GetValue<bool>() == true) pending++;
            slots.Add(vm);
        }

        return new JsonObject
        {
            ["config"] = new JsonObject
            {
                ["path"] = ConfigPath,
                ["fileName"] = Path.GetFileName(ConfigPath),
                ["handWritten"] = _handWritten,
            },
            ["device"] = new JsonObject
            {
                ["connected"] = _connected,
                ["vendorId"] = config.Device.VendorId,
                ["productId"] = config.Device.ProductId,
            },
            ["layout"] = new JsonObject
            {
                ["buttonCount"] = adv.ButtonCount,
                ["columns"] = Math.Max(1, adv.ButtonColumns),
                ["knobCount"] = adv.KnobCount,
                ["layerCount"] = Math.Max(1, adv.LayerCount),
            },
            ["slots"] = slots,
            ["upload"] = new JsonObject
            {
                ["hasRecord"] = _padState is not null,
                ["pending"] = pending,
                ["lastWrittenAt"] = _padState?.UpdatedAt?.ToString("yyyy-MM-dd HH:mm"),
                ["busy"] = _uploading,
            },
            ["startup"] = SafeStartup(),
            ["mediaKeys"] = new JsonArray(KeyTable.MediaKeys.Select(k => (JsonNode)new JsonObject
            {
                ["name"] = k.Name,
                ["label"] = KeyNames.Media(k.Name),
            }).ToArray()),
            ["advanced"] = new JsonObject
            {
                ["knobSlotBase"] = adv.KnobSlotBase,
                ["reportIdMode"] = adv.ReportIdMode,
                ["mediaModeByte"] = adv.MediaModeByte,
                ["keyDelay"] = adv.KeyDelay,
                ["packetIntervalMs"] = adv.PacketIntervalMs,
            },
            ["unreferencedActions"] = new JsonArray(_editor.UnreferencedActions().Select(a => (JsonNode)a).ToArray()),
        };
    }

    private bool SafeStartup()
    {
        try { return _platform.IsStartupEnabled(); }
        catch { return false; }
    }

    private JsonObject SlotState(SlotRef slot)
    {
        var assignment = _editor.Get(slot);
        var vm = new JsonObject
        {
            ["id"] = SlotId(slot),
            ["layer"] = slot.Layer,
            ["control"] = slot.IsButton ? "button" : "knob",
            ["index"] = slot.Index,
            ["knobAction"] = slot.IsButton ? null : KnobActionId(slot.KnobAction),
            ["name"] = SlotTitle(slot),
            ["position"] = SlotPosition(slot),
            ["label"] = SlotDisplay.Short(assignment, 40),
            ["detail"] = SlotDisplay.Kind(assignment),
            ["pending"] = _editor.NeedsUpload(slot, _padState),
        };

        switch (assignment)
        {
            case EmptySlot:
                vm["type"] = "empty";
                break;
            case PadKeysSlot { Binding: MediaBinding m } p:
                vm["type"] = "media";
                vm["media"] = m.Name;
                vm["padText"] = p.Text;
                break;
            case PadKeysSlot p:
                vm["type"] = "keys";
                vm["padText"] = p.Text;
                break;
            case PcActionSlot pc:
                vm["type"] = "pc";
                vm["pc"] = new JsonObject
                {
                    ["mode"] = PcMode(pc.Action),
                    ["trigger"] = pc.Trigger,
                    ["run"] = pc.Action.Run,
                    ["args"] = pc.Action.Args,
                    ["keys"] = new JsonArray((pc.Action.Keys ?? new()).Select(k => (JsonNode)k).ToArray()),
                    ["text"] = pc.Action.Text,
                    ["runName"] = pc.Action.Run is { Length: > 0 } r ? SlotDisplay.RunName(r) : null,
                    ["seek"] = pc.Action.Seek,
                    ["seekAccel"] = pc.Action.EffectiveSeekAccel,
                    ["cue"] = pc.Action.Cue,
                };
                break;
            case InvalidSlot bad:
                vm["type"] = "invalid";
                vm["padText"] = bad.Text;
                vm["error"] = bad.Error;
                break;
        }

        if (_padState?.Get(slot.Layer, slot.PadSlot(_editor.Options)) is { } record)
        {
            vm["padRecord"] = new JsonObject
            {
                ["source"] = record.Source,
                ["writtenAt"] = record.WrittenAt.ToString("yyyy-MM-dd HH:mm"),
            };
        }

        return vm;
    }

    /// <summary>PC 동작의 대표 종류: seek(재생 위치 이동) / cue(샘플 큐) / cueset(큐 지정 키) / app(프로그램/파일/폴더) / web / pckeys / text</summary>
    public static string PcMode(ActionConfig action)
    {
        if (action.HasSeek) return "seek";
        if (action.HasCue) return "cue";
        if (action.IsCueSet) return "cueset";
        if (!string.IsNullOrWhiteSpace(action.Run))
            return IsWebAddress(action.Run) ? "web" : "app";
        if (action.Keys is { Count: > 0 }) return "pckeys";
        return "text";
    }

    private static bool IsWebAddress(string run) =>
        Uri.TryCreate(run.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // ─────────────────────────── 칸 이름 / ID ───────────────────────────

    public static string SlotTitle(SlotRef slot) => slot.IsButton ? $"버튼 {slot.Index + 1}" : SlotRef.KnobName(slot.Index);

    public string SlotPosition(SlotRef slot)
    {
        if (!slot.IsButton)
        {
            return slot.KnobAction switch
            {
                KnobAction.Ccw => "왼쪽으로 돌리기",
                KnobAction.Press => "누르기",
                _ => "오른쪽으로 돌리기",
            };
        }
        var cols = Math.Max(1, _editor.Advanced.ButtonColumns);
        return $"{slot.Index / cols + 1}행 {slot.Index % cols + 1}열";
    }

    private static string KnobActionId(KnobAction a) => a switch
    {
        KnobAction.Ccw => "ccw",
        KnobAction.Press => "press",
        _ => "cw",
    };

    /// <summary>"L0-B4" (레이어1 버튼5), "L0-K1-ccw" (레이어1 아래 노브 왼쪽)</summary>
    public static string SlotId(SlotRef slot) => slot.IsButton
        ? $"L{slot.Layer}-B{slot.Index}"
        : $"L{slot.Layer}-K{slot.Index}-{KnobActionId(slot.KnobAction)}";

    public static SlotRef ParseSlotId(string id)
    {
        var parts = id.Split('-');
        try
        {
            if (parts.Length >= 2 && parts[0].StartsWith('L'))
            {
                var layer = int.Parse(parts[0][1..]);
                if (parts.Length == 2 && parts[1].StartsWith('B'))
                    return SlotRef.Button(layer, int.Parse(parts[1][1..]));
                if (parts.Length == 3 && parts[1].StartsWith('K'))
                {
                    var action = parts[2] switch
                    {
                        "ccw" => KnobAction.Ccw,
                        "press" => KnobAction.Press,
                        "cw" => KnobAction.Cw,
                        _ => throw new FormatException(),
                    };
                    return SlotRef.Knob(layer, int.Parse(parts[1][1..]), action);
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
        }
        throw new FormatException($"잘못된 칸 이름입니다: {id}");
    }

    // ─────────────────────────── JSON 도우미 ───────────────────────────

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool Bool(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>숫자 값 (문자열 "1.5" 도 받음 — 화면 입력칸 값 그대로 올 수 있어서)</summary>
    private static double? Num(JsonObject o, string name)
    {
        if (o[name] is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<string>(out var s)
            && double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p))
            return p;
        return null;
    }
}
