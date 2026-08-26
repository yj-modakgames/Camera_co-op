# 06. macOS 인계 문서 — 웹캠 검증 이어받기

> 작성 2026-08-26 · 기준 commit: `db0263a` 다음 (이 문서를 포함한 commit)
> 배경: 개발 PC(Windows 11)에 웹캠이 없어 Phase 1의 하드웨어 의존 검증만 남았다. 웹캠이 있는 Mac에서 이어받는다.

---

## 1. 지금 어디까지 됐나

Phase 1(웹캠 손동작 → Unity 손 커서)의 **코드와 씬은 전부 완성**이고, **하드웨어 없이 검증 가능한 항목은 전부 통과**했다. 남은 것은 실제 웹캠·실제 손이 필요한 항목뿐이다.

| | 상태 |
|---|---|
| Python 손 추적 서버 (`PythonTracker/`) | 코드 완성. venv·의존성 설치 확인. 카메라 부재 에러 경로 검증 |
| Unity 수신·커서 (`Assets/_CameraCoop/`) | 코드 완성. EditMode 30/30 pass |
| 테스트 씬·프리팹 | `Scenes/HandTrackingTest.unity`, `Prefabs/HandCursor.prefab` 배선 완료 |
| Unity CLI 자동화 | `com.unity.pipeline` 설치됨. Editor 자동화 가능 |
| 품질 체크리스트 | **9.75 / 10** (감점: 웹캠 미검증 0.15 + 매직넘버 0.1) |

### 이 세션에서 잡은 버그 하나

`UdpHandReceiver`의 `lastSeq`에 reset 경로가 없어서, **Python을 재시작하면 커서가 영구히 살아나지 않았다.** 재시작한 Python은 `seq`를 0부터 보내는데 `seq <= lastSeq` 폐기 규칙이 그걸 전부 버렸기 때문이다. `PacketFilter.IsNewSession`으로 고쳤다 (commit `e9975b8`). 이건 문서(docs/02 §4)가 약속했지만 코드에 없던 동작이라, **Mac에서 Python을 Ctrl+C 하고 다시 켜보면 바로 확인 가능한 회귀 지점**이다.

---

## 2. 시작 전 필수 확인 — CPU 아키텍처로 의존성이 갈린다

```bash
uname -m        # arm64 -> Apple Silicon / x86_64 -> Intel
sw_vers         # ProductVersion
```

**`mediapipe 1.0.1`은 macOS arm64 wheel만 배포한다** (`mediapipe-1.0.1-py3-none-macosx_11_0_arm64.whl`). 그래서 Intel Mac은 별도 경로를 쓴다.

| | Apple Silicon (arm64) | **Intel (x86_64)** |
|---|---|---|
| requirements | `requirements.txt` | **`requirements-intel-mac.txt`** |
| Python | 3.12 ~ 3.14 | **3.12 고정** |
| mediapipe | 1.0.1 | **0.10.21** |
| cv2 출처 | `opencv-python 5.0.0.93` | `opencv-contrib-python` (mediapipe가 끌고 옴) |
| 최소 macOS | 11 | 11 (opencv가 OS에 맞춰 자동 선택) |

### Intel Mac 상세

**Python 3.12가 필요하다.** mediapipe 0.10.21의 wheel은 cp39/cp310/cp311/cp312까지다. macOS 기본 `python3`가 3.13 이상이면 설치가 안 된다.

```bash
brew install python@3.12          # 또는 python.org 설치본
python3.12 --version
```

**코드 수정은 필요 없다.** 0.10.21에도 Tasks API가 그대로 있고, `hand_tracker.py`가 쓰는 표면 전체를 실제로 검증했다 (2026-08-26, Windows의 Python 3.12 venv):

- `from mediapipe.tasks.python import vision, BaseOptions` 통과
- `create_landmarker()` → `HandLandmarker` 생성 (커밋된 모델 파일 그대로 로드)
- `detect_for_video()` → `HandLandmarkerResult`, `.hand_landmarks` / `.handedness` 존재
- `HandLandmarkerOptions`의 `num_hands`·`min_*_confidence` 4개 필드 전부 수용
- `draw_preview()` 통과, `hand_tracker.py` 전체 실행까지 정상 (카메라 부재 경로 exit 1)

