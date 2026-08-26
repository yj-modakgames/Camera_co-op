# PythonTracker — 손 추적 서버

웹캠에서 손 랜드마크를 추출해 UDP(`127.0.0.1:5052`)로 Unity에 전송한다.
프로토콜 명세는 `docs/02_protocol.md`, 설계는 `docs/03_python_tracker.md` 참고.

> macOS에서 처음 셋업한다면 `docs/06_handoff_macos.md`를 먼저 읽어라. Apple Silicon 필수 조건과 카메라 권한·방화벽 함정이 정리돼 있다.

## 1. 가상환경 생성

```bash
# macOS / Linux
python3 -m venv PythonTracker/.venv
```
```powershell
# Windows
python -m venv PythonTracker\.venv
```

## 2. 패키지 설치

```bash
# macOS / Linux
PythonTracker/.venv/bin/python -m pip install -r PythonTracker/requirements.txt
```
```powershell
# Windows
PythonTracker\.venv\Scripts\python.exe -m pip install -r PythonTracker\requirements.txt
```

`mediapipe 1.0.1`은 macOS arm64 wheel만 있다. **Intel Mac에서는 설치가 실패한다.**

설치 확인:
```bash
PythonTracker/.venv/bin/python -c "import cv2, mediapipe as mp; from mediapipe.tasks.python import vision; print(cv2.__version__, mp.__version__, hasattr(vision,'HandLandmarker'))"
```
기대 출력: `5.0.0 1.0.1 True`

## 3. 모델 파일

`models/hand_landmarker.task` (7,819,105 bytes)는 이미 저장소에 커밋되어 있다.
크기가 다르거나 없으면 아래에서 받아 같은 경로에 저장한다.

```bash
# macOS / Linux
curl -L -o PythonTracker/models/hand_landmarker.task \
  "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task"
```
```powershell
# Windows
Invoke-WebRequest -Uri "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task" -OutFile "PythonTracker\models\hand_landmarker.task"
```

## 4. 실행

```bash
# macOS / Linux
PythonTracker/.venv/bin/python PythonTracker/hand_tracker.py
```
```powershell
# Windows
PythonTracker\.venv\Scripts\python.exe PythonTracker\hand_tracker.py
```

- 프리뷰 창이 뜨면 `q` 키로 종료. 콘솔에서는 `Ctrl+C`로 종료 가능.
- 웹캠이 없거나 모델 파일이 없으면 원인을 알려주는 메시지와 함께 즉시 종료한다. 카메라 권한 안내는 실행 중인 OS에 맞춰 나온다.
- 설정값(카메라 인덱스, 필터 파라미터 등)은 전부 `config.py`에서 관리한다.
- 카메라 백엔드는 `camera_backend()`가 OS별로 고른다 — Windows는 `CAP_DSHOW`, 그 외는 `CAP_ANY`(macOS → AVFoundation).

## 5. fake_hand.py — 웹캠 없이 Unity만 검증

프로토콜 v1 패킷을 합성해 보내는 진단 도구. stdlib만 사용한다.
커서가 안 움직일 때 **원인이 Unity인지 카메라·MediaPipe인지 가르는** 용도로 쓴다.

```bash
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py --selfcheck   # 패킷 스키마 자체 점검
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py 30            # 두 손, 30초
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py 30 --one      # 왼손만
PythonTracker/.venv/bin/python PythonTracker/fake_hand.py 30 --empty    # 빈 hands heartbeat
```

`hand_tracker.py`와 포트가 같으니 둘을 동시에 띄우지 말 것.
