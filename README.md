# MacroPad

알리 매크로 패드(**VID 514C / PID 8850**, 12키 + 노브 2개)를 판매자 프로그램 없이 쓰기 위한 도구.

- 드라이버 설치 없음 (Windows 기본 HID API만 사용)
- **편집기 창**에서 칸을 클릭해서 키 매핑 (미디어 키 / 단축키 / 프로그램 실행 / 문자열 입력)
- 설정은 `macropad.json` 파일 하나로 관리 (직접 편집도 가능)
- C# / .NET 10 / Visual Studio 솔루션

[![CI](https://github.com/BangJeongHyeok/ali-pad-mapper/actions/workflows/ci.yml/badge.svg)](https://github.com/BangJeongHyeok/ali-pad-mapper/actions/workflows/ci.yml)

---

## 1. 어떻게 동작하나

비유하면 패드는 **버튼에 기능을 새겨 넣을 수 있는 리모컨**이야.

```
[macropad.json]
   │
   ├─ layers  ──(PadFlasher 또는 트레이 메뉴 "패드에 설정 쓰기")──▶ 패드 메모리에 새김
   │                                                              → 이후엔 PC 프로그램 없이도 동작
   │                                                                (볼륨, 재생/정지, 단축키 등)
   │
   └─ actions ──(PadAgent 트레이 앱이 실행 중일 때)──▶ 패드가 보낸 F13~F24 를 받아서
                                                       프로그램 실행 / 단축키 묶음 / 문자열 입력
```

| 프로젝트 | 역할 |
|---|---|
| `src/MacroPad.Core` | 설정 파일 해석, 키 이름 표, 패드 프로토콜(패킷 생성), HID 통신 |
| `src/PadFlasher` | 명령줄 도구. 패드 찾기 / 통신 확인 / 설정 쓰기 / 패킷 미리보기 |
| `src/PadAgent` | 트레이 상주 앱 + **편집기 창**. Windows 시작 시 자동 실행, actions 처리, 패드에 쓰기 |
| `ui` | 편집기 화면 (HTML/CSS/JS, exe 안에 포함) |
| `tools/UiPreview` | 편집기 화면을 브라우저에서 미리 보는 개발용 서버 |
| `tests/MacroPad.Tests` | 패킷 바이트 검증 테스트 (패드 없이 실행 가능) |

**노브 볼륨처럼 패드에 새긴 기능은 PadAgent 없이도 동작해.** PadAgent는 "프로그램 실행" 같은 PC 쪽 동작이 필요할 때만 켜두면 돼.

---

## 2. 준비

- Visual Studio 2026 (".NET 데스크톱 개발" 워크로드) 또는 .NET 10 SDK
- VS 2022에서 열어야 하면: `Directory.Build.props` 의 `net10.0` → `net8.0` 으로 한 줄만 바꾸면 됨

`MacroPad.sln` 을 열고 **빌드(Ctrl+Shift+B)**.

---

## 3. 첫 테스트: 위 노브만 볼륨으로 바꾸기

> ⚠️ 이 프로토콜은 같은 계열 패드를 공개 분석한 자료를 바탕으로 만들었고, **이 패드 실물로는 아직 테스트 전**이야.
> 그래서 처음엔 위 노브 3칸(왼쪽/누름/오른쪽)만 바꿔서 확인하고, 되면 나머지로 넓히는 순서로 가자.

솔루션 폴더(`MacroPad.sln` 있는 곳)에서 PowerShell 또는 터미널을 열고 차례대로:

### ① 패드 찾기
```powershell
dotnet run --project src/PadFlasher -- list
```
`★ 설정 통로` 표시가 붙은 줄이 하나 나오면 OK.
(UsagePage 0xFF00, 출력 65B 같은 정보가 보일 거야)

### ② 통신 확인 (선택)
```powershell
dotnet run --project src/PadFlasher -- info
```
`응답: 03 FB 0C 02 …` 처럼 나오면 "키 12개, 노브 2개"라는 뜻. 응답이 없어도 쓰기는 될 수 있으니 다음 단계로 가도 돼.

### ③ 보낼 패킷 미리보기 (실제로 안 보냄)
```powershell
dotnet run --project src/PadFlasher -- upload configs/01-top-knob-test.json --dry-run
```
이런 게 나오면 정상:
```
레이어1 위 노브 왼쪽 (슬롯 16): "volumedown" → 미디어 volumedown (0xEA)
    03 FD 10 01 02 00 02 00 00 EA …(0)
  확정
    03 FD FE FF …(0)
```

### ④ 실제로 쓰기
```powershell
dotnet run --project src/PadFlasher -- upload configs/01-top-knob-test.json
```
`패드에 쓸까? (y/N)` → `y`

### ⑤ 확인
메모장을 열어두고 **위 노브**를 돌려봐.
- 볼륨 표시가 뜨고 글자(ㄱ/ㅔ)가 안 찍히면 → **성공!** 4장으로.
- 여전히 글자가 찍히면 → 아래 "6. 안 될 때" 참고.
- 아래 노브는 아직 원래대로(ㄴ/ㅕ) 찍히는 게 정상.

---

## 4. 편집기로 설정하기 (추천)

![편집기](docs/images/editor-panel.png)

```powershell
dotnet run --project src/PadAgent
```
또는 publish 한 `PadAgent.exe` 를 실행하면 편집기 창이 열려. 이미 트레이에 떠 있으면 그쪽 창이 열리고, 트레이 아이콘을 더블클릭해도 같아.

1. 가운데 패드 그림에서 **버튼**을 누르거나, 오른쪽 **노브 설명**(왼쪽으로 돌리기 / 누르기 / 오른쪽으로 돌리기)을 누름
2. 오른쪽에 열리는 목록에서 동작을 고름. 고르는 즉시 `macropad.json` 에 자동 저장돼
   | 묶음 | 내용 | 동작하는 곳 |
   |---|---|---|
   | 할당 안 함 | 패드에 쓸 때 이 칸은 건드리지 않음 | – |
   | 미디어 | 볼륨, 음소거, 재생/일시정지, 다음/이전 곡 | **패드** (PC 앱 없이 동작) |
   | 자주 쓰는 단축키 | 복사, 붙여넣기, 화면 캡처, 작업 관리자 등 | **패드** |
   | 직접 지정 › 키보드 단축키 | 칸을 누르고 키 조합을 직접 눌러서 입력 (연속 입력 가능) | **패드** |
   | 직접 지정 › 프로그램/웹사이트/문장/단축키 묶음 | 프로그램·폴더·URL 열기, 문장(한글) 입력, 긴 단축키 묶음 | **PC** (MacroPad 실행 중일 때) |
3. 아래 **[패드에 적용]** 을 누르면 바뀐 칸만 패드에 써. PC 동작은 저장 즉시 반영돼

- 주황 점 = 패드에 아직 적용 안 한 칸. 패드는 설정을 읽어올 수 없어서, 이 PC에서 마지막으로 쓴 내용을
  `%LOCALAPPDATA%\MacroPad\pad-514C-8850.json` 에 기록해 두고 비교해. 기록이 없으면 처음 한 번은 전체를 적용해.
- PC 동작의 트리거 키(F13~F24, 모자라면 shift+/ctrl+ 조합)는 **자동 배정**이라 신경 쓸 필요 없어.
- 편집기로 처음 저장하면 기존 `macropad.json` 의 주석이 없어지고 편집기 형식으로 바뀌어 (원본은 `macropad.json.bak`).
- 화면은 WebView2(Windows 기본 Edge 엔진)로 그려. Windows 11은 기본 포함, Windows 10도 대부분 설치돼 있어.
  없는 PC에서는 예전 WinForms 편집기("기본 편집기")가 대신 열려. 트레이 메뉴에서 직접 열 수도 있어.

### 화면 고치기 (UI 개발)
화면 파일은 `ui/` (index.html, app.css, app.js) 에 있고, 빌드할 때 exe 안에 들어가.
패드나 Windows 없이 브라우저에서 바로 볼 수 있어:
```powershell
dotnet run --project tools/UiPreview
```
→ http://localhost:5180 . 실제 `macropad.json` 은 건드리지 않고 임시 복사본을 써. 패드 쓰기는 흉내만 내.
`ui/` 파일을 고치고 브라우저를 새로 고치면 바로 반영돼.

---

## 5. 명령줄로 전체 설정 쓰기 + 트레이 앱

### 패드에 전체 설정 쓰기
`macropad.json` 을 원하는 대로 고친 뒤:
```powershell
dotnet run --project src/PadFlasher -- upload
```
(설정 파일을 생략하면 솔루션 폴더의 `macropad.json` 을 씀)
`upload --changed` 를 붙이면 지난번에 쓴 것과 달라진 칸만 써.

기본 `macropad.json` 배치:
```
[재생/정지][이전 곡][다음 곡][ F21 ]      위 노브:  볼륨- / 음소거 / 볼륨+
[  F13  ][  F14  ][  F15  ][ F16 ]      아래 노브: 축소(ctrl+-) / 100%(ctrl+0) / 확대(ctrl+=)
[  F17  ][  F18  ][  F19  ][ F20 ]
```
F13~F24 칸은 PadAgent의 `actions` 와 연결돼 있어 (메모장, 계산기, 캡처, 작업 관리자 등 예시).

### PadAgent (트레이 앱) 실행
VS에서 시작 프로젝트를 `PadAgent` 로 바꾸고 실행(F5), 또는:
```powershell
dotnet run --project src/PadAgent
```
작업 표시줄 오른쪽 알림 영역에 파란 노브 아이콘이 생겨. 우클릭 메뉴:

| 메뉴 | 설명 |
|---|---|
| 편집기 열기… | 편집기 창 (아이콘 더블클릭도 동일) |
| 설정 파일 열기 (JSON) | 지금 쓰는 `macropad.json` 을 메모장 등으로 열기 |
| 설정 다시 불러오기 | 수동 새로고침 (저장하면 자동으로도 다시 읽음) |
| 패드에 설정 쓰기… | `layers` 를 패드에 씀 (PadFlasher upload 와 같음) |
| Windows 시작 시 자동 실행 | 체크하면 로그인할 때마다 자동 실행 (관리자 권한 불필요) |
| 로그 보기 | `%LOCALAPPDATA%\MacroPad\agent.log` |

> `actions` 는 저장하는 순간 바로 반영되고, `layers` 는 "패드에 설정 쓰기"를 해야 반영돼.

### 계속 쓸 실행 파일 만들기 (자동 실행용)
VS 디버그 빌드 대신 고정된 exe 를 시작 프로그램에 등록하는 게 좋아:
```powershell
.\tools\publish.ps1
```
→ `publish\PadAgent.exe` 를 실행하고 트레이 메뉴에서 "Windows 시작 시 자동 실행" 체크.
(자동 실행은 `--tray` 인자로 등록돼서 로그인할 때는 편집기 창 없이 트레이에만 떠)

GitHub Actions(CI)도 매번 같은 방식으로 빌드해서 `MacroPad-win-x64` 산출물로 올려 둬.

`publish` 폴더가 솔루션 폴더 안에 있는 동안은 **솔루션 폴더의 `macropad.json` 을 그대로 씀** (설정 파일이 하나로 유지됨). publish 폴더를 다른 곳으로 옮기면 그 옆의 `macropad.json` 을 씀.

---

## 6. 안 될 때

| 증상 | 해볼 것 |
|---|---|
| `list` 에 ★ 줄이 없음 | USB 다시 꽂기. `list --all` 로 VID 514C 가 보이는지 확인 |
| "열 수 없어" 오류 | 판매자 프로그램 같은 다른 프로그램이 패드를 잡고 있으면 끄기 |
| 쓰기는 성공인데 노브가 여전히 ㄱ/ㅔ 를 찍음 | `macropad.json`/테스트 파일의 `advanced.reportIdMode` 를 `"zero"` → `"payload"` 순서로 바꿔서 다시 쓰기 (테스트 파일엔 `"advanced": { "reportIdMode": "zero" }` 를 추가) |
| 글자는 안 찍히는데 볼륨이 안 변함 | `advanced.mediaModeByte` 를 `1` 로 바꿔서 다시 쓰기 |
| 왼쪽/오른쪽이 반대 | `ccw` 와 `cw` 값을 서로 바꾸기 |
| 엉뚱한 칸이 바뀜 | `advanced.knobSlotBase` 확인 (이 모델은 16) |
| 원래대로 되돌리고 싶음 | `upload configs/00-factory-default.json` (처음 상태 a~l, p~u) |
| PadAgent 단축키가 특정 창에서 안 먹음 | 관리자 권한으로 실행된 창에는 일반 앱이 키 입력을 못 보냄 (Windows 보안 정책) |

문제가 생기면 `upload … --dry-run` 출력과 실제 `upload` 출력을 같이 보면 원인 찾기가 쉬워.

**실험용**: 패킷 하나를 직접 보내볼 수 있어.
```powershell
dotnet run --project src/PadFlasher -- raw 03 FB FB FB
```

---

## 7. 설정 파일 문법

### 패드 칸 (`layers`)
| 쓰는 법 | 예 |
|---|---|
| 미디어 키 | `"volumeup"`, `"volumedown"`, `"mute"`, `"play"`, `"next"`, `"prev"`, `"stop"` |
| 키 하나 | `"a"`, `"f5"`, `"enter"`, `"f13"` |
| 단축키 | `"ctrl+c"`, `"ctrl+shift+esc"`, `"win+shift+s"` |
| 연속 입력 | `"ctrl+a, ctrl+c"` (쉼표 구분, 수식 키 포함 최대 18개) |
| 그대로 두기 | `null` 또는 `""` |

- 미디어 키는 단독으로만 가능 (`ctrl+volumeup` 불가)
- `+` 는 구분자라서 `+` 키는 `numpadplus` 또는 `shift+equal`
- 전체 키 이름: `dotnet run --project src/PadFlasher -- keys`
- 레이어는 최대 3개 (패드의 레이어 전환 버튼이 있는 경우)

### PC 동작 (`actions`)
```jsonc
"actions": {
  "f13": { "run": "notepad.exe" },                                   // 프로그램
  "f14": { "run": "C:\\Tools\\app.exe", "args": "--foo", "workingDir": "C:\\Tools" },
  "f15": { "run": "https://www.google.com" },                        // URL
  "f16": { "run": "%USERPROFILE%\\Downloads" },                      // 폴더
  "f17": { "keys": ["ctrl+c", "alt+tab", "ctrl+v"], "delayMs": 60 }, // 단축키 묶음
  "f18": { "text": "확인했습니다!" }                                  // 문자열 (한글 가능)
}
```
- 트리거는 **F13~F24** 를 추천 (일반 키보드엔 없는 키라 다른 입력을 안 가로챔)
- 경로의 `\` 는 JSON 이라 `\\` 로 두 번

---

## 8. 코드 지도 (수정할 때)

| 바꾸고 싶은 것 | 파일 |
|---|---|
| 키 이름 추가 / 미디어 키 추가 | `src/MacroPad.Core/Keys/KeyTable.cs` |
| 패킷 형식 | `src/MacroPad.Core/Protocol/K8850Protocol.cs` |
| 설정 파일 항목 추가 | `src/MacroPad.Core/Config/PadConfig.cs` |
| HID 장치 찾기 / 전송 | `src/MacroPad.Core/Hid/HidDevices.cs` |
| PC 동작 종류 추가 (예: 앱별 볼륨) | `src/PadAgent/ActionRunner.cs`, `TriggerMap.cs` |
| 트레이 메뉴 | `src/PadAgent/TrayApp.cs` |
| 편집기 화면 (모양, 동작 목록) | `ui/index.html`, `ui/app.css`, `ui/app.js` |
| 화면 ↔ C# 요청 처리 | `src/MacroPad.Core/Editing/EditorSession.cs` |
| 편집기 창 (WebView2 호스트) | `src/PadAgent/Editor/WebEditorForm.cs`, `WindowsEditorPlatform.cs` |
| 기본 편집기 (WebView2 없을 때) | `src/PadAgent/Editor/ClassicEditorForm.cs`, `KeyCaptureDialog.cs` |
| 칸 편집 규칙 (트리거 자동 배정 등) | `src/MacroPad.Core/Editing/ConfigEditor.cs` |
| 설정 파일 저장 형식 | `src/MacroPad.Core/Config/ConfigWriter.cs` |
| 패드에 쓴 기록 / 바뀐 칸만 쓰기 | `src/MacroPad.Core/Protocol/PadState.cs`, `UploadPlan.cs` |
| 키 가로채기 | `src/PadAgent/Native/KeyboardHook.cs` |

프로토콜 요약 (패킷 64바이트):
```
바인딩:  03 FD <슬롯> <레이어 1~3> <종류> 00 <내용…>
           슬롯: 버튼 1~12 / 위 노브 16·17·18 / 아래 노브 19·20·21 (왼쪽·누름·오른쪽)
           종류 1 키보드: 키 하나 → 01 00 00 <HID코드> 00
                          그 외   → <개수> 00 00 + 항목마다 [<코드> 00 <지연>]
                                    (수식 키 코드: Ctrl F1, Shift F2, Alt F3, Win F4)
           종류 2 미디어: 02 00 00 <Consumer 코드>
확정:    03 FD FE FF   (바인딩마다 뒤에 하나씩)
질의:    03 FB FB FB   → 응답 03 FB <키 개수> <노브 개수> …
```
Windows 에서는 HID 출력 리포트(65바이트 = 리포트 ID 1바이트 + 64바이트)로 보냄.

테스트 실행: VS 테스트 탐색기 또는 `dotnet test`

---

## 9. 참고 / 출처

- 프로토콜 분석: [kriomant/ch57x-keyboard-tool](https://github.com/kriomant/ch57x-keyboard-tool) (MIT)
  - 이슈 #89 "Supporting 0x514c : 0x8850" — 12키+노브2 모델 슬롯 번호, 미디어 코드, Windows HID 방식
  - PR #154 / #174 — 판매자 프로그램 HID 캡처 기반 `FD` 바인딩 형식
- 이 프로젝트 코드는 위 공개 분석 내용을 바탕으로 C#으로 새로 작성한 것.
- PadAgent의 키보드 훅은 `actions` 에 적힌 트리거 키만 확인하고, 나머지 입력은 저장하거나 기록하지 않아 (`TrayApp.OnKey` 참고). 회사 보안 프로그램이 "전역 키보드 훅"을 경고하면 이 점을 참고.
