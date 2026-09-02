# CLAUDE.md — Camera_co-op

카메라 핸드트래킹 협동 게임. Python이 손을 추적해 UDP로 보내고, Unity가 받아 렌더한다.

## 문서 지도 (자동 로드되지 않음 — 필요할 때 읽을 것)

| 문서 | 내용 |
|------|------|
| `docs/README.md` | 문서 인덱스 |
| `docs/01_architecture.md` | 전체 구조 |
| `docs/02_protocol.md` | UDP v1 JSON 패킷 스키마 |
| `docs/03_python_tracker.md` | PythonTracker 설계 |
| `docs/04_unity_client.md` | Unity 수신·렌더 |
| `docs/05_test_plan.md` | 검증 DoD (Python 측 판정 기준) |
| `docs/09_relay_quiz_mode.md` | 현재 branch(`codex/relayquiz-steam-2p`)의 대상 기능 |
| `docs/10_build.md` | 빌드 |
| `docs/16_implementation_roadmap.md` | 로드맵 |
| `QUALITY_CHECKLIST.md` | 품질 채점표 (아래 게이트) |
| `PythonTracker/README.md` | venv·실행·fake_hand |

## 스택 / 사실

- Unity **6000.3.15f1** · URP · C#
- Unity 코드: `Assets/_CameraCoop/` — `Scripts/`(CameraCoop.Runtime.asmdef), `Tests/EditMode/`, `Tests/Support/`, `Editor/`, `Prefabs/`, `Scenes/`, `Data/`, `Materials/`
- Python 코드: `PythonTracker/` — `hand_tracker.py`, `camera_utils.py`, `one_euro_filter.py`, `fake_hand.py`, `config.py`
- UDP 포트 `5052` (127.0.0.1)
- Steam 빌드: `steam_appid.txt`, 산출물 `Builds/`
- MCP: `.mcp.json`의 `unity-editor-mcp` (`unity mcp --project-path C:\git\Camera_co-op`), `.claude/settings.local.json`에서 활성화됨

## ⚠️ 먼저 읽을 것 — 이 환경의 함정

1. **Unity Editor가 열려 있으면 `unity` CLI batch 명령(test/build)은 exit 1로 실패한다.** Editor를 먼저 닫아라.
2. **`execute_code` 금지.** 코드 변경 검증은 `refresh_unity` → `read_console` 경로만 쓴다 (`docs/05_test_plan.md`).
3. 컴파일 판정 기준: C# 오류 0. 기존 경고(MCP WebSocket 재연결, `com.unity.pipeline` automated mode)는 알려진 잔존 경고이므로 신규 경고와 구분해서 보고한다.
4. `docs/05_test_plan.md` 1-3의 UDP 수신 원라이너는 **Unity가 포트를 놓은 상태에서만** 쓸 수 있다. Unity 실행 중에 쓰면 bind 실패한다.
5. `PythonTracker/` 명령은 항상 venv 인터프리터로 실행한다. 전역 python 쓰면 mediapipe가 없다.

## 품질 게이트 — 기능 구현마다

- 기준 문서: **`QUALITY_CHECKLIST.md`** (이 repo 자체 문서. 재생성 금지, 그대로 사용)
- 배점: 기능 2.0 / 성능 2.0 / 검증 2.0 / 코드 품질 2.0 / 최적화 2.0 = 10.0
- 총점 **9.0 미만이면 코드를 고쳐서 재채점**한다. 점수 이력을 남긴다 (예: 7.8 → 8.6 → 9.2).
- 채점은 증거 기반. 성능은 측정·코드분석 근거, 검증은 실제 실행 결과 인용. 코드 변경 없이 점수만 올리는 것 금지.
- 적용 범위: Unity 측(`Assets/_CameraCoop/`). Python 측(`PythonTracker/`)은 `docs/05_test_plan.md`의 DoD로 판정한다.

## 절대 규칙

- 승인된 `docs/` 설계 명세를 임의로 축소·확대하지 않는다. 명세와 다르게 가려면 먼저 보고한다.
- `Assets/_CameraCoop/` 밖(`Assets/Plugins`, `Assets/Settings`, `Assets/TutorialInfo`, `Assets/_Recovery`)은 요청 없이 수정하지 않는다.
- `Library/`, `Temp/`, `Logs/`, `ProfilerCaptures/`, `Builds/`는 생성물이다. 커밋·수정 대상 아니다.
- `.meta` 파일을 손으로 만들거나 지우지 않는다. Unity가 만든다.

## 명령

```bash
# Python venv (Windows)
python -m venv PythonTracker\.venv
PythonTracker\.venv\Scripts\python.exe -m pip install -r PythonTracker\requirements.txt

# 트래커 실행
PythonTracker\.venv\Scripts\python.exe PythonTracker\hand_tracker.py

# 카메라 없이 가짜 패킷 (스키마 점검 / 두 손 30초)
PythonTracker\.venv\Scripts\python.exe PythonTracker\fake_hand.py --selfcheck
PythonTracker\.venv\Scripts\python.exe PythonTracker\fake_hand.py 30

# Unity EditMode 테스트 — Editor 닫은 상태에서
unity test C:\git\Camera_co-op --mode EditMode --output test-results.xml

# 빌드
unity build C:\git\Camera_co-op
```

CLI 확인: `unity test --help` / `unity build --help`. `test`는 위치 인수로 프로젝트 경로를 받고 플랫폼은 `--mode`다 (`--project-path`·`--platform` 아님).
