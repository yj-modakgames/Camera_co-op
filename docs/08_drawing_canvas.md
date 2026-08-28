# 08. 드로잉 캔버스와 로컬 보관

> 작성: 2026-08-28 · 상태: **Step 3 구현·자동 검증 반영 · 사용자 Play 대기**
> 대상 씬: `Assets/_CameraCoop/Scenes/RelayQuiz.unity` (신규 로컬 씬)
> 연계 문서: [06_player_controller.md](06_player_controller.md), [07_hand_interaction.md](07_hand_interaction.md), [09_relay_quiz_mode.md](09_relay_quiz_mode.md), [05_test_plan.md](05_test_plan.md)

## 1. 목표와 범위

기존 `CanvasSurface`와 `LineRenderer` 기반 평면 드로잉을 유지하면서, 로컬 릴레이 퀴즈에 필요한 완성 그림 보관·복원·실행 취소·갤러리 표시를 추가한다.

| 포함 | 제외 |
|---|---|
| 정규화 좌표 기반 직렬화 데이터 | 디스크 저장·불러오기 |
| 완성 스트로크 단위 Undo | Redo |
| 작업 캔버스 전체 Clear | 텍스처 페인팅·픽셀 지우기 |
| 메모리 내 저장·복원 | 자유 3D 드로잉 |
| 읽기 전용 관찰·갤러리 렌더링 | 네트워크 프로토콜·온라인 게임 변경 |

기존 온라인 씬, `NetSession`, `GameSession`은 이 Phase에서 수정하지 않는다. 기존 `StrokeSnapshot`은 필드 형태의 선례일 뿐이며 로컬 보관의 정본으로 사용하지 않는다.

## 2. 현재 구현과 변경 경계

### 2.1 Step 3 이전 기준 동작

- `CanvasSurface`는 좌상단 원점 `[0,1]` 좌표와 월드 평면을 상호 변환한다.
- `HandPointer`는 캔버스 hit를 `norm`과 월드 좌표로 변환해 기존 `OnCanvasStrokeStart/Move/End`를 발행한다.
- `DrawingController`는 손별 활성 스트로크와 완성 `LineRenderer`를 보유한다. 점 간격 필터와 순간이동 분할 뒤의 월드 점만 로컬 화면에 남는다.
- 스타일은 스트로크 시작 시 `ToolState`에서 읽어 고정한다.
- 기존 `ClearAll()`은 활성·완성 렌더 오브젝트를 모두 파괴한다. Undo, export, load, archive는 없다.

### 2.2 제안 변경 원칙

`DrawingController`가 **필터·분할을 통과해 실제로 완성된 로컬 스트로크**의 정본을 소유한다. `LineRenderer`는 그 정본의 표현이며 데이터 원본이 아니다. 네트워크 계층이 별도로 모은 raw 점열이나 `LineRenderer.GetPositions()`에서 보관 데이터를 역산하지 않는다.

로컬 보관 경로의 명시적 활성 조건은 연결된 `HandPointer.InputSource == HandRouter`다. 이 경로에서는 canvasSurface가 필수이며 누락 시 입력을 중단하고 오류를 보고한다. LegacyCursorEvents는 기존 world 점·렌더 경로와 ClearAll 의미를 유지하고 normalized 폭 계산·보관 API를 사용하지 않는다. 보관 API의 호출자는 로컬 controller로 제한한다.

작업 캔버스 데이터와 보관 데이터는 분리한다.

- 작업 데이터: 현재 `DrawingController`가 수정할 수 있는 스트로크 집합
- 보관 데이터: `RelayTurnRecord.drawing`에 깊은 복사된 `CanvasDrawingData`
- 프리뷰 데이터: 보관 데이터를 읽어 만든 `CanvasDrawingPresenter`의 렌더 오브젝트

작업 캔버스 Clear, Undo, 지우개는 보관 데이터와 프리뷰를 바꾸지 않는다.

## 3. 데이터 계약

아래 타입은 제안 API다. 모든 배열은 경계를 넘을 때 깊은 복사하고 Unity 오브젝트 참조를 포함하지 않는다.