**opencv를 pin하지 않는 이유:** `opencv-python 5.0.0.93`을 함께 pin하면 **numpy 요구가 충돌한다** — mediapipe 0.10.21은 `numpy<2`, opencv-python 5.x는 `numpy>=2`. mediapipe가 `opencv-contrib-python`을 버전 무제한으로 끌고 오므로 pip가 실행 OS에 맞는 wheel을 고르게 둔다. Intel macOS x86_64 wheel의 최소 OS가 버전마다 다르기 때문이다 (4.11/4.12 → macOS 13+, 4.10 → 12+, 4.9 → 10.16+). `hand_tracker.py`가 쓰는 cv2 API는 전부 오래된 안정 API라 이 범위 어디서든 돈다.

**주의:** Intel Mac은 CPU 추론이 Apple Silicon보다 느리다. 30Hz가 안 나오면 `config.py`의 `FRAME_WIDTH`/`FRAME_HEIGHT`를 낮추고(예: 480×360), 그래도 부족하면 `NUM_HANDS`를 1로 줄여 확인한다. 프레임레이트가 떨어지면 One Euro Filter의 체감 지연도 늘어나므로 §5-2 튜닝을 프레임레이트 확정 후에 해야 한다.

---

## 3. 환경 구축 (macOS)

### 3-1. Python

Apple Silicon:
```bash
cd /path/to/Camera_co-op
python3 -m venv PythonTracker/.venv
PythonTracker/.venv/bin/python -m pip install --upgrade pip
PythonTracker/.venv/bin/python -m pip install -r PythonTracker/requirements.txt
```

**Intel — Python 3.12와 전용 requirements를 쓴다:**
```bash
cd /path/to/Camera_co-op
python3.12 -m venv PythonTracker/.venv
PythonTracker/.venv/bin/python -m pip install --upgrade pip
PythonTracker/.venv/bin/python -m pip install -r PythonTracker/requirements-intel-mac.txt
```

설치 확인 (양쪽 공통):
```bash
PythonTracker/.venv/bin/python -c "import cv2, mediapipe as mp; from mediapipe.tasks.python import vision; print(cv2.__version__, mp.__version__, hasattr(vision,'HandLandmarker'))"
```
- Apple Silicon 기대 출력: `5.0.0 1.0.1 True`
- Intel 기대 출력: `4.11.0 0.10.21 True` (cv2 버전은 macOS 버전에 따라 4.9~4.12 사이에서 달라질 수 있다)

세 번째 값이 `True`면 Tasks API가 살아 있다는 뜻이고, 그게 코드가 요구하는 전부다.

`PythonTracker/.venv/`는 `.gitignore`에 있으니 Mac에서 새로 만들면 된다. 모델 파일 `PythonTracker/models/hand_landmarker.task`(7,819,105 bytes)는 repo에 커밋돼 있고 git이 binary로 인식하므로 checkout만으로 온전하다 — 크기가 다르면 손상이니 README의 다운로드 URL로 다시 받아라.

### 3-2. macOS 카메라 권한

**Windows와 가장 크게 다른 지점이다.** macOS는 카메라 권한을 **실행 주체(터미널 앱) 단위로** 묻는다.

1. 처음 `hand_tracker.py`를 돌리면 "터미널이 카메라에 접근하려 합니다" 프롬프트가 뜬다 → 허용.
2. 프롬프트를 놓쳤거나 거부했으면: **시스템 설정 > 개인정보 보호 및 보안 > 카메라**에서 실행 주체(터미널 / iTerm / VS Code)를 켠다.
3. **권한을 켠 뒤에는 그 앱을 완전히 종료(Cmd+Q)하고 다시 열어야 적용된다.** 탭만 새로 열면 안 먹는다.

권한이 없으면 `open_camera()`가 macOS 전용 안내 메시지와 함께 exit 1 한다. 이 메시지가 보이면 코드 문제가 아니라 권한 문제다.

