using MacroPad.Core.Config;
using MacroPad.Core.Protocol;
using Xunit;

namespace MacroPad.Tests;

/// <summary>
/// 기대값은 kriomant/ch57x-keyboard-tool PR #174 (판매자 프로그램 HID 캡처로 확인된 값)
/// 및 이슈 #89 의 12키+노브2 모델 분석 내용에서 가져옴.
/// </summary>
public class ProtocolTests
{
    private static readonly ProtocolOptions Defaults = new();

    private static byte[] Bind(int layer, int slot, string text) =>
        K8850Protocol.BindingPacket(layer, slot, PadBinding.Parse(text)!, Defaults);

    private static void AssertPrefix(byte[] actual, params byte[] expected)
    {
        Assert.Equal(64, actual.Length);
        Assert.Equal(expected, actual[..expected.Length]);
        Assert.All(actual[expected.Length..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void SingleKey_UsesCompactForm()
    {
        // 버튼1, 레이어1, 'i' (HID 0x0C)
        AssertPrefix(Bind(0, 1, "i"), 0x03, 0xFD, 0x01, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x0C);
    }

    [Fact]
    public void SingleModifier_UsesTriplets()
    {
        AssertPrefix(Bind(0, 1, "ctrl+a"),
            0x03, 0xFD, 0x01, 0x01, 0x01, 0x00, 0x02, 0x00, 0x00,
            0xF1, 0x00, 0x32, 0x04, 0x00, 0x32);
    }

    [Fact]
    public void TwoModifiers()
    {
        AssertPrefix(Bind(0, 1, "ctrl+alt+space"),
            0x03, 0xFD, 0x01, 0x01, 0x01, 0x00, 0x03, 0x00, 0x00,
            0xF1, 0x00, 0x32, 0xF3, 0x00, 0x32, 0x2C, 0x00, 0x32);
    }

    [Fact]
    public void ThreeModifiers_F13()
    {
        AssertPrefix(Bind(0, 1, "ctrl+alt+win+f13"),
            0x03, 0xFD, 0x01, 0x01, 0x01, 0x00, 0x04, 0x00, 0x00,
            0xF1, 0x00, 0x32, 0xF3, 0x00, 0x32, 0xF4, 0x00, 0x32, 0x68, 0x00, 0x32);
    }

    [Fact]
    public void MacroSequence()
    {
        AssertPrefix(Bind(0, 1, "a, b"),
            0x03, 0xFD, 0x01, 0x01, 0x01, 0x00, 0x02, 0x00, 0x00,
            0x04, 0x00, 0x32, 0x05, 0x00, 0x32);
    }

    [Fact]
    public void MediaPlay()
    {
        AssertPrefix(Bind(0, 1, "play"), 0x03, 0xFD, 0x01, 0x01, 0x02, 0x00, 0x02, 0x00, 0x00, 0xCD);
    }

    [Theory]
    [InlineData("volumeup", 0xE9)]
    [InlineData("volumedown", 0xEA)]
    [InlineData("mute", 0xE2)]
    [InlineData("next", 0xB5)]
    [InlineData("prev", 0xB6)]
    public void MediaCodes(string name, int code)
    {
        var b = Assert.IsType<MediaBinding>(PadBinding.Parse(name));
        Assert.Equal(code, b.ConsumerCode);
    }

    [Fact]
    public void FinalizePacket_IsFixed()
    {
        AssertPrefix(K8850Protocol.FinalizePacket(), 0x03, 0xFD, 0xFE, 0xFF);
    }

    [Fact]
    public void F13ToF24_HidCodes()
    {
        AssertPrefix(Bind(0, 5, "f13"), 0x03, 0xFD, 0x05, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x68);
        AssertPrefix(Bind(0, 5, "f24"), 0x03, 0xFD, 0x05, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x73);
    }

    [Fact]
    public void LayerByteIsOneBased()
    {
        Assert.Equal(3, Bind(2, 1, "a")[3]);
    }

    [Fact]
    public void KnobSlots_MatchTwelveKeyModel()
    {
        // 12키+노브2 모델: 위 노브 16/17/18, 아래 노브 19/20/21
        // (공장 기본 글자 p q r / s t u = 슬롯 16~21 과 일치)
        Assert.Equal(16, K8850Protocol.KnobSlot(0, KnobAction.Ccw, Defaults));
        Assert.Equal(17, K8850Protocol.KnobSlot(0, KnobAction.Press, Defaults));
        Assert.Equal(18, K8850Protocol.KnobSlot(0, KnobAction.Cw, Defaults));
        Assert.Equal(19, K8850Protocol.KnobSlot(1, KnobAction.Ccw, Defaults));
        Assert.Equal(20, K8850Protocol.KnobSlot(1, KnobAction.Press, Defaults));
        Assert.Equal(21, K8850Protocol.KnobSlot(1, KnobAction.Cw, Defaults));
    }

    [Fact]
    public void TooLongMacro_IsRejected()
    {
        var text = string.Join(",", Enumerable.Repeat("a", 19));
        Assert.Throws<FormatException>(() => Bind(0, 1, text));
    }

    [Theory]
    [InlineData("ctrl+volumeup")]
    [InlineData("a, play")]
    [InlineData("nosuchkey")]
    [InlineData("a+b")]
    public void InvalidBindings_AreRejected(string text)
    {
        Assert.Throws<FormatException>(() => PadBinding.Parse(text));
    }

    [Fact]
    public void FactoryDefaultConfig_ReproducesDefaultLetters()
    {
        var config = PadConfig.Load(Path.Combine(AppContext.BaseDirectory, "TestConfigs", "00-factory-default.json"));
        var plan = UploadPlan.Create(config);
        Assert.True(plan.IsValid, string.Join("\n", plan.Errors));
        Assert.Equal(18, plan.Bindings.Count);

        // 슬롯 n 의 기본 글자 = 'a' + (n - 1)
        foreach (var b in plan.Bindings)
        {
            var packet = K8850Protocol.BindingPacket(b.LayerIndex, b.Slot, b.Binding, plan.Options);
            Assert.Equal(0x04 + (b.Slot - 1), packet[9]);
        }
    }

    [Fact]
    public void MainConfig_IsValid()
    {
        var config = PadConfig.Load(Path.Combine(AppContext.BaseDirectory, "TestConfigs", "macropad.json"));
        var plan = UploadPlan.Create(config);
        Assert.True(plan.IsValid, string.Join("\n", plan.Errors));
        Assert.Equal(18, plan.Bindings.Count);
        Assert.Equal(9, config.Actions.Count);
        Assert.True(config.Actions.ContainsKey("F13")); // 대소문자 무시
    }

    [Fact]
    public void MainConfig_ActionKeysAreSendableFromPc()
    {
        var config = PadConfig.Load(Path.Combine(AppContext.BaseDirectory, "TestConfigs", "macropad.json"));
        foreach (var (name, action) in config.Actions)
        {
            var trigger = MacroPad.Core.Keys.Chord.Parse(name);
            Assert.True(trigger.Key?.Vk is not null, $"트리거 {name}");
            foreach (var step in action.Keys ?? new())
                foreach (var chord in MacroPad.Core.Keys.Chord.ParseSequence(step))
                    Assert.True(chord.Key is null || chord.Key.Vk is not null, $"{name}: {step}");
        }
    }

    [Fact]
    public void TopKnobTestConfig_WritesOnlyThreeSlots()
    {
        var config = PadConfig.Load(Path.Combine(AppContext.BaseDirectory, "TestConfigs", "01-top-knob-test.json"));
        var plan = UploadPlan.Create(config);
        Assert.True(plan.IsValid, string.Join("\n", plan.Errors));
        Assert.Equal(new[] { 16, 17, 18 }, plan.Bindings.Select(b => b.Slot).ToArray());
        Assert.Equal(1 + 3 * 2, plan.Packets().Count()); // 초기화 1 + (바인딩+확정) x 3
    }
}