| 타입 | 필드 | 계약 |
|---|---|---|
| `CanvasStrokeData` | `strokeId int` | 한 작업 컨트롤러 안에서 단조 증가하는 양수 식별자 |
|  | `order int` | 실제 스트로크 시작 시 부여하는 단조 증가 렌더·Undo 순서 |
|  | `xy float[]` | `(x,y)` 쌍을 평탄화한 좌상단 원점 `[0,1]` 좌표 |
|  | `colorArgb int` | `0xAARRGGBB` |
|  | `widthNormalized float` | `worldWidth / 캔버스의 더 짧은 월드 공간 변 길이` |
|  | `brushId int` | 로컬 브러시 정의 인덱스 |
| `CanvasDrawingData` | `version int` | 현재 `1` |
|  | `strokes CanvasStrokeData[]` | `order` 오름차순 완성 스트로크의 깊은 복사 |

`RelayTurnRecord`는 [09_relay_quiz_mode.md](09_relay_quiz_mode.md)가 소유하며 다음 공통 계약을 따른다.

| 필드 | 계약 |
|---|---|
| `playerIndex int` | 0-based |
| `drawingIndex int` | 0-based |
| `drawing CanvasDrawingData` | export 시점 완성본의 깊은 복사 |

`CanvasDrawingData`는 `[Serializable]` 데이터일 뿐 `GameObject`, `Transform`, `Material`, `LineRenderer`, `CanvasSurface` 참조를 갖지 않는다. 빈 그림은 `version=1`, 길이 0의 `strokes`로 표현한다.

### 3.1 스타일과 크기

- 색·폭·브러시는 스트로크 시작 시 고정한다. 그리는 도중 팔레트가 바뀌어도 활성 스트로크 스타일은 변하지 않는다.
- 순간이동 분할로 새 스트로크가 시작되면 새 `strokeId`와 `order`를 부여하고 그 시점의 스타일을 다시 고정한다.
- 시작 시 캔버스의 월드 X·Y 변 길이 중 작은 값을 기준으로 `widthNormalized`를 고정하고 export는 그 값을 그대로 복사한다.
- load·preview 시 대상 `CanvasSurface`의 같은 짧은 변 길이를 곱해 월드 폭을 복원한다.
- 작업·관찰·갤러리 surface는 동일 종횡비를 사용하고 표시 크기만 바꾼다.

### 3.2 순서와 식별자

`strokeId`와 `order`는 스트로크 시작 시 증가하며 Clear나 Undo 뒤에도 현재 `DrawingController` 수명 동안 재사용하지 않는다. load 뒤 새 값은 로드된 최댓값과 기존 high-water mark보다 커야 한다. 두 손이 동시에 그려도 시작 순서가 전체 순서를 결정한다.

점 2개 미만 스트로크는 현재 규칙대로 종료 시 폐기한다. 폐기된 id와 order는 재사용하지 않으며 export에 포함하지 않는다.

## 4. `DrawingController` 제안 API

아래 표의 기존·제안 구분은 현재 코드와 설계를 혼동하지 않기 위한 것이다.

| API | 상태 | 계약 |
|---|---|---|
| `ClearAll()` | 기존, 로컬 동작 확장 제안 | 로컬에서는 활성 스트로크를 마무리하고 작업 데이터·렌더만 비운다. 보관본은 불변이다. Legacy의 기존 제거 동작은 유지한다. |
| `FinalizeActiveStrokes()` | 제안 | 모든 손의 활성 스트로크를 멱등적으로 종료한다. 1점 스트로크는 폐기하고 나머지는 완성 목록으로 옮긴다. |
| `ExportDrawing()` | 제안 | 먼저 `FinalizeActiveStrokes()`를 멱등 호출한 뒤 완성본 전체를 `order` 순으로 깊은 복사한다. UI나 입력 라우터를 참조하지 않는다. |
| `LoadDrawing(CanvasDrawingData)` | 제안 | 전체 입력을 먼저 검증·깊은 복사한다. 성공 시 활성 스트로크를 마무리한 뒤 작업 캔버스를 원자적으로 교체하고 렌더를 재생성한다. 보관 원본을 참조하지 않는다. |
| `UndoLastStroke()` | 제안 | 활성 스트로크를 방어적으로 마무리한 뒤 완성 스트로크 중 가장 큰 `order` 하나와 그 렌더를 제거한다. |