### 3-3. macOS 방화벽

Unity가 UDP 5052를 **바인딩(수신)** 하므로, Play 모드 첫 진입 때 macOS가 *"Unity에서 들어오는 네트워크 연결을 허용하겠습니까?"* 를 물을 수 있다. **허용**해야 한다. 거부하면 패킷이 안 들어와 커서가 안 뜨고, Unity 콘솔에는 아무 에러도 안 남는다 — 조용히 실패하는 유일한 케이스라 제일 먼저 의심할 것.

### 3-4. Unity

- **Unity 6000.3.15f1** 이 필요하다 (`ProjectSettings/ProjectVersion.txt`). Hub에서 같은 버전을 설치할 것. 다른 마이너 버전으로 열면 URP 에셋이 또 업그레이드되며 diff가 생긴다.
- `com.unity.pipeline 0.5.0-exp.1`은 이미 `Packages/manifest.json`에 있으니 프로젝트를 열면 자동 설치된다.
- Unity CLI 바이너리 경로는 Windows와 다르다. `which unity`로 확인하고, 없으면 Unity Hub에서 CLI를 설치한다. 붙었는지 확인:

```bash
unity pipeline list      # Server Reachable 이 true 여야 한다
unity status             # port / project / version / PID
unity cmd run_tests --mode EditMode
```

Pipeline HTTP server는 **Editor 창에 포커스가 한 번 가야** 뜬다. `Server Reachable`이 false면 Unity 창을 클릭하고 다시 확인.

---

## 4. 남은 검증 항목 (이게 인계 작업의 본체)

docs/05의 DoD 중 **웹캠이 필요해서 못 한 것만** 추렸다. 나머지는 이미 통과했으니 다시 안 해도 된다.

### Step 1 — Python 손 추적 서버

| # | 기준 | 확인 방법 |
|---|---|---|
| 1-1 | venv에서 `hand_tracker.py` 실행 시 에러 없이 루프 진입 | 콘솔 |
| 1-2 | 프리뷰 창에 랜드마크 21개가 손 위에 오버레이 | 육안 |
| 1-3 | UDP 5052로 v1 스키마 JSON이 약 30Hz 송신 | 아래 원라이너 |
| 1-4 | 손 미검출 시에도 `hands: []` 계속 송신 | 손 숨기고 원라이너 확인 |
| 1-5 | **pinch 값이 핀치 시 약 0.15~0.25, 벌림 시 약 0.8+** | 원라이너 출력 |
| 1-7 | `q` / Ctrl+C로 리소스 정리 후 정상 종료 | 콘솔 |

수신 확인 원라이너 (Unity를 켜지 않고 Python 출력만 볼 때):
```bash
PythonTracker/.venv/bin/python -c "
import socket, json
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.bind(('127.0.0.1', 5052))
for _ in range(20):
    p = json.loads(s.recvfrom(65535)[0])
    print(p['seq'], [round(h['pinch'], 3) for h in p['hands']])
"
```
Unity Play 모드와 **동시에 쓰면 포트가 충돌**하니 하나만 띄울 것.

### Step 3 — 손 커서 (육안)

| # | 기준 |
|---|---|
| 3-1 | 검지 끝 이동에 커서가 **미러 방향 일치**로 추종 (오른손을 오른쪽으로 → 화면 오른쪽) |
| 3-2 | 좌/우 커서 색 구분 + handedness가 실제 손과 일치 |
| 3-3 | 핀치 시 커서 축소 + 색 변화, 히스테리시스로 경계 떨림 없음 |

### Step 4 — 통합 시나리오

`S1`(한 손) `S2`(두 손) `S4`(손 lost) `S5`(서버 단절) `S6`(서버 재시작) `S7`(5분)은 **합성 송신기로 이미 통과**했다. 실제 손으로 다시 확인이 필요한 것은:

