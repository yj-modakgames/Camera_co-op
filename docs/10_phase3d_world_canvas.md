# 10. Phase 3d 설계 — 3D 월드 캔버스 (Netplay3D)

> 작성 2026-08-27 · 상태: **구현 완료** (commit `1be0318..ba54feb`. 사용자 확인 기준: 3D 룸+월드 캔버스 / 새 씬 / Editor 검증까지 — 그 범위 내에서 검증 완료. Steam 2인 실기(N-5)는 다음 빌드 갱신 시 별도 수행)
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
| **알려진 한계**: Game view aspect < 1.30 | 캔버스 좌우가 화면 밖으로 잘린다 (카메라 z=-1.6, FOV 60, 캔버스 폭 2.4 기준 필요 aspect ≥ 1.30). Task 5 실측: aspect 1.081(Free Aspect 500×462)에서 `canvasTLscreen x=-51`로 좌측 경계가 화면 밖. 빌드 기본 해상도 1600×900(=16:9, aspect 1.778)에서는 정상이라 수용 — Phase 3d 범위 밖 |

## 5. 테스트 / DoD

| # | 기준 | 확인 방법 | 결과 |
|---|---|---|---|
| W-1 | EditMode: CanvasSurfaceLogic 매핑 (코너 4점·중심·transform 이동/회전 반영) + 기존 전체 pass | `unity cmd run_tests` | **PASS** — 81/81 (Failed 0). 진행 이력 72(3a baseline) → 77(Task1 +5) → 79(Task2 +2) → 80(MacBuild.cs Windows CS0234 가드 후 stale 카운트 복구) → 81(Netplay3D TestCase 추가). Task 4 report |
| W-2 | Loopback 4인 in Netplay3D: 가짜 피어 3 + 로컬 1 커서·스트로크가 캔버스 Quad 위 4색 표시 | eval + `capture_game_view` | **PASS** — `remoteStrokes=2 onCanvas=2 remoteCursors=1 players=4`. 스트로크 첫 점 z=-0.0050 = `CanvasSurface.surfaceOffset`(-0.005)과 정확히 일치(캔버스 평면 위). 좌표 검산 일치(fake-1 norm(0.2,0.2)→world(-0.72,1.905)). 캡처(1280×720)로 4색 표시가 캔버스 Quad 안쪽에 있음을 육안 확인. Task 5 report |
| W-3 | 늦은 참가 스냅샷 + 피어 이탈 (N-2/N-3 절차를 3D 씬에서) | 자동 | **PASS** — 피어 이탈: `players 4→3 remoteStrokesPreserved=2`. 늦은 참가(빈 슬롯 재사용): `type=Welcome players=4 snapshot=2`. `players=4`는 `SessionLogic.MaxPlayers=4` 제한에 따른 정정값(plan의 `players=5`는 도달 불가 — 위 plan 정정 참조). Task 5 report |
| W-4 | NetplayTest 2D 씬 무회귀 (Loopback smoke) | 육안/capture | **PASS** — `cam.transform.InverseTransformPoint(strokePoint).z = 5.0000` = `planeDistance`(기존 카메라 평면 경로), `canvasSurface=null`(두 소비자 모두 미할당으로 legacy 분기 확인). 월드 z=6.785로 캔버스 평면(z≈0)과 명확히 구분 — 월드 캔버스 경로로의 회귀 없음. plan의 `z ≈ camZ+5` 판정식은 카메라 회전(euler 26.33,225,0) 때문에 성립하지 않아 정정. Task 5 report |
| W-5 | QUALITY_CHECKLIST ≥ 9.0 | 채점 보고 | §7 참조 |

콘솔 에러: 두 세션(Netplay3D/NetplayTest) 모두 **0건** (Task 5 report).

실 Steam 2인 3D 검증은 기기 2대 필요 — 사용자 수동 (빌드 갱신 요청 시 함께, docs/09 §3).

## 6. 구현 분담

전역 규칙 §3: Fable5(메인)는 계획·검수, 구현·검증은 subagent(sonnet/opus) 위임. Unity Editor 조작은 `unity cmd` — docs/09 §4 함정 목록을 프롬프트에 포함할 것.

## 7. 채점 (W-5) — QUALITY_CHECKLIST.md 기준

> 채점일 2026-08-27 · 대상 commit 범위 `1be0318..ba54feb` · 채점 원칙: 감점 요인 우선 탐색, 코드 미분석/추측 만점 금지 (QUALITY_CHECKLIST.md 채점 원칙).

