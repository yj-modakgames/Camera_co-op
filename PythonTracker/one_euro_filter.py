# 표준 One Euro Filter 구현 (Casiez et al., 2012, "1€ Filter: A Simple Speed-based
# Low-pass Filter for Noisy Input in Interactive Systems"). 좌표 1개(스칼라) 값을
# 시간축에 대해 평활화한다. x,y 각 축·각 랜드마크마다 별도 인스턴스를 둔다.

import math


def _smoothing_factor(t_e, cutoff):
    # 저역 통과 계수 alpha 계산. cutoff가 높을수록 원신호에 더 가깝게(덜 평활) 반응한다.
    r = 2.0 * math.pi * cutoff * t_e
    return r / (r + 1.0)


def _exponential_smoothing(a, x, x_prev):
    return a * x + (1.0 - a) * x_prev


class OneEuroFilter:
    """단일 스칼라 신호용 One Euro Filter.

    min_cutoff: 저속 구간 안정성(낮을수록 안정, 지연 증가)
    beta: 고속 구간 추종성(높을수록 빠른 움직임에 덜 지연)
    d_cutoff: 속도(미분) 신호 자체의 컷오프. 통상 1.0 고정
    """

    def __init__(self, t0, x0, dx0=0.0, min_cutoff=1.0, beta=0.0, d_cutoff=1.0):
        self.min_cutoff = float(min_cutoff)
        self.beta = float(beta)
        self.d_cutoff = float(d_cutoff)
        self.x_prev = float(x0)
        self.dx_prev = float(dx0)
        self.t_prev = float(t0)

    def __call__(self, t, x):
        t_e = t - self.t_prev
        if t_e <= 0.0:
            # 타임스탬프 역전/중복 방어: 아주 작은 양수로 대체해 0-division 회피
            t_e = 1e-6

        # 1. 속도(미분) 신호를 먼저 평활화
        a_d = _smoothing_factor(t_e, self.d_cutoff)
        dx = (x - self.x_prev) / t_e
        dx_hat = _exponential_smoothing(a_d, dx, self.dx_prev)

        # 2. 속도에 따라 적응적 cutoff를 정해 신호 자체를 평활화
        cutoff = self.min_cutoff + self.beta * abs(dx_hat)
        a = _smoothing_factor(t_e, cutoff)
        x_hat = _exponential_smoothing(a, x, self.x_prev)

        self.x_prev = x_hat
        self.dx_prev = dx_hat
        self.t_prev = t
        return x_hat


if __name__ == "__main__":
    # 최소 self-check: (1) 상수 입력 → 출력이 입력에 수렴 (2) 지터 입력 → 분산 감소
    import random

    # (1) 상수 신호: 노이즈 없는 동일 값이 반복되면 필터 출력도 즉시 그 값에 수렴해야 한다.
    f = OneEuroFilter(t0=0.0, x0=0.5, min_cutoff=1.0, beta=0.007, d_cutoff=1.0)
    dt = 1.0 / 30.0
    const_value = 0.5
    for i in range(1, 30):
        out = f(t=i * dt, x=const_value)
        assert abs(out - const_value) < 1e-6, f"constant input did not converge: {out}"
    print("[OK] constant input converges to steady value")

    # (2) 지터 신호: 평균 0.5 주변에 균일 노이즈를 섞은 입력의 분산이 필터를 거치면 줄어야 한다.
    random.seed(42)
    f2 = OneEuroFilter(t0=0.0, x0=0.5, min_cutoff=1.0, beta=0.007, d_cutoff=1.0)
    raw_samples = [0.5 + random.uniform(-0.05, 0.05) for _ in range(300)]
    filtered_samples = []
    for i, x in enumerate(raw_samples):
        filtered_samples.append(f2(t=i * dt, x=x))

    def _variance(values):
        mean = sum(values) / len(values)
        return sum((v - mean) ** 2 for v in values) / len(values)

    # 워밍업 구간 제외하고 비교 (필터가 안정화된 뒤의 구간만)
    raw_var = _variance(raw_samples[50:])
    filtered_var = _variance(filtered_samples[50:])
    assert filtered_var < raw_var, (
        f"filtered variance ({filtered_var}) not lower than raw variance ({raw_var})"
    )
    print(f"[OK] jitter reduced: raw_var={raw_var:.6f} -> filtered_var={filtered_var:.6f}")

    print("one_euro_filter.py self-check passed")
