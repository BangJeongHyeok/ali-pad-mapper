# MacroPad 프로젝트 인수인계 (2026-11-18 기준)

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
macropad.json                메인 설정 (JSONC)
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
src/PadFlasher/              콘솔: list / info / upload [--dry-run] [--yes] / keys / raw
src/PadAgent/                WinForms 트레이 앱
  TrayApp.cs                 메뉴, 설정 자동 재로드(FileSystemWatcher), 패드에 쓰기, 알림
  TriggerMap.cs              actions → (VK, 수식 키 플래그) 표
  ActionRunner.cs            run(ShellExecute) / keys(SendInput) / text(유니코드 입력)
  Native/KeyboardHook.cs     WH_KEYBOARD_LL, 트리거만 삼킴
  Native/InputSender.cs      SendInput, dwExtraInfo 서명 'MPAD'로 자기 입력 무시
  Native/StartupRegistration.cs
  AgentLog.cs                %LOCALAPPDATA%\MacroPad\agent.log
tests/MacroPad.Tests/        xUnit 24케이스 (공개 테스트 벡터와 바이트 비교, 설정 파일 검증)
tools/publish.ps1            단일 exe publish (-SelfContained 선택)
```

동작 흐름:
```
macropad.json ─ layers ─▶ UploadPlan ─▶ K8850Protocol 패킷 ─▶ HidConnection(MI_00) ─▶ 패드 메모리
              └ actions ─▶ TriggerMap ─▶ KeyboardHook(F13~F24 삼킴) ─▶ ActionRunner
```

---

## 6. 알려진 제약 / 주의점

- 관리자 권한으로 실행된 창에는 PadAgent의 SendInput이 막힘 (Windows UIPI)
- `win+l` 같은 보안 단축키는 SendInput으로 안 됨
- 회사 보안 프로그램(EDR)이 전역 키보드 훅을 경고할 수 있음. 훅은 actions의 트리거 키만 확인하고 다른 입력은 기록하지 않음
- 트리거를 F13~F24가 아닌 일반 키로 잡으면 원래 키보드 입력도 가로챔 (설정 로드 시 경고 표시)
- 트리거 키를 누르고 있을 때의 자동 반복은 첫 번째만 실행
- 미디어 키는 단독으로만 가능 (`ctrl+volumeup` 불가). 밝기(`brightnessup/down`)는 패드 전용(PC 에이전트용 VK 없음)
- 패드는 설정 읽기 불가 → 쓰기 전 상태 백업은 설정 파일로만

---

## 7. 다음 작업 후보 (아이디어, 미결정)

- [ ] `info` 질의 실물 확인 → 연결된 패드의 키/노브 수를 읽어 설정 검증에 활용
- [ ] 레이어 2·3 동작/전환 방법 확인
- [ ] 마우스 바인딩 (공개 자료상 종류 3, 17바이트 형식. 클릭/휠/이동)
- [ ] 설정 편집 GUI (현재는 JSON 직접 편집)
- [ ] 활성 창(앱)별 actions 프로필 전환 (PC 에이전트 쪽)
- [ ] 패드 USB 연결/분리 감지, 트레이 아이콘에 연결 상태 표시
- [ ] 바뀐 슬롯만 쓰기 (이전에 쓴 설정과 비교)
- [ ] CI (GitHub Actions `windows-latest` 에서 build + test)
- [ ] 배포용 publish에 `-p:DebugType=none` 추가 (아래 8장)

---

## 8. 공유 / 배포 시 개인정보 체크

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
dotnet run --project src/PadAgent
dotnet test
.\tools\publish.ps1
```
