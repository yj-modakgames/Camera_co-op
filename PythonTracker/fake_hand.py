"""프로토콜 v1 패킷 합성 송신기. 웹캠 없이 Unity 수신부·커서를 검증한다.
stdlib만 사용. docs/02_protocol.md 스키마 준수."""
import json
import math
import socket
import sys
import time

UDP = ("127.0.0.1", 5052)
HZ = 30.0
# 손목(0) 기준 21개 랜드마크의 대략적 오프셋. index tip(8)만 커서에 쓰이지만
# 패킷을 실제와 비슷하게 채운다. 단위는 정규화 좌표.
OFFSETS = [
    (0.00, 0.00), (-0.03, -0.03), (-0.05, -0.06), (-0.06, -0.09), (-0.07, -0.12),  # 0-4 thumb
    (-0.01, -0.10), (-0.01, -0.15), (-0.01, -0.18), (-0.01, -0.21),                # 5-8 index
    (0.01, -0.10), (0.01, -0.15), (0.01, -0.18), (0.01, -0.21),                    # 9-12 middle
    (0.03, -0.09), (0.03, -0.13), (0.03, -0.16), (0.03, -0.19),                    # 13-16 ring
    (0.05, -0.07), (0.05, -0.10), (0.05, -0.12), (0.05, -0.14),                    # 17-20 pinky
]


def build_hand(handedness, cx, cy, pinch):
    lm = []
    for ox, oy in OFFSETS:
        lm += [round(cx + ox, 4), round(cy + oy, 4), 0.0]
    return {"handedness": handedness, "landmarks": lm, "pinch": round(pinch, 4)}


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
        # index tip(8) 이 [0,1] 안에 들어오는지 (커서가 화면 밖으로 나가지 않게)
        x, y = h["landmarks"][24], h["landmarks"][25]
        assert 0.0 <= x <= 1.0 and 0.0 <= y <= 1.0, (x, y)
    # 핀치 주기가 threshold 양쪽을 실제로 통과하는지
    vals = [build_packet(i, i / HZ)["hands"][0]["pinch"] for i in range(int(HZ * 2))]
    assert min(vals) < 0.30 and max(vals) > 0.40, (min(vals), max(vals))
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


if __name__ == "__main__":
    if "--selfcheck" in sys.argv:
        selfcheck()
    else:
        dur = float(sys.argv[1]) if len(sys.argv) > 1 else 10.0
        run(dur, "--empty" not in sys.argv, "--one" in sys.argv)