반환형은 FinalizeActiveStrokes·ClearAll은 void, ExportDrawing은 CanvasDrawingData, LoadDrawing·UndoLastStroke는 bool이다. Load 거부·지울 선이 없는 Undo는 false다. Clear의 실행 취소와 Redo는 지원하지 않는다. 표의 보관 API는 유효하게 배선된 HandRouter 로컬 경로를 전제로 한다.

### 4.1 활성 스트로크 처리

정상 해제, 캔버스 이탈, hand lost, 입력 모드·턴 context 변경은 해당 스트로크를 종료하고 fresh pinch 전에는 재개하지 않는다. 이는 [07_hand_interaction.md](07_hand_interaction.md)의 canvas capture 규칙과 동일하다.

턴 종료·Undo·Clear 명령은 호출 측에서 다음 순서를 지킨다.

1. `HandInputRouter.CancelCanvasCaptures(reason)`으로 모든 canvas capture를 끝내고 해당 손을 재무장 대기로 둔다. 재무장 소유자는 Router이며 docs/07의 fresh open 0.10초·새 유효 샘플 2개 이상 조건을 따른다.
2. `DrawingController.FinalizeActiveStrokes()`를 호출해 남은 활성 상태를 멱등 정리한다.
3. 턴 종료는 `ExportDrawing()`, Undo는 `UndoLastStroke()`, Clear는 `ClearAll()`을 호출한다.

`ExportDrawing()` 자체는 2번을 다시 수행하므로 입력과 무관하게 안정된 완성본을 반환한다. 그러나 capture 취소는 UI·입력 context 책임이므로 export가 `HandInputRouter`를 역참조하지 않는다.

Undo 또는 Clear를 누른 손과 동시에 다른 손이 그리는 경우에도 위 순서로 **모든 손**을 먼저 마무리한다. Undo는 그 결과 중 최대 `order` 하나를 제거한다. Clear는 그 결과를 포함한 작업 전체를 비운다. 두 명령 뒤에는 held Move를 무시하고 docs/07의 재무장 조건을 충족한 새 pinch만 허용한다.

### 4.2 Load 검증과 원자성

load는 기존 작업 데이터를 지우기 전에 다음을 전부 확인한다.

- `version == 1`
- `strokes`와 각 `xy`가 null이 아니며 `xy.Length`가 4 이상인 짝수
- 좌표와 `widthNormalized`가 유한하며 좌표가 `[0,1]`, 폭이 양수
- `strokeId > 0`, `order >= 0`이며 그림 안에서 각각 중복되지 않음
- `brushId`가 로컬 브러시 정의 범위 안임

검증 실패 시 기존 작업·렌더를 그대로 유지하고 오류를 기록하며 load를 거부한다. 부분 load는 하지 않는다. 성공하면 입력 배열을 깊은 복사하고 `order` 오름차순으로 렌더를 만든다. 손 UI에서 load할 때도 호출 측이 먼저 canvas capture를 취소하며, `LoadDrawing()`은 입력 라우터를 참조하지 않는다.

## 5. 입력 연결

로컬 씬의 새 경로는 [07_hand_interaction.md](07_hand_interaction.md)를 따른다.

```text
HandInputRouter
  -> HandCanvasInteractable
  -> HandPointer.BeginCanvasStroke / MoveCanvasStroke / EndCanvasStroke
  -> 기존 OnCanvasStrokeStart / Move / End
  -> DrawingController
```