### 항목별 점수

| 카테고리 | 항목 | 배점 | 획득 | 근거 |
|---|---|---|---|---|
| 기능 | 1-1 요구사항 완전 충족 | 0.8 | 0.70 | `CanvasSurface`+3개 컴포넌트(DrawingController/RemotePresenter/HandCursorController) optional 주입 전부 구현, W-2/W-3/W-4 PASS(§5). **감점**: 로컬 드로잉 경로 — `DrawingController.ToPlanePoint`와 `HandCursorController.UpdateHand`의 `canvasSurface` 분기 — 는 웹캠 실입력으로도 Play 자동화(eval)로도 단 한 번도 실행되지 않았다. Task 5 검증은 Loopback 가짜 피어로 `RemotePresenter` 경로만 태웠다(Task 5 report Concern 5, "로컬 드로잉 실입력 경로는 이번 검증에 포함되지 않았다") |
| 기능 | 1-2 엣지 케이스 처리 | 0.6 | 0.45 | `canvasSurface` 미할당 시 기존 카메라 평면 폴백 정상(W-4 PASS, `canvasSurface=null` 확인). **감점**: (a) Game view aspect < 1.30에서 캔버스 좌우가 화면 밖으로 잘림 — Task 5 실측 `canvasTLscreen x=-51`(aspect 1.081) — §4 기록, 미해결 상태로 남음. (b) `Floor`(Plane, 20×20)가 `LeftWall`/`RightWall`(x ±6, z -9.25~1.25) 밖으로 삐져나옴 — 카메라 고정이라 육안 무해하나 처리되지 않은 경계값(Task 4 report Concern 3) |
| 기능 | 1-3 에러 핸들링 | 0.6 | 0.55 | 콘솔 에러 0건(Netplay3D·NetplayTest 두 Play 세션, Task 5 report). **감점**: `RemotePresenter.HandleCursor`의 `drawCamera` null 가드는 Task 3 최초 구현에 없었고 코드 리뷰에서 발견돼 별도 commit(`48db926`)으로 사후 수정됨 — 최초 구현 시점 결함 이력 |
| 성능 | 2-1 핫패스 GC 할당 최소화 | 0.7 | 0.65 | `CanvasSurfaceLogic.NormToLocal`/`HandScreenMapper.ToNormalized`/`NormToWorld` 전부 struct(`Vector2`/`Vector3`) 반환, 신규 코드에 LINQ·boxing·문자열 연결 없음(코드 직접 확인: CanvasSurface.cs, DrawingController.cs, RemotePresenter.cs, HandCursorController.cs). **감점**: Unity Profiler 실측 없이 코드 분석 근거만 (체크리스트 원칙상 코드분석도 인정되나 실측 대비 확증도가 낮음) |
| 성능 | 2-2 Update 내 고비용 호출 제거 | 0.7 | 0.70 | `canvasSurface`/`projectionCamera`/`drawCamera` 전부 `[SerializeField]` Inspector 직렬화 필드로 캐싱(코드 확인). Update 경로에 `GetComponent`/`GameObject.Find`/`Camera.main` 신규 호출 없음 — `HandCursorController.Update`는 캐싱된 `projectionCamera` 사용 |
| 성능 | 2-3 메모리 사용/누수 점검 | 0.6 | 0.55 | Phase 3d는 기존 `OnEnable`/`OnDisable` 대칭 이벤트 구독 패턴을 그대로 유지 — 신규 구독·소켓·스레드·영구 리소스 추가 없음(코드 확인). **감점**: Play 반복 진입/퇴장 시 메모리 잔류 여부는 이번 Task 범위에서 재측정하지 않음(N-7 10분 세션 재실행은 별건, docs/09 §3) |
| 검증 | 3-1 테스트 작성 | 0.7 | 0.40 | `CanvasSurfaceTests.cs` 7건(코너 3점·중심·position/scale·rotation·screen↔norm 왕복 2건)이 순수 로직(`CanvasSurfaceLogic`, `HandScreenMapper.ToNormalized`)을 촘촘히 덮는다. **큰 감점**: 실제 소비자 3곳(`DrawingController`/`RemotePresenter`/`HandCursorController`) 중 `RemotePresenter`만 Loopback으로 실행 검증됐고(W-2/W-3), `DrawingController.ToPlanePoint`·`HandCursorController.UpdateHand`의 `canvasSurface` 분기는 자동 테스트도 수동 Play 검증도 전혀 거치지 않았다(정적 코드 확인뿐) — 컨트롤러가 사전 지적한 항목과 일치 |
| 검증 | 3-2 테스트 실제 실행·통과 | 0.7 | 0.70 | `unity cmd run_tests --mode EditMode` 실제 실행, **81/81 Passed, Failed 0** (Task 4/5 report, 이 Task에서 재확인 — §"최종 테스트" 참조) |
| 검증 | 3-3 실제 실행 확인 / 로그·예외 클린 | 0.6 | 0.55 | `get_console_logs --severity error` — Netplay3D·NetplayTest 두 세션 모두 **0건**(Task 5 report 실행 결과 인용). **감점**: 이 확인은 Loopback 가짜 피어 경로만 커버 — 로컬 드로잉 경로는 3-1과 동일 사유로 실행 자체가 없었다 |
| 코드품질 | 4-1 네이밍·가독성 | 0.5 | 0.50 | 기존 코드베이스 패턴(`XxxLogic` 순수 함수 분리, 한글 주석 + docs 섹션 참조, PascalCase/camelCase 일관) 그대로 유지(코드 확인) |
| 코드품질 | 4-2 단일 책임·컴포지션 | 0.5 | 0.50 | `CanvasSurface`는 소비자(DrawingController 등)를 전혀 참조하지 않음(코드 확인, docs/01 §4 단방향 유지) — 3개 소비자도 기존 책임 분리 그대로에 optional 협력자만 추가 |
| 코드품질 | 4-3 매직넘버 제거 | 0.5 | 0.50 | `surfaceOffset`은 `[SerializeField]` + 기본값 + 의도 주석. 별도 width/height 필드 없이 `transform.localScale`이 곧 캔버스 크기(docs/10 §2 설계 의도 — 매핑과 시각 크기가 어긋날 수 없는 구조) |
| 코드품질 | 4-4 주석·구조·데드코드 | 0.5 | 0.40 | 데드코드 없음, 비자명 로직에 docs 참조 주석 있음. **감점**: `RemotePresenter`의 `drawCamera` null 가드 누락이 최초 커밋(Task 3)엔 없었고 리뷰 후 별도 fix 커밋(`48db926`)으로 보완 — 초안 완성도 이슈가 커밋 이력에 남음 |
| 최적화 | 5-1 오브젝트 풀링 | 0.5 | 0.50 | 이 기능(`CanvasSurface`) 자체는 런타임에 객체를 생성/파괴하지 않는 정적 매핑 컴포넌트 — 해당 없음 사유로 만점 (체크리스트 5-1 명시 규정) |
| 최적화 | 5-2 캐싱 | 0.5 | 0.50 | `canvasSurface`/`projectionCamera`/`drawCamera` 전부 Inspector에서 1회 배선, 런타임 재조회 없음(Task 4 report 배선 실측 — SerializedObject로 5건 non-null 확인) |
| 최적화 | 5-3 배칭/드로우콜 인식 | 0.5 | 0.45 | 신규 URP Lit 머티리얼 4개가 다중 프리미티브에 공유됨 — `RoomWall.mat`→BackWall/LeftWall/RightWall 3개, `EaselWood.mat`→Backboard/LegLeft/LegRight 3개(Task 4 report 표), `line.sharedMaterial` 사용(인스턴스 복제 없음). **감점**: SRP Batcher 실제 동작(Frame Debugger 등)은 측정하지 않음 — URP Lit 셰이더 선택에 근거한 추론뿐 |
| 최적화 | 5-4 불필요한 연산 제거 | 0.5 | 0.45 | 커서/스트로크는 이벤트 구동 갱신 유지, 신규 폴링 없음. **감점**: `LeftWall`/`RightWall`이 항상 카메라 시야 밖에 위치(Task 4 report 육안 확인) — frustum culling으로 런타임 렌더 비용은 0이지만, 애초에 시야 밖까지 덮는 룸 크기로 지오메트리를 배치한 것은 여유 마진 과다 |

