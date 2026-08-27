"""프로토콜 v1 패킷 합성 송신기. 웹캠 없이 Unity 수신부·커서를 검증한다.
stdlib만 사용. docs/02_protocol.md 스키마 준수."""
import argparse
import json
import math
import socket
import sys
import time
from typing import TypedDict

UDP = ("127.0.0.1", 5052)
HZ = 30.0
# 손목(0) 기준 21개 랜드마크의 대략적 오프셋. 단위는 정규화 좌표.
OFFSETS = [
    (0.00, 0.00), (-0.03, -0.03), (-0.05, -0.06), (-0.06, -0.09), (-0.07, -0.12),  # 0-4 thumb
    (-0.01, -0.10), (-0.01, -0.15), (-0.01, -0.18), (-0.01, -0.21),                # 5-8 index
    (0.01, -0.10), (0.01, -0.15), (0.01, -0.18), (0.01, -0.21),                    # 9-12 middle
    (0.03, -0.09), (0.03, -0.13), (0.03, -0.16), (0.03, -0.19),                    # 13-16 ring
    (0.05, -0.07), (0.05, -0.10), (0.05, -0.12), (0.05, -0.14),                    # 17-20 pinky
]


class HandPacket(TypedDict):
    handedness: str
    landmarks: list[float]
    pinch: float


class Packet(TypedDict):
    v: int
    seq: int
    timestamp: float
    hands: list[HandPacket]


def build_hand(handedness, cx, cy, pinch):
    lm = []
    for ox, oy in OFFSETS:
        lm += [round(cx + ox, 4), round(cy + oy, 4), 0.0]
    return {"handedness": handedness, "landmarks": lm, "pinch": round(pinch, 4)}


def build_target_packet(seq: int, target: tuple[float, float]) -> Packet:
    """Build one left hand whose wrist/MCP palm center is fixed at ``target``."""
    palm_indices = (0, 5, 9, 13, 17)
    hand: HandPacket = build_hand("Left", 0.0, 0.0, 0.15)
    for axis, target_value in enumerate(target):
        palm_center = sum(OFFSETS[index][axis] for index in palm_indices) / len(palm_indices)
        lower_extent = palm_center - min(offset[axis] for offset in OFFSETS)
        upper_extent = max(offset[axis] for offset in OFFSETS) - palm_center
        scale = min(1.0, target_value / lower_extent, (1.0 - target_value) / upper_extent)
        for index, offset in enumerate(OFFSETS):
            hand["landmarks"][index * 3 + axis] = round(target_value + (offset[axis] - palm_center) * scale, 4)
    return {"v": 1, "seq": seq, "timestamp": time.time(), "hands": [hand]}


def build_packet(seq, t, hands_visible=True, one_hand=False):
    hands = []
    if hands_visible:
        # 반지름 0.15 원 궤적. 손목 기준이므로 index tip(8)은 -0.21 위로 뜬다.
        a = t * 1.2
        # pinch: 2초 주기로 0.15(핀치) <-> 0.90(벌림). threshold 0.30/0.40을 확실히 통과.
        pinch = 0.525 + 0.375 * math.sin(t * math.pi)
        hands.append(build_hand("Left", 0.30 + 0.15 * math.cos(a), 0.60 + 0.15 * math.sin(a), pinch))
        if not one_hand:
            hands.append(build_hand("Right", 0.70 - 0.15 * math.cos(a), 0.60 + 0.15 * math.sin(a), 1.05 - pinch))
    return {"v": 1, "seq": seq, "timestamp": time.time(), "hands": hands}


