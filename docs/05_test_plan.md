# 05. 검증 계획

> 채점 프로토콜: 각 Step 구현 완료 시 `QUALITY_CHECKLIST.md`(프로젝트 루트)로 채점하고 총점 ≥9.0까지 개선 반복한다.

## 1. Step별 완료 판정 기준 (Definition of Done)

### Step 1 — Python 손 추적 서버
| # | 기준 | 확인 방법 |
|---|---|---|
| 1-1 | venv에서 `python hand_tracker.py` 실행 시 에러 없이 루프 진입 | 콘솔 출력 |
| 1-2 | 프리뷰 창에 랜드마크 21개가 손 위에 오버레이됨 | 육안 |
| 1-3 | UDP 5052로 프로토콜 v1 스키마의 JSON이 ~30Hz로 송신됨 | 임시 수신 원라이너: `python -c "import socket,json; s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM); s.bind(('127.0.0.1',5052)); [print(json.loads(s.recvfrom(65535)[0])['seq']) for _ in range(10)]"` |
| 1-4 | 손 미검출 시에도 `hands: []` 패킷이 계속 송신됨 (heartbeat) | 위 원라이너로 손 숨기고 확인 |
| 1-5 | pinch 값이 핀치 시 ≈0.15~0.25, 벌림 시 ≈0.8+ 범위 | 원라이너 출력 확인 |
| 1-6 | 카메라 부재/모델 부재 시 원인이 명시된 메시지로 종료 | 웹캠 분리 후 실행 |
| 1-7 | `q` / Ctrl+C로 리소스 정리 후 정상 종료 | 콘솔 확인 |

### Step 2 — Unity UDP 수신부
| # | 기준 | 확인 방법 |
|---|---|---|
| 2-1 | Play 모드 진입 시 수신 스레드 시작, 패킷 수신 확인 로그 | Console |
| 2-2 | `LatestPacket`에 손 데이터가 매 프레임 갱신됨 | 임시 디버그 로그 (검증 후 제거) |
| 2-3 | Python 종료 후 0.5초 내 `IsServerLost == true`, 재시작 시 자동 복구 | Play 중 Python 껐다 켜기 |
| 2-4 | Play 종료 시 스레드·소켓 정리, Editor 잔류 스레드 없음 | Play 재진입 반복 시 포트 바인딩 에러 없음 |
| 2-5 | `refresh_unity → read_console`에서 신규 에러·경고 0건 | Unity-MCP |

### Step 3 — 손 커서 표시
| # | 기준 | 확인 방법 |
|---|---|---|
| 3-1 | 검지 끝 이동에 커서가 미러 방향 일치로 추종 (오른쪽 이동 → 화면 오른쪽) | 육안 |
| 3-2 | 좌/우 손 커서가 색으로 구분되고 handedness가 실제 손과 일치 | 육안 |
| 3-3 | 핀치 시 커서 축소+색 변화, 히스테리시스로 경계 떨림 없음 | 육안 |
| 3-4 | 손 하나 숨기면 그 커서만 fade out, 서버 종료 시 둘 다 fade out | 육안 |
| 3-5 | `refresh_unity → read_console` 신규 에러·경고 0건 | Unity-MCP |

## 2. 통합 테스트 시나리오 (Step 4)

전제: Python 서버 실행 + Unity Play 모드.

| 시나리오 | 절차 | 기대 결과 |
|---|---|---|
| S1 한 손 추적 | 오른손만 카메라에 노출, 사각형 궤적 이동 | 오른손 커서만 표시, 부드러운 추종(가시적 지터 없음) |
| S2 두 손 동시 | 양손 노출, 교차 이동 | 두 커서 색 구분 유지, handedness 스왑 없음 |
| S3 핀치 토글 | 각 손으로 핀치 10회 반복 | 10/10 인식, 히스테리시스로 중간 떨림 없음 |
| S4 손 lost | 한 손을 화면 밖으로 | 해당 커서만 0.2초 fade out, 복귀 시 fade in |
| S5 서버 단절 | Python Ctrl+C 종료 | 0.5초 내 두 커서 fade out, Unity 에러 없음 |
| S6 서버 재시작 | Python 재실행 | 재연결 절차 없이 커서 자동 복구 |
| S7 장시간 | 5분 방치 후 조작 | 메모리 증가·프레임 드랍·커서 밀림 없음 |

## 3. 레이턴시 측정 방법

- **수신 간격:** `UdpHandReceiver`가 최근 N=100 패킷의 수신 간격(ms)을 누적해 평균/최대를 로그. 기대: 평균 ≈33ms (카메라 30fps), 최대 < 100ms.
- **end-to-end:** 동일 머신이므로 `LastLatencyMs = (Unity 수신 epoch − packet.timestamp) × 1000`으로 직접 측정. 기대: 파이프라인(캡처+추론+필터+전송+수신) < 100ms.
- 측정 로그는 개발용 토글(Inspector bool) 뒤에 두고 기본 off.

## 4. Step 4 절차

1. 위 S1~S7 수행, 결과를 표로 기록 (통과/실패 + 관찰값).
2. `refresh_unity → read_console`로 에러·워닝 확인 (`execute_code` 금지).
3. 구현–문서 차이 발견 시: **문서 갱신 → 승인 → 코드 반영** 순서. 차이점을 보고서에 명시.
4. 전 항목 통과 + 체크리스트 ≥9.0 시 커밋: `feat: Phase 1 hand tracking pipeline`.

## 5. 알려진 전제·리스크

- 이 PC에 현재 웹캠이 없다 (Phase 0 확인). **Step 1 검증 전 웹캠 연결 필수.**
- MediaPipe 첫 실행 시 모델 초기화로 수 초 지연될 수 있다 — lost 판정과 무관 (Unity는 수신 시작 전 상태를 lost가 아닌 "미수신"으로 취급).
