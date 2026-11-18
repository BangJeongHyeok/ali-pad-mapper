using MacroPad.Core.Config;
using MacroPad.Core.Editing;
using MacroPad.Core.Keys;
using MacroPad.Core.Protocol;
using Xunit;

namespace MacroPad.Tests;

public class EditingTests
{
    private static string TestConfig(string name) => Path.Combine(AppContext.BaseDirectory, "TestConfigs", name);

    private static ConfigEditor Empty() => new(PadConfig.Parse("{}"));

    private static readonly SlotRef B1 = SlotRef.Button(0, 0);
    private static readonly SlotRef B2 = SlotRef.Button(0, 1);
    private static readonly SlotRef TopCw = SlotRef.Knob(0, 0, KnobAction.Cw);

    // ─────────────── SlotRef / PadConfigSlots ───────────────

    [Fact]
    public void SetText_GrowsListsAndTrimsTrailingEmpties()
    {
        var config = PadConfig.Parse("{}");
        config.SetText(SlotRef.Button(1, 5), "a");
        Assert.Equal(2, config.Layers.Count);
        Assert.Equal(6, config.Layers[1].Buttons.Count);
        Assert.Equal("a", config.GetText(SlotRef.Button(1, 5)));

        config.SetText(SlotRef.Button(1, 5), null);
        Assert.Empty(config.Layers); // 끝쪽 빈 레이어 정리
    }

    [Fact]
    public void SetText_Knob()
    {
        var config = PadConfig.Parse("{}");
        config.SetText(SlotRef.Knob(0, 1, KnobAction.Press), "mute");
        Assert.Equal(2, config.Layers[0].Knobs.Count);
        Assert.Null(config.Layers[0].Knobs[0]);
        Assert.Equal("mute", config.Layers[0].Knobs[1]!.Press);
    }

    [Fact]
    public void SlotRef_PadSlotNumbers()
    {
        var o = new ProtocolOptions();
        Assert.Equal(1, SlotRef.Button(0, 0).PadSlot(o));
        Assert.Equal(12, SlotRef.Button(0, 11).PadSlot(o));
        Assert.Equal(16, SlotRef.Knob(0, 0, KnobAction.Ccw).PadSlot(o));
        Assert.Equal(21, SlotRef.Knob(0, 1, KnobAction.Cw).PadSlot(o));
        Assert.Equal(18, SlotRef.All(new AdvancedSection()).Count() / 3); // 레이어당 12 + 6
    }

    // ─────────────── ConfigEditor ───────────────

    [Fact]
    public void ReadsMainConfig()
    {
        var editor = new ConfigEditor(PadConfig.Load(TestConfig("macropad.json")));

        var play = Assert.IsType<PadKeysSlot>(editor.Get(B1));
        Assert.True(play.IsMedia);

        var notepad = Assert.IsType<PcActionSlot>(editor.Get(SlotRef.Button(0, 4))); // f13
        Assert.Equal("notepad.exe", notepad.Action.Run);

        var zoom = Assert.IsType<PadKeysSlot>(editor.Get(SlotRef.Knob(0, 1, KnobAction.Cw)));
        Assert.Equal("ctrl+equal", zoom.Text);

        Assert.IsType<EmptySlot>(editor.Get(SlotRef.Button(1, 0)));
        Assert.Empty(editor.UnreferencedActions());
    }

    [Fact]
    public void InvalidText_IsReportedNotThrown()
    {
        var config = PadConfig.Parse("""{ "layers": [ { "buttons": ["nosuchkey"] } ] }""");
        Assert.IsType<InvalidSlot>(new ConfigEditor(config).Get(B1));
    }

    [Fact]
    public void SetPadKeys_ValidatesBeforeChanging()
    {
        var editor = Empty();
        editor.SetPadKeys(B1, "ctrl+c");
        Assert.True(editor.IsDirty);

        Assert.Throws<FormatException>(() => editor.SetPadKeys(B1, "ctrl+volumeup"));
        Assert.Equal("ctrl+c", editor.Config.GetText(B1));
    }

    [Fact]
    public void SetPcAction_AllocatesTriggersInOrder()
    {
        var editor = Empty();
        Assert.Equal("f13", editor.SetPcAction(B1, new ActionConfig { Run = "notepad.exe" }));
        Assert.Equal("f14", editor.SetPcAction(B2, new ActionConfig { Text = "안녕" }));
        Assert.Equal("f13", editor.Config.GetText(B1));
        Assert.Equal("notepad.exe", editor.Config.Actions["F13"].Run);
    }

    [Fact]
    public void SetPcAction_SkipsTriggersAlreadyUsedAnywhere()
    {
        var editor = Empty();
        editor.SetPadKeys(B1, "f13");                 // 패드 키로 직접 쓴 f13
        editor.Config.Actions["F14"] = new ActionConfig { Run = "x" }; // 칸 없이 남아 있는 동작
        Assert.Equal("f15", editor.SetPcAction(B2, new ActionConfig { Run = "calc.exe" }));
    }

