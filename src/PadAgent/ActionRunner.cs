using System.Diagnostics;
using MacroPad.Core.Config;
using MacroPad.Core.Media;
using PadAgent.Media;
using PadAgent.Native;

namespace PadAgent;

/// <summary>
/// 트리거가 눌렸을 때 실제 동작 실행 (프로그램 실행 / 단축키 / 문자열 / 재생 위치 이동 / 샘플 큐).
/// UI 스레드에서만 부름 (TrayApp 이 넘겨줌).
/// </summary>
internal sealed class ActionRunner
{
    private readonly Action<string> _onError;
    private readonly Action<string> _onInfo;
    private readonly SemaphoreSlim _inputGate = new(1, 1); // 키 입력 동작끼리 섞이지 않게 하나씩
    private readonly CueBank _cues = new();
    private DateTime _lastSeekError = DateTime.MinValue;

    public ActionRunner(Action<string> onError, Action<string> onInfo)
    {
        _onError = onError;
        _onInfo = onInfo;
    }

    /// <summary>저장한 샘플 큐 개수</summary>
    public int CueCount => _cues.Count;

    /// <summary>저장한 샘플 큐를 모두 지움</summary>
    public void ClearCues() => _cues.Clear();

    /// <param name="at">키가 눌린 시각 (훅에서 잰 값. 샘플 큐는 이 순간의 재생 위치를 저장)</param>
    public void Run(Trigger trigger, DateTimeOffset at)
    {
        var action = trigger.Action;

        // 재생 위치 이동·샘플 큐는 연달아 누르는 동작이라 로그를 남기지 않음 (실패만 남김)
        if (action.Seek is { } seek)
            _ = SeekAsync(trigger, seek);
        else if (action.IsCueSet)
            _cues.SetKeyDown(at);
        else if (action.Cue is { } cue)
            _ = CueAsync(trigger, cue, at);
        else
        {
            AgentLog.Write($"트리거: {trigger.Name}");
        }

        if (!string.IsNullOrWhiteSpace(action.Run))
        {
            try
            {
                StartProcess(action);
            }
            catch (Exception ex)
            {
                Fail(trigger, "실행 실패", ex);
            }
        }

        if (trigger.Keys.Count > 0 || !string.IsNullOrEmpty(action.Text))
            _ = SendInputsAsync(trigger);
    }

    /// <summary>트리거 키를 뗐을 때 (누르고 있는 동안 의미가 있는 큐 지정 키만)</summary>
    public void Release(Trigger trigger, DateTimeOffset at)
    {
        if (trigger.Action.IsCueSet)
            _cues.SetKeyUp(at);
    }

    internal static void StartProcess(ActionConfig action)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(action.Run!.Trim()),
            Arguments = action.Args is null ? "" : Environment.ExpandEnvironmentVariables(action.Args),
            UseShellExecute = true, // .exe 뿐 아니라 바로가기(.lnk), 폴더, URL 도 열 수 있음
        };
        if (!string.IsNullOrWhiteSpace(action.WorkingDir))
            psi.WorkingDirectory = Environment.ExpandEnvironmentVariables(action.WorkingDir);

        Process.Start(psi)?.Dispose();
    }

    private async Task SendInputsAsync(Trigger trigger)
    {
        await _inputGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var delay = trigger.Action.EffectiveDelayMs;

            // 패드의 트리거 키가 완전히 떨어질 시간을 조금 줌
            await Task.Delay(30).ConfigureAwait(false);

            foreach (var chord in trigger.Keys)
            {
                InputSender.SendChord(chord);
                if (delay > 0) await Task.Delay(delay).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(trigger.Action.Text))
                InputSender.SendText(trigger.Action.Text);
        }
        catch (Exception ex)
        {
            Fail(trigger, "키 입력 실패", ex);
        }
        finally
        {
            _inputGate.Release();
        }
    }

    private async Task SeekAsync(Trigger trigger, double seconds)
    {
        try
        {
            await MediaSeeker.Shared.SeekAsync(seconds, trigger.Action.EffectiveSeekAccel);
        }
        catch (Exception ex)
        {
            MediaFail(trigger, "재생 위치 이동 실패", ex);
        }
    }

    /// <summary>샘플 큐: 큐 지정 키를 누르고 있거나 비어 있는 큐면 지금 위치 저장, 아니면 저장한 위치로 이동</summary>
    private async Task CueAsync(Trigger trigger, int cue, DateTimeOffset at)
    {
        var mode = _cues.Press(at, cue);
        try
        {
            if (mode == CuePress.Set)
            {
                var point = await MediaSeeker.Shared.CaptureAsync(at);
                _cues.Store(cue, point);
                var title = point.Title.Length > 0 ? $" · {point.Title}" : "";
                AgentLog.Write($"[{trigger.Name}] 큐 {cue} 저장: {SeekTracker.Format(point.Position)}");
                _onInfo($"큐 {cue} ← {SeekTracker.Format(point.Position)}{title}");
            }
            else if (_cues.TryGet(cue, out var point))
            {
                await MediaSeeker.Shared.JumpAsync(point.Position);
            }
        }
        catch (Exception ex)
        {
            MediaFail(trigger, mode == CuePress.Set ? $"큐 {cue} 저장 실패" : $"큐 {cue} 이동 실패", ex);
        }
    }

    private void MediaFail(Trigger trigger, string what, Exception ex)
    {
        AgentLog.Write($"[{trigger.Name}] {what}: {ex}");
        // 노브를 돌리거나 큐를 연달아 누르는 동안 칸마다 알림이 뜨지 않게 가끔만
        var now = DateTime.UtcNow;
        if (now - _lastSeekError < TimeSpan.FromSeconds(30)) return;
        _lastSeekError = now;
        _onError(ex is InvalidOperationException ? ex.Message : what + ": " + ex.Message);
    }

    private void Fail(Trigger trigger, string what, Exception ex)
    {
        AgentLog.Write($"[{trigger.Name}] {what}: {ex}");
        _onError($"[{trigger.Name}] {what}: {ex.Message}");
    }
}
