# 손 추적 메인 루프 (진입점). docs/03_python_tracker.md §6, docs/01_architecture.md §3(단일 스레드) 준수.
#
# 흐름: read -> flip(셀피 미러) -> BGR->RGB -> mp.Image -> detect_for_video -> One Euro 필터 -> pinch -> JSON -> sendto
# 손 미검출 프레임에도 빈 hands로 계속 전송한다 (heartbeat, docs/02_protocol.md §4).

import json
import argparse
import math
import os
import socket
import sys
import time
from typing import Sequence

import config
from one_euro_filter import OneEuroFilter
from camera_utils import CameraUnavailableError, discover_cameras, open_selected_camera

PROTOCOL_VERSION = 1
NUM_LANDMARKS = 21

# pinch 계산에 쓰는 랜드마크 인덱스 (docs/02_protocol.md §2)
THUMB_TIP = 4
INDEX_TIP = 8
WRIST = 0
MIDDLE_MCP = 9


def dist2d(landmarks_flat, idx_a, idx_b):
    # 평탄화된 [x0,y0,z0, x1,y1,z1, ...] 배열에서 두 랜드마크의 2D(x,y) 유클리드 거리
    ax, ay = landmarks_flat[idx_a * 3], landmarks_flat[idx_a * 3 + 1]
    bx, by = landmarks_flat[idx_b * 3], landmarks_flat[idx_b * 3 + 1]
    return math.hypot(ax - bx, ay - by)


def compute_pinch(landmarks_flat):
    # pinch = dist2D(4,8) / dist2D(0,9). 분모 0 방어(손바닥 길이가 0에 수렴하는 비정상 프레임)
    palm_length = dist2d(landmarks_flat, WRIST, MIDDLE_MCP)
    if palm_length < 1e-6:
        return 0.0
    pinch_dist = dist2d(landmarks_flat, THUMB_TIP, INDEX_TIP)
    return pinch_dist / palm_length


def build_packet(seq, timestamp, hands):
    """패킷 조립 순수 함수. 카메라 없이도 스키마 검증 가능.

    hands: [(handedness: str, landmarks_flat: list[float] len 63, pinch: float), ...]
    반환: docs/02_protocol.md 스키마 v1과 일치하는 dict
    """
    return {
        "v": PROTOCOL_VERSION,
        "seq": seq,
        "timestamp": timestamp,
        "hands": [
            {"handedness": handedness, "landmarks": landmarks_flat, "pinch": pinch}
            for (handedness, landmarks_flat, pinch) in hands
        ],
    }


def resolve_model_path():
    # 스크립트 위치 기준 상대경로 (docs/03 §2)
    script_dir = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(script_dir, config.MODEL_PATH)


def camera_backend():
    # CAP_DSHOW는 Windows 전용(초기화 지연 회피). 다른 OS에 넘기면 카메라가 열리지 않으므로
    # macOS/Linux는 CAP_ANY로 OpenCV가 백엔드를 고르게 둔다 (macOS -> AVFoundation).
    return cv2.CAP_DSHOW if sys.platform == "win32" else cv2.CAP_ANY


def permission_hint():
    # 카메라 권한 안내는 OS마다 경로가 달라 실행 플랫폼에 맞는 문구만 낸다.
    if sys.platform == "darwin":
        return (
            "        macOS가 카메라 접근을 차단했을 수 있습니다. 권한은 실행 주체(터미널/IDE) 단위로 묻습니다.\n"
            "  해결: 1) 웹캠 연결 확인  2) 카메라를 쓰는 다른 앱(Zoom 등) 종료\n"
            "        3) 시스템 설정 > 개인정보 보호 및 보안 > 카메라에서 터미널/iTerm/VS Code 허용\n"
            "        4) 권한을 켠 뒤에는 그 앱을 완전히 종료하고 다시 실행해야 적용된다\n"
        )
    if sys.platform == "win32":
        return (
            "        Windows 카메라 개인정보 설정에서 접근이 차단되었을 수 있습니다.\n"
            "  해결: 1) 웹캠 연결 확인  2) 카메라를 쓰는 다른 앱(Zoom 등) 종료\n"
            "        3) 설정 > 개인정보 및 보안 > 카메라에서 앱 접근 허용 확인\n"
        )
    return (
        "        OS가 카메라 접근을 차단했을 수 있습니다.\n"
        "  해결: 1) 웹캠 연결 확인  2) 카메라를 쓰는 다른 앱 종료\n"
    )


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="CameraCo-op hand tracker")
    parser.add_argument("--camera", type=int, default=config.CAMERA_INDEX, help="OpenCV camera index")
    parser.add_argument("--list-cameras", action="store_true", help="List cameras that open and yield a frame")
    preview = parser.add_mutually_exclusive_group()
    preview.add_argument("--preview", dest="preview", action="store_true", help="Show camera preview")
    preview.add_argument("--no-preview", dest="preview", action="store_false", help="Disable camera preview")
    parser.set_defaults(preview=config.SHOW_PREVIEW)
    return parser.parse_args(argv)


def open_camera(index: int):
    if index < 0:
        sys.exit("[ERROR] camera index must be non-negative: {}".format(index))
    try:
        cap = open_selected_camera(
            lambda selected: cv2.VideoCapture(selected, camera_backend()), index
        )
    except CameraUnavailableError as error:
        sys.exit(
            "[ERROR] 카메라를 열 수 없습니다 (index={}).\n  {}\n{}"
            "  선택한 index를 자동으로 바꾸지 않습니다.".format(index, error, permission_hint())
        )
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, config.FRAME_WIDTH)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, config.FRAME_HEIGHT)
    return cap