- 새 로컬 `HandPointer.inputSource`는 `HandRouter`다. 기존 온라인 씬의 기본값은 `LegacyCursorEvents`로 유지한다.
- `HandRouter` 모드에서는 legacy `HandCursorController` pinch 구독을 끄고 중복 스트로크를 막는다.
- `HandCanvasInteractable`은 실제 hit한 `CanvasSurface`와 normalized position을 전달한다. 제안 접점은 `BeginCanvasStroke(handedness, surface, normalizedPosition)`, `MoveCanvasStroke(handedness, surface, normalizedPosition)`, `EndCanvasStroke(handedness)`다.
- 로컬 입력 경로는 `InputModeManager.CanDraw`가 true이고 대상 surface가 현재 writable surface일 때만 시작·이동을 허용한다. capture 도중 다른 surface로 바꾸지 않는다.
- `ToolState.Mode.Erase`의 Start/Move는 기존 `OnCanvasErase` 경로를 사용한다. 새 그리기 스트로크를 만들지 않으며 완료 스트로크 하나를 통째로 지우는 규칙을 유지한다.
- 로컬 씬에는 `GameSession`과 `NetSession`이 없다. 온라인 `StrokesEnabled`/`StrokeGate` 경로는 수정하지 않는다.
- 팔레트는 overlay의 `HandButtonInteractable`을 통해 기존 `ToolState`를 재사용한다. 색·두께·브러시·지우개 선택 상태는 작업 컨트롤러가 새 스트로크를 시작할 때만 복사한다.
- 버튼 press가 tracking loss, context 변경, 대상 이탈로 취소되면 action을 실행하지 않는다.

로컬 씬의 `DrawingController.clearKey`는 `Key.None`으로 배선한다. 팔레트·Undo·Clear는 손 UI로만 조작하며 C 단축키는 쓰지 않는다.

## 6. 정본과 렌더 동기화

로컬 완성 스트로크마다 normalized 점열, 고정 스타일, id, order와 대응 `LineRenderer`를 함께 관리한다. 재생 시에도 order로 정렬한 표시 인덱스를 sortingOrder에 적용한다. 겹치는 선·반투명 브러시의 실제 표시 순서는 원본과 갤러리를 사용자 Play로 비교한다.

| 동작 | 정본 데이터 | 작업 렌더 | 보관 데이터 |
|---|---|---|---|
| Begin/Move | 필터 통과 점만 활성 데이터에 추가 | 같은 점을 즉시 표시 | 변경 없음 |
| End | 유효 스트로크를 완성 목록으로 이동 | 유지 | 변경 없음 |
| 지우개 | hit한 완성 스트로크 하나 제거 | 같은 렌더 제거 | 변경 없음 |
| Undo | 최대 order 완성 스트로크 제거 | 같은 렌더 제거 | 변경 없음 |
| Clear | 작업 완성·활성 목록 제거 | 작업 렌더 제거 | 변경 없음 |
| Export | 활성 종료 후 깊은 복사 생성 | 종료 처리 뒤 유지, 1점은 폐기 | 호출자가 반환된 독립 완성본을 보관 |
| Load | 검증된 깊은 복사로 작업 목록 교체 | 전부 재생성 | 입력 원본 변경 없음 |

기존 `OnLocalStrokeStarted`와 `OnLocalStrokeErased`는 온라인 호환을 위해 유지한다. 새 로컬 보관 기능은 `NetSession` 이벤트나 전역 network stroke id에 의존하지 않는다.

## 7. 읽기 전용 프리뷰와 갤러리

제안 `CanvasDrawingPresenter`는 보관 데이터를 읽어 별도 `LineRenderer`를 만드는 표현 전용 컴포넌트다.

| API | 계약 |
|---|---|
| `Show(CanvasDrawingData, CanvasSurface)` | 기존 presentation을 교체하고 대상 surface에 order 순으로 렌더한다. 입력 데이터를 수정하지 않는다. |
| `Hide()` | 생성된 표현만 숨긴다. archive와 생성 데이터는 유지한다. |
| `ClearPresentation()` | presenter가 만든 렌더 오브젝트만 파괴한다. archive는 유지한다. |

presenter는 작업 `DrawingController`, `ToolState`, `HandPointer`를 참조하지 않고 편집·지우개·Undo API를 제공하지 않는다. 렌더에 필요한 색·폭·브러시 값만 소비하며 입력 데이터 배열을 보관 원본으로 취급하거나 변경하지 않는다.

관찰 단계가 끝나면 `Hide()` 또는 `ClearPresentation()`으로 이전 그림을 가리고 작업 캔버스는 별도로 비운다. 이전 그림을 숨기는 행위와 `ClearAll()`은 서로 다른 책임이다.

