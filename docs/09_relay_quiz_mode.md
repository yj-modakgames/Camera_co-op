# 09. 로컬 기억 릴레이 퀴즈 모드 설계

> 작성: 2026-08-28 · Phase D 설계 초안 · **구현 승인 대기**
> 대상: 한 화면을 차례로 사용하는 로컬 2~4인 · 전용 씬 `Assets/_CameraCoop/Scenes/RelayQuiz.unity`

> 현재 online 4인 entry는 이 local mode와 별개인 `Assets/_CameraCoop/Scenes/RelayQuizOnline.unity`이며, `RelayCopy`/`MemoryCopy`/`CoopMural`은 [15_3d_party_game_design.md](15_3d_party_game_design.md)와 [17_player_game_guide.md](17_player_game_guide.md)의 additive Scene 계약을 따른다.
> 입력·플레이어·캔버스 공통 계약: [docs/06](./06_player_controller.md), [docs/07](./07_hand_interaction.md), [docs/08](./08_drawing_canvas.md)

## 1. 목표와 범위

플레이어 2~4명이 물리적으로 합의한 고정 순서 `player1..N`으로 한 화면을 넘겨받아 기억 릴레이를 진행한다. 키보드 이름 설정은 없으며 화면 표시는 `플레이어 1..N`이다.

1. 첫 플레이어만 제시어를 본 뒤 빈 캔버스에 그린다.
2. 중간 플레이어는 직전 그림을 잠깐 보고, 그림이 완전히 가려진 빈 캔버스에 기억으로 다시 그린다.
3. 마지막 플레이어는 직전 그림을 보면서 답을 입력한다.
4. 결과에서 정답을 공개하고, 갤러리에서 `N-1`개 그림을 작성 순서대로 함께 본다.

2인 게임은 중간 관찰·재그리기 단계를 건너뛴다. 첫 플레이어의 그림 1개를 보존한 뒤 두 번째 플레이어가 바로 답을 입력한다.

### 1-1. 보존 경계

- 기존 온라인 `GuessGameLogic.GameMode.Relay`, `GameSession`, `NetSession`, 전송 프로토콜과 기존 씬은 의미와 동작을 바꾸지 않는다.
- 로컬 씬에는 network transport, `NetSession`, `GameSession`을 두지 않는다.
- 기존 `Assets/_CameraCoop/Data/words_ko.txt` 180개 단어와 이를 사용하는 온라인 경로는 수정하지 않는다.
- 온라인 동기화, 재접속, 원격 개인정보 보호, protocol version 변경은 이 Phase의 범위 밖이다.

## 2. 핵심 게임 규칙

| 항목 | 규칙 |
|---|---|
| 인원 | 2~4명. `Setup`에서 손 버튼으로 선택하며 시작 뒤 순서는 바뀌지 않는다. |
| 제시어 | 첫 플레이어에게만 5초 표시한다. 이후 결과 공개 전까지 어떤 화면·그림 기록에도 넣지 않는다. |
| 그리기 | 플레이어당 60초. 손 `그림 완료` 버튼으로 일찍 끝낼 수 있다. |
| 관찰 | 중간 플레이어에게 직전 그림만 5초 표시한다. 종료 즉시 실제 렌더와 입력 표면을 숨긴 뒤 빈 캔버스로 전환한다. |
| 답변 | 마지막 플레이어에게 직전 그림을 유지해 보여 주고 30초 동안 답을 편집하게 한다. |
| 판정 | `GuessJudge`와 같은 규칙: 양끝을 포함한 모든 whitespace 제거, invariant lowercase, 완전 일치. fuzzy match와 오타 허용은 없다. |
| 결과 | 제출 또는 답변 시간 만료 뒤 정답·입력값·정오를 공개한다. 자동으로 넘어가지 않고 손 `갤러리` 버튼을 기다린다. |
| 갤러리 | `N-1`개 불변 그림을 순서대로 나란히 표시한다. 손 `다시 시작` 버튼으로만 새 세션을 연다. |

