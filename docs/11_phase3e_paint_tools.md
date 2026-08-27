# 11. Phase 3e 설계 — 3D 이동 + 그림 도구 팔레트

> 작성 2026-08-27 · 상태: **3D 구현·실행 검증 완료, 기존 2D 입력 경로 보존 승인 대기**
> 전제: Phase 3d 완료 (docs/10, `Netplay3D.unity` 동작). 대상 씬 = `Netplay3D.unity` (신규 씬 없음).
> 위치: 3b 미니게임 프레임워크와 독립 — 입력·표현 계층만 바꾼다.

## 1. 목표와 범위

플레이어가 **3D 룸을 WASD로 돌아다니고**, 이젤 옆 **팔레트를 손으로 클릭해** 색·두께·브러시·지우개를 바꾸며 그린다.

핵심 통찰: 이번 요청의 본체는 기능 추가가 아니라 **입력 모델 교체**다. 현재는 손 norm 좌표가 카메라와 무관하게 캔버스에 직결(`CanvasSurface.NormToWorld`)돼 있어, 이동해도 그리는 위치가 따라오지 않고 팔레트를 겨냥할 수단이 없다. 이를 **카메라 레이캐스트 조준**으로 바꾸면 이동·팔레트 클릭·드로잉이 하나의 경로로 통일된다.

와이어 좌표계는 유지된다 — 레이 hit 지점을 캔버스 로컬로 역변환하면 그것이 곧 기존 norm [0,1]이다. 프로토콜은 **스타일 필드 추가분만** 바뀐다.

### 명시적 배제 (요청 시 별도 Phase)

RenderTexture 픽셀 페인팅(=부분 지우기, 채우기 도구), 3D 공간 드로잉(z 입력), 플레이어 아바타·원격 플레이어 표시, 실행 취소(Undo), 도구 상태의 네트워크 브로드캐스트(내가 무슨 도구를 들었는지 남에게 보이기), 외부 에셋, 기존 씬 3개(`NetplayTest`/`DrawingTest`/`HandTrackingTest`) 수정.

## 2. 아키텍처

### 신규 4개

```
Scripts/Input/HandPointer.cs      — 레이캐스트 조준 단독 소유 + 캔버스/도구 분기 (MonoBehaviour)
Scripts/Input/PlayerController.cs — WASD 이동 + 우클릭 홀드 마우스 룩 (MonoBehaviour)
Scripts/Drawing/ToolState.cs      — 색·두께·브러시·모드 보유 (MonoBehaviour)
Scripts/Drawing/ToolButton.cs     — 팔레트 버튼 1개의 종류·인덱스 (MonoBehaviour)
```

순수 로직은 기존 패턴(docs/04 §5)대로 `internal static` 클래스로 분리 — EditMode 테스트 대상:

| 순수 클래스 | 책임 |
|---|---|
| `CanvasSurfaceLogic.LocalToNorm` | `NormToLocal`의 역함수 (왕복 테스트로 검증) |
| `PointerRouteLogic.Decide` | (hit 종류 × 핀치 종류 × 그리는 중 여부) → 행동. `StrokeLogic.Decide`와 같은 형태 |
| `PlayerMoveLogic.Step` / `.ClampToRoom` | 입력·yaw → 이동 델타, 방 경계 clamp |
| `EraseLogic.HitsSegment` | 점-선분 최소거리 ≤ 반경 판정 (월드 단위) |

### 참조 방향 (docs/01 §4 유지)

```
HandPointer ──구독──> HandCursorController         (기존 핀치 이벤트)
HandPointer ──조회──> CanvasSurface                (norm 역변환)
DrawingController ──구독──> HandPointer            (캔버스 핀치)
DrawingController ──조회──> ToolState              (색·두께·브러시·모드)
NetSession ──구독──> HandPointer, DrawingController(지우개)
RemotePresenter ──구독──> NetSession               (기존)
ToolState ──구독──> HandPointer                    (도구 클릭)
```

`HandCursorController`/`CanvasSurface`/`ToolButton`은 소비자를 모른다. `NetSession`은 여전히 기존 컴포넌트를 **구독만** 한다.

### 기존 5개 수정

