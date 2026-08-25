# PythonTracker — 손 추적 서버

웹캠에서 손 랜드마크를 추출해 UDP(`127.0.0.1:5052`)로 Unity에 전송한다.
프로토콜 명세는 `docs/02_protocol.md`, 설계는 `docs/03_python_tracker.md` 참고.

## 1. 가상환경 생성

```powershell
python -m venv PythonTracker\.venv
```

## 2. 패키지 설치

```powershell
PythonTracker\.venv\Scripts\python.exe -m pip install -r PythonTracker\requirements.txt
```

## 3. 모델 다운로드

`models/hand_landmarker.task` (약 7.5MB)는 이미 저장소에 커밋되어 있다.
만약 없다면 아래에서 받아 `PythonTracker/models/hand_landmarker.task`에 저장한다.

```powershell
Invoke-WebRequest -Uri "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task" -OutFile "PythonTracker\models\hand_landmarker.task"
```

## 4. 실행

```powershell
PythonTracker\.venv\Scripts\python.exe PythonTracker\hand_tracker.py
```

- 프리뷰 창이 뜨면 `q` 키로 종료. 콘솔에서는 `Ctrl+C`로 종료 가능.
- 웹캠이 없거나 모델 파일이 없으면 원인을 알려주는 메시지와 함께 즉시 종료한다.
- 설정값(`카메라 인덱스`, `필터 파라미터` 등)은 전부 `config.py`에서 관리한다.
