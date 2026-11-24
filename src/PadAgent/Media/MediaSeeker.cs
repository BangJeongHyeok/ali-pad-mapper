using MacroPad.Core.Media;
using Windows.Media.Control;

namespace PadAgent.Media;

/// <summary>
/// 지금 재생 중인 미디어(크롬 유튜브, 유튜브 뮤직 앱 등)의 재생 위치를 이동.
/// Windows 미디어 제어(볼륨 키를 누르면 뜨는 미디어 팝업과 같은 기능)를 쓰므로
/// 그 앱 창이 앞에 있지 않아도 되고, 키보드 단축키(유튜브 5초)보다 잘게 움직일 수 있음.
///
/// UI 스레드에서만 부름 (TrayApp 이 트리거를 UI 스레드로 넘겨줌).
/// </summary>
internal sealed class MediaSeeker
{
    public static MediaSeeker Shared { get; } = new();

    private readonly SeekTracker _tracker = new();
    private Task<GlobalSystemMediaTransportControlsSessionManager>? _manager;

    /// <summary>
    /// seconds 만큼 이동 (음수면 뒤로). 성공하면 "앱 이름 · 곡 제목 · 1:05" 같은 설명.
    /// 실패하면 화면에 그대로 보여줄 수 있는 문장으로 InvalidOperationException.
    /// </summary>
    public async Task<string> SeekAsync(double seconds, bool accel, bool describe = false)
    {
        // 가속은 칸이 들어온 시각 기준으로 (뒤의 await 로 늦어지기 전에)
        var delta = _tracker.Step(DateTimeOffset.Now, seconds, accel);

        var (session, timeline) = await CurrentAsync();
        var target = _tracker.Target(DateTimeOffset.Now, session.SourceAppUserModelId ?? "", timeline, delta);
        await MoveAsync(session, target);

        if (!describe) return "";
        return string.Join(" · ", new[] { AppName(session.SourceAppUserModelId), await TitleAsync(session), SeekTracker.Format(target) }
            .Where(s => s.Length > 0));
    }

    /// <summary>
    /// at 시점의 재생 위치 (샘플 큐 저장용). at 은 키가 눌린 시각 — 미디어 제어에 연결하느라 늦어져도
    /// 누른 순간의 위치를 저장하도록 그 시각 기준으로 계산.
    /// </summary>
    public async Task<CuePoint> CaptureAsync(DateTimeOffset at)
    {
        var (session, timeline) = await CurrentAsync();
        var source = session.SourceAppUserModelId ?? "";
        return new CuePoint(_tracker.Current(at, source, timeline), AppName(source), await TitleAsync(session));
    }

    /// <summary>정한 위치로 이동 (샘플 큐). 실제로 이동한 위치를 돌려줌 (곡 길이에 맞춰 잘릴 수 있음).</summary>
    public async Task<TimeSpan> JumpAsync(TimeSpan position)
    {
        var (session, timeline) = await CurrentAsync();
        var target = _tracker.JumpTo(DateTimeOffset.Now, session.SourceAppUserModelId ?? "", timeline, position);
        await MoveAsync(session, target);
        return target;
    }

    private async Task<(GlobalSystemMediaTransportControlsSession Session, MediaTimeline Timeline)> CurrentAsync()
    {
        var manager = await GetManagerAsync();
        var session = PickSession(manager)
            ?? throw new InvalidOperationException("지금 재생 중인 미디어를 찾지 못했습니다. 유튜브 등을 먼저 재생해 주십시오.");

        var info = session.GetPlaybackInfo();
        var tl = session.GetTimelineProperties();
        var timeline = new MediaTimeline(
            tl.Position, tl.LastUpdatedTime,
            info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            tl.StartTime, tl.EndTime, tl.MinSeekTime, tl.MaxSeekTime);
        return (session, timeline);
    }

    private async Task MoveAsync(GlobalSystemMediaTransportControlsSession session, TimeSpan target)
    {
        var app = AppName(session.SourceAppUserModelId);
        bool ok;
        try
        {
            ok = await session.TryChangePlaybackPositionAsync(target.Ticks);
        }
        catch (Exception ex)
        {
            _tracker.Forget();
            throw new InvalidOperationException($"{app}의 재생 위치를 옮기지 못했습니다: {ex.Message}", ex);
        }

        if (!ok)
        {
            _tracker.Forget();
            throw new InvalidOperationException($"{app}이(가) 재생 위치 이동을 받아 주지 않았습니다. 이 앱은 지원하지 않는 것 같습니다.");
        }
    }

    private static async Task<string> TitleAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            return props?.Title ?? "";
        }
        catch
        {
            return ""; // 제목은 없어도 됨
        }
    }

    private async Task<GlobalSystemMediaTransportControlsSessionManager> GetManagerAsync()
    {
        _manager ??= GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
        try
        {
            return await _manager;
        }
        catch (Exception ex)
        {
            _manager = null; // 다음에 다시 시도
            throw new InvalidOperationException("Windows 미디어 제어에 연결하지 못했습니다: " + ex.Message, ex);
        }
    }

    /// <summary>재생 중인 앱 우선 (여러 개면 Windows 가 '현재'로 고른 앱), 없으면 현재 앱</summary>
    private static GlobalSystemMediaTransportControlsSession? PickSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var current = manager.GetCurrentSession();
        var playing = manager.GetSessions()
            .Where(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            .ToList();

        if (playing.Count > 0)
        {
            return playing.FirstOrDefault(s => current is not null && s.SourceAppUserModelId == current.SourceAppUserModelId)
                   ?? playing[0];
        }
        return current;
    }

    /// <summary>앱 ID → 보여줄 이름. 크롬으로 설치한 웹앱은 "Chrome._crx_<앱 ID>" 형태</summary>
    internal static string AppName(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "재생 중인 앱";
        if (id.Contains("cinhimbnkkaeohfgghhklpknlkffjgod", StringComparison.OrdinalIgnoreCase)) return "YouTube Music";
        if (id.Contains("_crx_", StringComparison.OrdinalIgnoreCase)) return "크롬 웹앱";
        if (id.StartsWith("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (id.StartsWith("msedge", StringComparison.OrdinalIgnoreCase) || id.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (id.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";

        var name = id;
        var bang = name.IndexOf('!');
        if (bang > 0) name = name[..bang];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }
}
