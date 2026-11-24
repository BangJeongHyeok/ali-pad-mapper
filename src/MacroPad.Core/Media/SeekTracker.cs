namespace MacroPad.Core.Media;

/// <summary>Windows 미디어 제어에서 읽은 지금 재생 중인 곡의 위치 정보.</summary>
/// <param name="Position">마지막으로 알려진 재생 위치</param>
/// <param name="LastUpdated">Position 을 기록한 시각 (재생 중이면 그 뒤로 흐른 시간만큼 더 진행됨)</param>
public readonly record struct MediaTimeline(
    TimeSpan Position,
    DateTimeOffset LastUpdated,
    bool IsPlaying,
    TimeSpan Start,
    TimeSpan End,
    TimeSpan MinSeek,
    TimeSpan MaxSeek);

/// <summary>
/// 노브로 재생 위치 앞뒤로 감기 (턴테이블처럼).
///   - 빨리 돌리면 한 칸에 더 크게 이동 (가속)
///   - 연속으로 돌리는 동안은 직전에 보낸 목표 위치를 기준으로 계산.
///     플레이어가 새 위치를 알려주기 전에 다음 칸이 오면 같은 위치로 또 이동하는 걸 막기 위함.
/// 시간은 바깥에서 넣어 줌 (테스트 가능하게).
/// </summary>
public sealed class SeekTracker
{
    /// <summary>이보다 빨리 다음 칸이 오면 가속</summary>
    public static readonly TimeSpan FastWindow = TimeSpan.FromMilliseconds(150);

    /// <summary>직전 목표 위치를 기억하는 시간</summary>
    public static readonly TimeSpan Memory = TimeSpan.FromMilliseconds(1500);

    /// <summary>앞으로 감다가 곡 끝을 넘어 다음 곡으로 넘어가지 않게 남겨 두는 시간</summary>
    public static readonly TimeSpan EndMargin = TimeSpan.FromSeconds(1);

    /// <summary>가속 최대 배수</summary>
    public const double MaxBoost = 6;

    private DateTimeOffset _lastTick = DateTimeOffset.MinValue;
    private int _lastDirection;

    private string? _source;
    private TimeSpan _target;
    private DateTimeOffset _targetAt = DateTimeOffset.MinValue;

    /// <summary>
    /// 노브 한 칸 → 이번에 이동할 초 (가속 반영). 칸이 올 때마다 시간 순서대로 부름.
    /// 같은 방향으로 FastWindow 안에 다음 칸이 오면 간격에 반비례해서 커짐 (최대 MaxBoost 배).
    /// </summary>
    public double Step(DateTimeOffset now, double seconds, bool accel)
    {
        var direction = Math.Sign(seconds);
        var gap = now - _lastTick;
        var boost = 1.0;
        if (accel && direction == _lastDirection && gap >= TimeSpan.Zero && gap < FastWindow)
            boost = Math.Clamp(FastWindow.TotalMilliseconds / Math.Max(gap.TotalMilliseconds, 1), 1, MaxBoost);

        _lastTick = now;
        _lastDirection = direction;
        return seconds * boost;
    }

    /// <summary>
    /// 이동할 목표 위치. source 는 재생 중인 앱 구분용 (앱이 바뀌면 기억한 위치를 버림).
    /// </summary>
    public TimeSpan Target(DateTimeOffset now, string source, MediaTimeline timeline, double deltaSeconds) =>
        Remember(now, source, Clamp(Current(now, source, timeline) + TimeSpan.FromSeconds(deltaSeconds), timeline));

    /// <summary>
    /// 정한 위치로 바로 이동할 목표 (샘플 큐). 곡 길이 안으로 맞추고, 이어서 노브로 감거나
    /// 큐를 다시 저장할 때 이 위치를 기준으로 삼도록 기억해 둠.
    /// </summary>
    public TimeSpan JumpTo(DateTimeOffset now, string source, MediaTimeline timeline, TimeSpan position) =>
        Remember(now, source, Clamp(position, timeline));

    /// <summary>
    /// now 시점의 재생 위치 추정 (상태는 안 바꿈).
    /// 방금 이동시킨 직후면 플레이어가 아직 새 위치를 알려주지 않았을 수 있어서 직전 목표 위치를 기준으로 계산.
    /// </summary>
    public TimeSpan Current(DateTimeOffset now, string source, MediaTimeline timeline)
    {
        TimeSpan current;
        var sinceTarget = now - _targetAt;
        if (_source == source && sinceTarget >= TimeSpan.Zero && sinceTarget < Memory)
        {
            current = _target + (timeline.IsPlaying ? sinceTarget : TimeSpan.Zero);
        }
        else
        {
            current = timeline.Position;
            var age = now - timeline.LastUpdated;
            // 기록 시각이 이상하면(앱이 안 채웠거나 시계가 다르면) 흐른 시간은 더하지 않음
            if (timeline.IsPlaying && age > TimeSpan.Zero && age < TimeSpan.FromHours(12))
                current += age;
        }
        return current < TimeSpan.Zero ? TimeSpan.Zero : current;
    }

    private TimeSpan Remember(DateTimeOffset now, string source, TimeSpan target)
    {
        _source = source;
        _target = target;
        _targetAt = now;
        return target;
    }

    /// <summary>이동에 실패했을 때: 기억한 위치를 버리고 다음에는 플레이어가 알려주는 위치부터 다시 계산</summary>
    public void Forget()
    {
        _source = null;
        _targetAt = DateTimeOffset.MinValue;
    }

    private static TimeSpan Clamp(TimeSpan value, MediaTimeline t)
    {
        var lo = t.Start;
        if (t.MinSeek > lo) lo = t.MinSeek;

        var hi = t.End;
        if (t.MaxSeek > lo && t.MaxSeek < hi) hi = t.MaxSeek;

        // 길이를 모르는 경우(실시간 방송 등)는 위쪽 제한 없음
        if (hi > lo)
        {
            var end = hi - EndMargin > lo ? hi - EndMargin : lo;
            if (value > end) value = end;
        }
        return value < lo ? lo : value;
    }

    /// <summary>1:05, 1:02:03 같은 표시용 시간</summary>
    public static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    /// <summary>"1초 뒤로", "0.5초 앞으로"</summary>
    public static string Describe(double seconds) =>
        $"{Math.Abs(seconds).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}초 {(seconds < 0 ? "뒤로" : "앞으로")}";
}
