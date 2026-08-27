# 04. Unity 클라이언트 설계

## 1. 배치

```
Assets/_CameraCoop/
├── Scenes/HandTrackingTest.unity      # Phase 1 테스트 씬
├── Scenes/DrawingTest.unity           # Phase 2 테스트 씬 (docs/07)
├── Scripts/
│   ├── CameraCoop.Runtime.asmdef      # 런타임 어셈블리 (테스트 참조용)
│   ├── Net/UdpHandReceiver.cs         # 수신 + 파싱 + lost 판정
│   ├── Net/HandData.cs                # 프로토콜 DTO
│   ├── Input/HandCursorController.cs  # 커서 표현 + 핀치 판정
│   └── Drawing/                       # 드로잉 컨트롤러 + 순수 로직 (docs/07)
├── Tests/EditMode/
│   ├── CameraCoop.Tests.EditMode.asmdef  # 테스트 어셈블리 (Runtime + nunit 참조)
│   └── ProtocolTests.cs               # 순수 로직 Edit Mode 테스트
├── Prefabs/HandCursor.prefab          # 커서 UI (Image + CanvasGroup)
└── Materials/                         # 드로잉 스트로크/배경 머티리얼 (docs/07 §7)
```
- Unity 6000.3.15f1, URP. **런타임 패키지 추가 없음.** tooling으로 `com.unity.pipeline`만 추가한다 (Unity CLI가 에디터에 붙기 위한 필수 패키지. 런타임 코드는 이 패키지에 의존하지 않으며, 제거해도 게임 동작에 영향 없음).
- 씬 구성: `Canvas`(Screen Space - Overlay) 아래 커서 2개, 빈 GO `HandTracking`에 UdpHandReceiver + HandCursorController 부착. `EventSystem`·`GraphicRaycaster`는 넣지 않는다 — 커서는 UI raycast를 쓰지 않는다 (프리팹 Image의 `raycastTarget`도 off).
- NetplayTest.unity는 로비 UI가 있어 EventSystem + InputSystemUIInputModule을 사용한다 (docs/08 §5). 커서 전용 씬의 EventSystem 금지는 유지.
- 커서 프리팹 비주얼: sprite 없는 64×64 흰 사각형. 색은 런타임에 `leftColor`/`rightColor`로 입힌다. Phase 1 테스트 목적이므로 전용 스프라이트 에셋을 만들지 않는다.

## 2. HandData.cs — 프로토콜 DTO

```csharp
[Serializable] public class HandPacket { public int v; public uint seq; public double timestamp; public HandData[] hands; }
[Serializable] public class HandData   { public string handedness; public float[] landmarks; public float pinch; }
```
- `docs/02_protocol.md` 스키마와 1:1. 로직 없는 순수 데이터.
- 랜드마크 접근 헬퍼: `HandData.GetLandmark(int index)` → `new Vector3(landmarks[i*3], landmarks[i*3+1], landmarks[i*3+2])` (범위 검사 포함).

### JSON 파싱 방식 결정: JsonUtility
- **근거:** JsonUtility의 알려진 한계는 중첩 배열(`float[][]`)·Dictionary 미지원인데, 프로토콜이 landmarks를 float[63] 평탄화 배열로 확정했으므로 (`docs/02_protocol.md` §2) 위 DTO는 JsonUtility로 완전히 파싱된다. Newtonsoft 등 패키지 추가가 불필요하다 (패키지 추가 금지 규칙과도 부합).
- GC 참고: `JsonUtility.FromJson`은 패킷당 소량 할당이 발생한다. 30Hz × 수 KB 수준으로 Phase 1에서는 허용. 프로파일러에서 문제로 측정되면 Phase 2에서 개선한다.

## 3. UdpHandReceiver.cs

책임: UDP 수신, 최신 패킷 유지, seq/버전 검사, lost 판정. **표현·게임 판단 없음.**

### 스레드 안전성
- 수신 스레드: `UdpClient.Receive` 블로킹 루프 → UTF-8 디코드 → `lock`으로 최신 문자열 슬롯 덮어쓰기. **여기까지만.**
- 메인 스레드: `Update()`에서 슬롯을 꺼내(`lock`, 꺼낸 뒤 null 처리) `JsonUtility.FromJson<HandPacket>` 파싱 → `v` 불일치 폐기(경고 1회) → `seq <= lastSeq` 폐기 → `LatestPacket` 갱신 + 수신 시각 기록.
- ConcurrentQueue를 쓰지 않는 이유: 커서는 최신 상태만 의미가 있어 백로그가 무가치하고, 슬롯 1개 + lock이 가장 단순하다 (`docs/01_architecture.md` §3).

### public 인터페이스
```csharp
public HandPacket LatestPacket { get; }   // 아직 없으면 null
public float TimeSinceLastPacket { get; } // Time.realtimeSinceStartup 기준 경과 초
public bool IsServerLost { get; }         // TimeSinceLastPacket >= lostTimeout
public double LastLatencyMs { get; }      // (수신 epoch − packet.timestamp) ms, 테스트 플랜용
```

### Inspector 노출 ([SerializeField])
| 필드 | 기본값 | 설명 |
|---|---|---|
| `port` | 5052 | `docs/02_protocol.md` 준수 |
| `lostTimeout` | 0.5f | 서버 lost 판정 초 |

### 수명 주기
- `Awake`: UdpClient 생성(127.0.0.1 바인딩), 수신 스레드 시작 (`IsBackground = true`).
- `OnDestroy`/`OnApplicationQuit`: `running=false` → `client.Close()` (블로킹 해제) → `thread.Join(500ms)`. Close로 인한 `SocketException`은 종료 경로에서만 삼킨다 (그 외 예외는 로그).

