using System.Text.Json.Nodes;
using MacroPad.Core.Config;
using MacroPad.Core.Editing;
using MacroPad.Core.Keys;
using Xunit;

namespace MacroPad.Tests;

/// <summary>편집기 '키 테스트': 칸 찾기, 누른 시간, 샘플 큐 판단 흉내, 힌트</summary>
public sealed class KeyTestTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const ushort F13 = 0x7C, F14 = 0x7D, F20 = 0x83, A = 0x41, Shift = 0x10;

    private static KeyTester Tester() => new(new ConfigEditor(PadConfig.Parse("""
        {
          "layers": [ { "buttons": ["f20", "f13", "shift+f14", "volumeup", "a"] } ],
          "actions": { "f20": { "cueSet": true }, "f13": { "cue": 1 }, "shift+f14": { "text": "x" } }
        }
        """)));

    [Fact]
    public void FindsSlotsAndMeasuresHold()
    {
        var t = Tester();
        var down = t.Handle(F13, ModifierFlags.None, true, T0)!;
        Assert.True(down.Down);
        Assert.Equal("F13", down.Key);
        Assert.Equal("L0-B1", EditorSession.SlotId(Assert.Single(down.Slots)));
        Assert.Null(down.SincePreviousMs);

        Assert.Null(t.Handle(F13, ModifierFlags.None, true, T0.AddMilliseconds(500))); // 자동 반복
        var up = t.Handle(F13, ModifierFlags.None, false, T0.AddMilliseconds(700))!;
        Assert.False(up.Down);
        Assert.Equal(700, up.HeldMs);
        Assert.Equal(700, up.SincePreviousMs);
    }

    [Fact]
    public void MatchesModifiersAndPadKeys_IgnoresOthers()
    {
        var t = Tester();
        Assert.Null(t.Handle(Shift, ModifierFlags.None, true, T0));            // 수식 키 자체
        var e = t.Handle(F14, ModifierFlags.Shift, true, T0)!;
        Assert.Equal("L0-B2", EditorSession.SlotId(Assert.Single(e.Slots)));
        Assert.Equal("Shift+F14", e.Key);

        Assert.Equal("L0-B3", EditorSession.SlotId(Assert.Single(t.Handle(0xAF, ModifierFlags.None, true, T0)!.Slots))); // 볼륨 +
        Assert.Equal("L0-B4", EditorSession.SlotId(Assert.Single(t.Handle(A, ModifierFlags.None, true, T0)!.Slots)));

        Assert.Null(t.Handle(0x42, ModifierFlags.None, true, T0));             // 설정에 없는 일반 키(B)는 기록 안 함
        var unknown = t.Handle(0x87, ModifierFlags.None, true, T0)!;           // F24: 설정엔 없지만 신호 키
        Assert.Empty(unknown.Slots);
    }

    [Fact]
    public void SimulatesCueDecisions()
    {
        var t = Tester();
        Assert.Equal("빈 큐라 저장", t.Handle(F13, 0, true, T0)!.Result);
        t.Handle(F13, 0, false, T0.AddMilliseconds(50));
        Assert.Equal("이동", t.Handle(F13, 0, true, T0.AddSeconds(1))!.Result);
        t.Handle(F13, 0, false, T0.AddSeconds(1.05));

        t.Handle(F20, 0, true, T0.AddSeconds(2));
        var save = t.Handle(F13, 0, true, T0.AddSeconds(2.6))!;
        Assert.Equal("저장 (큐 지정 키 누르는 중)", save.Result);
        Assert.Null(save.Hint);
    }

    [Fact]
    public void HintsWhenSetKeyReleasedJustBeforeCue()
    {
        var t = Tester();
        t.Handle(F13, 0, true, T0);
        t.Handle(F13, 0, false, T0.AddMilliseconds(50));

        t.Handle(F20, 0, true, T0.AddSeconds(1));
        t.Handle(F20, 0, false, T0.AddSeconds(1.6));
        var e = t.Handle(F13, 0, true, T0.AddSeconds(1.64))!;
        Assert.Equal("이동", e.Result);
        Assert.Contains("40ms 먼저 떼졌습니다", e.Hint);
    }

    [Fact]
    public void TapOnSetKey_IsReported()
    {
        var t = Tester();
        t.Handle(F20, 0, true, T0);
        Assert.Contains("짧게 톡", t.Handle(F20, 0, false, T0.AddMilliseconds(40))!.Result);
        Assert.Equal("저장 (톡 다음 한 번)", t.Handle(F13, 0, true, T0.AddSeconds(1))!.Result);
    }

    [Fact]
    public async Task Session_TurnsKeyTestOnAndOff()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macropad-kt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "macropad.json");
            File.WriteAllText(path, """{ "layers": [ { "buttons": ["f13"] } ], "actions": { "f13": { "cue": 2 } } }""");
            var session = new EditorSession(path, new SessionTests.FakePlatform(), Path.Combine(dir, "state.json"));
            Assert.Null(session.HandleKeyTest(F13, 0, true, T0));

            await session.HandleAsync("""{ "id": 1, "type": "keyTest", "enabled": true }""");
            Assert.True(session.KeyTestActive);
            var msg = JsonNode.Parse(session.HandleKeyTest(F13, 0, true, T0)!)!;
            Assert.Equal("keyTest", msg["type"]!.GetValue<string>());
            Assert.Equal("L0-B0", msg["event"]!["slots"]![0]!.GetValue<string>());
            Assert.Equal("레이어 1 버튼 1", msg["event"]!["names"]![0]!.GetValue<string>());
            Assert.Equal("빈 큐라 저장", msg["event"]!["result"]!.GetValue<string>());

            var reply = JsonNode.Parse(await session.HandleAsync("""{ "id": 2, "type": "keyTestInject", "slot": "L0-B0", "down": false }"""))!;
            Assert.False(reply["result"]!["event"]!["down"]!.GetValue<bool>());

            await session.HandleAsync("""{ "id": 3, "type": "keyTest", "enabled": false }""");
            Assert.False(session.KeyTestActive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
