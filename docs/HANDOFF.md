# MacroPad 프로젝트 인수인계 (2026-11-18 기준, 레포 이전 후 갱신)

알리 매크로 패드(12키 + 노브 2개)를 판매자 프로그램 없이 쓰기 위해 직접 만든 C# 도구.
이 문서는 지금까지의 결정, 확인된 사실, 코드 구조, 남은 일을 다음 작업 세션에 넘기기 위한 정리.

---

## 1. 배경과 목표

- 회사 PC에 알리에서 산 매크로 패드를 연결함. 판매자는 중국 회사 설정 프로그램(`szxiaozi.com/tool.zip`) 설치를 요구함
  → **회사 PC에 설치하기 애매해서 사용 안 함** (설치·실행 이력 없음)
- 목표
  1. 노브로 **시스템 전체 볼륨** 조절 (앱별 볼륨 아님) ✅
  2. 키로 재생/정지, 다음/이전 곡 ✅
  3. 키로 특정 프로그램 실행, 단축키 묶음 실행 ✅
  4. PC 시작 시 자동 실행 ✅
  5. exe만이 아니라 계속 수정 가능한 프로젝트 형태 ✅
  6. **최종 목표: 키 매핑을 자유롭게 할 수 있는 Windows 응용 프로그램**
     → 1차 WinForms 편집기 (main 머지) → 사용자 피드백 "UI/UX가 구려서 파악이 어렵다"
     → 2차: Logi Options+ 같은 깔끔한 기기 설정 앱을 참고해 WebView2 + HTML 로 새로 그림 (`claude/editor-redesign`)

---

## 2. 장치 정보 (실물 확인)

| 항목 | 값 |
|---|---|
| VID / PID | `514C` / `8850` |
| 배열 | 버튼 4열 × 3행 = 12개 + 오른쪽 노브 2개 (각각 왼쪽/누름/오른쪽) |
| 제품 문자열 | `USB Composite Device` |

`PadFlasher list` 결과 (HID 컬렉션):

| 인터페이스 | UsagePage / Usage | 입력 / 출력 | 출력 ReportID | 용도 |
|---|---|---|---|---|
| `MI_00` | `0xFF00` / `0x01` | 65B / 65B | **3** | **제조사 정의 = 설정 쓰기 통로** |
| `MI_01&Col01` | `0x0001` / `0x06` | 9B / 2B | 1 | 키보드 |
| `MI_01&Col02` | `0x0001` / `0x02` | 5B / 0B | – | 마우스 |
| `MI_01&Col03` | `0x0001` / `0x06` | 12B / 2B | 4 | 키보드 (2번째) |
| `MI_01&Col04` | `0x000C` / `0x01` | 3B / 0B | – | Consumer (미디어 키) |

공장 초기 입력값 (슬롯 n → 글자 `'a' + (n-1)`):
```
[a][b][c][d]     위 노브:   p(왼쪽) q(누름) r(오른쪽)   = 슬롯 16 17 18
[e][f][g][h]     아래 노브: s(왼쪽) t(누름) u(오른쪽)   = 슬롯 19 20 21
[i][j][k][l]     (슬롯 13~15는 이 모델에 없음)
```
- 패드는 **설정을 읽어오는 기능이 없음** → `configs/00-factory-default.json` 이 원래 상태 백업 역할.
- 레이어는 3개로 알려져 있으나 이 모델의 레이어 전환 방법은 **미확인**.

---

## 3. 프로토콜 (확인된 것)

패킷 64바이트. Windows에서는 HID 출력 리포트 65바이트로 보냄:
**`[ReportID 03][패킷의 2번째 바이트부터 64바이트]`** (= 패킷 첫 바이트 `03`이 곧 리포트 ID)

