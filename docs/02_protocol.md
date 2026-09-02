# 02. UDP 프로토콜 명세 (v1)

> 갱신: 2026-08-28 · 현재 wire 계약은 v1 유지. 로컬 손 UI 확장은 [07_hand_interaction](07_hand_interaction.md)의 **구현 대기 설계**이며 wire 변경이 아니다.

## 1. 전송 계층

| 항목 | 값 |
|---|---|
| 방향 | Python → Unity 단방향 |
| 주소 | `127.0.0.1:5052` (루프백 전용, 외부 바인딩 금지) |
| 인코딩 | UTF-8 JSON, 패킷당 1개 JSON 객체 |
| 전송 주기 | 성공적으로 캡처·추론한 프레임마다. 처리 시간에 의존하며 고정 Hz가 아님 |
| 패킷 크기 | 손 2개 기준 약 2~3KB. 루프백이므로 단편화 무관 |

## 2. JSON 스키마

```json
{
  "v": 1,
  "seq": 42,
  "timestamp": 1234567890.123,
  "hands": [
    {
      "handedness": "Right",
      "landmarks": [0.51, 0.42, -0.01, ...],
      "pinch": 0.21
    }
  ]
}
```

| 필드 | 타입 | 단위/범위 | 설명 |
|---|---|---|---|
| `v` | int | 1 | 프로토콜 버전. 수신 측은 미지원 버전 패킷을 무시하고 경고 1회 로그 |
| `seq` | uint | 0부터 증가 | 패킷 순번. 수신 측은 마지막 처리 seq 이하인 패킷을 폐기 (UDP 역전/중복 대응) |
| `timestamp` | double | Unix epoch 초 | 추론 완료 후, 필터·직렬화 전의 `time.time()`. Unity 수용 시각과의 차이는 추론 이후 구간 지연이며 캡처·추론은 포함하지 않음 |
| `hands` | array | 0~2개 | 캡처·추론 성공 후 **손 미검출이면 빈 배열 전송**. 카메라 read 실패 중에는 송신하지 않음 |
| `handedness` | string | `"Left"` \| `"Right"` | 사용자 기준 실제 손. 셀피 미러(flip) 후 추론 시 MediaPipe 판정값이 실제 손과 반대로 나오므로 Python이 좌우 반전해 송신한다 (2026-08-26 Intel Mac·mediapipe 0.10.21, 2026-08-27 Windows·mediapipe 1.0.1 양쪽 실물 검증 — 두 스택 모두 반전 필요. 검증법: 양손을 동시에 올려 flip 후 `wrist.x`가 큰 쪽이 실제 오른손임을 정답으로 두고 raw 라벨과 대조) |
| `landmarks` | float[63] | 아래 좌표계 | 21개 랜드마크 × (x,y,z) **평탄화 배열**. index i번 랜드마크 = `[i*3], [i*3+1], [i*3+2]` |
| `pinch` | float | 비율 (무단위) | `dist2D(4,8) / dist2D(0,9)`. 손 크기로 정규화된 엄지-검지 거리 |

confidence, tracking-valid, pinched boolean은 wire에 없다. MediaPipe의 confidence 설정은 추론 옵션이다. 현재 수신부는 파싱·버전·seq를 검사하며 landmarks 길이·유한값 등 완전한 스키마 검증은 하지 않는다. 새 손 UI의 유효성 검사는 기존 wire 계약과 별도로 설계한다.

### 초안 대비 변경점과 근거

1. **`landmarks`를 `[[x,y,z],...]` 중첩 배열 → 평탄화 float[63]으로 변경.** Unity `JsonUtility`는 중첩 배열(`float[][]`)을 파싱하지 못한다. 평탄화하면 JsonUtility만으로 충분해 외부 JSON 패키지 추가가 필요 없다 (`docs/04_unity_client.md` 참조).
2. **`v` 버전 필드 추가** (초안의 검토 항목 채택). 스키마 변경 시 v를 올리고 수신 측이 방어한다.
3. **`seq` 필드 추가.** UDP 순서 역전·중복을 수신 측에서 한 줄 비교로 걸러낸다.
4. **`pinch`를 raw 거리 → 손 크기 정규화 비율로 변경.** raw 거리는 카메라-손 거리에 따라 스케일이 변해 threshold가 성립하지 않는다. 손바닥 길이(손목 0 ↔ 중지 MCP 9)로 나눠 거리 불변으로 만든다. 경험적 기준: 핀치 시 ≈ 0.15~0.25, 벌림 시 ≈ 0.8~1.2. 판정 threshold는 Unity Inspector 파라미터.

## 3. 좌표계 정의