Inspector 기본값은 `wordRevealSeconds=5`, `drawingSeconds=60`, `observeSeconds=5`, `guessSeconds=30`이다. `Reveal`에는 자동 종료 시간이 없다.

## 3. 상태와 화면 계약

상태 이름은 `Setup`, `Handover`, `WordReveal`, `Drawing`, `ObservePrevious`, `Guessing`, `Reveal`, `Gallery`로 고정한다. 별도 `Pause` 상태는 추가하지 않고 현재 상태, 남은 시간과 `paused` flag를 함께 보존한다.

| 상태 | 보이는 UI·콘텐츠 | 시간 | `InputContext` | 허용 진행 |
|---|---|---:|---|---|
| `Setup` | 인원 선택, 손 `시작` 버튼 | 없음 | `UiOnly` | `Start` → `Handover(player0)` |
| `Handover` | 화면 전체 불투명 방패, 현재 플레이어 번호, 손 `준비` 버튼 | 없음 | `UiOnly` | 준비 pinch 후 release |
| `WordReveal` | 제시어와 남은 시간. 그림·이전 화면은 숨김 | 5초 | `UiOnly` | 만료 → 첫 `Drawing` |
| `Drawing` | 빈 active canvas, 도구 UI, 남은 시간, 손 `그림 완료` | 60초 | `Drawing` | 완료·만료 → archive 후 다음 `Handover` |
| `ObservePrevious` | 직전 그림 한 장, 남은 시간. 쓰기 표면 없음 | 5초 | `UiOnly` | 만료 → 숨김·빈 `Drawing` |
| `Guessing` | 직전 그림, 답 입력창, 남은 시간, 손 `입력 포커스`·`제출` | 30초 | `UiOnly` | 제출·만료 → `Reveal` |
| `Reveal` | 정답, 입력값, 정오, 손 `갤러리` 버튼 | 없음 | `UiOnly` | 손 버튼 → `Gallery` |
| `Gallery` | 모든 그림을 순서대로 나란히, 손 `다시 시작` | 없음 | `Explore` | 다시 시작 → 안전 reset 후 `Setup` |

각 상태의 overlay root는 해당 상태에서만 active다. `InputModeManager.SetContext(InputContext)`와 공개 권한 `CanMove`, `CanLook`, `CanUseHandUi`, `CanDraw`, `CanToggleMode`의 단일 계산 규칙은 [docs/06](./06_player_controller.md)을 따른다. 카메라 패널은 이 일반 입력 계약의 전용 예외지만, 카메라 control이 available이고 app focus와 `Interact`가 모두 성립해야 한다. `Blocked`에서는 여기에 더해 카메라가 수신 중이 아닌 준비 상태일 때만 왼쪽 클릭을 받는다.

- `Explore`에서는 `Tab`으로 Move/Interact를 바꿀 수 있다. `WASD` 이동은 `Gallery`의 Move일 때만 가능하다.
- `UiOnly`는 Interact를 강제하고 현재 활성 UI만 허용한다.
- `Drawing`은 Interact와 active canvas·현재 도구 UI만 허용한다.
- `Blocked`는 일반 입력 권한을 모두 끈다.
- 키보드는 `WASD`, Explore의 mode key, 답변 편집에만 사용한다. 시작·준비·완료·포커스·제출·갤러리·재시작은 모두 손 버튼 전용이다. 카메라 시작·재시도·종료만 별도 패널의 왼쪽 클릭 예외다.

## 4. 정상 전이

### 4-1. 시작과 첫 그림

1. `Setup`의 손 `시작`은 이전 답·그림·선택·capture를 reset하고 `playerIndex=0`인 `Handover`로 간다.
2. 준비된 손이 `준비`를 pinch한 뒤 release해야 `WordReveal`로 간다. held pinch만으로 다음 화면을 열지 않는다.
3. 5초 뒤 제시어 render를 먼저 숨기고 빈 canvas를 만든 뒤 첫 `Drawing`을 허용한다.

### 4-2. 중간 플레이어