| 컴포넌트 | 변경 |
|---|---|
| `CanvasSurface` | `Vector2 WorldToNorm(Vector3 world)` 추가 (`NormToWorld` 유지 — 원격 재생이 계속 쓴다) |
| `HandCursorController` | `canvasSurface`/`projectionCamera` 필드와 투영 분기 **삭제**. 조준 방식에서 커서는 레이 원점인 screenPos에 있어야 한다. Phase 3d에서 추가됐으나 실행 검증된 적 없는 코드(docs/10 §7 -0.30 감점분)를 그대로 제거한다 |
| `DrawingController` | 입력원을 `HandCursorController` → `HandPointer`로 교체. 색·두께·브러시를 `ToolState`에서 읽는다(좌청/우주황 하드코딩 제거 — docs/08 §3이 "3b에서 재검토"로 남긴 항목이 여기서 해소된다). 지우개 모드 처리 + `localStrokeId` 발급 + `OnLocalStrokeErased` 이벤트 |
| `NetSession` | 입력원을 `HandCursorController`(핀치) → `HandPointer`로 교체(내부 screen→norm 변환 삭제, norm을 직접 받는다). 커서 송신은 `UdpHandReceiver` 패킷의 손바닥 좌표를 직접 읽는다. `StrokeStart`에 스타일 3필드 동봉, `StrokeErase` 송수신, `localStrokeId → 전역 strokeId` 매핑 |
| `RemotePresenter` | 수신 스타일(색·두께·브러시)을 적용해 렌더. `playerPalette` 폴백은 스타일 누락 시(구버전 스냅샷 등) 유지. `StrokeErase` 수신 시 해당 스트로크 파괴 |

### 조준 규칙

- `Physics.Raycast(aimCamera.ScreenPointToRay(screenPos), maxDistance)` 1회 → `ToolButton` 컴포넌트가 붙어 있으면 도구, `CanvasSurface`가 붙어 있으면 캔버스, 그 외/미스는 없음.
- **레이어 추가 없음** — 컴포넌트 유무로 구분한다(ProjectSettings 변경 회피).
- 캔버스에는 `MeshCollider`, 버튼에는 `BoxCollider`를 씬에서 붙인다. 현재 `Netplay3D.unity`에는 collider가 0개다.

### 지우개

LineRenderer는 벡터라 픽셀 단위 지우기가 원리적으로 불가능하다. **닿은 스트로크를 통째로 지운다**(Excalidraw·Jamboard 방식). 판정은 hit 월드 지점과 각 스트로크 점열 간 점-선분 거리(`EraseLogic`) — collider 불필요, 완료 스트로크만 대상.

### strokeId 소유권

지우개는 "어느 스트로크"를 지목해야 하는데 현재 `strokeId`는 `NetSession`만 발급한다(`NetProtocol.MakeStrokeId`). 참조 방향을 뒤집지 않기 위해:

- `DrawingController`가 **로컬 id**(단조 증가 int)를 발급해 스트로크에 보관하고, 지울 때 `OnLocalStrokeErased(int localId)`를 발행한다.
- `NetSession`은 `OnCanvasPinchStart` 시 자기 카운터로 전역 strokeId를 만들면서 `localId → strokeId` 매핑을 유지하고, 지우개 이벤트를 받아 `StrokeErase`로 변환 송신한다.
- **판정을 원격에서 재실행하지 않는다** — 부동소수·스트로크 목록 차이로 결과가 갈릴 수 있다. 지운 쪽이 id를 명시한다.

## 3. 프로토콜 (network v2)

`NetProtocol.Version = 1 → 2`. 필드만 추가하고 버전을 두면 구버전과 섞였을 때 **조용히 틀린 색**으로 그려진다 — 거부가 맞다. Steam 실검증(N-5)이 아직이라 실사용 빌드가 없는 지금이 올릴 타이밍이다.

| 메시지 | 변경 |
|---|---|
| `StrokeStart` | `+ int color` (packed 0xAARRGGBB), `+ float width` (월드 단위), `+ int brush` (브러시 인덱스) |
| `StrokeErase` | **신규**, reliable ordered. `{ strokeId }`. host 경유 rebroadcast가 정본 (`ClearCanvas`와 동일 취급) |
| `Welcome.snapshot` | `StrokeSnapshot`에 같은 3필드 추가. 지워진 스트로크는 스냅샷에서 제외 |

색을 팔레트 인덱스가 아닌 packed ARGB로 보내는 이유: 팔레트가 Inspector 배열이라 클라이언트 간 불일치 가능성이 있다. 구현은 Marker의 반투명도까지 원격에 보존하도록 알파를 포함한다.

## 4. 씬 — Netplay3D.unity 변경