```
바인딩: 03 FD <슬롯> <레이어 1~3> <종류> 00 <내용…>
  종류 1 키보드:
    키 하나(수식 키 없음): 01 00 00 <HID 코드> 00
    그 외(단축키/연속):     <항목 수> 00 00 + 항목마다 [<코드> 00 <지연 0x32>]
                           수식 키 코드: Ctrl F1, Shift F2, Alt F3, Win F4, 오른쪽 F5~F8
                           최대 18항목
  종류 2 미디어: 02 00 00 <Consumer 코드 1바이트>
                 (E9 볼륨+, EA 볼륨-, E2 음소거, CD 재생/정지, B5 다음, B6 이전, B7 정지)
                 16비트 Consumer 코드(계산기 0x192 등)는 이 펌웨어가 안 받음

확정:  03 FD FE FF            ← 바인딩 하나마다 뒤에 하나씩
질의:  03 FB FB FB            → 응답 03 FB <키 수> <노브 수> … (공개 자료 기준, 이 패드에서 미실행)
초기화: 0으로 채운 패킷 1개 (쓰기 시작 전. 필요한지는 불명, 보내도 문제없었음)
```

검증 상태:
- ✅ 실물 확인: 미디어 키 바인딩(위 노브 슬롯 16~18), ReportID 3 자동 감지, 확정 패킷
- ✅ 사용자 보고: 전체 설정 업로드(미디어/F13~F21/ctrl 단축키) + PadAgent 동작 문제 없음
- ⬜ 미확인: `info` 질의 응답, 레이어 2·3, 마우스 바인딩(미구현, 아래 참고), LED

