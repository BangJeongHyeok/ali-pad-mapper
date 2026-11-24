using System.Text.Json.Nodes;
using MacroPad.Core.Config;
using MacroPad.Core.Editing;
using MacroPad.Core.Media;
using MacroPad.Core.Protocol;
using Xunit;

namespace MacroPad.Tests;

/// <summary>샘플 큐: 큐 지정 키(펑션) + 큐 키로 재생 위치 저장/이동</summary>
public sealed class CueTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static MediaTimeline Song(double positionSec, bool playing = true, double lengthSec = 240, DateTimeOffset? updated = null) =>
        new(TimeSpan.FromSeconds(positionSec), updated ?? T0, playing,
            TimeSpan.Zero, TimeSpan.FromSeconds(lengthSec), TimeSpan.Zero, TimeSpan.FromSeconds(lengthSec));

    private static readonly CuePoint P = new(TimeSpan.FromSeconds(30), "yt", "곡");

    // ─────────────── CueBank: 이동/저장 판단 ───────────────

    [Fact]
    public void EmptyCue_IsSetOnFirstPress_ThenJumps()
    {
        var bank = new CueBank();
        Assert.Equal(CuePress.SetEmpty, bank.Press(T0, 1));
        bank.Store(1, P);
        Assert.Equal(CuePress.Jump, bank.Press(T0.AddSeconds(1), 1));
        Assert.Equal(CuePress.SetEmpty, bank.Press(T0.AddSeconds(2), 2)); // 다른 큐는 아직 비어 있음

        // 큐 지정 키로 저장하면 비어 있어도 Set (알림 대상)
        bank.SetKeyDown(T0.AddSeconds(3));
        Assert.Equal(CuePress.Set, bank.Press(T0.AddSeconds(3.5), 3));
    }

    [Fact]
    public void HoldingSetKey_OverwritesExistingCue()
    {
        var bank = new CueBank();
        bank.Store(1, P);

        bank.SetKeyDown(T0);
        Assert.True(bank.IsSetHeld);
        Assert.Equal(CuePress.Set, bank.Press(T0.AddMilliseconds(600), 1));
        Assert.Equal(CuePress.Set, bank.Press(T0.AddMilliseconds(900), 1)); // 누르고 있는 동안은 계속
        bank.SetKeyUp(T0.AddSeconds(1));

        Assert.False(bank.IsSetHeld);
        Assert.Equal(CuePress.Jump, bank.Press(T0.AddSeconds(2), 1));
    }

    [Fact]
    public void QuickHoldWithCue_DoesNotArmAfterRelease()
    {
        // 짧게 눌렀어도 그 사이에 큐 키를 눌렀으면 '톡'이 아님
        var bank = new CueBank();
        bank.Store(1, P);
        bank.SetKeyDown(T0);
        bank.Press(T0.AddMilliseconds(100), 1);
        bank.SetKeyUp(T0.AddMilliseconds(200));

        Assert.False(bank.IsArmed(T0.AddMilliseconds(300)));
        Assert.Equal(CuePress.Jump, bank.Press(T0.AddMilliseconds(400), 1));
    }

    [Fact]
    public void TapSetKey_ArmsNextPressOnly()
    {
        // 패드가 키를 바로 떼서 보내도 쓸 수 있게: 톡 → 다음 큐 한 번만 저장
        var bank = new CueBank();
        bank.Store(1, P);
        bank.SetKeyDown(T0);
        bank.SetKeyUp(T0.AddMilliseconds(30));

        Assert.True(bank.IsArmed(T0.AddSeconds(1)));
        Assert.Equal(CuePress.Set, bank.Press(T0.AddSeconds(1), 1));
        Assert.Equal(CuePress.Jump, bank.Press(T0.AddSeconds(2), 1));
    }

    [Fact]
    public void TapArm_ExpiresAndCanBeCancelledByTappingAgain()
    {
        var bank = new CueBank();
        bank.Store(1, P);

        bank.SetKeyDown(T0);
        bank.SetKeyUp(T0.AddMilliseconds(30));
        Assert.Equal(CuePress.Jump, bank.Press(T0 + CueBank.ArmTimeout + TimeSpan.FromSeconds(1), 1));

        var t1 = T0.AddSeconds(10);
        bank.SetKeyDown(t1);
        bank.SetKeyUp(t1.AddMilliseconds(30));
        bank.SetKeyDown(t1.AddMilliseconds(300)); // 한 번 더 톡 = 취소
        bank.SetKeyUp(t1.AddMilliseconds(330));
        Assert.False(bank.IsArmed(t1.AddMilliseconds(400)));
        Assert.Equal(CuePress.Jump, bank.Press(t1.AddMilliseconds(500), 1));
    }

    [Fact]
    public void LongHoldWithoutCue_DoesNotArm()
    {
        var bank = new CueBank();
        bank.Store(1, P);
        bank.SetKeyDown(T0);
        bank.SetKeyUp(T0.AddSeconds(1));
        Assert.Equal(CuePress.Jump, bank.Press(T0.AddSeconds(1.5), 1));
    }

    [Fact]
    public void LostKeyUp_IsForgottenAfterAWhile()
    {
        var bank = new CueBank();
        bank.Store(1, P);
        bank.SetKeyDown(T0); // '뗌'이 오지 않음
        Assert.Equal(CuePress.Jump, bank.Press(T0 + CueBank.StuckAfter + TimeSpan.FromSeconds(1), 1));
    }

    [Fact]
    public void TwoSetKeys_StayHeldUntilBothReleased()
    {
        var bank = new CueBank();
        bank.Store(1, P);
        bank.SetKeyDown(T0);
        bank.SetKeyDown(T0.AddMilliseconds(500));
        bank.SetKeyUp(T0.AddMilliseconds(800));
        Assert.Equal(CuePress.Set, bank.Press(T0.AddMilliseconds(900), 1));
        bank.SetKeyUp(T0.AddSeconds(1));
        Assert.Equal(CuePress.Jump, bank.Press(T0.AddSeconds(2), 1));
    }

    [Fact]
    public void Clear_RemovesAllCues()
    {
        var bank = new CueBank();
        bank.Store(1, P);
        bank.Store(5, P);
        Assert.Equal(2, bank.Count);
        bank.Clear();
        Assert.Equal(0, bank.Count);
        Assert.False(bank.TryGet(1, out _));
    }

    // ─────────────── SeekTracker: 지금 위치 / 바로 이동 ───────────────

    [Fact]
    public void Current_ExtrapolatesFromLastUpdate()
    {
        var t = new SeekTracker();
        Assert.Equal(TimeSpan.FromSeconds(62.5), t.Current(T0.AddSeconds(2.5), "yt", Song(60)));
        Assert.Equal(TimeSpan.FromSeconds(60), t.Current(T0.AddSeconds(2.5), "yt", Song(60, playing: false)));
    }

    [Fact]
    public void JumpTo_ClampsToSongAndIsRememberedForNextCapture()
    {
        var t = new SeekTracker();
        // 큐가 다른(더 긴) 곡에서 저장된 경우: 곡 끝 1초 전까지
        Assert.Equal(TimeSpan.FromSeconds(239), t.JumpTo(T0, "yt", Song(10), TimeSpan.FromSeconds(500)));

        Assert.Equal(TimeSpan.FromSeconds(30), t.JumpTo(T0, "yt", Song(10), TimeSpan.FromSeconds(30)));
        // 플레이어가 아직 옛 위치(10초)를 알려줘도, 방금 이동한 위치 + 흐른 시간으로 계산
        Assert.Equal(TimeSpan.FromSeconds(30.5), t.Current(T0.AddMilliseconds(500), "yt", Song(10)));
        // 이어서 노브로 감으면 이동한 위치 기준
        Assert.Equal(TimeSpan.FromSeconds(31.5), t.Target(T0.AddMilliseconds(500), "yt", Song(10), 1));
    }

    // ─────────────── 설정 파일 ───────────────

    [Fact]
    public void Config_ParsesValidatesAndWritesCue()
    {
        var config = PadConfig.Parse("""{ "actions": { "f13": { "cueSet": true }, "f14": { "cue": 3 } } }""");
        Assert.True(config.Actions["f13"].IsCueSet);
        Assert.Equal(3, config.Actions["f14"].Cue);
        config.Actions["f13"].ParseKeys();
        config.Actions["f14"].ParseKeys();

        var text = ConfigWriter.Serialize(config);
        Assert.Contains("\"cueSet\": true", text);
        Assert.Contains("\"cue\": 3", text);
        var again = PadConfig.Parse(text);
        Assert.Equal(3, again.Actions["f14"].Cue);
        Assert.True(again.Actions["f13"].IsCueSet);
    }

    [Theory]
    [InlineData("""{ "cue": 0 }""")]
    [InlineData("""{ "cue": 100 }""")]
    [InlineData("""{ "cue": 1, "cueSet": true }""")]
    [InlineData("""{ "cueSet": false }""")]
    public void Config_RejectsBadCue(string action)
    {
        var config = PadConfig.Parse($$"""{ "actions": { "f13": {{action}} } }""");
        Assert.Throws<FormatException>(() => config.Actions["f13"].ParseKeys());
    }

    // ─────────────── 편집 모델: 큐 지정 키는 수식 키 없는 트리거만 ───────────────

    [Fact]
    public void CueSetKey_GetsPlainTrigger_EvenWhenOnlyModifiedOnesAreLeftAfterIt()
    {
        var editor = new ConfigEditor(PadConfig.Parse("{}"));
        // f13~f23 을 다른 PC 동작으로 채움
        for (int i = 0; i < 11; i++)
            editor.SetPcAction(SlotRef.Button(0, i), new ActionConfig { Cue = i + 1 });

        editor.SetPcAction(SlotRef.Button(0, 11), new ActionConfig { CueSet = true });
        Assert.Equal("f24", editor.Config.GetText(SlotRef.Button(0, 11)));

        // 이제 수식 키 없는 F 키가 없음 → 일반 동작은 shift+ 로, 큐 지정 키는 오류
        editor.SetPcAction(SlotRef.Button(1, 0), new ActionConfig { Cue = 20 });
        Assert.Equal("shift+f13", editor.Config.GetText(SlotRef.Button(1, 0)));
        var ex = Assert.Throws<FormatException>(() => editor.SetPcAction(SlotRef.Button(1, 1), new ActionConfig { CueSet = true }));
        Assert.Contains("F13~F24", ex.Message);
    }

    [Fact]
    public void CueSetKey_ReplacesModifiedTriggerOfSameSlot()
    {
        var config = PadConfig.Parse("""{ "layers": [ { "buttons": ["shift+f13"] } ], "actions": { "shift+f13": { "text": "x" } } }""");
        var editor = new ConfigEditor(config);
        var trigger = editor.SetPcAction(SlotRef.Button(0, 0), new ActionConfig { CueSet = true });
        Assert.Equal("f13", trigger);
        Assert.False(config.Actions.ContainsKey("shift+f13"));
        Assert.True(ConfigEditor.IsPlainTrigger("f13"));
        Assert.False(ConfigEditor.IsPlainTrigger("ctrl+f13"));
    }

    // ─────────────── 편집기 화면 메시지 ───────────────

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "macropad-cue-" + Guid.NewGuid().ToString("N"));

    public CueTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task<JsonObject> Send(EditorSession session, object message)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(message, EditorSession.JsonOptions);
        return JsonNode.Parse(await session.HandleAsync(json))!.AsObject();
    }

    private static JsonObject Slot(JsonObject reply, string id) =>
        reply["state"]!["slots"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == id)!.AsObject();

    [Fact]
    public async Task Session_SetsCueAndCueSetKeys()
    {
        var path = Path.Combine(_dir, "macropad.json");
        File.WriteAllText(path, "{}");
        var session = new EditorSession(path, new SessionTests.FakePlatform(), Path.Combine(_dir, "state.json"));

        var reply = await Send(session, new { id = 1, type = "setSlot", slot = "L0-B0", value = new { type = "pc", mode = "cueset" } });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        reply = await Send(session, new { id = 2, type = "setSlot", slot = "L0-B1", value = new { type = "pc", mode = "cue", cue = 1 } });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        reply = await Send(session, new { id = 3, type = "setSlot", slot = "L0-B2", value = new { type = "pc", mode = "cue", cue = "2" } });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());

        var fn = Slot(reply, "L0-B0");
        Assert.Equal("cueset", fn["pc"]!["mode"]!.GetValue<string>());
        Assert.Equal("[큐 지정 키]", fn["label"]!.GetValue<string>());
        var cue = Slot(reply, "L0-B2");
        Assert.Equal("cue", cue["pc"]!["mode"]!.GetValue<string>());
        Assert.Equal(2, cue["pc"]!["cue"]!.GetValue<int>());
        Assert.Equal("[큐] 2", cue["label"]!.GetValue<string>());

        var config = PadConfig.Load(path);
        Assert.True(config.Actions[config.GetText(SlotRef.Button(0, 0))!].IsCueSet);
        Assert.Equal(1, config.Actions[config.GetText(SlotRef.Button(0, 1))!].Cue);

        // 큐 번호를 바꿔도 같은 트리거 유지 (패드에 다시 쓸 필요 없음)
        var before = config.GetText(SlotRef.Button(0, 1));
        reply = await Send(session, new { id = 4, type = "setSlot", slot = "L0-B1", value = new { type = "pc", mode = "cue", cue = 7 } });
        Assert.True(reply["ok"]!.GetValue<bool>(), reply["error"]?.ToString());
        Assert.Equal(before, PadConfig.Load(path).GetText(SlotRef.Button(0, 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    [InlineData(100)]
    public async Task Session_RejectsBadCueNumber(double cue)
    {
        var path = Path.Combine(_dir, "macropad.json");
        File.WriteAllText(path, "{}");
        var session = new EditorSession(path, new SessionTests.FakePlatform(), Path.Combine(_dir, "state.json"));
        var reply = await Send(session, new { id = 1, type = "setSlot", slot = "L0-B1", value = new { type = "pc", mode = "cue", cue } });
        Assert.False(reply["ok"]!.GetValue<bool>());
        Assert.Contains("큐 번호", reply["error"]!.GetValue<string>());
    }
}