def create_landmarker():
    model_path = resolve_model_path()
    if not os.path.isfile(model_path):
        sys.exit(
            "[ERROR] 모델 파일을 찾을 수 없습니다: {}\n"
            "  원인: hand_landmarker.task가 다운로드되지 않았습니다.\n"
            "  해결: README.md의 '모델 다운로드' 단계를 실행하세요.\n"
            "        (PowerShell) Invoke-WebRequest -Uri \"https://storage.googleapis.com/"
            "mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/"
            "hand_landmarker.task\" -OutFile \"{}\"".format(model_path, model_path)
        )
    options = vision.HandLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=model_path),
        running_mode=vision.RunningMode.VIDEO,
        num_hands=config.NUM_HANDS,
        min_hand_detection_confidence=config.MIN_HAND_DETECTION_CONFIDENCE,
        min_hand_presence_confidence=config.MIN_HAND_PRESENCE_CONFIDENCE,
        min_tracking_confidence=config.MIN_TRACKING_CONFIDENCE,
    )
    return vision.HandLandmarker.create_from_options(options)


def draw_preview(frame, hand_landmarks_list):
    # 랜드마크 21개를 원으로 오버레이 (디버그 프리뷰 전용)
    h, w = frame.shape[:2]
    for hand_landmarks in hand_landmarks_list:
        for lm in hand_landmarks:
            cx, cy = int(lm.x * w), int(lm.y * h)
            cv2.circle(frame, (cx, cy), 4, (0, 255, 0), -1)
    return frame


def main(argv: Sequence[str] | None = None) -> int:
    global cv2, mp, vision, BaseOptions
    args = parse_args(argv)
    import cv2
    if args.list_cameras:
        devices = discover_cameras(
            lambda index: cv2.VideoCapture(index, camera_backend()),
            max_index=10,
        )
        print(json.dumps([{"index": device.index, "available": device.available} for device in devices]))
        return 0

    import mediapipe as mp
    from mediapipe.tasks.python import BaseOptions, vision

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    cap = open_camera(args.camera)
    landmarker = create_landmarker()

    # 손별 One Euro 필터: filters[handedness][landmark_idx][axis] ('x' 또는 'y')
    filters = {}
    seq = 0

    print("[INFO] 손 추적 서버 시작. UDP {}:{}로 송신합니다. 종료: q 또는 Ctrl+C".format(
        config.UDP_IP, config.UDP_PORT
    ))

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                print("[WARN] 프레임을 읽지 못했습니다. 재시도합니다.")
                continue

            frame = cv2.flip(frame, 1)  # 셀피 미러 (docs/02_protocol.md §3)
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
            timestamp_ms = int(time.time() * 1000)
            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            now = time.time()
            hands_out = []
            seen_handedness = set()

            hand_landmarks_list = result.hand_landmarks or []
            handedness_list = result.handedness or []

            for hand_landmarks, handedness_info in zip(hand_landmarks_list, handedness_list):
                handedness = handedness_info[0].category_name  # "Left" | "Right"
                # 셀피 flip 후 MediaPipe 판정은 실제 손과 반대 -> 반전 (docs/02 §2, 2026-08-26 Intel Mac 실측)
                handedness = "Right" if handedness == "Left" else "Left"
                seen_handedness.add(handedness)
                hand_filters = filters.setdefault(handedness, {})

                landmarks_flat = [0.0] * (NUM_LANDMARKS * 3)
                for idx, lm in enumerate(hand_landmarks):
                    axis_filters = hand_filters.setdefault(idx, {})

                    if "x" not in axis_filters:
                        axis_filters["x"] = OneEuroFilter(
                            t0=now, x0=lm.x,
                            min_cutoff=config.FILTER_MIN_CUTOFF,
                            beta=config.FILTER_BETA,
                            d_cutoff=config.FILTER_D_CUTOFF,
                        )
                        filtered_x = lm.x
                    else:
                        filtered_x = axis_filters["x"](t=now, x=lm.x)

                    if "y" not in axis_filters:
                        axis_filters["y"] = OneEuroFilter(
                            t0=now, x0=lm.y,
                            min_cutoff=config.FILTER_MIN_CUTOFF,
                            beta=config.FILTER_BETA,
                            d_cutoff=config.FILTER_D_CUTOFF,
                        )
                        filtered_y = lm.y
                    else:
                        filtered_y = axis_filters["y"](t=now, x=lm.y)

                    landmarks_flat[idx * 3] = filtered_x
                    landmarks_flat[idx * 3 + 1] = filtered_y
                    landmarks_flat[idx * 3 + 2] = lm.z  # z는 Phase 1 미사용, 원본 그대로 전달

                pinch = compute_pinch(landmarks_flat)
                hands_out.append((handedness, landmarks_flat, pinch))

            # 사라진 손의 필터는 삭제 (재검출 시 새로 생성해 점프 방지, docs/03 §4)
            for stale_handedness in list(filters.keys()):
                if stale_handedness not in seen_handedness:
                    del filters[stale_handedness]

            packet = build_packet(seq=seq, timestamp=now, hands=hands_out)
            sock.sendto(json.dumps(packet).encode("utf-8"), (config.UDP_IP, config.UDP_PORT))

            if config.LOG_SEND_EVERY and seq % config.LOG_SEND_EVERY == 0:
                print("[INFO] seq={} hands={}".format(seq, len(hands_out)))

            seq += 1

            if args.preview:
                preview = draw_preview(frame, hand_landmarks_list)
                cv2.imshow("PythonTracker (q to quit)", preview)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break

    except KeyboardInterrupt:
        print("\n[INFO] Ctrl+C 감지, 종료합니다.")
    finally:
        cap.release()
        cv2.destroyAllWindows()
        landmarker.close()
        sock.close()
        print("[INFO] 리소스 정리 완료, 종료합니다.")
    return 0


if __name__ == "__main__":
    main()