| 시나리오 | 왜 다시 하나 |
|---|---|
| **S1 / S2** | handedness가 셀피 미러 뒤에 실제 손과 맞는지는 실물로만 확인된다 |
| **S3 핀치 토글 10회** | 실제 손 pinch 분포는 합성값과 다르다. 여기서 threshold 재조정 여부가 결정된다 |
| **S7 5분** | MediaPipe 추론이 포함된 5분은 아직 안 돌렸다 (Unity 쪽만 확인) |

---

## 5. 튜닝 작업 — 실제 손이 있어야 하는 판단

여기가 웹캠 없이 손도 못 댄 영역이다.

### 5-1. pinch threshold

- 현재: `pinchThreshold 0.30` / `pinchReleaseThreshold 0.40` (히스테리시스 폭 0.10). `HandCursorController`의 Inspector 값.
- docs/02 §2의 경험적 기대치는 핀치 0.15~0.25 / 벌림 0.8~1.2. **이 기대치 자체가 미검증**이다.
- 절차: 위 원라이너로 실제 pinch 값을 찍어보고 → 핀치/벌림 분포의 중간에 threshold가 오게 조정 → 떨림이 있으면 히스테리시스 폭을 넓힌다.
- 판정은 Unity 책임이므로 **`config.py`가 아니라 Inspector 값만 바꾼다** (docs/03 §5).

### 5-2. One Euro Filter

- 현재: `FILTER_MIN_CUTOFF 1.0` / `FILTER_BETA 0.007` / `FILTER_D_CUTOFF 1.0` (`config.py`).
- `MIN_CUTOFF`를 낮추면 정지 상태가 안정되지만 지연이 늘고, `BETA`를 올리면 빠른 움직임 추종이 좋아지지만 지터가 남는다.
- 절차: 손을 **정지**시켰을 때 커서가 떨리면 `MIN_CUTOFF`를 내린다. 손을 **빠르게** 움직였을 때 커서가 뒤처지면 `BETA`를 올린다. 한 번에 하나만 바꿀 것.
- 이건 육안 판단이 유일한 근거인 작업이라 수치를 미리 정해줄 수 없다.

---

## 6. Windows에서 넘어온 커밋들 — 뭔지 알고 있어야 하는 것

프로젝트 설정에 이미 들어간 변경이다. Mac에서 보고 놀라지 않도록.

| 변경 | 이유 | Mac에서 |
|---|---|---|
| `runInBackground: 1` | Python 프리뷰 창에 포커스가 가도 Unity가 계속 tick 하도록 | 그대로 필요하다. 되돌리면 창 전환 시 커서가 멈춘다 |
| `com.unity.pipeline 0.5.0-exp.1` | Unity CLI가 Editor에 붙기 위한 필수 tooling 패키지 | 런타임 무의존. 자동 설치됨 |
| `com.coplaydev.unity-mcp` 제거 | 브리지가 안 떠서 Unity CLI로 대체 | 되살릴 필요 없음 |
| URP RPAsset `k_AssetVersion` 12→13 | 에디터가 올린 포맷 업그레이드 | 같은 에디터 버전이면 추가 변경 없음 |
| `*.slnx`, `__pycache__/`, `*.pyc` gitignore | 잡파일 | 무해 |
| `EventSystem` / `GraphicRaycaster` 없음 | 커서는 UI raycast를 쓰지 않아 의도적으로 뺐다 | 실수로 다시 넣지 말 것 (docs/04 §1) |

---

## 7. 합성 송신기 — Unity 쪽만 격리 검증

`PythonTracker/fake_hand.py`는 웹캠·MediaPipe 없이 프로토콜 v1 패킷을 만들어 보낸다. stdlib만 쓴다.

```bash
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py --selfcheck   # 패킷 스키마 자체 점검
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py 30            # 두 손, 30초
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py 30 --one      # 왼손만 (S1)
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py 30 --empty    # 빈 hands heartbeat (S4)
```

**쓰는 때:** 커서가 안 움직일 때 원인이 Unity인지 카메라·MediaPipe인지 가르는 용도. 합성 송신기로 커서가 잘 움직이면 Unity는 정상이고 문제는 Python 쪽 파이프라인에 있다.

`--empty`로도 커서가 안 사라지거나, 프로세스를 껐다 켰을 때 커서가 안 돌아오면 그건 `IsNewSession` 회귀다.

