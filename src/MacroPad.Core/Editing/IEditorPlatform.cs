using MacroPad.Core.Config;

namespace MacroPad.Core.Editing;

/// <summary>
/// 편집기 화면이 필요로 하는 OS 기능. Windows(PadAgent)와 미리보기 서버(tools/UiPreview)가 각각 구현.
/// 모든 메서드는 UI 스레드에서 불림 (UploadAsync 는 내부에서 백그라운드로 넘겨야 함).
/// </summary>
public interface IEditorPlatform
{
    /// <summary>패드의 설정 통로가 연결돼 있는지</summary>
    bool IsDeviceConnected(PadConfig config);

    /// <summary>패드에 쓰기. 반환값: 쓴 칸 수. 실패하면 예외.</summary>
    Task<int> UploadAsync(PadConfig config, bool onlyChanged, string statePath, Action<string> log);

    /// <summary>파일 고르기 창. 취소하면 null.</summary>
    string? BrowseFile();

    /// <summary>폴더 고르기 창. 취소하면 null.</summary>
    string? BrowseFolder();

    /// <summary>파일/폴더를 기본 프로그램으로 열기</summary>
    void OpenPath(string path);

    /// <summary>파일 탐색기에서 이 파일 위치 보여주기</summary>
    void RevealPath(string path);

    /// <summary>로그 파일 경로 (없으면 null)</summary>
    string? LogPath { get; }

    bool IsStartupEnabled();

    void SetStartupEnabled(bool enabled);

    /// <summary>PC 동작 미리 실행 (프로그램/웹사이트 열기만)</summary>
    void RunAction(ActionConfig action);
}