갤러리는 플레이어 수 N에 대해 N-1개의 읽기 전용 world canvas를 사용한다. 각 canvas는 표시용 `CanvasSurface`만 가지며 `HandCanvasInteractable`을 붙이지 않는다. collider와 writable raycast target도 두지 않아 손 입력이 갤러리 그림을 수정하거나 활성 작업 surface를 가로채지 못하게 한다. 표시할 `RelayTurnRecord` 선택과 canvas 배치는 [09_relay_quiz_mode.md](09_relay_quiz_mode.md)가 소유한다.

## 8. Inspector 배선

### 8.1 작업 캔버스

| 컴포넌트 | 필드 | RelayQuiz 배선 |
|---|---|---|
| `CanvasSurface` | `surfaceOffset` | 기존 기본값 유지 |
| `DrawingController` | `handPointer` | 로컬 `HandPointer` |
|  | `toolState` | overlay 팔레트의 공유 `ToolState` |
|  | 제안 `canvasSurface` | 유일한 active writable surface |
|  | `minPointDistance`, `maxSegmentWorldDistance`, `lineMaterial` | 기존 값·머티리얼 재사용 |
|  | `clearKey` | `Key.None` |
| `HandCanvasInteractable` | target surface | 위 active writable `CanvasSurface` |
| `HandPointer` | `inputSource` | `HandRouter` |
| `InputModeManager` | drawing permission | 현재 phase·mode로 `CanDraw` 산출 |

제안 `canvasSurface`는 시작 시 폭 정규화와 load 재생 대상이다. 기존 온라인 씬에서는 optional로 유지해 현재 world 좌표 이벤트 경로를 깨지 않는다.

### 8.2 프리뷰·갤러리

| 컴포넌트 | 필드 | 배선 |
|---|---|---|
| `CanvasDrawingPresenter` | 제안 `lineMaterial` | 기존 stroke material fallback 재사용 |
|  | 제안 `brushMaterials` | `brushId` 순서와 동일한 읽기 전용 배열 |
| `CanvasSurface` | transform | 각 preview/gallery quad의 크기·위치 |

## 9. 계획 파일 소유권

| 경로 | 변경 | 이 문서의 책임 |
|---|---|---|
| `Assets/_CameraCoop/Scripts/Drawing/CanvasDrawingData.cs` | 신규 | `CanvasStrokeData`, `CanvasDrawingData` 계약·검증·깊은 복사 경계 |
| `Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs` | 수정 | 정본 normalized 데이터, finalize/export/load/undo/clear와 작업 렌더 동기화 |
| `Assets/_CameraCoop/Scripts/Drawing/ToolState.cs` | 수정 | Overlay 선택 표시용 CurrentColorIndex·CurrentWidthIndex 읽기 전용 속성; 기존 Apply 유지 |
| `Assets/_CameraCoop/Scripts/Drawing/CanvasDrawingPresenter.cs` | 신규 | 읽기 전용 preview/gallery 렌더 |
| `Assets/_CameraCoop/Tests/EditMode/DrawingTests.cs` | 수정 | 데이터·순서·활성 종료·Undo·Clear·load 원자성 회귀 |

입력 파일은 [07_hand_interaction.md](07_hand_interaction.md), 플레이어·mode 파일은 [06_player_controller.md](06_player_controller.md), `RelayTurnRecord`와 `RelayQuiz.unity` 조립은 [09_relay_quiz_mode.md](09_relay_quiz_mode.md)가 소유한다. 이 문서는 그 파일의 대체 구현이나 네트워크 변경을 정의하지 않는다.

## 10. Step 3 완료 판정 기준

아래 항목은 구현 후 검증할 기준이며 현재 통과로 표시하지 않는다. 전체 실행 절차와 증거 형식은 [05_test_plan.md](05_test_plan.md)를 따른다.