1. `Drawing` 완료·만료는 drawing input과 모든 canvas capture를 먼저 끈 뒤 active stroke를 확정하고 deep-copy 그림을 한 번만 archive한다.
2. 불투명 방패를 올린 뒤 `playerIndex`를 증가시켜 `Handover`로 간다.
3. 마지막이 아닌 플레이어의 준비 release는 `ObservePrevious`를 연다.
4. 5초 뒤 직전 그림의 실제 presentation을 숨기고 방패를 유지한 채 빈 canvas를 만든 다음 방패를 내려 `Drawing`을 연다.

### 4-3. 마지막 플레이어와 결과

1. 마지막 플레이어의 준비 release는 `Guessing`을 연다. 직전 그림은 계속 보이지만 제시어를 render나 UI 표시 상태에 넣지 않는다. 판정용 secret은 순수 게임 상태에 별도로 보존한다.
2. 답 입력창은 손 `입력 포커스`로만 focus한다. 키보드는 글자 편집과 IME 조합에만 쓰며 Enter가 제출 버튼을 대신하지 않는다.
3. 손 `제출` 또는 30초 만료가 현재 답을 판정해 `Reveal`로 간다. 빈 답은 오답이다.
4. 손 `갤러리`는 `Gallery`를 열고, 손 `다시 시작`은 그림 presentation·archive·답·제시어·timer·capture를 지우고 `paused=false`, player index 초기화와 generation 갱신 뒤 `Setup`으로 간다.

## 5. 전이 원자성·중복 방지

`RelayQuizLogic`은 `Tick(deltaSeconds)`로만 시간을 받는다. RelayQuizController의 LateUpdate가 프레임당 한 번 unscaled delta를 전달한다. wall clock으로 남은 시간을 다시 계산하지 않으며 pause 동안 `Tick`에 게임 시간을 더하지 않는다. 새 상태로 전이한 프레임의 남은 delta를 새 타이머에 다시 적용하지 않는다.

모든 상태·화면 경계마다 증가하는 `phaseGeneration` token을 둔다. 손 action은 capture를 시작한 generation을 함께 전달하며 현재 token과 다르면 무시한다. 완료가 승인되면 token을 먼저 증가시켜 같은 프레임의 두 번째 완료와 이전 화면의 늦은 release를 폐기한다.

controller는 새 화면을 활성화하기 전에 `HandInputRouter.SetViewGeneration(phaseGeneration)`을 호출한다. HandButtonInteractable의 `OnHandClick(HandClickContext)`에 담긴 down 시점 viewGeneration을 RelayQuizUI가 action의 captureGeneration으로 복사해 큐잉한다. Button.onClick에 별도 게임 콜백을 연결하지 않는다. LateUpdate에서 이 큐를 소비하며 현재 세대·상태에서 허용된 action인지 다시 검사한다. pause 화면 진입·이탈도 세대를 갱신하지만 게임 상태·남은 시간은 유지한다.

프레임 처리는 다음 순서를 지킨다.

1. app focus를 확인하고, Drawing에서는 Router.HasFreshHand도 확인해 필요한 경우 먼저 `Blocked`·pause로 전환한다.
2. 현재 generation의 유효한 손 action을 처리한다.
3. 아직 전이하지 않았을 때만 timer 만료를 처리한다.
4. 전이 시 이전 입력을 끄고 capture를 취소한 뒤 secret·그림의 실제 render를 숨긴다.
5. 필요한 snapshot load·canvas clear·overlay 교체를 끝낸 다음 새 context를 설정하고 손 입력을 다시 arm한다.

따라서 `그림 완료`와 drawing timeout이 같은 프레임에 와도 공통 완료 gate가 그림을 한 번만 확정·archive하고 한 번만 전이한다. 명시적 답 제출과 guess timeout이 같은 프레임이면 현재 generation의 제출을 먼저 처리한다.

## 6. 손 인계와 재활성화

[docs/07](./07_hand_interaction.md)의 capture 규칙 위에 아래 게임 규칙을 추가한다.

