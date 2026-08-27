# 10. Phase 3d 설계 — 3D 월드 캔버스 (Netplay3D)

> 작성 2026-08-27 · 상태: **설계 승인** (사용자 확인: 3D 룸+월드 캔버스 / 새 씬 / Editor 검증까지)
> 전제: Phase 3a 완료 (docs/08), Steam 2인 실검증(N-5) 사용자 확인 완료.
> 위치: 3b 미니게임 프레임워크와 독립 — 표시 계층만 바꾸므로 3b 설계 입력(docs/09 §3)에 영향 없음.

## 1. 목표와 범위

Steam/Loopback 멀티플레이 드로잉을 **3D 환경(룸 + 월드 공간 캔버스)**에서 동작하게 한다.

핵심 통찰: 와이어 프로토콜(network v1)은 이미 **정규화 [0,1] 캔버스 좌표**다 (docs/08 §3). 따라서 네트워크 계층(`NetProtocol`/`NetSession`/transport)은 무수정 — 바뀌는 것은 "norm 좌표를 어디에 그리나"의 **매핑 함수뿐**이다.

### 명시적 배제 (요청 시 별도 Phase)

3D 공간 드로잉(z 입력), 플레이어 아바타, 카메라 이동/회전 조작, 외부 에셋, RenderTexture 텍스처 페인팅, Windows 빌드 갱신(사용자 결정 — Editor 검증까지만), 기존 NetplayTest 씬 수정.

## 2. 아키텍처

### 신규 1개

```
Scripts/Drawing/CanvasSurface.cs — 월드 공간 캔버스 평면 (MonoBehaviour)
```

- 1×1 Quad에 부착 — **transform 스케일이 곧 캔버스 크기** (별도 width/height 필드 없음 → 시각 크기와 매핑이 어긋날 수 없다).
- `Vector3 NormToWorld(Vector2 norm)` — norm [0,1] 좌상단 원점(docs/02 §3 좌표계) → 캔버스 표면 위 월드 좌표 (`transform.TransformPoint`). 로컬 z에 `surfaceOffset`(기본 -0.005, Quad 정면 -Z 쪽)을 둬 z-fighting 방지.
- 순수 매핑 수식은 `internal static CanvasSurfaceLogic`으로 분리 — EditMode 테스트 대상 (docs/04 §5 패턴).

### 기존 3개에 optional 주입 (미할당 = 기존 동작 → NetplayTest 무회귀)

| 컴포넌트 | 변경 지점 | canvasSurface 할당 시 동작 |
|---|---|---|
| `DrawingController` | `ToPlanePoint(screenPos)` | screenPos → norm → `NormToWorld` (기존: `ScreenToWorldPoint(planeDistance)`) |
| `RemotePresenter` | `ToWorld(norm)` + 커서 위치 | `NormToWorld(norm)`. uGUI 커서는 `drawCamera.WorldToScreenPoint(NormToWorld(norm))`로 투영 |
| `HandCursorController` | 커서 표시 위치 | `projectionCamera.WorldToScreenPoint(NormToWorld(norm))` 투영 (신규 optional Camera 필드) |

- **핀치 이벤트의 screenPos 계약은 불변** — `OnPinchStart/Move`는 기존 `HandScreenMapper.ToScreen` 값 그대로 발행한다. `NetSession.ToNormalized` 왕복(screen↔norm)이 무손실로 유지되므로 NetSession 무수정.
- screen→norm 역변환은 `HandScreenMapper`에 `ToNormalized(screenPos, w, h)` static으로 추가하고 `NetSession`의 private 중복(`NetSession.cs:510`)도 이것으로 교체 — 단일 진실 원천.
- `DrawingController.ShouldSplitStroke`(재검출 스냅 가드)는 screenPos 기반 그대로 동작 — 수정 없음.
- norm 클램프 없음 — 기존 화면 공간 경로와 동일 (일관성). 캔버스 밖 스트로크는 기존과 같은 규칙으로 허용.

### 참조 방향 (docs/01 §4 유지)

`DrawingController/RemotePresenter/HandCursorController → CanvasSurface` 단방향. CanvasSurface는 소비자를 모른다. 네트워크 계층은 CanvasSurface의 존재를 모른다.

## 3. 씬 — Netplay3D.unity

`Scenes/Netplay3D.unity` 신규. 빌드 씬 등록. 외부 에셋 없이 프리미티브 + URP material.

- **룸**: 바닥 Plane + 뒷벽/옆벽 (URP Lit, 차분한 색), Directional Light 1 + 보조 광
- **이젤 + 캔버스**: 큐브 조합 이젤 위 Quad. **캔버스 비율 16:9** (웹캠·화면 norm 좌표와 동일 비율 — 그림 왜곡 방지). 흰색 material. `CanvasSurface` 부착 (Quad 스케일 = 캔버스 크기)
- **카메라**: 캔버스 정면 고정, 캔버스가 화면 대부분을 차지
- **배선**: NetplayTest와 동일 오브젝트 세트 (UdpHandReceiver, HandCursorController+HandCursor prefab×2, DrawingController, NetSession, RemotePresenter, NetplayUI, TrackerLauncher, 로비 UI Canvas+**GraphicRaycaster**+EventSystem) + canvasSurface/카메라 주입
- 스트로크 lineWidth는 캔버스 크기에 맞게 조정 (기본 0.02 월드 단위 기준 검토)

## 4. 엣지 케이스

| 상황 | 처리 |
|---|---|
| canvasSurface 미할당 | 기존 화면 공간 동작 (NetplayTest 그대로) |
| 화면비 ≠ 캔버스비 | norm 기준 매핑이라 캔버스비를 따름 — 왜곡 없음. 16:9 고정으로 실질 비발생 |
| norm 범위 밖 (재검출 순간 등) | 클램프 없이 통과 — 기존 경로와 동일 규칙 |
| 카메라가 캔버스 뒤 | 씬에서 카메라 고정이라 비발생 — 방어 코드 생략 (YAGNI) |

## 5. 테스트 / DoD

| # | 기준 | 확인 방법 |
|---|---|---|
| W-1 | EditMode: CanvasSurfaceLogic 매핑 (코너 4점·중심·transform 이동/회전 반영) + 기존 72 전체 pass | `unity cmd run_tests` |
| W-2 | Loopback 4인 in Netplay3D: 가짜 피어 3 + 로컬 1 커서·스트로크가 캔버스 Quad 위 4색 표시 | eval + `capture_game_view` |
| W-3 | 늦은 참가 스냅샷 + 피어 이탈 (N-2/N-3 절차를 3D 씬에서) | 자동 |
| W-4 | NetplayTest 2D 씬 무회귀 (Loopback smoke) | 육안/capture |
| W-5 | QUALITY_CHECKLIST ≥ 9.0 | 채점 보고 |

실 Steam 2인 3D 검증은 기기 2대 필요 — 사용자 수동 (빌드 갱신 요청 시 함께).

## 6. 구현 분담

전역 규칙 §3: Fable5(메인)는 계획·검수, 구현·검증은 subagent(sonnet/opus) 위임. Unity Editor 조작은 `unity cmd` — docs/09 §4 함정 목록을 프롬프트에 포함할 것.