| # | 기준 | 확인 방법 |
|---|---|---|
| D-1 | Begin/Move/End와 양손 동시 입력이 필터·분할 후 normalized 정본과 같은 선을 만든다 | EditMode + 로컬 수동 |
| D-2 | 스타일이 시작 시 고정되고 폭이 서로 다른 크기의 surface에서 짧은 변 기준으로 동일 비율 복원된다 | EditMode |
| D-3 | `ExportDrawing()`이 모든 활성 스트로크를 마무리하고 모든 중첩 배열을 깊은 복사한다 | EditMode |
| D-4 | `LoadDrawing()`이 export 결과를 동일 순서·좌표·스타일로 복원하며 잘못된 입력은 기존 작업을 바꾸지 않는다 | EditMode |
| D-5 | Undo가 모든 활성 손을 종료한 뒤 최대 order 완성 스트로크 하나만 제거하고 fresh pinch를 요구한다 | EditMode + 로컬 수동 |
| D-6 | Clear가 작업 데이터·렌더만 제거하고 이미 만든 `RelayTurnRecord.drawing`을 바꾸지 않는다 | EditMode |
| D-7 | 지우개, Undo, Clear 뒤 정본 데이터와 `LineRenderer` 개수·식별자가 일치한다 | EditMode |
| D-8 | 프리뷰 Show/Hide/ClearPresentation과 N-1 갤러리가 archive를 변경하지 않으며 손 raycast 대상이 아니다 | 씬 정적 검사 + 로컬 수동 |
| D-9 | `LegacyCursorEvents` 동작을 보존하고 기존 온라인 씬·`NetSession`·`GameSession` 파일에 변경이 없다 | diff + 기존 회귀 계획 |
| D-10 | `RelayQuiz`에서 C가 Clear를 일으키지 않고 팔레트·Undo·Clear가 손 UI로만 동작한다 | Inspector 검사 + 로컬 수동 |

## 11. 리스크와 비범위 확인

- 스트로크당 `LineRenderer` 구조이므로 긴 세션에서 오브젝트 수가 증가한다. Step 3에서 실제 N-1 갤러리 최대량을 측정하고, 문제 확인 전 texture path를 추가하지 않는다.
- `brushId`는 로컬 Inspector 배열 순서에 의존한다. RelayQuiz 작업·preview presenter의 배열 순서를 동일하게 배선하고 정적 검사한다.
- `CanvasSurface`의 비균일 scale은 좌표에는 반영되지만 폭은 짧은 변 기준 단일 값이다. 이는 이 Phase의 명시적 규칙이다.
- 보관 데이터의 런타임 수명은 로컬 게임 세션까지다. 앱 종료 뒤 복구는 지원하지 않는다.
- 기존 온라인 snapshot, Clear, eraser 동기화는 변경하지 않으며 이 문서의 완료 조건에 포함하지 않는다.

## 12. Step 3 구현과 시험 화면

- 위 데이터·controller·presenter API를 구현했다. 최종 전체 EditMode는 484건 중 480통과·기존 레이캐스트 4실패다. 새 48건은 모두 통과했고, DrawingTests는 51/51이다. 상세·Play 절차는 [검증 기록](05_test_plan.md#7-4-step-3-실행-기록--2026-08-28)에 있다.
- `HandDrawingWorkspace`가 이번 시험용 보관본을 소유한다. `RelayTurnRecord`·턴 진행은 Step 4에서 추가하며 현재 선구현하지 않았다. 디스크 저장은 없다.
- 작업 명령은 capture 취소 → 활성 선 종료 → Undo/Clear/Export/Load 순서다. 프리뷰 숨김은 작업과 보관 데이터를 변경하지 않는다.
- 작업 Quad는 3.2×2.133333의 3:2 surface다. 오른쪽 보관 프리뷰는 같은 비율이며 `previewCamera`와 overlay `previewViewport`의 명시적 참조로 위치·크기를 계산한다. 해상도 변경 시 보관 데이터에서 표현만 다시 만든다.
- `HandDrawingWorkspace`의 Router·DrawingController·Presenter·previewSurface·previewCamera·previewViewport·5개 HandButton·2개 Text를 Inspector에 연결했다. 팔레트는 공유 ToolState와 각 ToolButton/선택 표시를 연결했다.
- preview와 GalleryCanvas1~3에는 collider·HandCanvasInteractable이 없다. 갤러리 슬롯만 준비됐으며 실제 N-1 그림 선택·턴별 배치는 Step 4 범위다.
- 정적 Full HD 정상 프레임은 확인했지만 편집 모드 저장 뒤 글자 누락·깨짐이 재현됐다. 실제 손 입력·겹침 표현·다른 해상도·안정적인 한글 표시는 사용자 Play로 확인해야 한다.