출처 (공개 분석, MIT): [kriomant/ch57x-keyboard-tool](https://github.com/kriomant/ch57x-keyboard-tool)
- 이슈 #89 "Supporting 0x514c : 0x8850": 12키+노브2 모델 슬롯 번호, 1바이트 미디어 코드, Windows `hid.dll` 방식, 65B 리포트
- PR #174 (미병합), #154 (병합, `k8850_4x4.rs`): 판매자 프로그램 HID 캡처 기반 `FD` 형식, 테스트 벡터
- 참고: 해당 도구 master는 `514c:8850` 을 4x4+노브3 모델 드라이버로 처리하며 노브 시작 슬롯이 17.
  **이 12키 모델은 16** (공장 기본 글자로 확인). Windows에서는 UsbDk 드라이버가 필요해서 채택 안 함.

---

## 4. 주요 결정과 이유

| 결정 | 이유 |
|---|---|
| 판매자 프로그램 미사용 | 회사 PC 보안 |
| ch57x-keyboard-tool 미사용 | 최신 GitHub 릴리스(v1.7.0)는 이 모델 미지원, master는 4x4+노브3 모델 기준(노브 슬롯이 달라 이 모델과 안 맞음). 게다가 Windows에서 UsbDk(커널 드라이버) 설치 필요 |
| PowerToys/AutoHotkey 단독 미사용 | 공장 기본값이 일반 알파벳이라 PC 쪽에서 "패드 입력"과 "진짜 키보드 입력"을 구분할 수 없음 |
| **패드 펌웨어에 직접 쓰기 + PC 에이전트** 구조 | 볼륨·재생 등은 패드에 새겨서 PC 프로그램 없이 동작. 프로그램 실행은 패드가 F13~F24를 보내고 에이전트가 가로챔 |
| 트리거로 F13~F24 사용 | 일반 키보드에 없어서 다른 입력과 안 겹침. 장치별 구분(Interception 드라이버, DLL 주입 훅)이 필요 없어짐 |
| 드라이버 없는 Windows 기본 HID API | `cfgmgr32`(장치 목록) + `hid.dll`(속성/캡) + `CreateFile`/`FileStream`(쓰기·읽기), 실패 시 `HidD_SetOutputReport` |
| 시작 프로그램 = `HKCU\...\Run` | 관리자 권한 불필요 |
| 설정 = JSONC 파일 하나 | 패드 설정(layers)과 PC 동작(actions)을 한 곳에서 관리, 주석 허용 |
| .NET 10 / VS 2026 | 사용자 환경 (`Directory.Build.props` 한 줄로 net8 전환 가능) |

---

## 5. 코드 구조

```
MacroPad.sln
Directory.Build.props        공통 설정 (MacroPadDotNet = net10.0)
macropad.example.json        설정 예시 (JSONC). 실제 설정 macropad.json 은 개인 데이터라 .gitignore, 없으면 예시를 복사해 만듦
configs/
  00-factory-default.json    공장 초기값 복원
  01-top-knob-test.json      첫 테스트용 (위 노브만)
src/MacroPad.Core/           OS 무관 로직 + Windows HID
  Keys/KeyTable.cs           키 이름 → HID 코드 / VK 코드 / Consumer 코드, 수식 키
  Keys/Chord.cs              "ctrl+shift+esc", "ctrl+c, ctrl+v" 파싱
  Protocol/PadBinding.cs     설정 문자열 → MediaBinding / KeySequenceBinding
  Protocol/K8850Protocol.cs  패킷 생성 (슬롯 계산, 바인딩/확정/질의 패킷)
  Protocol/UploadPlan.cs     설정 전체 → 쓸 목록 + 오류 검증 + 패킷 열거
  Config/PadConfig.cs        JSON 모델 (Device / Layers / Actions / Advanced)
  Config/ConfigLocator.cs    설정 파일 찾기: --config → 상위 폴더에 MacroPad.sln 있으면 그 옆 → exe 옆
  Hid/HidNative.cs           P/Invoke
  Hid/HidDevices.cs          장치 열거, 설정 통로 선택(UsagePage ≥ 0xFF00), 출력 ReportID 감지, 쓰기/읽기
  PadProgrammer.cs           Upload / Query / SendRaw (PadFlasher·PadAgent 공통)
  Protocol/SlotRef.cs        칸 식별(레이어+버튼/노브), 칸 이름, 슬롯 번호, PadConfig 칸 읽기/쓰기
  Protocol/PadState.cs       패드에 마지막으로 쓴 내용 기록 (%LOCALAPPDATA%\MacroPad\pad-VID-PID.json)
  Editing/ConfigEditor.cs    칸 단위 편집 모델. PC 동작 트리거(F13~F24 → shift+/ctrl+/ctrl+shift+) 자동 배정·회수
  Editing/SlotDisplay.cs     칸 표시 글자 ("[실행] notepad", "볼륨 +")
  Editing/EditorSession.cs   화면 ↔ C# 메시지 처리 + 화면 상태(JSON). 자동 저장. OS 기능은 IEditorPlatform 으로
  Editing/IEditorPlatform.cs
  Media/SeekTracker.cs       재생 위치 이동: 노브 가속(빨리 돌리면 최대 6배), 목표 위치 계산(연속 회전 시 직전 목표 기준), 곡 끝 1초 전까지
  Editing/KeyTester.cs       편집기 '키 테스트': 키 → 칸 찾기, 누른 시간, 샘플 큐 판단 흉내(별도 CueBank), "큐 지정 키가 먼저 떼짐" 힌트
  Media/CueBank.cs           샘플 큐: 큐 지정 키(펑션) 누름 상태 + 큐별 저장 위치. 누른 채로=저장, 그냥=이동(비었으면 저장), 짧게 톡=다음 한 번 저장
  Config/ConfigWriter.cs     macropad.json 저장 (버튼 4개씩 한 줄, .bak 백업, 임시 파일 후 교체)
  Keys/KeyNames.cs           키/미디어 표시 이름
src/PadFlasher/              콘솔: list / info / upload [--dry-run] [--yes] [--changed] / keys / raw
src/PadAgent/                WinForms 트레이 앱 + 편집기
  Program.cs                 단일 실행. 두 번째 실행 시 기존 프로세스에 "편집기 열기" 신호. --tray 면 트레이만
  Editor/WebEditorForm.cs    편집기 창 = WebView2 호스트. ui/ 를 exe 리소스에서 https://macropad.local/ 로 서빙
  Editor/WindowsEditorPlatform.cs  IEditorPlatform 구현 (패드 쓰기, 파일 고르기, 자동 실행, 연결 확인)
  Editor/ClassicEditorForm.cs  WinForms 기본 편집기 (WebView2 런타임 없을 때 대체)
  Editor/KeyCaptureDialog.cs "키 눌러서 입력" (기본 편집기용)
  TrayApp.cs                 메뉴, 설정 자동 재로드(FileSystemWatcher), 패드에 쓰기, 알림
  TriggerMap.cs              actions → (VK, 수식 키 플래그) 표
  ActionRunner.cs            run(ShellExecute) / keys(SendInput) / text(유니코드 입력) / seek(재생 위치 이동)
  Media/MediaSeeker.cs       Windows 미디어 제어(GlobalSystemMediaTransportControls)로 재생 중인 앱의 위치 이동
  Native/KeyboardHook.cs     WH_KEYBOARD_LL, 트리거만 삼킴. 전용 스레드(자체 메시지 루프)에서 돌고, 키 입력에 찍힌 시각을 넘김
  Native/InputSender.cs      SendInput, dwExtraInfo 서명 'MPAD'로 자기 입력 무시
  Native/StartupRegistration.cs
  AgentLog.cs                %LOCALAPPDATA%\MacroPad\agent.log
ui/                          편집기 화면 index.html / app.css / app.js (프레임워크·CDN 없음, 오프라인)
tools/UiPreview/             브라우저 미리보기 서버 (HttpListener, 가짜 패드). http://localhost:5180
tests/MacroPad.Tests/        xUnit 83케이스 (패킷 바이트, 설정 파일, 편집 모델, 저장 왕복, 패드 상태, 화면 메시지, 재생 위치 이동)
tools/publish.ps1            단일 exe publish (-SelfContained 선택, DebugType=none)
.github/workflows/ci.yml     windows-latest: build → test → publish → 산출물 MacroPad-win-x64
```

동작 흐름:
```
EditorForm ─ ConfigEditor ─ ConfigWriter ─▶ macropad.json
macropad.json ─ layers ─▶ UploadPlan(.OnlyChanged) ─▶ K8850Protocol 패킷 ─▶ HidConnection(MI_00) ─▶ 패드 메모리
                                                                              └▶ PadState 기록
              └ actions ─▶ TriggerMap ─▶ KeyboardHook(F13~F24 삼킴) ─▶ ActionRunner
```

편집기 화면 설계 메모 (2차):
- 레이아웃: 왼쪽 메뉴(장치 카드·연결 상태, 버튼 할당/설정) / 가운데 패드 그림(키캡에 동작 표시, 노브는 오른쪽 설명 카드)
  / 오른쪽 동작 패널(검색, 할당 안 함·미디어·자주 쓰는 단축키·직접 지정) / 아래 "패드에 적용" 막대
- 자동 저장(파일), 패드 쓰기만 명시적 버튼. 주황 점 = 패드에 아직 적용 안 한 칸
- 화면 메시지: { id, type, ... } → { id, ok, error?, result?, state }. 앱이 먼저 보내는 건 { type: "state", state }
  (패드 연결/분리 2초 폴링, 창 활성화 시 외부 변경 확인)
- 키 입력 받기는 JS 에서 e.code 기준 (한글 IME 상태여도 물리 키로 인식)
- 라이트/다크 모드 (OS 설정 따라감)

편집기 설계 메모:
- 설정 파일 형식은 그대로 둠 (layers 칸 = 트리거 키, actions = 동작). 편집기가 트리거를 자동 배정해서
  사용자는 "이 칸 = 메모장 실행"으로만 다룸. 같은 칸의 동작만 바꾸면 트리거 유지 → 패드에 다시 쓸 필요 없음
- 트리거 후보에서 alt/win 조합 제외: 패드가 alt/win을 눌렀다 떼면 메뉴 막대/시작 메뉴가 열릴 수 있음
- 입력 즉시 메모리 설정에 반영, [저장]해야 파일에 씀. 잘못된 입력은 설정을 안 바꾸고 오류만 표시
- 편집기 저장은 주석을 보존하지 않음 → 편집기 형식이 아닌 파일을 처음 덮어쓸 때 확인 + .bak 백업

---

## 6. 알려진 제약 / 주의점

- 관리자 권한으로 실행된 창에는 PadAgent의 SendInput이 막힘 (Windows UIPI)
- `win+l` 같은 보안 단축키는 SendInput으로 안 됨
- 회사 보안 프로그램(EDR)이 전역 키보드 훅을 경고할 수 있음. 훅은 actions의 트리거 키만 확인하고 다른 입력은 기록하지 않음
- 트리거를 F13~F24가 아닌 일반 키로 잡으면 원래 키보드 입력도 가로챔 (설정 로드 시 경고 표시)
- 트리거 키를 누르고 있을 때의 자동 반복은 첫 번째만 실행
- 미디어 키는 단독으로만 가능 (`ctrl+volumeup` 불가). 밝기(`brightnessup/down`)는 패드 전용(PC 에이전트용 VK 없음)
- 패드는 설정 읽기 불가 → 쓰기 전 상태 백업은 설정 파일로만
- 재생 위치 이동(`seek`)은 Windows 미디어 제어 API 를 써서 PadAgent 대상이 `net10.0-windows10.0.19041.0`(Windows 10 1809 이상).
  Windows SDK 연결 DLL 때문에 PadAgent.exe 가 약 1.4MB → 약 27MB 로 커짐 (줄이려면 필요한 인터페이스만 직접 COM 으로 선언하는 방법이 있음)
- 재생 위치 이동은 앱마다 지원이 다름. 크롬 계열(유튜브, 유튜브 뮤직 웹앱)은 미디어 세션 seekto 로 받는 것으로 예상, **실물 확인 전**.
  Windows 가 알려주는 재생 위치는 상태가 바뀔 때만 갱신될 수 있어서, 재생 중이면 LastUpdatedTime 이후 흐른 시간을 더해서 계산

---

## 7. 다음 작업 후보 (아이디어, 미결정)

- [ ] `info` 질의 실물 확인 → 연결된 패드의 키/노브 수를 읽어 설정 검증에 활용
- [ ] 레이어 2·3 동작/전환 방법 확인
- [ ] 마우스 바인딩 (공개 자료상 종류 3, 17바이트 형식. 클릭/휠/이동)
- [x] 설정 편집 GUI → `EditorForm` 1차 구현 (**Windows 실물 실행 확인 전**: Linux에서 빌드·테스트만 함)
- [ ] 활성 창(앱)별 actions 프로필 전환 (PC 에이전트 쪽)
- [ ] 패드 USB 연결/분리 감지, 트레이 아이콘에 연결 상태 표시
- [x] 바뀐 슬롯만 쓰기 (PadState 기록과 패킷 비교, 편집기 [패드에 쓰기] / `upload --changed`)
- [x] CI (GitHub Actions `windows-latest` 에서 build + test + publish 산출물)
- [x] 배포용 publish에 `-p:DebugType=none` 추가 (아래 8장)
- [ ] **새 편집기(WebView2) Windows 실물 확인** — 미리보기 서버에서만 확인함. 특히 키 입력 받기, 파일 고르기, 패드에 적용
- [x] 편집기에서 PC 동작 "지금 열어 보기" 버튼
- [x] 편집기가 열려 있는 동안 파일이 밖에서 바뀌면 자동 반영 (창 활성화 시)
- [x] 패드 USB 연결/분리 감지 (편집기 화면에 표시. 트레이 아이콘 표시는 아직)
- [ ] 칸 복사/붙여넣기 (EditorSession 에 copySlot 은 있음, 화면 연결 안 함)
- [x] 말투 통일: 사용자에게 보이는 문장은 전부 합니다체 (트레이 알림, 대화상자, 오류 메시지, 편집기 화면, 기본 편집기, PadFlasher)
- [x] 노브로 재생 위치 이동 (턴테이블처럼, 가속) — `seek` 동작. **유튜브/유튜브 뮤직 실물 확인 전**
- [ ] 재생 위치 이동이 안 되는 앱용 대체 수단 (앱별 단축키 보내기 등)
- [x] 키 테스트 (편집기 왼쪽 메뉴). 테스트 중에는 PC 동작 실행 안 함 (TrayApp → WebEditorForm.OnPadKey)
- [x] 키보드 훅을 UI 스레드에서 전용 스레드로 분리
  - 이유: 저수준 훅은 콜백이 Windows 제한 시간(기본 약 300ms) 안에 안 끝나면 그 키를 우리를 거치지 않고 넘기고,
    반복되면 훅이 말없이 제거됨. UI 스레드는 미디어 제어(GSMTC) 동기 호출·WebView2·알림 등으로 막힐 수 있어서
    PC가 바쁠 때 큐 지정 키의 누름/뗌을 놓칠 수 있었음 → 뗌을 놓치면 최대 20초 동안 큐 키가 전부 '저장'으로 처리됨
  - 같은 키의 누름이 2초 넘게 끊겼다가 다시 오면 그 사이 뗌을 놓친 것으로 보고 뗀 것으로 처리 (TrayApp.MissedKeyUpGap)
- [x] 샘플 큐 (`cue` / `cueSet`) — 펑션 키를 누른 채로 큐 키 = 지금 위치 저장, 큐 키만 = 그 위치로 이동. **실물 확인 전**
  - 패드가 단독 키(F13~F24) 바인딩을 누르고 있는 동안 계속 눌린 상태로 보내는지 미확인.
    바로 떼서 보내는 경우를 대비해 "짧게 톡 → 3초 안의 다음 큐 키 한 번 저장"도 넣어 둠 (CueBank.TapMax/ArmTimeout)
  - 큐 위치는 메모리에만 (앱 재시작 시 사라짐). 곡이 바뀌어도 안 지움 (사용자 요청: 곡이 바뀌면 제대로 안 돼도 됨)
  - 키보드 훅에서 누른/뗀 시각을 재서 넘김 → 미디어 제어 연결이 늦어도 누른 순간의 위치를 저장

---

## 8. 공유 / 배포 시 개인정보 체크

- 편집기 백업/임시 파일(`*.bak`, `*.tmp`)은 `.gitignore`로 제외
- `macropad.json` 은 `.gitignore` 로 제외 (레포에는 `macropad.example.json` 만). 예시를 고칠 때 개인 경로/문구를 넣지 않기

- 소스·설정 파일에는 개인정보 없음
- 단, **사용자 PC에서 빌드한 결과물**에는 로컬 경로(예: `C:\Users\<사용자>\…`)가 들어감
  - `bin/`, `obj/` (`project.assets.json`, `.pdb`): `.gitignore`로 제외되어 있음
  - publish한 exe 안에도 PDB 경로가 기록됨 → 배포할 땐 `publish.ps1`의 공통 인자에 `-p:DebugType=none` 추가 권장
- `macropad.json`의 `actions`에 개인 프로그램 경로나 `text` 문구를 넣었다면 공유 전에 확인
- `%LOCALAPPDATA%\MacroPad\agent.log`는 로컬에만 있음 (트리거 이름과 오류만 기록)

---

## 9. 자주 쓰는 명령

```powershell
dotnet run --project src/PadFlasher -- list
dotnet run --project src/PadFlasher -- upload --dry-run          # macropad.json 미리보기
dotnet run --project src/PadFlasher -- upload                    # 패드에 쓰기
dotnet run --project src/PadFlasher -- upload configs/00-factory-default.json   # 초기화
dotnet run --project src/PadFlasher -- raw 03 FB FB FB           # 패킷 직접 전송(실험)
dotnet run --project src/PadAgent                                # 트레이 + 편집기 창
dotnet run --project src/PadFlasher -- upload --changed          # 바뀐 칸만 쓰기
dotnet test
.\tools\publish.ps1
```