- **정규화 [0,1]**, 원점 = 화면 **좌상단**, x → 오른쪽, y → 아래쪽.
- **셀피 미러 뷰 기준.** Python이 추론 전에 `cv2.flip(frame, 1)`을 적용한다. 따라서 사용자가 오른손을 오른쪽으로 움직이면 x가 증가한다. `handedness`도 사용자 실제 손과 일치한다 (flip으로 반전된 MediaPipe 판정값을 Python이 다시 반전해 송신하기 때문. §2 참조).
- z: MediaPipe 상대 깊이 (손목 기준, 음수 = 카메라 쪽). Phase 1에서는 사용하지 않고 전달만 한다.
- Unity 화면 변환 (화면 원점 좌하단):
  ```
  screenX = x * Screen.width
  screenY = (1 - y) * Screen.height
  ```

## 4. 유실·단절 대응

| 상황 | 판정 | 대응 |
|---|---|---|
| 패킷 유실 | 대응 없음 | 상태 스냅샷 방식이므로 다음 패킷이 자연 대체. 재전송 없음 |
| 패킷 역전/중복 | `seq <= lastSeq` | 폐기 |
| 손 lost | 최신 수용 패킷의 `hands`에 해당 handedness 없음 | 진행 중 pinch End 후 해당 커서 fade out |
| 서버 lost | 메인 스레드의 마지막 패킷 **수용 이후 0.5초 이상** | controller가 관측한 Update에서 pinch End·전체 fade 시작. 새 수용 패킷으로 복구 |
| 송신 측 재시작 | timeout 이후 새 패킷 | 버전 확인 후 seq 비교를 생략하고 새 seq로 체인 시작 |

- 손 lost와 서버 lost를 구분하기 위해 Python은 캡처·추론에 성공하면 손이 없어도 빈 `hands`를 전송한다. 카메라 read 실패는 heartbeat와 다르다.
- **`seq` 체인 리셋:** 최초 패킷 또는 마지막 수용 이후 timeout에 도달한 패킷은 새 세션으로 처리한다 (`PacketFilter.IsNewSession`). 그 외에는 seq가 이전 값보다 커야 한다. 빠른 재시작은 즉시 구분하지 못하며 이전 seq를 넘거나 수용 timeout에 도달해야 한다.
- 거부 패킷의 UDP 도착은 lost 시계를 갱신하지 않는다. 최초 수용 전 `IsServerLost`는 false이며, 커서는 `LatestPacket == null`을 별도로 미수신 상태로 처리한다.
- 현재 `OnPinchEnd`에는 종료 사유·해제 좌표가 없다. 손/server lost를 정상 release와 같은 클릭 확정으로 해석하면 안 된다. fade 기본 0.2초는 lost 판정 시간과 별개다.
- `seq`는 uint 롤오버를 무시한다. <!-- ponytail: 30Hz 기준 롤오버까지 4.5년. 문제되면 wrap-around 비교로 교체 -->

## 5. 버전 정책

- 필드 추가·의미 변경·좌표계 변경 시 `v`를 +1 한다.
- 수신 측(`UdpHandReceiver`)은 `v != 지원 버전`이면 패킷을 버리고 경고를 1회만 로그한다.

## 6. Steam party protocol v4 전환 필드

UDP 손 센서 wire는 v1을 유지한다. Steam party game의 별도 `OnlineRelayQuizProtocol`은 `GameId=camera-coop-relayquiz-4p`, `Version=4`, `PlayerCount=4`, `MaxMessageBytes=64*1024`를 사용한다. 두 버전을 혼용하지 않는다.

v4 packet의 전환 관련 필드는 `sessionId`, `rosterGeneration`, `selectedMode`, `modeGeneration`, `startSignal`, `transitionGeneration`, `transitionPhase`, `sceneReadyMask`이다. `startSignal`은 mode 선택과 additive Scene load를 확정하는 epoch이며, `START` 자체에서는 증가하지 않는다. `SelectModeAndBeginLoad`가 mode 선택을 승인할 때 증가한다. `transitionGeneration`은 additive Scene load/unload의 순서를 나타낸다. `transitionPhase`의 정확한 값은 `Lobby`, `SelectingMode`, `LoadingGame`, `InGame`, `ReturningToLobby`다. `sceneReadyMask`는 네 slot의 Scene 준비 상태를 나타낸다. `CoopMural` wire는 별도 `muralEpoch` 필드를 만들지 않고 `startSignal`을 mural session epoch로 사용하며, layer의 `revision`과 함께 늦은 layer·중복 완료를 거부한다.

v4는 동일 `sessionId`와 최신 generation만 수용한다. Scene load failure와 timeout은 host가 실패 전환을 broadcast하고 private drawing·secret을 공개하지 않은 채 game Scene을 정리해 lobby로 돌아간다. disconnect는 `Abort`를 broadcast하고 round를 폐기하며 새 invite가 필요하다. drawing payload는 별도 reliable chunk로 전송하며 raw camera 영상·hand landmarks를 보내지 않는다.