### 총점: **9.05 / 10** (반올림 표기 9.1/10)

기능 1.70 + 성능 1.90 + 검증 1.65 + 코드품질 1.90 + 최적화 1.90 = **9.05**

### 판단 근거

- **회귀 없음이 실측으로 확인됐다**: W-2(`remoteStrokes=2 onCanvas=2 remoteCursors=1 players=4`, 스트로크 z=-0.0050=`surfaceOffset`), W-3(피어 이탈 `players 4→3 remoteStrokesPreserved=2`, 늦은 참가 `type=Welcome players=4 snapshot=2`), W-4(`cam.transform.InverseTransformPoint(strokePoint).z=5.0000`=`planeDistance`, `canvasSurface=null`) 전부 PASS — 위 §5 표, 원본 Task 5 report.
- **테스트는 늘었지만 커버리지 구멍이 남는다**: EditMode 81/81 실제 통과(Task 4 report)했고 pure-logic(`CanvasSurfaceLogic`, `HandScreenMapper`)은 잘 덮였지만, 실제 소비자 분기 3곳 중 2곳(`DrawingController`, `HandCursorController`)은 자동·수동 어느 검증에도 걸리지 않았다 — 이번 채점에서 가장 큰 단일 감점 사유(3-1 -0.30, 1-1 -0.10, 3-3 -0.05).
- **알려진 한계는 문서화됐지 해소되지 않았다**: aspect<1.30 캔버스 잘림(§4), Floor 오버행(Task4 Concern3), overlay 로비 UI가 캔버스 좌상단을 rect 기준 3.6% 덮지만 실제 글리프 가림은 0%이고 입력은 uGUI raycast를 거치지 않아 방해받지 않음(Task 5 "컨트롤러 추가 지시 1" 실측, 무감점 처리 — 영향이 실측으로 0에 수렴함이 확인됐기 때문).
- **lineWidth/minPointDistance 미조정은 감점하지 않았다**: 0.02 유지 결정은 화면 굵기 7.8px 대 2.3px(widthMultiplier 0.006) 육안 A/B 비교와 `minPointDistance`/`lineWidth` 비율 불변(0.5) 근거를 갖춘 검증된 판단이다(Task 5 "컨트롤러 추가 지시 2") — 추측이 아니라 증거 기반 결론이므로 무감점.