---

## 8. 알려진 함정

### Unity CLI
- **Play 모드 진입·domain reload마다 `instanceId`가 전부 무효화된다.** `unity cmd`로 오브젝트를 다룰 때는 매번 `get_scene_hierarchy`로 다시 읽어야 한다.
- 파라미터 이름이 tool 설명과 다른 것들: `delete_asset --asset`, `create_prefab --source`, `set_serialized_field --field`, `package_remove --name`.
- Unity 6.3의 UI 메뉴 경로는 `GameObject/UI (Canvas)/Canvas`다. `GameObject/UI/Canvas`는 없다.
- `instantiate_prefab --parent`가 무시된다. `set_parent`를 따로 부를 것.
- `clear_console`은 Unity 콘솔만 지우고 Pipeline의 log buffer는 남는다.

### 레이턴시 측정
- `UdpHandReceiver`의 `logStats`(Inspector, 기본 off)를 켜면 수신 간격과 end-to-end가 로그로 나온다.
- **이 "수신 간격"은 UDP 도착 간격이 아니라 Unity `Update`가 패킷을 처리한 간격이다.** 프레임 갭이 그대로 들어간다. Windows 실측에서 CLI로 폴링하며 재면 100ms 초과가 22%까지 올랐고 폴링을 끊으면 1.1%로 떨어졌다 — **측정 행위가 결과를 바꾼다.**
- Windows 실측치(docs/05 §3)의 end-to-end는 합성 송신기 기준이라 **MediaPipe 추론 시간이 빠져 있다.** Mac에서 실제 파이프라인으로 재면 값이 크게 올라가는 게 정상이다. 기대는 여전히 100ms 미만.

### 플랫폼
- `cv2.CAP_DSHOW`는 Windows 전용이라 `camera_backend()`가 OS별로 갈라준다 (macOS는 `CAP_ANY` → AVFoundation). 카메라가 안 열릴 때 이 함수를 의심할 필요는 없다.
- macOS에서 `cv2.imshow` 프리뷰 창은 메인 스레드에서만 뜬다. `hand_tracker.py`는 단일 스레드라 문제 없지만, 창이 안 뜨거나 응답이 없으면 `SHOW_PREVIEW = False`로 끄고 UDP만 확인해 원인을 나눠라.
- Retina 디스플레이에서 `Screen.width/height` 기반 커서 좌표가 어긋나 보이면 3-1 항목으로 기록해달라. Windows에서는 확인할 수 없었던 지점이다.

---

## 9. 권장 순서

1. `uname -m`으로 아키텍처 확인 → **Intel이면 Python 3.12 + `requirements-intel-mac.txt`** (§2) → venv 생성 → 의존성 설치 → import 확인
2. `unity pipeline list`로 CLI 연결 확인 → `unity cmd run_tests --mode EditMode` → **30/30** 확인 (환경이 온전하다는 기준선)
3. `fake_hand.py`로 Unity 쪽 먼저 확인 (여기서 실패하면 카메라 문제가 아니다)
4. `hand_tracker.py` 실행 → 카메라 권한 허용 → Step 1 DoD (1-1~1-5, 1-7)
5. `HandTrackingTest.unity` Play → Step 3 DoD (3-1~3-3)
6. pinch threshold / One Euro 파라미터 튜닝 (§5)
7. S1 / S2 / S3 / S7 실물 재확인
8. 전 항목 통과 시 `QUALITY_CHECKLIST.md` 재채점 → 도달 가능 총점 9.9

## 10. 막히면

- 조용히 실패하는 케이스는 사실상 **macOS 방화벽 거부** 하나다 (§3-3). 커서가 안 뜨고 로그도 없으면 여기부터.
- `docs/02_protocol.md`가 프로토콜의 단일 진실 원천이다. 값이 이상하면 스키마와 좌표계 정의(§2, §3)를 먼저 대조할 것.
- 구현과 문서가 다르면 **문서 갱신 → 승인 → 코드 반영** 순서다 (docs/05 §4).