    [Fact]
    public void SetPcAction_KeepsTriggerWhenEditingSameSlot()
    {
        var editor = Empty();
        editor.SetPcAction(B1, new ActionConfig { Run = "notepad.exe" });
        Assert.Equal("f13", editor.SetPcAction(B1, new ActionConfig { Run = "calc.exe" }));
        Assert.Single(editor.Config.Actions);
        Assert.Equal("calc.exe", editor.Config.Actions["f13"].Run);
    }

    [Fact]
    public void SetPcAction_SharedTriggerGetsSplit()
    {
        // 두 칸이 같은 트리거를 쓰는 상태에서 하나만 바꾸면, 바꾼 칸에 새 트리거를 줌
        var config = PadConfig.Parse("""
            { "layers": [ { "buttons": ["f13", "f13"] } ], "actions": { "f13": { "run": "a.exe" } } }
            """);
        var editor = new ConfigEditor(config);
        Assert.Equal("f14", editor.SetPcAction(B2, new ActionConfig { Run = "b.exe" }));
        Assert.Equal("a.exe", editor.Config.Actions["f13"].Run);
        Assert.Equal("b.exe", editor.Config.Actions["f14"].Run);
    }

    [Fact]
    public void ReplacingPcAction_RemovesUnusedAction()
    {
        var editor = Empty();
        editor.SetPcAction(B1, new ActionConfig { Run = "notepad.exe" });
        editor.SetPadKeys(B1, "volumeup");
        Assert.Empty(editor.Config.Actions);

        editor.SetPcAction(B1, new ActionConfig { Run = "notepad.exe" });
        editor.Clear(B1);
        Assert.Empty(editor.Config.Actions);
        Assert.IsType<EmptySlot>(editor.Get(B1));
    }

    [Fact]
    public void SetPcAction_RejectsEmptyOrBadAction()
    {
        var editor = Empty();
        Assert.Throws<FormatException>(() => editor.SetPcAction(B1, new ActionConfig()));
        Assert.Throws<FormatException>(() => editor.SetPcAction(B1, new ActionConfig { Keys = ["brightnessup"] }));
        Assert.Empty(editor.Config.Actions);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void SetPadKeys_RejectsTriggerOfExistingAction()
    {
        var editor = Empty();
        editor.SetPcAction(B1, new ActionConfig { Run = "notepad.exe" });
        Assert.Throws<FormatException>(() => editor.SetPadKeys(B2, "F13"));
    }

    [Fact]
    public void TriggerCandidates_AvoidAltAndWin()
    {
        var all = ConfigEditor.TriggerCandidates().ToList();
        Assert.Equal(48, all.Count);
        Assert.Equal(48, all.Distinct().Count());
        Assert.DoesNotContain(all, c => c.Contains("alt") || c.Contains("win"));
        Assert.All(all, c => Assert.NotNull(PadBinding.Parse(c)));
    }

    [Fact]
    public void TriggerId_IgnoresCaseOrderAndSide()
    {
        Assert.Equal(ConfigEditor.TriggerId("ctrl+shift+f13"), ConfigEditor.TriggerId("Shift+Ctrl+F13"));
        Assert.Equal(ConfigEditor.TriggerId("shift+f13"), ConfigEditor.TriggerId("rshift+f13"));
        Assert.NotEqual(ConfigEditor.TriggerId("f13"), ConfigEditor.TriggerId("shift+f13"));
        Assert.Null(ConfigEditor.TriggerId("a, b"));
    }

    // ─────────────── PadState / 바뀐 칸만 쓰기 ───────────────

    [Fact]
    public void OnlyChanged_SkipsSlotsAlreadyOnPad()
    {
        var editor = Empty();
        editor.SetPadKeys(B1, "volumeup");
        editor.SetPadKeys(B2, "ctrl+c");
        var plan = UploadPlan.Create(editor.Config);
        Assert.Equal(2, plan.Bindings.Count);

        Assert.Same(plan, plan.OnlyChanged(null)); // 기록 없음 → 전부

        var state = new PadState();
        foreach (var b in plan.Bindings) state.Record(b);
        Assert.Empty(plan.OnlyChanged(state).Bindings);
        Assert.False(editor.NeedsUpload(B1, state));

        editor.SetPadKeys(B2, "Control+C"); // 표기만 다르고 패킷은 같음
        Assert.False(editor.NeedsUpload(B2, state));

        editor.SetPadKeys(B2, "ctrl+v");
        var changed = UploadPlan.Create(editor.Config).OnlyChanged(state);
        Assert.Equal(1, changed.SkippedUnchanged);
        Assert.Equal(2, Assert.Single(changed.Bindings).Slot);
        Assert.True(editor.NeedsUpload(B2, state));
        Assert.False(editor.NeedsUpload(SlotRef.Button(0, 5), state)); // 빈 칸은 쓸 일 없음
    }

    [Fact]
    public void PadState_SaveAndLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macropad-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "state.json");
            Assert.Null(PadState.Load(path));

            var editor = Empty();
            editor.SetPadKeys(TopCw, "volumeup");
            var binding = Assert.Single(UploadPlan.Create(editor.Config).Bindings);

