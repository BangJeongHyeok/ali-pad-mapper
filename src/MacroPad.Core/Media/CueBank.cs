namespace MacroPad.Core.Media;

/// <summary>저장해 둔 재생 위치 하나 (샘플 큐).</summary>
/// <param name="Position">곡 안의 위치</param>
/// <param name="Source">저장할 때 재생 중이던 앱 (표시용)</param>
/// <param name="Title">저장할 때 재생 중이던 곡 제목 (표시용, 모르면 빈 문자열)</param>
public readonly record struct CuePoint(TimeSpan Position, string Source, string Title);

/// <summary>큐 키를 눌렀을 때 할 일</summary>
public enum CuePress
{
    /// <summary>저장해 둔 위치로 이동</summary>
    Jump,

    /// <summary>지금 위치를 이 큐에 저장</summary>
    Set,
}

/// <summary>
/// 샘플러처럼 쓰는 재생 위치 큐 (DJ 컨트롤러의 핫 큐와 같은 방식).
///   - 큐 키만 누르기          → 저장해 둔 위치로 이동 (비어 있으면 지금 위치를 저장)
///   - 큐 지정 키를 누른 채로   → 지금 위치를 그 큐에 저장 (이미 있으면 덮어씀)
///   - 큐 지정 키를 짧게 톡    → 다음에 누르는 큐 키 한 번만 저장 (ArmTimeout 안에)
///     패드가 키를 누르고 있는 동안 계속 누른 상태로 보내지 않고 바로 떼는 경우에도 쓸 수 있게.
///     한 번 더 톡 하면 취소.
///
/// 위치는 메모리에만 둠 (앱을 끄면 사라짐). 곡이 바뀌어도 지우지 않음 → 다른 곡에서는 같은 시각으로 이동.
/// 시간은 바깥에서 넣어 줌 (테스트 가능하게). 한 스레드(UI 스레드)에서만 부름.
/// </summary>
public sealed class CueBank
{
    /// <summary>큐 지정 키를 이보다 짧게 눌렀다 떼면 '톡'으로 봄</summary>
    public static readonly TimeSpan TapMax = TimeSpan.FromMilliseconds(400);

    /// <summary>톡 한 뒤 다음 큐 키를 기다리는 시간</summary>
    public static readonly TimeSpan ArmTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 이보다 오래 눌려 있으면 뗀 신호를 놓친 것으로 봄 (잠금 화면 전환 등으로 '뗌'이 안 올 수 있음).
    /// 안 그러면 그 뒤로 큐 키를 누를 때마다 이동 대신 저장돼 버림.
    /// </summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(20);

    /// <summary>큐 번호 범위</summary>
    public const int MinCue = 1, MaxCue = 99;

    private readonly Dictionary<int, CuePoint> _points = new();

    private int _held;                   // 누르고 있는 큐 지정 키 개수
    private DateTimeOffset _downAt;      // 처음 누른 시각
    private bool _usedWhileHeld;         // 누르고 있는 동안 큐 키를 눌렀는지
    private bool _wasArmed;              // 누를 때 이미 '톡' 대기 중이었는지 (→ 한 번 더 톡 = 취소)
    private DateTimeOffset? _armedUntil; // '톡' 대기 끝나는 시각

    /// <summary>큐 지정 키를 누르고 있는지</summary>
    public bool IsSetHeld => _held > 0;

    /// <summary>'톡' 해서 다음 큐 키를 기다리는 중인지</summary>
    public bool IsArmed(DateTimeOffset now) => _armedUntil is { } until && now <= until;

    public int Count => _points.Count;

    /// <summary>큐 지정 키 누름 (자동 반복은 바깥에서 걸러서 처음 한 번만)</summary>
    public void SetKeyDown(DateTimeOffset now)
    {
        if (_held > 0 && now - _downAt > StuckAfter)
            _held = 0;
        if (_held++ > 0) return;
        _downAt = now;
        _usedWhileHeld = false;
        _wasArmed = IsArmed(now);
        _armedUntil = null;
    }

    /// <summary>큐 지정 키 뗌. 큐 키 없이 짧게 톡 했으면 다음 한 번을 저장으로 (이미 대기 중이었으면 취소).</summary>
    public void SetKeyUp(DateTimeOffset now)
    {
        if (_held == 0) return;
        if (--_held > 0) return;

        var held = now - _downAt;
        if (!_usedWhileHeld && !_wasArmed && held >= TimeSpan.Zero && held <= TapMax)
            _armedUntil = now + ArmTimeout;
    }

    /// <summary>큐 키 눌림 → 이동할지 저장할지. 저장이면 위치를 알아낸 뒤 Store 를 불러 줘야 함.</summary>
    public CuePress Press(DateTimeOffset now, int cue)
    {
        if (_held > 0 && now - _downAt > StuckAfter)
            _held = 0;

        if (_held > 0)
        {
            _usedWhileHeld = true;
            return CuePress.Set;
        }

        if (IsArmed(now))
        {
            _armedUntil = null;
            return CuePress.Set;
        }

        _armedUntil = null;
        return _points.ContainsKey(cue) ? CuePress.Jump : CuePress.Set;
    }

    public void Store(int cue, CuePoint point) => _points[cue] = point;

    public bool TryGet(int cue, out CuePoint point) => _points.TryGetValue(cue, out point);

    /// <summary>저장한 큐를 모두 지움</summary>
    public void Clear()
    {
        _points.Clear();
        _armedUntil = null;
    }

    /// <summary>큐 번호가 쓸 수 있는 범위인지</summary>
    public static bool IsValidCue(int cue) => cue is >= MinCue and <= MaxCue;
}