- 상태·그림 view·pause overlay가 바뀔 때마다 모든 기존 capture를 취소한다.
- 각 손은 새 화면에서 **연속 0.10초 fresh open hand**가 확인된 뒤에만 버튼을 capture할 수 있다.
- 이전 화면에서 누른 pinch나 release 대기 상태를 다음 화면으로 가져오지 않는다.
- `Handover`의 `준비`는 open-hand rearm → pinch capture → 같은 손의 release 순서가 모두 끝나야 승인된다.
- 한 손만 missing/stale이면 그 손의 capture만 취소한다. Drawing에서도 다른 fresh 손이 있으면 timer는 계속 동작한다.
- Overlay는 활성 Graphic.raycastTarget·Button.interactable·HandInteractable로 판정한다. 가려진 UI root와 callback은 함께 비활성화한다. Physics collider는 작업 캔버스에만 사용하며 Overlay 버튼용 collider는 만들지 않는다.

## 7. Focus·tracking pause와 복구

> 계약 보완일: 2026-08-28.

`Setup`에서는 아직 secret·timer가 없으므로 app focus 상실이나 손 부재만으로 자동 pause·차폐하지 않는다. 그 밖의 상태에서 app focus를 잃으면 상태가 timed인지와 무관하게 즉시 `paused=true`, `InputContext.Blocked`로 바꾸고 상태 UI 기준 불투명 pause overlay를 최상단에 올린다. 카메라 패널은 이 shield보다 위에 남아 수신 중이 아닌 준비 상태의 복구용 mouse만 받는다. **손 추적만을 원인으로 하는 자동 pause는 Drawing에만 적용**하며 Router.HasFreshHand가 false일 때 발생한다. 원래 상태와 남은 시간은 보존한다.

WordReveal·ObservePrevious는 손을 내린 채 읽을 수 있어야 하므로 타이머를 계속 진행한다. Guessing도 손을 키보드로 내려 타이핑할 수 있어야 하므로 손 부재만으로 pause하지 않는다. 이 상태들에서도 invalid/stale 손의 UI 클릭은 취소된다. Setup·Handover·Reveal·Gallery는 손이 돌아올 때까지 해당 손 UI만 사용할 수 없다.

pause는 턴 완료나 archive를 발생시키지 않는다. capture 취소로 활성 선만 안전하게 종료하고 현재 작업 그림은 유지한다. `ClearAll()`로 작업을 지우거나 재검출 지점까지 선을 이어 붙이지 않는다.

복구는 자동 resume하지 않는다.

1. app focus가 돌아오고 Router.HasFreshHand가 true일 때까지 `Blocked`를 유지한다. focus가 없는 동안에는 카메라 패널도 클릭을 받지 않는다. Blocked 중에도 Router는 샘플·신선도·재무장을 계속 관찰한다. app focus가 돌아온 뒤 카메라 control이 available이고 카메라가 수신 중이 아닌 준비 상태라면 카메라 패널의 복구용 왼쪽 클릭만 허용하고, 수신 중에는 허용하지 않는다.
2. 조건이 충족되면 timer는 멈춘 채 pause overlay만 조작하는 `UiOnly`로 바꾼다. 손 `계속`은 Router.HasArmedHand, 즉 새 유효 샘플 2개 이상·fresh open 0.10초 조건 후에만 누를 수 있다. context 변경으로 재무장이 초기화되어도 타이머는 재개하지 않는다.
3. `계속`을 pinch 후 release하면 capture를 다시 취소·rearm하고 저장한 상태의 context로 돌아가 timer를 재개한다.
4. `계속` 전에 focus 또는 모든 손 freshness를 다시 잃으면 `Blocked`로 돌아간다.

pause overlay는 기존 secret·그림 위를 단순히 덮는 데 그치지 않는다. 아래 실제 word renderer, drawing presenter와 writable surface도 숨기거나 비활성화한다. 복구 중 underlying 상태 UI는 입력을 받지 않는다.

## 8. 그림 archive와 표시 계약

그림 데이터 계약은 [docs/08](./08_drawing_canvas.md)을 따른다.