            var state = new PadState();
            state.Record(binding);
            state.Save(path);

            var loaded = PadState.Load(path);
            Assert.NotNull(loaded);
            Assert.True(loaded.Matches(binding));
            Assert.Equal("volumeup", loaded.Get(0, 18)!.Source);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PacketsWithBindings_MarksBindingOnFinalize()
    {
        var editor = Empty();
        editor.SetPadKeys(B1, "a");
        var packets = UploadPlan.Create(editor.Config).PacketsWithBindings().ToList();
        Assert.Equal(3, packets.Count);
        Assert.Null(packets[0].Binding); // 초기화
        Assert.Null(packets[1].Binding); // 바인딩 패킷
        Assert.NotNull(packets[2].Binding); // 확정 패킷 뒤에 기록
    }

    // ─────────────── ConfigWriter ───────────────

    [Theory]
    [InlineData("macropad.json")]
    [InlineData("00-factory-default.json")]
    [InlineData("01-top-knob-test.json")]
    public void Writer_RoundTripsConfigs(string name)
    {
        var original = PadConfig.Load(TestConfig(name));
        var text = ConfigWriter.Serialize(original);
        var reloaded = PadConfig.Parse(text);

        var a = UploadPlan.Create(original);
        var b = UploadPlan.Create(reloaded);
        Assert.True(b.IsValid, string.Join("\n", b.Errors));
        Assert.Equal(a.Packets().Select(p => Convert.ToHexString(p.Packet)), b.Packets().Select(p => Convert.ToHexString(p.Packet)));

        Assert.Equal(original.Actions.Count, reloaded.Actions.Count);
        foreach (var (key, action) in original.Actions)
        {
            var other = reloaded.Actions[key];
            Assert.Equal(action.Run, other.Run);
            Assert.Equal(action.Text, other.Text);
            Assert.Equal(action.Keys ?? new(), other.Keys ?? new());
        }

        // 다시 써도 같은 결과 (안정적)
        Assert.Equal(text, ConfigWriter.Serialize(reloaded));
    }

    [Fact]
    public void Writer_KeepsKoreanAndBackslashesReadable()
    {
        var editor = Empty();
        editor.SetPcAction(B1, new ActionConfig { Text = "확인했습니다 \"감사\"" });
        editor.SetPcAction(B2, new ActionConfig { Run = @"%USERPROFILE%\Downloads" });
        var text = ConfigWriter.Serialize(editor.Config);
        Assert.Contains("확인했습니다", text);
        Assert.Contains(@"%USERPROFILE%\\Downloads", text);
        Assert.Contains("\"f13\", \"f14\"", text); // 버튼은 한 줄에 4개씩

        var reloaded = PadConfig.Parse(text);
        Assert.Equal("확인했습니다 \"감사\"", reloaded.Actions["f13"].Text);
        Assert.Equal(@"%USERPROFILE%\Downloads", reloaded.Actions["f14"].Run);
    }

    [Fact]
    public void Writer_SaveMakesBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macropad-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "macropad.json");
            File.WriteAllText(path, "// 원래 파일\n{}");

            var editor = Empty();
            editor.SetPadKeys(B1, "play");
            ConfigWriter.Save(editor.Config, path);

            Assert.Equal("// 원래 파일\n{}", File.ReadAllText(path + ".bak"));
            Assert.Equal("play", PadConfig.Load(path).GetText(B1));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ─────────────── 표시 ───────────────

    [Fact]
    public void SlotDisplay_ShortLabels()
    {
        var editor = new ConfigEditor(PadConfig.Load(TestConfig("macropad.json")));
        Assert.Equal("재생/정지", SlotDisplay.Short(editor.Get(B1)));
        Assert.Equal("[실행] notepad", SlotDisplay.Short(editor.Get(SlotRef.Button(0, 4))));
        Assert.Equal("[단축키] Win+Shift+S", SlotDisplay.Short(editor.Get(SlotRef.Button(0, 6)), 40));
        Assert.Equal("[실행] www.google.com", SlotDisplay.Short(editor.Get(SlotRef.Button(0, 8)), 40));
        Assert.Equal("[실행] Downloads", SlotDisplay.Short(editor.Get(SlotRef.Button(0, 11)), 40));
        Assert.Equal("Ctrl+=", SlotDisplay.Short(editor.Get(SlotRef.Knob(0, 1, KnobAction.Cw))));
        Assert.Equal("", SlotDisplay.Short(EmptySlot.Instance));
    }

    [Fact]
    public void KeyTable_ReverseLookupByVk()
    {
        Assert.True(KeyTable.TryGetByVk(0x41, out var a));
        Assert.Equal("a", a.Name);
        Assert.True(KeyTable.TryGetByVk(0x0D, out var enter));
        Assert.Equal("enter", enter.Name); // numpadenter 와 VK 가 같으면 먼저 등록된 enter
        Assert.True(KeyTable.TryGetByVk(0x7C, out var f13));
        Assert.Equal("f13", f13.Name);
        Assert.True(KeyTable.TryGetByVk(0xAF, out var volUp));
        Assert.True(volUp.IsMedia);
    }
}