- **Player 리그**: 신규 빈 오브젝트 `Player`(위치 = 기존 Camera 위치) 아래로 기존 `Camera`를 이동. `PlayerController` 부착. 기존 컴포넌트의 Camera 참조는 오브젝트 이동만으로 유지된다.
- **DrawCanvas**: `MeshCollider` 추가 (Quad mesh).
- **Palette**: 이젤 오른쪽에 트레이(Cube) + 버튼 큐브들. 색 6 · 두께 3 · 브러시 3 · 지우개 1 = **13개**. Clear 버튼은 만들지 않는다 — 로비 UI에 이미 있고(`NetplayUI.OnClickClear`, host 전용) 물리 버튼을 더하면 비호스트 무피드백 문제(docs/09 §3)만 늘어난다. 각각 `BoxCollider` + `ToolButton`. 선택 표시는 선택된 버튼의 로컬 z를 살짝 띄우는 방식(머티리얼 인스턴스 생성 회피).
- **Wiring**: `HandPointer`(cursorController·aimCamera·canvasSurface·toolState), `ToolState`(팔레트·두께·브러시 정의), `DrawingController`(handPointer·toolState), `NetSession`(handPointer 추가).
- 방 경계: 벽 안쪽 x [-5.5, 5.5], z [-8.75, 0.75] (현재 벽 좌표 기준. 실측 후 조정).

## 5. 엣지 케이스

| 상황 | 처리 |
|---|---|
| 핀치 중 레이가 캔버스를 벗어남 | 스트로크 **End** (캔버스 밖으로 선이 튀지 않게). 다시 들어오면 새 스트로크 |
| 핀치 Start가 버튼에 맞음 | 도구 변경만, 스트로크 시작 안 함 |
| 그리는 중 레이가 버튼 위를 지나감 | 무시 — 드래그 중에는 도구가 바뀌지 않는다 |
| 손 lost / 서버 단절 | 기존 계약대로 End 발행 (`HandCursorController`가 이미 보장) |
| 레이 미스(허공) | 아무 일 없음. 핀치 Start였다면 스트로크도 시작 안 함 |
| 두 손 동시 핀치 | 손별 독립 스트로크 유지(기존). 도구는 **공유** — 마지막 클릭이 양손에 적용 |
| 지우개로 아무것도 못 맞춤 | 무시(에러 아님) |
| 지우개가 진행 중 스트로크에 닿음 | 완료 스트로크만 대상 — 자기 손 아래 그리던 선은 지우지 않는다 |
| 캔버스 뒤로 걸어감 | Quad 단면이라 보이지도 맞지도 않는다 → 그릴 수 없음. 방어 코드 없음 |
| 마우스 룩 중 UI 클릭 | 우클릭 홀드 동안만 룩 — 커서 잠금 없음. 로비 버튼(Host/Clear)은 평소대로 좌클릭 |
| 구버전 클라이언트 접속 | `v` 불일치로 envelope 폐기 (기존 규칙) |

## 6. 테스트 / DoD

| # | 기준 | 확인 방법 | 결과 |
|---|---|---|---|
| P-1 | EditMode: `LocalToNorm` 왕복 · `PointerRouteLogic` 분기표 전건 · `PlayerMoveLogic` 경계 · `EraseLogic` 거리 판정 + 기존 전체 pass | `unity cmd --timeout 180 run_tests --mode EditMode` | PASS: 손바닥 조준 회귀 5건 포함 132/132, 실패 0 (2026-08-27) |
| P-2 | **로컬 드로잉 경로 실행 검증** — `fake_hand.py`로 캔버스 위 좌표에 핀치를 발생시켜 `HandPointer`→`DrawingController` 경로가 실제로 실행되고 스트로크가 캔버스 평면 위(z ≈ `surfaceOffset`)에 생김 | fake_hand + eval | PASS: 원 궤적 119 UDP 패킷, 9점 스트로크, local z=-0.005000 |
| P-3 | **팔레트 클릭** — `fake_hand.py` 고정 좌표 모드로 색·두께·브러시·지우개 버튼을 겨냥해 핀치 → `ToolState`가 바뀌고, 그 뒤 스트로크가 새 색·두께로 그려짐 | fake_hand + eval | PASS: Color_1 / Width_2 / Brush_1 / Eraser 실제 UDP 클릭. 새 선 width=0.099, alpha≈0.549, StrokeSoft |
| P-4 | **지우개** — 완료 스트로크에 지우개 핀치 → 해당 스트로크만 사라지고 나머지는 보존 | eval | PASS: 각 31점인 선 3개 중 가운데만 제거, 나머지 2개 instance ID 보존 |
| P-5 | **이동** — WASD 4방향 이동 + 우클릭 룩 후에도 캔버스를 겨냥하면 정상 드로잉. 방 경계 밖으로 못 나감 | eval + capture | PASS: Step 4방향 ±0.3m, clamp x±5.45/z[-8.75,0.65]. 가상 우클릭 yaw9.6°/pitch-3.6° 후 실제 UDP 31점 선, 모든 점 local z=-0.005 |
| P-6 | **Loopback 동기화** — 가짜 피어의 스트로크가 그쪽 색·두께로 재생되고, `StrokeErase` 수신 시 사라짐. 늦은 참가 스냅샷에 스타일 포함 | eval | PASS: 원격 RGB(0.2,0.8,0.333)/width0.04/brush2, 늦은 참가 스타일 보존. 중복 erase 2회 후 오브젝트·스냅샷에서 제거, 콘솔 error0 |
| P-7 | 2D `NetplayTest` 무회귀 (Loopback smoke, 콘솔 에러 0) | eval + capture | 보류: 기존 계획의 입력 필드 삭제와 2D 씬 무수정 조건이 충돌. 기존 2D 경로를 코드에 유지하는 방안 승인 대기 |
| P-8 | QUALITY_CHECKLIST ≥ 9.0 | 채점 보고 | 미채점: P-7과 최종 빌드/마감이 남아 전체 Phase 완료로 보고하지 않음 |

