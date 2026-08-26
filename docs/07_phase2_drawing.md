# 07. Phase 2 설계 — 드로잉 메카닉

> 작성 2026-08-26 · 상태: 설계 승인됨 (구현 전)
> 전제: Phase 1 완료 (웹캠 검증 통과, commit `caf8302`). 프로토콜 v1 무변경.

## 1. 목표와 범위

핀치 제스처로 3D 씬 안 고정 평면에 선을 그리는 **로컬 드로잉 메카닉**.

- 핀치 시작 → 스트로크 시작, 핀치 유지 → 선 연장, 핀치 해제 → 스트로크 확정
- 양손 동시 드로잉 (좌/우 독립 스트로크, 손별 색)
- 키보드 한 키로 전체 지우기

### 명시적 배제 (Phase 3+)

undo, 지우개 제스처, 선 두께/색 선택 UI, depth(z) 드로잉, 저장/불러오기, 게임 규칙(목표·판정), 네트워크 동기화. 새 제스처가 필요한 기능은 프로토콜 v2와 함께 별도 설계한다.

## 2. 아키텍처

```
Assets/_CameraCoop/Scripts/Drawing/
  StrokeLogic.cs        — 순수 정적 로직 (EditMode 테스트 대상)
  DrawingController.cs  — MonoBehaviour (이벤트 구독, LineRenderer 관리)
Assets/_CameraCoop/Scripts/Input/
  HandCursorController.cs — 최소 수정 1건 (§4)
```

- 참조 방향: `DrawingController → HandCursorController` 단방향 (SerializeField 직접 할당, Find 계열 금지).
- 기존 컴포넌트는 드로잉의 존재를 모른다 (docs/01 §4의 이벤트 구독 접점 약속 이행).
- 렌더링: 스트로크당 LineRenderer GameObject 1개. Mesh 리본·TrailRenderer는 검토 후 배제 (복잡도 대비 이득 없음 / 스트로크 관리 불가).

## 3. 데이터 흐름

```
OnPinchStart(handedness, screenPos)
  → 화면 좌표를 drawCamera 기준 planeDistance 평면에 투영 (ScreenToWorldPoint)
  → LineRenderer GameObject 생성 (색 = 손별 색), 첫 점 추가
OnPinchMove(handedness, screenPos)
  → 투영 → StrokeLogic.ShouldAppendPoint 통과 시 점 추가 (SetPosition, 무할당)
OnPinchEnd(handedness)
  → 점 2개 미만이면 GameObject 파괴 (점 찍기 미지원), 아니면 스트로크 확정·보관
clearKey 입력
  → 전 스트로크 파괴 (진행 중 포함), 활성 상태 리셋
```

- `ShouldAppendPoint(last, next, minPointDistance)`: 실효 입력 ~14Hz(Intel Mac 실측)에서 중복·근접 점을 거른다. 선의 각짐이 문제로 관찰되면 Catmull-Rom 보간을 순수 함수로 추가한다 — **측정 후 필요 시에만** (docs/01 §4 원칙).

## 4. HandCursorController 수정 — lost 시 이벤트 계약 보장

현재 결함: 서버 lost(`Update` early return) 또는 손 미검출(`UpdateHand` early return) 시 핀치 상태 갱신을 스킵하므로, **핀치 중 손이 사라지면 `OnPinchEnd`가 발행되지 않는다.** 구독자 입장에서 "모든 Start는 End로 닫힌다" 불변식이 깨진다.

수정: lost로 갱신을 스킵하기 전에 `state.pinched == true`면 `OnPinchEnd(handedness)` 발행 + `state.pinched = false`. 판정 로직은 `CursorStateLogic`에 순수 함수로 두어 테스트한다. docs/04 §4 명세에 이 동작을 추가한다.

근거: 구독자마다 타임아웃 방어를 복제하는 대신 발행자가 계약을 보장한다.

## 5. DrawingController Inspector 노출

| 필드 | 기본값 | 설명 |
|---|---|---|
| `cursorController` | — | HandCursorController 참조. 직접 할당 |
| `drawCamera` | — | 투영 기준 카메라. 직접 할당 |
| `planeDistance` | 5.0f | 카메라로부터 드로잉 평면까지 거리 (m) |
| `minPointDistance` | 0.01f | 점 추가 최소 간격 (월드 단위) |
| `lineWidth` | 0.02f | LineRenderer 폭 |
| `lineMaterial` | — | URP Unlit 공유 머티리얼 1개 (SRP Batcher 호환) |
| `leftStrokeColor` / `rightStrokeColor` | 청 / 주황 | 커서 색과 같은 계열 (독립 필드 — 커플링 회피) |
| `clearKey` | Key.C | 전체 지우기 키. **주의: 프로젝트가 새 Input System 전용**(`activeInputHandler: 1`)이므로 `UnityEngine.InputSystem.Key` + `Keyboard.current`를 쓴다. legacy `Input.GetKeyDown`은 예외 발생 |

## 6. 엣지 케이스

| 상황 | 처리 |
|---|---|
| 핀치 중 손/서버 lost | §4 수정으로 `OnPinchEnd` 발행 → 정상 종료. 복귀 시 새 핀치부터 새 스트로크 |
| Start 없는 Move/End | 활성 스트로크 없으면 무시 |
| 같은 손 중복 Start | 기존 활성 스트로크 종료 후 새로 시작 (방어) |
| 점 1개 스트로크 | End 시 파괴. 점 찍기는 미지원 명시 |
| clear 중 핀치 진행 | 진행 중 스트로크 포함 파괴, 이후 Move는 고아 Move로 무시 |
| Python 재시작 (S6) | 수신 계층이 처리. 드로잉 무영향 — 기존 스트로크 유지 |

## 7. 씬

새 씬 `Scenes/DrawingTest.unity`. `HandTrackingTest.unity`는 Phase 1 검증용으로 무수정 보존.

- Camera + Canvas(기존 HandCursor 프리팹 2개) + HandTracking(UdpHandReceiver + HandCursorController + DrawingController) + 어두운 backdrop (드로잉 평면 위치 시각화)
- EventSystem/GraphicRaycaster 없음 유지 (docs/04 §1)

## 8. 테스트 (EditMode, 기존 30개에 추가)

| 대상 | 케이스 |
|---|---|
| `StrokeLogic.ShouldAppendPoint` | 첫 점 true / 미만 false / 이상 true / 경계값 |
| `CursorStateLogic` lost 경로 | 핀치 중 lost → End, 비핀치 중 lost → None |
| 스트로크 상태 전이 | 중복 Start / 고아 Move·End / clear 리셋 |

## 9. DoD

| # | 기준 | 확인 방법 |
|---|---|---|
| D-1 | 핀치로 선이 그려지고 해제로 끊긴다 | 육안 (웹캠) |
| D-2 | 양손 동시에 색 구분된 두 스트로크 | 육안 (웹캠) |
| D-3 | clear 키로 전부 삭제, 진행 중 스트로크 포함 | 육안 |
| D-4 | 핀치 중 손을 화면 밖으로 → 스트로크 종료, 점프 이어짐 없음 | 육안 (웹캠) |
| D-5 | EditMode 테스트 전체 pass (기존 30 + 신규) | `unity cmd run_tests` |
| D-6 | QUALITY_CHECKLIST ≥ 9.0 | 채점 보고 |
| D-7 | 5분 드로잉 세션 에러·누수 없음 | 콘솔 + 메모리 |
