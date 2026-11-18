using System.Text.Json.Nodes;
using MacroPad.Core.Config;
using MacroPad.Core.Editing;
using MacroPad.Core.Protocol;
using Xunit;

namespace MacroPad.Tests;

/// <summary>편집기 화면 ↔ C# 메시지 처리 (패드/OS 없이 가짜 플랫폼으로)</summary>
public sealed class SessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "macropad-session-" + Guid.NewGuid().ToString("N"));
    private readonly FakePlatform _platform = new();

    public SessionTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string ConfigPath => Path.Combine(_dir, "macropad.json");
    private string StatePath => Path.Combine(_dir, "state.json");

    private EditorSession NewSession(string? configText = null)
    {
        if (configText is not null) File.WriteAllText(ConfigPath, configText);
        return new EditorSession(ConfigPath, _platform, StatePath);
    }

    private static async Task<JsonObject> Send(EditorSession session, object message)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(message, EditorSession.JsonOptions);
        return JsonNode.Parse(await session.HandleAsync(json))!.AsObject();
    }

    private static JsonObject Slot(JsonObject reply, string id) =>
        reply["state"]!["slots"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == id)!.AsObject();

    [Fact]
    public async Task GetState_DescribesEverySlot()
    {
        var session = NewSession(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestConfigs", "macropad.json")));
        var reply = await Send(session, new { id = 1, type = "getState" });

        Assert.True(reply["ok"]!.GetValue<bool>());
        Assert.Equal(1, reply["id"]!.GetValue<int>());
        Assert.Equal(18 * 3, reply["state"]!["slots"]!.AsArray().Count);
        Assert.True(reply["state"]!["config"]!["handWritten"]!.GetValue<bool>());

        var play = Slot(reply, "L0-B0");
        Assert.Equal("media", play["type"]!.GetValue<string>());
        Assert.Equal("재생/정지", play["label"]!.GetValue<string>());

        var notepad = Slot(reply, "L0-B4");
        Assert.Equal("pc", notepad["type"]!.GetValue<string>());
        Assert.Equal("app", notepad["pc"]!["mode"]!.GetValue<string>());
        Assert.Equal("f13", notepad["pc"]!["trigger"]!.GetValue<string>());

        var google = Slot(reply, "L0-B8");
        Assert.Equal("web", google["pc"]!["mode"]!.GetValue<string>());

        var knob = Slot(reply, "L0-K0-cw");
        Assert.Equal("위 노브", knob["name"]!.GetValue<string>());
        Assert.Equal("오른쪽으로 돌리기", knob["position"]!.GetValue<string>());

        // 기록이 없으니 비어 있지 않은 칸은 전부 "패드에 적용 필요"
        Assert.False(reply["state"]!["upload"]!["hasRecord"]!.GetValue<bool>());
        Assert.Equal(18, reply["state"]!["upload"]!["pending"]!.GetValue<int>());
    }

    [Fact]
    public async Task SetSlot_AutoSavesAndBacksUpHandWrittenFileOnce()
    {
        var session = NewSession("// 손으로 쓴 파일\n{}");

        var reply = await Send(session, new { id = 1, type = "setSlot", slot = "L0-B0", value = new { type = "pad", text = "ctrl+c" } });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        Assert.Equal(ConfigPath + ".bak", reply["result"]!["backup"]!.GetValue<string>());
        Assert.Equal("ctrl+c", PadConfig.Load(ConfigPath).GetText(SlotRef.Button(0, 0)));
        Assert.Equal("// 손으로 쓴 파일\n{}", File.ReadAllText(ConfigPath + ".bak"));

        reply = await Send(session, new { id = 2, type = "setSlot", slot = "L0-B1", value = new { type = "pad", text = "volumeup" } });
        Assert.Null(reply["result"]!["backup"]);
        Assert.Equal("// 손으로 쓴 파일\n{}", File.ReadAllText(ConfigPath + ".bak")); // 원본 백업 유지
    }

    [Fact]
    public async Task SetSlot_InvalidValueReturnsErrorAndKeepsConfig()
    {
        var session = NewSession();
        await Send(session, new { type = "setSlot", slot = "L0-B0", value = new { type = "pad", text = "a" } });

        var reply = await Send(session, new { type = "setSlot", slot = "L0-B0", value = new { type = "pad", text = "ctrl+volumeup" } });
        Assert.False(reply["ok"]!.GetValue<bool>());
        Assert.NotNull(reply["error"]);
        Assert.Equal("keys", Slot(reply, "L0-B0")["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task SetSlot_PcModes()
    {
        var session = NewSession();

        var reply = await Send(session, new { type = "setSlot", slot = "L0-B0", value = new { type = "pc", mode = "web", run = "example.com" } });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        Assert.Equal("https://example.com", Slot(reply, "L0-B0")["pc"]!["run"]!.GetValue<string>());

        // 종류를 바꾸면 이전 값은 사라지고, 트리거는 유지 (패드에 다시 쓸 필요 없음)
        reply = await Send(session, new { type = "setSlot", slot = "L0-B0", value = new { type = "pc", mode = "text", text = "안녕하세요" } });
        var pc = Slot(reply, "L0-B0")["pc"]!;
        Assert.Equal("text", pc["mode"]!.GetValue<string>());
        Assert.Null(pc["run"]);
        Assert.Equal("f13", pc["trigger"]!.GetValue<string>());

        reply = await Send(session, new { type = "setSlot", slot = "L0-B1", value = new { type = "pc", mode = "pckeys", keys = new[] { "win+l" } } });
        Assert.Equal("f14", Slot(reply, "L0-B1")["pc"]!["trigger"]!.GetValue<string>());

        reply = await Send(session, new { type = "setSlot", slot = "L0-B2", value = new { type = "pc", mode = "app", run = "  " } });
        Assert.False(reply["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CopySlot_ToOtherLayer()
    {
        var session = NewSession();
        await Send(session, new { type = "setSlot", slot = "L0-B0", value = new { type = "pc", mode = "app", run = "notepad.exe" } });
        var reply = await Send(session, new { type = "copySlot", from = "L0-B0", to = "L1-B0" });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        Assert.Equal("notepad.exe", Slot(reply, "L1-B0")["pc"]!["run"]!.GetValue<string>());
    }

    [Fact]
    public async Task Upload_RecordsStateAndClearsPending()
    {
        var session = NewSession();
        await Send(session, new { type = "setSlot", slot = "L0-K0-cw", value = new { type = "pad", text = "volumeup" } });
        await Send(session, new { type = "setSlot", slot = "L0-K0-ccw", value = new { type = "pad", text = "volumedown" } });

        var preview = await Send(session, new { type = "previewUpload", onlyChanged = true });
        Assert.Equal(2, preview["result"]!["items"]!.AsArray().Count);

        _platform.Connected = false;
        var failed = await Send(session, new { type = "upload", onlyChanged = true });
        Assert.False(failed["ok"]!.GetValue<bool>());

        _platform.Connected = true;
        var reply = await Send(session, new { type = "upload", onlyChanged = true });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        Assert.Equal(2, reply["result"]!["written"]!.GetValue<int>());
        Assert.Equal(0, reply["state"]!["upload"]!["pending"]!.GetValue<int>());
        Assert.True(reply["state"]!["device"]!["connected"]!.GetValue<bool>());

        // 하나만 바꾸면 그것만 다시 써야 함
        await Send(session, new { type = "setSlot", slot = "L0-K0-cw", value = new { type = "pad", text = "next" } });
        preview = await Send(session, new { type = "previewUpload", onlyChanged = true });
        Assert.Single(preview["result"]!["items"]!.AsArray());
        Assert.Equal(1, preview["result"]!["skipped"]!.GetValue<int>());
    }

    [Fact]
    public async Task RefreshExternal_PicksUpHandEdits()
    {
        var session = NewSession("{}");
        Assert.False(session.RefreshExternal());

        File.WriteAllText(ConfigPath, """{ "layers": [ { "buttons": ["mute"] } ] }""");
        File.SetLastWriteTimeUtc(ConfigPath, DateTime.UtcNow.AddSeconds(5));
        Assert.True(session.RefreshExternal());

        var reply = await Send(session, new { type = "getState" });
        Assert.Equal("media", Slot(reply, "L0-B0")["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownMessage_IsErrorNotCrash()
    {
        var session = NewSession();
        var reply = await Send(session, new { type = "nope" });
        Assert.False(reply["ok"]!.GetValue<bool>());
        Assert.NotNull(reply["state"]);

        var bad = JsonNode.Parse(await session.HandleAsync("not json"))!;
        Assert.False(bad["ok"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("L0-B0")]
    [InlineData("L2-B11")]
    [InlineData("L1-K0-press")]
    [InlineData("L0-K1-ccw")]
    public void SlotId_RoundTrips(string id) => Assert.Equal(id, EditorSession.SlotId(EditorSession.ParseSlotId(id)));

    [Fact]
    public async Task SetSlot_SeekOnKnob_SavesActionAndCanBeTested()
    {
        var session = NewSession("{}");

        var reply = await Send(session, new { id = 1, type = "setSlot", slot = "L0-K0-ccw", value = new { type = "pc", mode = "seek", seconds = "0.5", direction = "back", accel = true } });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        reply = await Send(session, new { id = 2, type = "setSlot", slot = "L0-K0-cw", value = new { type = "pc", mode = "seek", seconds = 0.5, direction = "forward", accel = false } });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());

        var ccw = Slot(reply, "L0-K0-ccw");
        Assert.Equal("seek", ccw["pc"]!["mode"]!.GetValue<string>());
        Assert.Equal(-0.5, ccw["pc"]!["seek"]!.GetValue<double>());
        Assert.True(ccw["pc"]!["seekAccel"]!.GetValue<bool>());
        Assert.Equal("[이동] 0.5초 뒤로", ccw["label"]!.GetValue<string>());

        // 파일에는 기본값(가속 켬)은 안 쓰고, 끈 것만 씀
        var config = PadConfig.Load(ConfigPath);
        var ccwTrigger = config.GetText(SlotRef.Knob(0, 0, KnobAction.Ccw))!;
        var cwTrigger = config.GetText(SlotRef.Knob(0, 0, KnobAction.Cw))!;
        Assert.NotEqual(ccwTrigger, cwTrigger);
        Assert.Equal(-0.5, config.Actions[ccwTrigger]!.Seek);
        Assert.Null(config.Actions[ccwTrigger]!.SeekAccel);
        Assert.False(config.Actions[cwTrigger]!.SeekAccel);
        Assert.Contains("\"seek\": -0.5", File.ReadAllText(ConfigPath));

        reply = await Send(session, new { id = 3, type = "testAction", slot = "L0-K0-ccw" });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        Assert.Equal([-0.5], _platform.Seeks);
        Assert.Equal("테스트 · -0.5", reply["result"]!["message"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("601")]
    [InlineData("abc")]
    public async Task SetSlot_SeekRejectsBadSeconds(string seconds)
    {
        var session = NewSession("{}");
        var reply = await Send(session, new { id = 1, type = "setSlot", slot = "L0-K0-ccw", value = new { type = "pc", mode = "seek", seconds, direction = "back" } });
        Assert.False(reply["ok"]!.GetValue<bool>());
        Assert.Equal("empty", Slot(reply, "L0-K0-ccw")["type"]!.GetValue<string>());
    }

    private sealed class FakePlatform : IEditorPlatform
    {
        public bool Connected { get; set; } = true;

        public bool IsDeviceConnected(PadConfig config) => Connected;

        public Task<int> UploadAsync(PadConfig config, bool onlyChanged, string statePath, Action<string> log)
        {
            // 실제 전송 대신 기록만 남김
            var plan = UploadPlan.Create(config);
            var state = PadState.Load(statePath);
            if (onlyChanged) plan = plan.OnlyChanged(state);
            state ??= new PadState();
            foreach (var b in plan.Bindings) state.Record(b);
            state.Save(statePath);
            return Task.FromResult(plan.Bindings.Count);
        }

        public string? BrowseFile() => null;
        public string? BrowseFolder() => null;
        public void OpenPath(string path) { }
        public void RevealPath(string path) { }
        public string? LogPath => null;
        public bool IsStartupEnabled() => false;
        public void SetStartupEnabled(bool enabled) { }
        public void RunAction(ActionConfig action) { }

        public List<double> Seeks { get; } = new();

        public Task<string> SeekMediaAsync(double seconds)
        {
            Seeks.Add(seconds);
            return Task.FromResult($"테스트 · {seconds}");
        }
    }
}