| 타입 | 필드·불변식 |
|---|---|
| `CanvasStrokeData` | `strokeId int`, `order int`, `xy float[]`, `colorArgb int`, `widthNormalized float`, `brushId int` |
| `CanvasDrawingData` | `version=1`, `strokes CanvasStrokeData[]` |
| `RelayTurnRecord` | `playerIndex int`(zero-based), `drawingIndex int`(zero-based), `drawing CanvasDrawingData` |

- snapshot에는 Unity object reference를 넣지 않는다. 배열과 stroke 객체까지 deep copy하며 이후 canvas 변경이 archive를 바꾸지 못한다.
- secret word는 세션 상태에만 두고 `RelayTurnRecord`나 per-drawing metadata에는 넣지 않는다.
- 정상 게임의 record 수는 정확히 `N-1`이며 `playerIndex`와 `drawingIndex`는 `0..N-2` 순서다.
- `DrawingController.FinalizeActiveStrokes()` 뒤 `ExportDrawing()` 결과만 archive한다. `LoadDrawing()`, `UndoLastStroke()`, `ClearAll()`의 공통 계약은 docs/08을 따른다.
- 관찰과 갤러리는 `CanvasDrawingPresenter.Show(data,surface)`, `Hide()`, `ClearPresentation()`으로 read-only 표시한다.
- preview·gallery surface에는 writable collider나 `HandCanvasInteractable`을 붙이지 않는다. 갤러리는 `N-1`개를 방 안에 순서대로 나란히 둔다.

비공개 전환은 실제 renderer·presenter를 숨기는 조치와 최상단 불투명 방패를 함께 사용한다. 오브젝트 teleport, 카메라 뒤 배치, back-face만으로 숨겼다고 간주하지 않는다.

단일 공유 모니터는 옆 사람이 물리적으로 엿보는 일을 기술적으로 막을 수 없다. `Handover` 화면이 뜨면 지목된 플레이어 외에는 화면을 보지 않는다는 사회적 규칙을 시작 화면에 명시한다.

## 9. 정답과 단어 데이터

정답은 기존 `GuessJudge.Normalize`·`IsMatch` 알고리즘을 재사용한다. whitespace·case 외의 변형, 유사어, 조사 제거, Levenshtein 보정은 하지 않는다.

신규 `RelayQuizWordList`는 ScriptableObject asset `Assets/_CameraCoop/Data/RelayQuizWords.asset`의 데이터 schema다. 각 entry는 `text`와 `difficulty`(`Easy`, `Medium`, `Hard`)를 가지며 빈 문자열과 normalize 기준 중복을 거부한다. 검증된 단어를 기존 `WordBank`에 전달해 무중복 shuffle과 seed 주입을 재사용한다. runtime 추출 cursor는 게임 인스턴스가 소유하고 ScriptableObject asset 자체는 변경하지 않는다. 첫 구현은 모든 difficulty를 같은 pool에서 사용하고, tag별 runtime filter UI는 후속 범위로 남긴다.

기존 생성자는 `WordBank(string textContent, int seed)`이므로 검증된 text를 줄바꿈으로 연결해 전달한다. controller는 씬 실행 동안 같은 덱을 보유하고 `다시 시작`에서도 추출 cursor를 유지한다. 20개 소진 후 기존 재셔플을 사용한다. runtime seed는 최초 덱 생성 시 정하고, 테스트에는 고정 seed를 주입한다. 그림·답·타이머 reset과 덱 수명은 별개다.

명시적 초기 sample 20개는 다음과 같다.

| `difficulty` | 단어 |
|---|---|
| `Easy` | 사과, 바나나, 고양이, 강아지, 집, 자동차, 우산 |
| `Medium` | 자전거, 피아노, 소방차, 눈사람, 선풍기, 로봇, 해바라기 |
| `Hard` | 롤러코스터, 잠수함, 우주정거장, 신호등, 회전목마, 망원경 |

## 10. 답 입력과 IME

`Guessing`에서만 answer field를 활성화한다. 손 `입력 포커스` 뒤 키보드의 문자, Backspace/Delete, caret 이동, IME 조합·확정은 답 편집으로 인정한다. 제출은 손 버튼만 가능하다.

