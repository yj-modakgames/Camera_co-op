# 손 추적 서버 설정 상수 (단일 출처). docs/03_python_tracker.md §2 준수.

# 카메라
CAMERA_INDEX = 0  # OpenCV 카메라 인덱스
# Intel Mac 실측(2026-08-26): 640x480에서 손 검출 중 ~15Hz로 하락 -> 480x360 적용 (docs/06 §2)
FRAME_WIDTH = 480
FRAME_HEIGHT = 360

# UDP 송신 대상 (docs/02_protocol.md 준수)
UDP_IP = "127.0.0.1"
UDP_PORT = 5052

# MediaPipe HandLandmarker
MODEL_PATH = "models/hand_landmarker.task"  # 스크립트 위치 기준 상대경로
NUM_HANDS = 2
MIN_HAND_DETECTION_CONFIDENCE = 0.5
MIN_HAND_PRESENCE_CONFIDENCE = 0.5
MIN_TRACKING_CONFIDENCE = 0.5

# One Euro Filter
FILTER_MIN_CUTOFF = 1.0  # 낮출수록 저속에서 안정, 지연 증가
FILTER_BETA = 0.007  # 높일수록 고속 추종성 증가
FILTER_D_CUTOFF = 1.0  # 미분 신호 컷오프 (통상 고정)

# 디버그/로깅
SHOW_PREVIEW = True  # 프리뷰 창 on/off. 랜드마크 오버레이 표시, q 키로 종료
LOG_SEND_EVERY = 30  # N패킷마다 전송 상태 1줄 로그 (0이면 끔)
