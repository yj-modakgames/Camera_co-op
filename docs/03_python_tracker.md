# 03. Python 손 추적 서버 설계

## 1. 실행 환경

| 항목 | 값 | 근거 |
|---|---|---|
| Python | 3.14.4 (시스템 PATH) | 2026-08-26 실측. **이 PC에 3.13은 없다** (py launcher는 3.12만 등록). Phase 0 기록의 3.13.3은 오류 |
| 가상환경 | `PythonTracker/.venv` | `.gitignore`에 `PythonTracker/.venv/` 추가 (Step 1에서) |
| mediapipe | `1.0.1` 고정 | wheel 태그 `py3-none-win_amd64` — **Python 버전 비의존**. 1.x는 Tasks API 전용 |
| opencv-python | `5.0.0.93` 고정 | wheel 태그 `cp37-abi3-win_amd64` — Python 3.7+ 공통 ABI |
| 모델 파일 | `PythonTracker/models/hand_landmarker.task` | Tasks API 필수 에셋. **git에 커밋** (약 8MB, 팀원 편의 우선). 다운로드 URL은 README에 기록 |

> 버전 주의: 두 wheel 모두 특정 CPython 버전에 묶이지 않으므로 3.12/3.14 어디서든 설치된다. 실제 검증은 3.14.4에서 수행했다 (`cv2 5.0.0`, `mediapipe 1.0.1`, `vision.HandLandmarker` import 확인). 의존성으로 `numpy 2.5.2`, `opencv-contrib-python`이 함께 설치된다.

## 2. 모듈 구성

```
PythonTracker/
├── .venv/                  # git 제외
├── models/
│   └── hand_landmarker.task
├── config.py               # 모든 설정 상수 (단일 출처)
├── one_euro_filter.py      # OneEuroFilter 클래스
├── hand_tracker.py         # 메인 루프 (진입점)
├── requirements.txt
└── README.md               # 설치·실행 방법
```

### config.py — 설정 항목 목록
모듈 수준 상수로 정의한다. 클래스·파일 로딩 없음.

| 상수 | 기본값 | 설명 |
|---|---|---|
| `CAMERA_INDEX` | 0 | OpenCV 카메라 인덱스. `cv2.CAP_DSHOW` 백엔드 사용 (Windows 초기화 지연 회피) |
| `FRAME_WIDTH` / `FRAME_HEIGHT` | 640 / 480 | 캡처 해상도. 추론 속도 우선 |
| `UDP_IP` / `UDP_PORT` | "127.0.0.1" / 5052 | `docs/02_protocol.md` 준수 |
| `MODEL_PATH` | "models/hand_landmarker.task" | 스크립트 위치 기준 상대경로 |
| `NUM_HANDS` | 2 | HandLandmarker `num_hands` |
| `MIN_HAND_DETECTION_CONFIDENCE` | 0.5 | Tasks API 옵션 |
| `MIN_HAND_PRESENCE_CONFIDENCE` | 0.5 | Tasks API 옵션 |
| `MIN_TRACKING_CONFIDENCE` | 0.5 | Tasks API 옵션 |
| `FILTER_MIN_CUTOFF` | 1.0 | One Euro min_cutoff. 낮출수록 저속에서 안정, 지연 증가 |
| `FILTER_BETA` | 0.007 | One Euro beta. 높일수록 고속 추종성 증가 |
| `FILTER_D_CUTOFF` | 1.0 | 미분 신호 컷오프 (통상 고정) |
| `SHOW_PREVIEW` | True | 프리뷰 창 on/off. 랜드마크 오버레이 표시, `q` 키로 종료 |
| `LOG_SEND_EVERY` | 30 | N패킷마다 전송 상태 1줄 로그 (0이면 끔) |

## 3. MediaPipe 설정

- **API: Tasks API `HandLandmarker`** (mediapipe 1.x에는 구형 `mp.solutions.hands`가 없음).
- **RunningMode: `VIDEO`** — 캡처 루프에서 `detect_for_video(mp_image, timestamp_ms)` 동기 호출. timestamp_ms는 단조 증가 정수(프레임 시각 ms).
- 옵션 매핑: `HandLandmarkerOptions(base_options=BaseOptions(model_asset_path=MODEL_PATH), running_mode=VIDEO, num_hands=NUM_HANDS, min_hand_detection_confidence=..., min_hand_presence_confidence=..., min_tracking_confidence=...)`
- 결과 사용: `result.hand_landmarks[i][j].x/.y/.z` (정규화), `result.handedness[i][0].category_name` ("Left"/"Right").
- **추론 전 `cv2.flip(frame, 1)`** — 셀피 미러. 좌표계와 handedness 정의는 `docs/02_protocol.md` §3.

## 4. One Euro Filter

- 적용 지점: **추론 직후, 정규화 좌표의 x·y에만** 적용. z는 Phase 1에서 미사용이므로 원본 통과.
- 필터 인스턴스 관리: `filters[handedness][landmark_idx][axis]` 딕셔너리. 손별 21 × 2 = 42개.
- 리셋 정책: 프레임에서 해당 handedness가 사라지면 그 손의 필터를 삭제한다. 재검출 시 새로 생성 (오래된 상태로 인한 점프 방지).
- 파라미터는 config에서만 관리. 코드에 숫자 하드코딩 금지.
- 구현: 표준 One Euro (Casiez 2012) — `low-pass(x, alpha(cutoff))`, `cutoff = min_cutoff + beta * |dx_filtered|`. 타임스탬프는 `time.time()` 초 단위.

## 5. pinch 계산

```
pinch = dist2D(landmark 4, landmark 8) / dist2D(landmark 0, landmark 9)
```
- thumb tip(4) ↔ index tip(8)의 2D 거리(필터 적용 후 x,y)를 손바닥 길이(wrist 0 ↔ middle MCP 9)로 나눈 비율.
- 정규화 근거와 기대 범위는 `docs/02_protocol.md` §2 참조. 판정(threshold)은 Unity 책임 — Python은 값만 보낸다.

## 6. 메인 루프 (hand_tracker.py)

```
초기화: config 로드 → 소켓 생성 → 카메라 오픈(실패 시 명확한 에러 메시지 후 종료) → HandLandmarker 생성
루프:   read → flip → BGR→RGB → mp.Image → detect_for_video → 필터 → pinch → JSON 직렬화 → sendto
        (SHOW_PREVIEW면 랜드마크 그린 프레임 표시, q로 종료)
종료:   Ctrl+C 또는 q → 카메라 release, 창 destroy, landmarker close
```
- 단일 스레드. 근거는 `docs/01_architecture.md` §3.
- 손 미검출 프레임에도 빈 `hands`로 전송한다 (heartbeat, `docs/02_protocol.md` §4).
- 카메라 오픈 실패, 모델 파일 부재는 **원인과 해결 방법을 담은 메시지**로 즉시 종료한다. silent retry 금지.

## 7. requirements.txt

```
mediapipe==1.0.1
opencv-python==5.0.0.93
```
(numpy 등은 의존성으로 자동 설치. 직접 import하는 패키지만 명시)
