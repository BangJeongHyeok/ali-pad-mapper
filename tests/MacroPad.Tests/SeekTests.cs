using MacroPad.Core.Config;
using MacroPad.Core.Media;
using Xunit;

namespace MacroPad.Tests;

/// <summary>노브로 재생 위치 이동: 가속, 위치 계산, 설정 파일</summary>
public sealed class SeekTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static MediaTimeline Song(double positionSec, bool playing = true, double lengthSec = 240, DateTimeOffset? updated = null) =>
        new(TimeSpan.FromSeconds(positionSec), updated ?? T0, playing,
            TimeSpan.Zero, TimeSpan.FromSeconds(lengthSec), TimeSpan.Zero, TimeSpan.FromSeconds(lengthSec));

    [Fact]
    public void Step_SlowTurnsMoveExactlyOneStep()
    {
        var t = new SeekTracker();
        Assert.Equal(-1, t.Step(T0, -1, accel: true));
        Assert.Equal(-1, t.Step(T0.AddMilliseconds(400), -1, accel: true));
        Assert.Equal(-1, t.Step(T0.AddMilliseconds(800), -1, accel: true));
    }

    [Fact]
    public void Step_FastTurnsAccelerate_UpToMax()
    {
        var t = new SeekTracker();
        t.Step(T0, 1, accel: true);
        Assert.Equal(3, t.Step(T0.AddMilliseconds(50), 1, accel: true), 3);   // 150/50
        Assert.Equal(SeekTracker.MaxBoost, t.Step(T0.AddMilliseconds(55), 1, accel: true), 3);
    }

    [Fact]
    public void Step_NoAccelWhenOffOrDirectionChanges()
    {
        var t = new SeekTracker();
        t.Step(T0, 1, accel: false);
        Assert.Equal(1, t.Step(T0.AddMilliseconds(20), 1, accel: false));

        var u = new SeekTracker();
        u.Step(T0, 1, accel: true);
        Assert.Equal(-1, u.Step(T0.AddMilliseconds(20), -1, accel: true));
    }

    [Fact]
    public void Target_ExtrapolatesPlayingPosition()
    {
        var t = new SeekTracker();
        // 10초 전에 60초였고 재생 중 → 지금 70초 → 1초 뒤로 = 69초
        var target = t.Target(T0.AddSeconds(10), "yt", Song(60), -1);
        Assert.Equal(TimeSpan.FromSeconds(69), target);
    }

    [Fact]
    public void Target_PausedDoesNotExtrapolate()
    {
        var t = new SeekTracker();
        Assert.Equal(TimeSpan.FromSeconds(59), t.Target(T0.AddSeconds(10), "yt", Song(60, playing: false), -1));
    }

    [Fact]
    public void Target_ContinuesFromLastTargetWhilePlayerHasNotCaughtUp()
    {
        var t = new SeekTracker();
        var first = t.Target(T0, "yt", Song(60, playing: false), -1);
        // 플레이어는 아직 60초라고 알려줌 → 그래도 59 에서 이어서 58
        var second = t.Target(T0.AddMilliseconds(100), "yt", Song(60, playing: false), -1);
        Assert.Equal(TimeSpan.FromSeconds(59), first);
        Assert.Equal(TimeSpan.FromSeconds(58), second);
    }

    [Fact]
    public void Target_ForgetsAfterMemoryOrAppChangeOrFailure()
    {
        var t = new SeekTracker();
        t.Target(T0, "yt", Song(60, playing: false), -10);
        Assert.Equal(TimeSpan.FromSeconds(31), t.Target(T0.AddSeconds(5), "yt", Song(30, playing: false), 1));

        t.Target(T0, "yt", Song(60, playing: false), -10);
        Assert.Equal(TimeSpan.FromSeconds(31), t.Target(T0.AddMilliseconds(100), "spotify", Song(30, playing: false), 1));

        t.Target(T0, "yt", Song(60, playing: false), -10);
        t.Forget();
        Assert.Equal(TimeSpan.FromSeconds(31), t.Target(T0.AddMilliseconds(100), "yt", Song(30, playing: false), 1));
    }

    [Fact]
    public void Target_ClampsToStartAndBeforeEnd()
    {
        var t = new SeekTracker();
        Assert.Equal(TimeSpan.Zero, t.Target(T0, "a", Song(2, playing: false), -5));
        t.Forget();
        Assert.Equal(TimeSpan.FromSeconds(239), t.Target(T0, "a", Song(238, playing: false), 5));
    }

    [Fact]
    public void Target_UnknownLengthHasNoUpperLimit()
    {
        var t = new SeekTracker();
        Assert.Equal(TimeSpan.FromSeconds(105), t.Target(T0, "live", Song(100, playing: false, lengthSec: 0), 5));
    }

    [Fact]
    public void Target_IgnoresBogusUpdateTime()
    {
        var t = new SeekTracker();
        var song = Song(60, updated: DateTimeOffset.MinValue);
        Assert.Equal(TimeSpan.FromSeconds(61), t.Target(T0, "yt", song, 1));
    }

    [Theory]
    [InlineData(-1, "1초 뒤로")]
    [InlineData(0.5, "0.5초 앞으로")]
    [InlineData(2.25, "2.25초 앞으로")]
    public void Describe(double seconds, string expected) => Assert.Equal(expected, SeekTracker.Describe(seconds));

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-3, "0:00")]
    public void Format(double seconds, string expected) => Assert.Equal(expected, SeekTracker.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Config_SeekRoundTripsAndValidates()
    {
        var config = PadConfig.Parse("""{ "actions": { "f13": { "seek": -0.5 }, "f14": { "seek": 2, "seekAccel": false } } }""");
        Assert.Equal(-0.5, config.Actions["f13"]!.Seek);
        Assert.True(config.Actions["f13"]!.EffectiveSeekAccel);
        Assert.False(config.Actions["f14"]!.EffectiveSeekAccel);
        Assert.Empty(config.Actions["f13"]!.ParseKeys());

        var text = ConfigWriter.Serialize(config);
        Assert.Contains("\"f13\": { \"seek\": -0.5 }", text);
        Assert.Contains("\"f14\": { \"seek\": 2, \"seekAccel\": false }", text);

        Assert.Throws<FormatException>(() => new ActionConfig { Seek = 0 }.ParseKeys());
        Assert.Throws<FormatException>(() => new ActionConfig { Seek = 1000 }.ParseKeys());
        Assert.False(new ActionConfig { Seek = 1 }.IsEmpty);
    }
}