`InputFocus.IsTyping`은 true인 동안 이동·look·mode toggle·drawing을 막지만 손 `제출`은 막지 않는다. RelayQuizUI가 로컬에서 이 flag의 유일한 작성자이며 상태 이탈·비활성화·포커스 상실 시 false로 복원한다. 기존 GameUI는 이 씬에 두지 않는다.

- IME 조합 중에는 제출 버튼을 비활성화하고 그 버튼의 down·select도 차단한다. `글자 조합을 마친 뒤 제출하세요`를 표시한다. Enter로 글자 조합을 확정할 수 있지만 Enter 제출은 없다.
- 손 제출은 조합이 없는 확정 문자열을 한 번 복사해 판정한다. 입력창 blur·onEndEdit에서는 제출하지 않는다.
- 시간 만료는 blur 전에 확정된 InputField.text를 복사해 판정하며 미확정 composition은 답에 포함하지 않는다. 빈 확정 문자열은 오답이다.
- 앱 포커스 상실 pause에서는 확정 문자열만 보존하고 미완료 조합을 취소한다. 복귀·계속 이후에도 답변창은 손으로 다시 선택한다. 남은 시간과 확정 답은 유지한다.
- 손이 카메라 밖에 있어도 타이핑과 Guessing 타이머는 계속된다. 제출하려면 손을 다시 보여 주고 재무장 뒤 새 핀치를 사용한다.

프로젝트의 Input System 전용 설정과 legacy input field 조합은 Editor와 player build에서 결과가 다를 수 있다. 기존 입력이 고장났다고 단정하지 않으며, 한글 IME 조합·삭제·재포커스·손 제출은 실제 Windows build에서 사용자 검증할 항목으로 남긴다.

## 11. 계획 파일과 Inspector 배선

| 경로 | 책임 |
|---|---|
| `Assets/_CameraCoop/Scenes/RelayQuiz.unity` | 로컬 전용 게임 씬. network object 없음 |
| `Assets/_CameraCoop/Scripts/RelayQuiz/RelayQuizLogic.cs` | 순수 상태, player index, timer, generation, `RelayTurnRecord` 정의·archive 순서, 단어 추출·판정 결과 |
| `Assets/_CameraCoop/Scripts/RelayQuiz/RelayQuizController.cs` | scene orchestration, `Tick`, focus·tracking pause flag와 복구, canvas export/load, context 전환 |
| `Assets/_CameraCoop/Scripts/RelayQuiz/RelayQuizUI.cs` | 상태별 overlay, hand-only 버튼, answer focus·편집, 비공개 render 전환 |
| `Assets/_CameraCoop/Scripts/RelayQuiz/RelayQuizGallery.cs` | read-only `N-1` drawing 배치·정리 |
| `Assets/_CameraCoop/Scripts/RelayQuiz/RelayQuizWordList.cs` | ScriptableObject schema와 validation. runtime 추출 상태는 보관하지 않음 |
| `Assets/_CameraCoop/Data/RelayQuizWords.asset` | difficulty tag가 붙은 한국어 sample 20개 |

`RelayQuizController` Inspector에는 `InputModeManager`, `HandInputRouter`, `PlayerController`, `WorkPose`, `GalleryPose`, `DrawingController`, active drawing surface, `CanvasDrawingPresenter`, preview surface, `RelayQuizUI`, `RelayQuizGallery`, `RelayQuizWordList`와 네 timer 기본값을 명시 연결한다. reference 누락 시 게임 시작을 거부하고 secret을 표시하지 않은 채 `Setup`에 오류 안내를 보여 준다.

로컬 PlayerController는 ModalFirstPerson 프로필이다. Setup·Handover에서 차폐된 동안 PlaceAt(WorkPose)로 다음 사람의 작업 시점을 정렬한다. Gallery 진입 시 차폐 중 PlaceAt(GalleryPose) 후 Explore·Move로 전환한다. 위치·동일 종횡비의 갤러리 크기는 [06_player_controller](06_player_controller.md) §6을 따른다. 참가자 기록 수만큼 읽기 전용 캔버스를 활성화하고 `플레이어 1 → 플레이어 2 → …` 표지를 함께 보여 준다.