def selfcheck():
    p = build_packet(0, 0.0)
    assert p["v"] == 1 and p["seq"] == 0
    assert len(p["hands"]) == 2
    for h in p["hands"]:
        assert len(h["landmarks"]) == 63, len(h["landmarks"])
        assert h["handedness"] in ("Left", "Right")
        assert 0.0 < h["pinch"] < 2.0
        # 손목 + MCP 4개의 평균인 손바닥 중심이 [0,1] 안에 들어오는지
        x, y = (sum(h["landmarks"][index * 3 + axis] for index in (0, 5, 9, 13, 17)) / 5 for axis in (0, 1))
        assert 0.0 <= x <= 1.0 and 0.0 <= y <= 1.0, (x, y)
    # 핀치 주기가 threshold 양쪽을 실제로 통과하는지
    vals = [build_packet(i, i / HZ)["hands"][0]["pinch"] for i in range(int(HZ * 2))]
    assert min(vals) < 0.30 and max(vals) > 0.40, (min(vals), max(vals))
    for target_x in (0.5, 0.0, 0.0001, 0.01, 0.99, 0.9999, 1.0):
        for target_y in (0.5, 0.0, 0.0001, 0.01, 0.99, 0.9999, 1.0):
            target_packet = build_target_packet(1, (target_x, target_y))
            assert target_packet["v"] == 1 and target_packet["seq"] == 1
            assert len(target_packet["hands"]) == 1
            target_hand = target_packet["hands"][0]
            assert len(target_hand["landmarks"]) == 63
            assert target_hand["handedness"] == "Left"
            assert target_hand["pinch"] < 0.30
            for axis, expected in enumerate((target_x, target_y)):
                actual = sum(target_hand["landmarks"][index * 3 + axis] for index in (0, 5, 9, 13, 17)) / 5
                assert abs(actual - expected) <= 0.0001, (axis, expected, actual)
            assert all(
                0.0 <= value <= 1.0
                for index, value in enumerate(target_hand["landmarks"])
                if index % 3 != 2
            )
    assert len(json.dumps(p).encode()) < 4000
    print("selfcheck OK  packet bytes:", len(json.dumps(p).encode()))


def run(duration, hands_visible, one_hand=False):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    t0 = time.time()
    seq = 0
    while time.time() - t0 < duration:
        pkt = build_packet(seq, time.time() - t0, hands_visible, one_hand)
        sock.sendto(json.dumps(pkt).encode("utf-8"), UDP)
        seq += 1
        time.sleep(1.0 / HZ)
    print(f"sent {seq} packets in {duration}s")


def run_target(target: tuple[float, float], duration: float) -> None:
    """Send a pinching hand at ``target`` for ``duration`` seconds."""
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        t0 = time.time()
        seq = 0
        while time.time() - t0 < duration:
            pkt = build_target_packet(seq, target)
            sock.sendto(json.dumps(pkt).encode("utf-8"), UDP)
            seq += 1
            time.sleep(1.0 / HZ)
    print(f"sent {seq} target packets in {duration}s")


def parse_target(raw: str) -> tuple[float, float]:
    """Parse and bound-check one normalized ``x,y`` coordinate pair."""
    parts = raw.split(",")
    if len(parts) != 2:
        raise argparse.ArgumentTypeError("--target must be two normalized coordinates in x,y form")
    try:
        target = (float(parts[0].strip()), float(parts[1].strip()))
    except ValueError as exc:
        raise argparse.ArgumentTypeError("--target coordinates must be numbers in x,y form") from exc
    if not all(math.isfinite(value) for value in target):
        raise argparse.ArgumentTypeError("--target coordinates must be finite numbers")
    if not all(0.0 <= value <= 1.0 for value in target):
        raise argparse.ArgumentTypeError("--target coordinates must be within [0, 1]")
    return target


def build_parser() -> argparse.ArgumentParser:
    """Build the command-line parser while retaining the original options."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("duration", nargs="?", type=float, default=10.0, help="circle mode duration in seconds")
    parser.add_argument("--empty", action="store_true", help="send heartbeat packets with no hands")
    parser.add_argument("--one", action="store_true", help="send only the left hand in circle mode")
    parser.add_argument("--selfcheck", action="store_true", help="validate packet generation and exit")
    parser.add_argument("--target", type=parse_target, metavar="X,Y", help="fixed palm center (wrist + four MCPs) in [0, 1]")
    parser.add_argument("--pinch-hold", type=float, metavar="SECONDS", help="pinch duration for --target mode")
    return parser


def main() -> int:
    """Run selfcheck, fixed-target mode, or the original circle mode."""
    parser = build_parser()
    args = parser.parse_args()
    if args.target is None and args.pinch_hold is not None:
        parser.error("--pinch-hold requires --target")
    if args.target is not None and args.pinch_hold is None:
        parser.error("--target requires --pinch-hold")
    if args.target is not None:
        if not math.isfinite(args.pinch_hold) or args.pinch_hold <= 0.0:
            parser.error("--pinch-hold must be a finite number greater than 0")
        run_target(args.target, args.pinch_hold)
        return 0
    if args.selfcheck:
        selfcheck()
        return 0
    run(args.duration, not args.empty, args.one)
    return 0


if __name__ == "__main__":
    sys.exit(main())