## 4. HandCursorController.cs

책임: 커서 위치·색·핀치 표현, lost fade, Phase 2용 핀치 이벤트 발행.

### 동작 명세
| 항목 | 명세 |
|---|---|
| 커서 위치 | 손바닥 중심(landmark 0, 5, 9, 13, 17의 평균) → `screenX = x * Screen.width`, `screenY = (1-y) * Screen.height`. 손가락을 쥐고 펼 때에도 같은 기준을 사용하며, Overlay 캔버스의 `RectTransform.position`에 직접 대입 |
| 좌/우 구분 | handedness `"Left"` → `leftCursor` + `leftColor`(청색 계열), `"Right"` → `rightCursor` + `rightColor`(주황 계열). 색은 Inspector에서 변경 가능 |
| 핀치 판정 | 히스테리시스: `pinch < pinchThreshold`면 핀치 시작, `pinch > pinchReleaseThreshold`면 해제. 경계 떨림 방지 |
| 핀치 표현 | 핀치 중 커서 스케일 `pinchScale`배 축소 + 색 강조(채도/밝기 변경) |
| 손 lost | 최신 패킷에 해당 handedness 없음 → 그 커서만 fade out (CanvasGroup.alpha → 0, `fadeDuration`) |
| 서버 lost | `receiver.IsServerLost` → 두 커서 모두 fade out. 수신 재개 시 fade in |
| 핀치 중 lost | 손/서버 lost로 갱신을 스킵하기 전에 `OnPinchEnd` 발행 + pinched 해제. 모든 Start는 End로 닫힌다 (docs/07 §4) |

### public 이벤트 (Phase 2 드로잉 접점)
```csharp
public event Action<string, Vector2> OnPinchStart; // handedness, 화면 좌표
public event Action<string, Vector2> OnPinchMove;  // 핀치 유지 중 매 프레임
public event Action<string> OnPinchEnd;
```
- Phase 1에서는 발행만 하고 구독자 없음. 드로잉은 이 이벤트만 구독해 붙는다 (`docs/01_architecture.md` §4).
- 조준점은 핀치 여부와 무관하게 손바닥 중심을 따른다. `NetSession`의 커서 송신도 `HandData.GetPalmCenter()`를 사용하며, 핀치 비율과 기존 Python 필터는 변경하지 않는다.

### Inspector 노출 ([SerializeField])
| 필드 | 기본값 | 설명 |
|---|---|---|
| `receiver` | — | UdpHandReceiver 참조. **Inspector 직접 할당** (Find 계열 금지) |
| `leftCursor` / `rightCursor` | — | 커서 RectTransform (CanvasGroup, Image 포함 프리팹). 직접 할당 |
| `pinchThreshold` | 0.30f | 핀치 시작 기준 (`docs/02_protocol.md` pinch 비율) |
| `pinchReleaseThreshold` | 0.40f | 핀치 해제 기준 (히스테리시스 폭) |
| `pinchScale` | 0.7f | 핀치 중 커서 스케일 |
| `leftColor` / `rightColor` | 청 / 주황 | 커서 기본 색 |
| `fadeDuration` | 0.2f | lost fade 시간 초 |

### 참조 관계
```
HandCursorController --(SerializeField)--> UdpHandReceiver
HandCursorController --(SerializeField)--> leftCursor/rightCursor (RectTransform)
```
- 단방향. Receiver는 아무도 참조하지 않는다. 모든 참조는 Inspector 직접 할당.

## 5. 테스트 가능 설계 (QUALITY_CHECKLIST 3-1 대응)

순수 로직은 MonoBehaviour 밖의 정적 함수로 분리해 Edit Mode 테스트 대상으로 삼는다.

| 순수 함수 | 위치 | 테스트 항목 |
|---|---|---|
| `HandData.GetLandmark(int)` | HandData.cs | 인덱스 → 배열 오프셋 매핑, 범위 밖 방어 |
| `PacketFilter.ShouldAccept(packet, lastSeq)` | HandData.cs | v 불일치 폐기, seq 역전/중복 폐기, 정상 통과 |
| `HandScreenMapper.ToScreen(x, y, w, h)` | HandData.cs | 정규화 → 화면 좌표 (y 반전) 변환 |
| `PinchStateMachine.Next(current, pinch, start, release)` | HandData.cs | 히스테리시스 경계값 판정 |
| `StrokeLogic.Decide / ShouldAppendPoint / ShouldDiscardOnEnd` | StrokeLogic.cs | 스트로크 상태 전이, 점 추가 최소 간격, 점 2개 미만 폐기 (docs/07 §8) |

- JsonUtility 파싱 왕복(스키마 v1 샘플 문자열 → DTO → 값 검증)도 Edit Mode 테스트에 포함한다.
- MonoBehaviour(수신 스레드·커서 표현)는 Edit Mode 테스트 대상에서 제외하고 Play 모드 통합 검증(docs/05)으로 커버한다.

## 6. 성능 규칙 (QUALITY_CHECKLIST 대응)
- Update 내 `GetComponent`/`Find`/`Camera.main` 금지 — 전 참조 Awake 캐싱 또는 Inspector 할당.
- 문자열 비교(`handedness`)는 프레임당 2회 수준으로 허용. <!-- ponytail: enum 변환은 측정상 문제될 때만 -->
- 파싱 외 핫패스 힙 할당 0 유지 (이벤트 발행 포함).