씬 overlay root는 `SetupRoot`, `HandoverRoot`, `WordRevealRoot`, `DrawingHudRoot`, `ObserveRoot`, `GuessRoot`, `RevealRoot`, `GalleryRoot`, 상태 UI 기준 최상단 `PauseShieldRoot`로 구분한다. `CameraPanel`은 `OverlayRoot`의 마지막 자식으로서 Handover·Pause shield보다 위에 보이지만, mouse 허용은 위의 camera control·focus·Interact·준비 상태 조건을 따른다. 한 프레임에 둘 이상의 상태 root를 상호작용 가능하게 두지 않는다.

## 12. 검증 계획

상세 실행 절차와 결과 기록 형식은 [docs/05](./05_test_plan.md)에 추가한다. 이 문서는 설계 기준만 정의하며 PASS를 주장하지 않는다.

| 시나리오 | 기대 결과 |
|---|---|
| 2인 | `player0` 그림 1개 archive → `player1` `Guessing`. `ObservePrevious`와 중간 `Drawing` 없음. gallery 1개 |
| 3인 | 그림 작성자 `player0,player1`, archive index `0,1`. `player1`만 관찰 후 빈 canvas에 재그리기. `player2` 답변. gallery 2개 |
| 4인 | 그림 작성자 `player0,player1,player2`, archive index `0,1,2`. 중간자 둘 모두 직전 한 장만 관찰. `player3` 답변. gallery 3개 |
| 비공개 | 제시어는 `player0`의 `WordReveal`과 `Reveal`에서만 보이며 record·preview·gallery에 없음 |
| 중복 전이 | 완료 button과 timeout 동시 입력, double release, stale generation action에서 archive·player 증가가 각각 1회 |
| hand rearm | 화면 경계의 held pinch로 다음 버튼이 눌리지 않고 fresh open 0.10초 뒤 pinch+release만 승인 |
| pause | `Setup`은 focus loss·손 stale/missing으로 자동 pause하지 않음. 그 밖의 focus loss는 차폐·timer 정지, 손 stale/missing은 Drawing에서만 차폐·timer 정지. 새 손 `계속` 전에는 재개하지 않음 |
| 키보드 답변 | Guessing에서 손을 내리고 한글을 타이핑해도 pause하지 않음. 조합 중 제출 차단, timeout은 확정 문자열만 판정 |
| snapshot | export 뒤 active canvas 수정·undo·clear를 해도 이전 `RelayTurnRecord`가 변하지 않음 |
| IME | Windows player build에서 한글 조합·삭제·재포커스·손 제출 후 normalize 판정 확인 |

순수 상태·중복 전이·정답 판정 테스트는 `Assets/_CameraCoop/Tests/EditMode/RelayQuizLogicTests.cs`에 둔다. archive deep-copy와 drawing data는 docs/08의 기존 `DrawingTests.cs`를 확장하며 별도 DrawingArchiveTests는 만들지 않는다. Play·build 검증 전에는 구현 완료나 입력 호환을 판정하지 않는다.

## 13. 향후 확장 경계

`RelayQuizLogic`, `CanvasDrawingData`, `RelayTurnRecord`는 Unity transport를 모르는 로컬 계약으로 유지한다. 향후 Phase 3에서 온라인화가 승인되면 별도 adapter가 이 상태와 snapshot을 network message에 매핑할 수 있다.

향후 동기화 후보는 상태·phaseGeneration, 참가 순서·현재 playerIndex, 남은 시간, 완료된 그림 record, 답 제출·판정 결과다. 제시어는 첫 작성자에게만, 직전 그림은 현재 관찰자·답변자에게만 보내는 별도 공개 정책이 필요하다. 이 목록은 확장 경계만 표시하며 현재 전송 메시지를 추가하지 않는다.

이번 Phase에서는 그 adapter, 메시지 타입, serializer, 보안 규칙, late join, host migration을 설계·구현하지 않는다. 기존 온라인 `Relay`를 이 로컬 모드로 이름 변경하거나 대체하지도 않는다.
