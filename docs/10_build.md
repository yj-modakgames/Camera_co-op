# 10. 빌드 조건

이 문서는 기존 build와 현재 4인 Steam `RelayQuizOnline` Scene catalog 조건을 구분해 기록한다. Task 14의 네 Scene Editor artifact와 validator는 확인됐으며, 실제 Steam 4계정·camera 기기 QA는 외부 검증 대기다.

## 1. 게임 용도와 scene 범위

| 용도 | Scene | 설명 |
|---|---|---|
| 기존 Steam 게임 | `Assets/_CameraCoop/Scenes/NetplayTest.unity` + `Assets/_CameraCoop/Scenes/Netplay3D.unity` | 기존 `NetplayTest`/`Netplay3D` netplay 용도와 기존 build를 보존한다. |
| 기존 local RelayQuiz | `Assets/_CameraCoop/Scenes/RelayQuiz.unity` | 한 화면에서 2–4명이 번갈아 진행하는 local game이다. |
| 신규 Steam online RelayQuiz | `Assets/_CameraCoop/Scenes/RelayQuizOnline.unity`, `RelayCopy.unity`, `MemoryCopy.unity`, `CoopMural.unity` | lobby와 세 additive game Scene으로 구성된 4p entry다. build catalog는 정확히 이 네 path다. |

기존 legacy menu scene 목록과 `ProjectSettings/EditorBuildSettings.asset`의 `NetplayTest` + `Netplay3D` 목록 사이의 기존 불일치는 이 문서에서 기록만 한다. `RelayQuizOnlineBuild`가 전역 `EditorBuildSettings`를 바꾼다고 가정하지 않는다.

공통 Unity version은 `6000.3.15f1`이다. 기존 build의 정확한 산출물·payload·DoD는 이 문서의 legacy 항목을 따른다. 신규 online build에는 전용 helper `Assets/_CameraCoop/Editor/RelayQuizOnlineBuild.cs`의 scene 배열과 output path를 사용한다.

## 2. 기존 build 조건

| 항목 | Windows | macOS |
|---|---|---|
| BuildTarget | `StandaloneWindows64` | `StandaloneOSX` |
| 기존 산출물 | `Builds/CameraCoop/CameraCoop.exe` | `Builds/CameraCoop/CameraCoop.app` |
| tracker 의존성 | `requirements.txt` (`mediapipe 1.0.1`) | `requirements-intel-mac.txt` (`mediapipe 0.10.21`) |
| setup script | `setup_tracker.bat` | `setup_tracker.sh` (+chmod) |

기존 Steam AppID는 source의 `SteamBootstrap.DevAppId` 값인 개발용 `480` (Spacewar)이다. 기존 build의 `EditorBuildSettings`는 NetplayTest + Netplay3D이며, local `RelayQuiz`는 별도 scene이다.

## 3. 신규 Steam online RelayQuiz 4p 조건

| 항목 | Windows | Intel Mac |
|---|---|---|
| Scene catalog | `Assets/_CameraCoop/Scenes/RelayQuizOnline.unity` → `Assets/_CameraCoop/Scenes/RelayCopy.unity` → `Assets/_CameraCoop/Scenes/MemoryCopy.unity` → `Assets/_CameraCoop/Scenes/CoopMural.unity` | 같은 네 path |
| BuildTarget | `StandaloneWindows64` | `StandaloneOSX` |
| Architecture | x64 | Intel x64 명시 |
| 산출물 | `C:/git/Camera_co-op/Builds/RelayQuizOnline/CameraCoopRelayOnline.exe` | `Builds/RelayQuizOnlineMac/CameraCoopRelayOnline.app` |
| helper entrypoint | `Camera Co-op/RelayQuiz Online/Build Windows x64` | `Camera Co-op/RelayQuiz Online/Build Intel Mac x64` |

2026-08-31 Windows Player build 기록(`Succeeded`, errors 0, warnings 1)은 이전 camera/setup 작업의 historical evidence다. Task 19 final gate에서는 fresh `BuildWindows64`가 `Succeeded`, errors `0`, warnings `1`로 재생성되었고, PE x64(`0x8664`/`0x020B`)와 exact PID 18초 Player smoke, this-run Player error-like `0`, exact PID 종료 및 final process `0`을 확인했다 ([Task 19 receipt](../.omo/evidence/party-scene-split/task-19-final-gate/receipt.md)). 단일 warning은 `No RuntimePipelineManager components found in build scenes`인 known Pipeline warning이다. 이 결과도 Steam 4 account, webcam/phone, physical gesture, long profile, Intel Mac 검증을 포함하지 않는다. Intel Mac build에는 macOS Build Support 설치 승인 또는 별도 Mac build environment가 필요하며, 아직 실행하지 않았다.

