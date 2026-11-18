using System.Diagnostics;
using MacroPad.Core.Config;
using PadAgent.Native;

namespace PadAgent;

/// <summary>트리거가 눌렸을 때 실제 동작 실행 (프로그램 실행 / 단축키 / 문자열).</summary>
internal sealed class ActionRunner
{
    private readonly Action<string> _onError;
    private readonly SemaphoreSlim _inputGate = new(1, 1); // 키 입력 동작끼리 섞이지 않게 하나씩

    public ActionRunner(Action<string> onError)
    {
        _onError = onError;
    }

    public void Run(Trigger trigger)
    {
        var action = trigger.Action;
        AgentLog.Write($"트리거: {trigger.Name}");

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

    private static void StartProcess(ActionConfig action)
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
            var delay = Math.Clamp(trigger.Action.DelayMs, 0, 5000);

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

    private void Fail(Trigger trigger, string what, Exception ex)
    {
        AgentLog.Write($"[{trigger.Name}] {what}: {ex}");
        _onError($"[{trigger.Name}] {what}: {ex.Message}");
    }
}
