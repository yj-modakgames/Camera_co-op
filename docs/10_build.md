# 10. 빌드 조건 (단일 출처)

> 이 문서가 빌드 조건의 단일 진실 원천이다. 실행 가능한 형태는
> `Assets/_CameraCoop/Editor/CameraCoopBuild.cs` — 문서를 고치면 그 파일도 같이 고친다.

---

## 1. 공통 전제 (OS 무관)

| 항목 | 값 |
|---|---|
| Unity | **6000.3.15f1** 고정 (다른 버전은 URP 에셋 업그레이드 diff 발생, docs/09 §2-2) |
| 빌드 씬 | **2개** — index 0 `Assets/_CameraCoop/Scenes/NetplayTest.unity`, index 1 `Assets/_CameraCoop/Scenes/Netplay3D.unity` (`ProjectSettings/EditorBuildSettings.asset`, commit `d1bf470`) |
| Input | 새 Input System 전용 (`activeInputHandler: 1`). legacy `Input` API 사용 금지 |
| Steam AppID | 개발용 **480** (Spacewar). 출시 시 `SteamBootstrap.DevAppId`만 교체 |
| 산출물 위치 | `Builds/CameraCoop/` (gitignore 대상) |

**Canvas에는 `GraphicRaycaster`가 있어야 한다.** 없으면 버튼이 에러도 로그도 없이 조용히 죽는다 (2026-08-27 실제 발생, docs/09 §4).

---

## 2. OS별 조건

| 항목 | Windows | macOS |
|---|---|---|
| BuildTarget | `StandaloneWindows64` | `StandaloneOSX` |
| 산출물 | `CameraCoop.exe` | `CameraCoop.app` |
| Steam plugin | `Facepunch.Steamworks.Win64.dll` + `steam_api64.dll` | `Facepunch.Steamworks.Posix.dll` + `libsteam_api.dylib` |
| tracker 의존성 | `requirements.txt` (mediapipe **1.0.1**) | `requirements-intel-mac.txt` (mediapipe **0.10.21**) |
| tracker 설치 스크립트 | `setup_tracker.bat` | `setup_tracker.sh` (+chmod) |
| Python 버전 제약 | 없음 (1.0.1은 3.14까지 확인) | **3.12 이하** (0.10.21 wheel이 cp312까지) |
| 프로세스 트리 종료 | `taskkill /PID <pid> /T /F` | `pkill -P <pid>` 후 부모 `Kill()` |

Steam plugin 전환은 `.meta`의 Editor OS filter가 자동 처리한다 — 손댈 것 없다.

**Intel Mac에서 mediapipe 1.0.1은 설치 자체가 실패한다** (arm64 wheel만 배포). 근거는 `PythonTracker/requirements-intel-mac.txt` 주석.

---

## 3. 빌드 방법

### 3-1. Editor 메뉴
`Camera Co-op > Build for This OS` — 현재 Editor가 도는 OS에 맞춰 target·산출물 이름을 고르고 빌드한다.

### 3-2. CLI
```
unity cmd build --target StandaloneWindows64 --outputPath "Builds/CameraCoop/CameraCoop.exe" --confirm true
unity cmd build --target StandaloneOSX       --outputPath "Builds/CameraCoop/CameraCoop.app" --confirm true
unity cmd build_status
```
`build`는 비동기다. `build_status`가 `completed`가 될 때까지 폴링한다.

빌드 전에 `editor_status`로 `compiling:false`, `playMode:"stopped"`를 확인한다 (docs/09 §4).

---

## 4. 빌드 후 자동 배치 (payload)

`CameraCoopBuildPayload`(`IPostprocessBuildWithReport`)가 **메뉴 빌드든 CLI 빌드든 항상** 실행되어 산출물 옆에 다음을 깐다. 손으로 복사하는 단계는 없다.

```
Builds/CameraCoop/
  CameraCoop.exe (또는 .app)
  steam_appid.txt          <- 프로젝트 루트에서
  fake_hand.py             <- PythonTracker/
  README_FIRST.txt         <- PythonTracker/dist/
  tracker/
    hand_tracker.py
    config.py
    one_euro_filter.py
    models/hand_landmarker.task
    requirements.txt       <- OS에 맞는 원본을 이 이름으로 복사
    setup_tracker.bat|sh   <- OS에 맞는 것만
    run_tracker.bat|sh
```

배포 원본은 `PythonTracker/dist/`에 있다. 안내문·설치 스크립트를 고칠 일이 있으면 **그쪽**을 고친다 — `Builds/` 밑은 매 빌드마다 덮어써진다.

`.venv`는 payload에 포함하지 않는다. 받는 쪽이 `setup_tracker` 를 1회 실행한다.

---

## 5. 배포 zip 만들기

`.venv`가 생긴 뒤에 묶으면 수백 MB가 딸려 들어간다. 반드시 확인하고 묶는다.

```powershell
# 확인
Get-ChildItem Builds\CameraCoop -Recurse -Directory -Filter .venv
# 묶기
Compress-Archive -Path 'Builds\CameraCoop\*' -DestinationPath 'Builds\CameraCoop_Steam2p.zip' -Force
```

---

## 6. 빌드 후 확인 (DoD)

| # | 확인 | 방법 |
|---|---|---|
| B-1 | 빌드 성공, errors 0 | `build_status`의 `result: Succeeded` |
| B-2 | Steam 초기화 | Player.log에 `Setting breakpad minidump AppID = 480` + `SteamInternal_SetMinidumpSteamID` |
| B-3 | overlay 주입 | 프로세스 모듈에 `gameoverlayrenderer64.dll` (Windows) |
| B-4 | 씬 기동 | Player.log에 `[UdpHandReceiver] listening on 127.0.0.1:5052` |
| B-5 | **UI 클릭** | 버튼을 실제로 눌러 반응 확인. Editor에서만 확인하면 안 된다 (B-5는 빌드에서만 드러나는 결함이 있었다) |
| B-6 | payload | `tracker/`, `steam_appid.txt`, `README_FIRST.txt` 존재 + `requirements.txt`가 해당 OS용인지 |

Player.log 위치:
- Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Camera_co-op\Player.log`
- macOS: `~/Library/Logs/DefaultCompany/Camera_co-op/Player.log`

---

## 7. 미해결

- **N-5 (Steam 2인 실기 검증)** 미실시 — Steam 계정 2개 필요 (docs/09 §3)
- macOS 빌드는 아직 한 번도 만들어진 적이 없다. §2의 macOS 열은 코드·문서 근거이며 실측이 아니다
