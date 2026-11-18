using System.Diagnostics;
using MacroPad.Core;
using MacroPad.Core.Config;
using MacroPad.Core.Editing;
using MacroPad.Core.Hid;
using PadAgent.Media;
using PadAgent.Native;

namespace PadAgent.Editor;

/// <summary>편집기 화면이 쓰는 Windows 기능 (패드 쓰기, 파일 고르기, 자동 실행 등).</summary>
internal sealed class WindowsEditorPlatform : IEditorPlatform
{
    private readonly IWin32Window _owner;

    public WindowsEditorPlatform(IWin32Window owner)
    {
        _owner = owner;
    }

    public bool IsDeviceConnected(PadConfig config) =>
        HidDevices.FindConfigInterface(config.Device.VendorIdValue, config.Device.ProductIdValue) is not null;

    public async Task<int> UploadAsync(PadConfig config, bool onlyChanged, string statePath, Action<string> log)
    {
        var lines = new List<string>();
        try
        {
            var written = await Task.Run(() => PadProgrammer.Upload(config, line =>
            {
                lock (lines) lines.Add(line);
            }, dryRun: false, onlyChanged: onlyChanged, statePath: statePath));
            AgentLog.Write("편집기: 패드에 쓰기 완료\n" + string.Join("\n", lines));
            return written;
        }
        catch (Exception ex)
        {
            AgentLog.Write("편집기: 패드에 쓰기 실패\n" + string.Join("\n", lines) + "\n" + ex);
            // EditorSession 이 메시지로 보여줄 수 있는 종류로 바꿔서 던짐
            throw new IOException(ex.Message, ex);
        }
        finally
        {
            foreach (var line in lines) log(line);
        }
    }

    public string? BrowseFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "열 프로그램 / 파일",
            Filter = "프로그램 (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|모든 파일 (*.*)|*.*",
            DereferenceLinks = false, // 바로가기(.lnk)는 바로가기 그대로
        };
        return dialog.ShowDialog(_owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "열 폴더", UseDescriptionForTitle = true };
        return dialog.ShowDialog(_owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public void OpenPath(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public void RevealPath(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();

    public string? LogPath => AgentLog.FilePath;

    public bool IsStartupEnabled() => StartupRegistration.IsEnabled();

    public void SetStartupEnabled(bool enabled) => StartupRegistration.SetEnabled(enabled);

    public void RunAction(ActionConfig action) => ActionRunner.StartProcess(action);

    public Task<string> SeekMediaAsync(double seconds) =>
        MediaSeeker.Shared.SeekAsync(seconds, accel: false, describe: true);
}
