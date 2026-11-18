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

    public string ConfigPath { get; }

    public PadConfig Config => _editor.Config;

    /// <summary>패드에 쓰는 중인지 (이때 창을 닫으면 안 됨)</summary>
    public bool IsBusy => _uploading;

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
    }

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
            var msg = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("빈 메시지예요.");
            id = msg["id"]?.DeepClone();
            var type = msg["type"]?.GetValue<string>() ?? throw new FormatException("메시지 종류가 없어요.");
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
                var slot = ParseSlotId(Str(msg, "slot") ?? throw new FormatException("칸이 지정되지 않았어요."));
                var value = msg["value"]?.AsObject() ?? throw new FormatException("값이 없어요.");
                EnsureNotUploading();
                ApplyValue(slot, value);
                return SaveIfDirty();
            }

            case "copySlot":
            {
                // 같은 동작을 다른 칸(다른 레이어 포함)에 복사
                var from = ParseSlotId(Str(msg, "from") ?? throw new FormatException("원본 칸이 없어요."));
                var to = ParseSlotId(Str(msg, "to") ?? throw new FormatException("대상 칸이 없어요."));
                EnsureNotUploading();
                switch (_editor.Get(from))
                {
                    case EmptySlot: _editor.Clear(to); break;
                    case PadKeysSlot p: _editor.SetPadKeys(to, p.Text); break;
                    case PcActionSlot pc: _editor.SetPcAction(to, pc.Action.Clone()); break;
                    case InvalidSlot: throw new FormatException("잘못된 값은 복사할 수 없어요.");
                }
                return SaveIfDirty();
            }

            case "reload":
                Load();
                return null;

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
                    throw new InvalidOperationException("패드가 연결돼 있지 않아요. USB를 확인해 주세요.");

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
                    throw new FileNotFoundException("아직 로그가 없어요.");
                _platform.OpenPath(log2);
                return null;

            case "setStartup":
                _platform.SetStartupEnabled(Bool(msg, "enabled"));
                return null;

            case "testAction":
            {
                var slot = ParseSlotId(Str(msg, "slot") ?? throw new FormatException("칸이 지정되지 않았어요."));
                if (_editor.Get(slot) is not PcActionSlot { Action.Run: { Length: > 0 } } pc)
                    throw new FormatException("미리 실행은 프로그램/웹사이트 열기만 할 수 있어요.");
                _platform.RunAction(new ActionConfig { Run = pc.Action.Run, Args = pc.Action.Args, WorkingDir = pc.Action.WorkingDir });
                return null;
            }

            default:
                throw new FormatException($"알 수 없는 요청이에요: {type}");
        }
    }

    private void EnsureNotUploading()
    {
        if (_uploading) throw new InvalidOperationException("패드에 쓰는 중이에요. 잠시 후 다시 해 주세요.");
    }

    private UploadPlan CreatePlan(bool onlyChanged)
    {
        var plan = UploadPlan.Create(_editor.Config);
        if (!plan.IsValid)
            throw new FormatException("설정에 문제가 있어요:\n" + string.Join("\n", plan.Errors));
        return onlyChanged ? plan.OnlyChanged(_padState) : plan;
    }

    // ─────────────────────────── 칸 값 적용 ───────────────────────────

    private void ApplyValue(SlotRef slot, JsonObject value)
    {
        var type = Str(value, "type") ?? throw new FormatException("값 종류가 없어요.");
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
                var mode = Str(value, "mode") ?? throw new FormatException("PC 동작 종류가 없어요.");
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
                            throw new FormatException(mode == "web" ? "웹사이트 주소를 입력해 주세요." : "열 프로그램이나 파일을 골라 주세요.");
                        if (mode == "web" && !action.Run.Contains("://", StringComparison.Ordinal))
                            action.Run = "https://" + action.Run;
                        break;
                    case "text":
                        action.Text = Str(value, "text")?.ReplaceLineEndings("\n");
                        if (string.IsNullOrEmpty(action.Text))
                            throw new FormatException("입력할 문장을 적어 주세요.");
                        break;
                    case "pckeys":
                        var keys = value["keys"]?.AsArray().Select(k => k?.GetValue<string>()?.Trim())
                            .Where(k => !string.IsNullOrEmpty(k)).Select(k => k!).ToList() ?? new();
                        if (keys.Count == 0)
                            throw new FormatException("보낼 키를 입력해 주세요.");
                        action.Keys = keys;
                        break;
                    default:
                        throw new FormatException($"알 수 없는 PC 동작 종류예요: {mode}");
                }

                _editor.SetPcAction(slot, action);
                break;
            }

            default:
                throw new FormatException($"알 수 없는 값 종류예요: {type}");
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

    /// <summary>PC 동작의 대표 종류: app(프로그램/파일/폴더) / web / pckeys / text</summary>
    public static string PcMode(ActionConfig action)
    {
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
        throw new FormatException($"잘못된 칸 이름이에요: {id}");
    }

    // ─────────────────────────── JSON 도우미 ───────────────────────────

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool Bool(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