P-2/P-3은 docs/10 §7에서 가장 큰 감점(-0.30, "로컬 드로잉 경로 실행 검증 0건")을 낸 항목의 직접 해소다 — 이번 Phase에서는 **필수**다.

## 7. 구현 분담

전역 규칙 §3: 메인 세션은 계획·검수, 구현·검증은 subagent(sonnet/opus) 위임. Unity Editor 조작은 `unity cmd` — docs/09 §4 함정 목록을 프롬프트에 포함할 것.

구현 계획(Task 단위 프롬프트): `docs/superpowers/plans/2026-08-27-phase3e-paint-tools.md`

## 8. Codex 재개 기록 (2026-08-27)

- Claude Code는 Task 5의 Player 리그·MeshCollider 생성 직후 사용 한도로 중단됐다. Task 4 코드는 존재했지만 계획 체크박스는 미갱신이었다.
- 팔레트 13개, 공유 머티리얼, 라벨, 이동 경계 실측 및 입력/네트워크 참조 배선을 완료하고 `Netplay3D.unity`를 저장했다.
- 남아 있던 클릭 구독·선택 표시 누락을 `ToolState`에서 보완했다. `HandPointer`의 로컬 잉크는 raw hit 대신 `NormToWorld`의 표면 offset을 사용한다. 새 회귀 테스트 3건의 실패를 확인한 뒤 수정, 전체 127건 통과.
- `fake_hand.py --target x,y --pinch-hold N`을 추가했다. 원 궤적·빈 손·한 손 모드는 유지된다.
- 실행 증거는 로컬 `Logs/Phase3eResume/verified-results.md`, 테스트 원문은 같은 폴더의 `tests-red.txt` / `tests-green.txt`, 캡처는 `styled.png` / `moved.png` / `network.png`에 있다. `Logs/`는 gitignore 대상이다.
- 독립 시각 검토 2건 모두 3D 화면 범위 PASS. 팔레트 13개·라벨·로컬/원격 선을 캡처 3장으로 확인했다. 기존 이젤 왼쪽 지지대가 캔버스 하단 일부와 겹치는 비차단 관찰은 남겼다. 전체 Phase 품질 채점과는 별개다.
- `ProjectSettings/ProjectSettings.asset`, `URPProjectSettings.asset`, 기존 씬 3개는 재개 전 파일 해시와 일치한다. 기존 사용자 변경을 복원·정리하지 않았다.
- 검증 후 실제 웹캠 입력이 유입되어 합성 입력과 겹치는 것을 확인했다. 사용자의 현재 Play/웹캠 상태는 유지한다. 마지막 겹친 입력으로 찍힌 캡처는 통과 근거에서 제외한다.
- 손을 쥐고 펼 때 검지 끝이 이동하던 문제는 손목·MCP 4개의 평균을 로컬 조준과 네트워크 커서에 공통 적용해 수정했다(`e5136f5`). 좌우 손·손바닥 이동·송신 좌표 회귀 테스트 5건을 추가했으며 사용자가 실제 손 입력으로 동작을 확인했다. `fake_hand.py --target`도 같은 손바닥 기준을 사용한다.
- 사용자 요청으로 남은 3D 구현·씬·에셋·회귀 테스트를 `2772833`, 손바닥 고정 좌표 UDP 테스트 도구를 `26eb0e2`에 커밋했다. 커밋 전 전체 EditMode 132건 통과, 씬 누락 스크립트 0·필수 참조 할당·팔레트 버튼 13개·머티리얼 16개 셰이더 연결을 확인했다. 임시 검증 로그와 캡처는 커밋에서 제외했다.
- **남은 결정:** `NetplayTest`/`DrawingTest` 파일을 수정하지 않고 기존 2D 입력 경로를 코드에 유지할지 승인받은 뒤 P-7을 검증한다. 새 Windows 빌드와 최종 품질 채점은 미완료다. 실제 Steam 2인 검증(N-5)은 별도 환경이 필요하다. 현재 변경을 커밋하는 것은 이 미완료 항목들의 통과를 뜻하지 않는다.