`PartySceneCatalog.BuildScenePaths`의 정확한 네 path를 build helper가 사용하며, `EditorBuildSettings`는 이 네 path를 앞부분에 같은 순서로 두고 기존 `NetplayTest`·`Netplay3D` legacy 항목을 뒤에 보존한다. `RelayQuizOnlineBuild`는 lobby와 세 additive Scene의 존재·순서를 검증한 뒤 build한다. Mac build 시 camera usage description도 임시 설정하지만, 이를 실제 macOS camera permission 검증으로 해석하지 않는다.

## 4. tracker·payload·운영 전제

- Windows와 Intel Mac은 서로 다른 tracker dependency/setup을 사용한다. Windows `.venv`를 Mac에 복사하지 않으며, 기존 `.venv`를 설치·수정·삭제하지 않는다.
- 신규 scene에서는 camera auto-start를 수행하지 않는다. 오른쪽 위 `CameraToggle`을 mouse로 눌러 시작하고, `.venv` 누락·dependency 오류·OS camera permission·occupied camera 등 실패 원인을 표시한다. 실패 후 자동 반복하지 않고 retry를 제공한다. local `RelayQuiz` scene은 기존처럼 manual camera start다.
- Camera raw video와 hand landmarks는 network payload로 보내지 않는다. Drawing은 recipient별 비공개 view 계약에 따라 실시간 전송하지 않는다.
- Hand game buttons와 drawing, keyboard answer는 유지한다. Mouse exception은 connection/invite, camera recovery, pause `계속`에 한정한다. Drawing `Resume`은 app focus와 fresh hand 수신이 모두 필요하다.
- Disconnect 시 현재 round를 abort하고 timer/input/private render를 중단한 뒤 re-invite한다. reconnect restoration과 host migration은 범위에 없다. 다음 game에서는 drawing/guessing role을 교대한다.

## 5. 신규 online 실행 전제

실기 확인에는 서로 다른 Steam account 네 개와 서로 다른 device 네 대가 필요하다. 현재 이 4계정·4device 시험은 실행하지 않았다. 두 build가 이미 실행된 상태에서 host가 invite하고 네 player가 ready인 뒤에만 시작한다. 이 절차는 app cold-start, deployment, store distribution을 보장하지 않는다.

## 6. 기존 build 후 확인 기록

기존 legacy build의 기존 확인 기록과 미해결 항목은 보존한다. 특히 기존 Steam 2인 실기 검증은 아직 미실시이고, macOS build도 실측되지 않았다. 이 문서의 legacy 기록을 신규 `RelayQuizOnline` build 성공 증거로 재사용하지 않는다.

Historical Windows evidence (2026-08-31): `Succeeded`, errors 0, warnings 1, output `Builds/RelayQuizOnline/CameraCoopRelayOnline.exe`, PE AMD64 `0x8664`/PE32+ `0x020B`; scene validator와 Play evidence도 통과했다. 배포 payload의 필수 `tracker/camera_utils.py`는 source/payload SHA256 일치와 import 성공까지 확인했다 ([payload evidence](../.omo/evidence/final-validation-20260831-security-postfix/windows-payload-import.txt)). Player는 10초 생존 후 owned PID가 종료됐고 log error-like line은 0이다 ([Player evidence](../.omo/evidence/final-validation-20260831-security-postfix/windows-player-launch.json)). 이는 Task 14 current Player gate가 아니며, Steam 4계정·실제 webcam/phone·Intel Mac target-device 검증도 미검증이다.

### 6-1. 기존 legacy build의 payload와 DoD

기존 `CameraCoopBuildPayload`(`IPostprocessBuildWithReport`)는 legacy build 산출물 옆에 `steam_appid.txt`, `fake_hand.py`, `README_FIRST.txt`, OS별 `tracker/` source·model·requirements·setup/run script를 배치한다. `.venv`는 payload에 포함하지 않는다. 기존 legacy build의 상세 payload 원본은 `PythonTracker/dist/`이며, `Builds/` 아래 결과는 다음 build에서 덮어써질 수 있다.

기존 legacy DoD 기록은 build report의 `Succeeded`와 errors 0, Steam 초기화 Player.log, Windows overlay module, `[UdpHandReceiver] listening on 127.0.0.1:5052`, 실제 build UI click, OS에 맞는 payload 확인이다. Player.log는 Windows에서 `%USERPROFILE%\\AppData\\LocalLow\\DefaultCompany\\Camera_co-op\\Player.log`, macOS에서 `~/Library/Logs/DefaultCompany/Camera_co-op/Player.log`에 있다. 이 항목들은 legacy evidence이며 신규 `RelayQuizOnline` 결과가 아니다.