### 이 구현 방식을 선택한 이유

- **norm 좌표 단일 진실 원천 유지**(docs/10 §1): 네트워크 계층이 이미 [0,1] 정규화 좌표를 사용하므로, `CanvasSurface`는 "norm→월드" 매핑 함수 하나만 추가하고 프로토콜은 무수정으로 남겼다 — 최소 변경 표면.
- **optional 주입 + null 폴백**: 3개 소비자에 `canvasSurface`를 붙이되 미할당 시 기존 카메라 평면 경로를 그대로 타게 해 `NetplayTest.unity` 무회귀를 구조적으로 보장했다(W-4 PASS로 실증).
- **Quad `transform.localScale` = 캔버스 크기**: 별도 width/height 필드를 두지 않아 시각 크기와 매핑 수식이 어긋날 가능성 자체를 제거했다(docs/10 §2).

### 감점 요인 및 개선 방안

| 항목 | 감점 | 개선 방안 |
|---|---|---|
| 3-1/1-1/3-3: 로컬 드로잉 경로(`DrawingController`/`HandCursorController`의 `canvasSurface` 분기) 실행 검증 0건 | -0.30/-0.10/-0.05 | Play 모드에서 가짜 손 좌표(또는 fake_hand.py)로 핀치 이벤트를 발생시켜 `ToPlanePoint`/`UpdateHand` 분기를 실제로 태우는 `[UnityTest]` 또는 eval 검증 1회 추가. 다음으로 웹캠 연결 육안 확인(사용자) |
| 1-2: aspect<1.30 캔버스 잘림 | -0.15 | 카메라 거리를 좁히거나 FOV를 넓혀 최소 지원 aspect를 낮추거나, 세로 비율 대응이 필요 없다면 빌드 해상도 하한을 문서에 명시해 범위를 확정 |
| 1-3/4-4: `RemotePresenter` null 가드 누락이 초안에 있었음 | -0.05/-0.10 | 이미 수정 완료(`48db926`). 후속 Phase에서 optional 주입 패턴을 새로 추가할 때 "두 필드 모두 null 체크" 체크리스트 항목화 |
| 5-3/5-4: 배칭 실측·시야 밖 지오메트리 마진 | -0.05/-0.05 | Frame Debugger로 SRP Batching 실제 확인(선택), `LeftWall`/`RightWall` 크기를 카메라 FOV 기준으로 재계산해 축소(선택, 현재도 성능 영향 없음 — 낮은 우선순위) |

**총점 9.05(반올림 9.1)/10 ≥ 9.0 — 이번 Task 6 범위(문서 전용)에서 추가 코드 개선은 수행하지 않았다.** 표의 개선 방안은 후속 Task 판단용 기록이다.
