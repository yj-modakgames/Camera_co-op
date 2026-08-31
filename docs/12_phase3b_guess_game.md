# 12. Phase 3b+3c 설계 — 미니게임 프레임워크 + 릴레이 그림 맞추기

> 작성 2026-08-27 · 상태 정정 2026-08-31: **핵심 구현과 합성 UDP·Loopback 통합 검증 완료. 실제 webcam·Steam 다인·한글 IME와 G-8 품질 평가는 대기**
> 전제: Phase 3e 3D 구현·실행 검증 완료 (docs/11). 대상 씬 = `Netplay3D.unity` (신규 씬 없음).
> 제품 방향(docs/08 §전제): 4인 온라인 협동 미니게임 컬렉션 — 이 문서가 3b(프레임워크)와 3c(첫 게임)를 함께 다룬다.
> 후속 게임(별도 Phase): 그림으로 말해요(텔레스트레이션), 과녁/풍선 터뜨리기, 따라 그리기 대결, 색칠 땅따먹기.

## 1. 목표와 범위

플레이어 2~4명이 3D 룸에서 **릴레이 제시어 그림 맞추기**를 한다. host가 게임을 시작하면 라운드마다 출제자가 이젤에 그림을 그리고 나머지가 키보드로 정답을 입력한다. 점수판·타이머·정답 피드가 화면 UI로 표시되고, 게임이 끝나면 자유 그리기로 복귀한다.

핵심 구조 결정: **게임 계층 분리.** `NetSession`은 게임을 모른 채 "게임 메시지 통로"만 열고, 신규 `GameSession`(공통 플러밍) + 게임별 순수 로직이 그 위에 얹힌다. 다음 게임 4종이 같은 통로를 재사용한다.

### 게임 규칙

**기본(턴 로테이션) 모드**
- 라운드마다 출제자 1명 순환. 사이클 2회 (4인 = 8라운드, 2인 = 4라운드).
- 출제자에게만 제시어 전송. 나머지 화면엔 글자 수 힌트(`◯◯◯`).
- 라운드 90초. 출제자만 그리기 가능.
- 정답자 점수 = `100 + floor(남은 초)`. 출제자 점수 = 정답자 1명당 50.
- 오답은 전원 피드에 노출(재미 요소). 정답은 피드에 `정답!`으로 가려짐(유출 방지). 이미 맞힌 사람의 재입력은 무시.
- 출제자가 아닌 전원이 맞히면 조기 종료. 라운드 끝에 제시어 공개(5초) 후 다음 라운드.

**릴레이(교대) 모드**
- 맞히는 사람 1명 순환, 나머지 전원이 제시어를 알고 **15초씩 교대로 이어 그리기**(캔버스 유지).
- 팀 협동 점수: 맞히면 전원에게 `100 + floor(남은 초)`. 못 맞히면 0.
- 3인 미만이면 기본 모드와 동일해지므로 host UI에서 2인일 때 릴레이 시작 버튼 비활성.

**공통**
- 정답 판정: `Trim` + 공백 전부 제거 후 완전 일치 (`GuessJudge.IsMatch`). 오타 허용 없음(요청 시 후속).
- 제시어: 내장 한국어 단어 TextAsset (`Data/words_ko.txt`, 1줄 1단어, ~150개). 세션 내 무중복 랜덤, 소진 시 재셔플.
- 라운드 사이 캔버스 클리어: 기존 `ClearCanvas` 재사용 (host 전용 규칙이 그대로 맞음).
- 게임 미진행 시(자유 그리기)에는 현재 동작과 100% 동일 — 무회귀가 프레임워크의 1번 요건.

### 명시적 배제 (후속 Phase)

그림으로 말해요, 액션 게임 3종, 범용 텍스트 채팅, 오타 허용(레벤슈타인), 카테고리별 단어장, host migration, 관전 전용 모드, 게임 중 재접속 상태 복구(늦은 참가 동기화와 다름 — 아래 §5), 2D 씬(`NetplayTest`/`DrawingTest`/`HandTrackingTest`) 수정.

## 2. 아키텍처

### 신규 파일 (Scripts/Game/ + Data/)

```
Scripts/Game/GameProtocol.cs   — 게임 메시지 type 상수 + payload 클래스 (순수 데이터)
Scripts/Game/GuessGameLogic.cs — 게임 상태 머신·턴 순환·점수·판정 위임 (순수, host에서만 구동)
Scripts/Game/GameClientState.cs— 수신 메시지 → 표시 상태 미러 (순수, host 포함 전원의 표시 단일 경로)
Scripts/Game/GuessJudge.cs     — 정답 정규화·일치 판정 (순수 static)
Scripts/Game/WordBank.cs       — 단어장 로드·무중복 랜덤 추출 (순수, 시드 주입 가능)
Scripts/Game/GameSession.cs    — MonoBehaviour. host: 로직 구동+타이머+브로드캐스트 / 전원: 수신 상태 미러+이벤트 발행
Scripts/Game/GameUI.cs         — MonoBehaviour. 점수판·타이머·배너·피드·정답 입력창·시작 버튼
Data/words_ko.txt              — 제시어 TextAsset
```

순수 로직/MonoBehaviour 분리는 기존 패턴(docs/04 §5) 그대로 — `GuessGameLogic`은 UnityEngine 의존 없이 EditMode 테스트 대상.

### 참조 방향 (docs/01 §4 유지 — NetSession은 게임을 모른다)

```
GameSession ──조회/송신──> NetSession        (SendGame*/StrokeGate 설정)
GameSession ──구독──> NetSession             (OnGameMessage, OnPeerJoinedSession, OnPlayersChanged)
GameSession ──보유──> GuessGameLogic, WordBank (host에서만 구동)
GameSession ──설정──> HandPointer.StrokesEnabled (로컬 그리기 게이트)
GameUI      ──구독──> GameSession            (상태 이벤트)
GameUI      ──호출──> GameSession            (StartGame/SubmitGuess)
PlayerController · DrawingController ──조회──> InputFocus.IsTyping (static 게이트)
```

`NetSession`·`HandPointer`는 소비자를 모른다. `NetplayUI`는 무수정.

### NetSession 변경 (게임 무지 유지, 6건)

| # | 변경 | 이유 |
|---|---|---|
| 1 | 중계 정책: 블랙리스트(Hello 제외 전부)→**화이트리스트**(기존 드로잉/커서/Clear 타입만 자동 중계). 미지 타입은 중계하지 않고 `OnGameMessage(type, sender, payloadJson)` 이벤트 발행 | `GuessSubmit`이 전원에게 중계되면 오답·정답이 즉시 유출된다. 게임 메시지의 중계 여부는 host의 GameSession이 결정 |
| 2 | `SendGameToHost(type, payload)` / `BroadcastGame(type, payload, exceptId)` / `SendGameTo(playerId, type, payload)` 공개 | 게임 계층의 송신 통로. 내부 Encode/Broadcast 재사용, 전부 reliable |
| 3 | `Func<string, bool> StrokeGate` 프로퍼티 (playerId→허용, null=전원 허용) | host가 라운드 중 출제자 아닌 피어의 `StrokeStart/Points/End/Erase`를 중계·반영 거부 (권위 게이트). 로컬 게이트(HandPointer)와 이중 방어 |
| 4 | `OnPeerJoinedSession(string playerId)` 이벤트 (host: HandleHello 직후 / 클라: PeerJoined 적용 직후) | 늦은 참가자에게 host가 `GameStateSync`를 보낼 트리거. `OnPlayersChanged`는 누가 새로 왔는지 알려주지 않는다 |
| 5 | `NetProtocol.Version = 2 → 3` | 게임 메시지를 모르는 구버전과 섞이면 게임 진행이 조용히 깨진다 — 기존 정책(docs/11 §3)대로 거부 |
| 6 | `string HostPlayerId` 프로퍼티 (host: 자기 id / 클라: Welcome sender, StopSession 시 null) | 클라 GameSession이 "host가 보낸 게임 메시지만 적용"(§5 위조 방어)을 판정할 기준. colorIndex 0 추정 같은 간접 추론 금지 |

StrokeGate는 로컬 송신에도 적용한다 — host 자신이 출제자가 아닐 때 자기 스트로크를 만들지 않도록 `HandleLocalStrokeStart`에서도 검사한다. 단 1차 방어는 HandPointer 게이트라 정상 경로에서는 도달하지 않는다.

### HandPointer 변경 (1건)

`bool StrokesEnabled` (기본 true). false면 캔버스 스트로크/지우개 이벤트를 발행하지 않는다(도구 클릭은 허용 — 무해하고, 막으면 분기만 는다). **false로 바뀌는 순간 진행 중 스트로크를 End** — 라운드 종료 시 그리다 만 선이 고아가 되지 않게. GameSession이 라운드 전이마다 `로컬 플레이어 == 출제자`로 설정한다. 게임 미진행 시 항상 true.

### 타이핑 게이트 — InputFocus (신규 static)

`Scripts/Input/InputFocus.cs`: `public static bool IsTyping`. GameUI가 입력창 포커스 획득/상실 시 설정한다.

- `PlayerController.Update`: IsTyping이면 이동 입력 무시 (마우스 룩은 우클릭 홀드라 충돌 없음 — 유지).
- `DrawingController.Update`: IsTyping이면 `clearKey(C)` 무시. **이게 없으면 정답에 c가 들어가는 순간 캔버스가 지워진다.**

### 상태 머신 (GuessGameLogic — host에서만 구동)

```
Idle ──StartGame──> RoundIntro(3s) ──> Drawing(90s) ──> RoundReveal(5s) ──┬──> RoundIntro (다음 라운드)
                                                                          └──> GameEnd(8s) ──> Idle
Drawing 조기 종료: 기본 모드 = 출제자 외 전원 정답 / 릴레이 모드 = 맞히는 사람 정답
GameAbort (인원 부족·host 중단): 어느 상태에서든 ──> Idle
```

- 전이는 전부 host가 결정해 브로드캐스트. 클라이언트 GameSession은 수신 메시지로 표시용 미러 상태만 갱신 — 판단하지 않는다.
- 타이머: host가 `RoundBegin`에 `durationSec`을 실어 보내고 만료를 자기 시계로 판정. 클라 카운트다운은 수신 시점 기준 로컬 표시(오차는 표시용 — 판정에 안 쓴다).
- 릴레이 모드: Drawing 중 host가 15초마다 `RelaySwap` 브로드캐스트, StrokeGate·HandPointer 게이트 갱신.
- 라운드 시작 시 host가 기존 `SendClear()` 호출 (스트로크 상태·원격 표시 정리 재사용).

### 정답 흐름 (기본 모드 예)

```
게스트가 입력창에 "사과" 입력 → GameUI → GameSession.SubmitGuess
  ├─ 로컬이 host: GuessGameLogic.SubmitGuess 직접 호출
  └─ 클라: SendGameToHost(GuessSubmit)
host: GuessGameLogic.SubmitGuess(playerId, text, 남은 초)
  ├─ 오답  → BroadcastGame(GuessFeed { playerId, text, correct:false })
  ├─ 정답  → BroadcastGame(GuessFeed { playerId, text:"", correct:true }) + 점수 반영
  │          (text를 비워 보낸다 — 아직 못 맞힌 사람에게 정답 문자열 유출 방지)
  └─ 무시(출제자 본인·이미 정답·라운드 아님) → 아무것도 안 보냄
```

## 3. 프로토콜 (network v3)

`NetProtocol.Version = 3`. 신규 타입은 전부 reliable, host 권위. JsonUtility 제약(딕셔너리 불가)에 따라 점수는 `string[] playerIds` + `int[] scores` 평행 배열.

| 메시지 | 방향 | payload | 비고 |
|---|---|---|---|
| `GameStart` | host→전원 | `{ gameId, mode }` | gameId=0(GuessGame). mode 0=기본 1=릴레이 |
| `RoundBegin` | host→전원 | `{ round, totalRounds, activeId, wordLen, introSec, durationSec }` | activeId = 기본:출제자 / 릴레이:맞히는 사람. 클라는 introSec 뒤 Drawing 표시로 자체 전환(별도 메시지 없음 — 드리프트는 표시용) |
| `WordAssign` | host→일부 | `{ word }` | SendTo — 기본:출제자 1명 / 릴레이:맞히는 사람 제외 전원 |
| `RelaySwap` | host→전원 | `{ drawerId }` | 릴레이 모드 교대 |
| `GuessSubmit` | 클라→host | `{ text }` | host는 중계하지 않는다 (화이트리스트 §2) |
| `GuessFeed` | host→전원 | `{ playerId, text, correct }` | correct=true면 text="" |
| `RoundEnd` | host→전원 | `{ word, playerIds[], scores[], reason }` | 제시어 공개 + 누적 점수. reason 0=시간 1=전원정답 2=출제자이탈 |
| `GameEnd` | host→전원 | `{ playerIds[], scores[] }` | 최종 점수판 |
| `GameAbort` | host→전원 | `{}` | 인원 부족·host 중단 → 전원 Idle 복귀 |
| `GameStateSync` | host→1명 | `{ phase, gameId, mode, round, totalRounds, activeId, wordLen, remainingSec, playerIds[], scores[] }` | 늦은 참가 관전 동기화. 제시어는 안 보낸다(관전자) |

기존 드로잉 프로토콜은 무변경 — 버전 숫자만 오른다. 화이트리스트 자동 중계 대상 = `CursorUpdate, StrokeStart, StrokePoints, StrokeEnd, StrokeErase, ClearCanvas` (기존 전부).

## 4. 씬 — Netplay3D.unity 변경

- **신규 오브젝트**: `GameSession`(GameSession 컴포넌트 — netSession·handPointer·wordAsset 참조. 캔버스 클리어는 기존 `OnCanvasCleared`→`NetplayUI`→`DrawingController.ClearAll` 경로 재사용이라 직접 참조 불필요), `GameUI`(기존 UI Canvas 아래).
- **UI (기존 로비 Canvas에 추가, 전부 legacy uGUI — 프로젝트에 TMP 미사용)**:
  - host 전용: `게임 시작(기본)` / `게임 시작(릴레이)` 버튼 (세션 중 + 2인 이상일 때만 표시, 릴레이는 3인 이상)
  - 상단 중앙: 라운드 배너(`라운드 2/8 — ○○님이 그립니다`), 타이머, 제시어(출제자)/글자수 힌트(게서)
  - 우측: 점수판(이름·점수·정답 표시), 정답 피드(최근 6줄 스크롤)
  - 하단: 정답 입력창(InputField) — Enter로 포커스/제출. 출제자와 관전자에게는 비활성
- **입력 주의 (구현 검증 필수)**: 프로젝트가 Input System 전용이라 legacy `InputField`의 텍스트 입력·한글 IME 동작을 Play 모드에서 실측한다. 문제가 있으면 `Keyboard.current.onTextInput` 기반 자체 캡처로 폴백 (구현 계획에 검증 Task로 명시).
- 3D 룸·이젤·팔레트·Player 리그 무변경.

## 5. 엣지 케이스

| 상황 | 처리 |
|---|---|
| 출제자(기본)/맞히는 사람(릴레이) 이탈 | host가 라운드 무효(`RoundEnd` reason=2, 점수 없음) → 다음 라운드. 순환 목록에서 제거 |
| 일반 게서 이탈 | 순환 목록에서 제거만. 남은 인원 2 미만이면 `GameAbort` |
| host 이탈 | 기존 규칙 그대로 — 클라 세션 종료(StopSession), 게임도 함께 끝 |
| 게임 중 늦은 참가 | 드로잉 스냅샷은 기존 Welcome이 처리. host가 `OnPeerJoinedSession`에서 `GameStateSync` 전송 → 관전(입력창 비활성), **다음 라운드부터** 순환에 포함 |
| 게임 중 host가 자유그리기 Clear 버튼 | 게임 중에는 `NetplayUI` Clear 버튼을 숨기지 않는다(무수정 원칙) — 눌러도 캔버스만 지워지고 게임 진행엔 무해 |
| 출제자가 정답 입력 | GuessGameLogic이 무시 (입력창도 비활성 — 이중 방어) |
| 이미 맞힌 사람 재입력 | 무시 (피드에도 안 실림 — 힌트 유출 방지) |
| 빈 문자열/공백만 제출 | 정규화 후 빈 문자열이면 무시 |
| 타이핑 중 WASD/C 키 | `InputFocus.IsTyping` 게이트 (§2) |
| 라운드 종료 순간 그리던 선 | `StrokesEnabled=false` 전이가 진행 중 스트로크 End 발행 → 정상 완결 (§2) |
| 단어장 소진 | 재셔플 후 계속 (세션 내 중복 허용으로 완화) |
| 클라가 위조 `RoundBegin` 송신 | host 화이트리스트가 중계 안 함 + 클라 GameSession은 **host sender의 게임 메시지만 적용** |
| 2인 릴레이 시작 시도 | UI에서 버튼 비활성 + GuessGameLogic.StartGame도 거부 (이중 방어) |

## 6. 테스트 / DoD

> **검증 환경 제약 (2026-08-27 사용자 확인):** 실제 웹캠 입력과 실제 다인(Steam) 접속은 현재 테스트 불가.
> 따라서 G-2~G-7은 전부 **합성 입력(`fake_hand.py` UDP) + Loopback 가짜 피어**로 수행한다 — 웹캠·별도 기기 불필요.
> 아래 3건은 **사용자 검증 대기**로 이연하며, 이 Phase의 DoD에서 제외한다 (docs/09 §3 N-5와 같은 취급):
> ① 실웹캠 손 트래킹으로 게임 1라운드 스모크 ② Steam 실기 2+인 게임 세션 ③ G-4 중 한글 IME 실타이핑(자동화 불가)

| # | 기준 | 확인 방법 | 결과 (2026-08-28, Task 7) |
|---|---|---|---|
| G-1 | EditMode: GuessGameLogic 상태 전이 전건·턴 순환(이탈 포함)·점수·조기 종료·릴레이 교대 / GuessJudge 정규화 / WordBank 무중복·재셔플 / NetSession 화이트리스트 중계·StrokeGate / 프로토콜 v3 인코딩 왕복 + 기존 전체 pass | `unity cmd run_tests --mode EditMode` | **PASS (Task 1~6 기준선)** — 239/239, 메인 세션 직접 실측 (docs/14 §9). Task 7에서 재실행하지 않음 |
| G-2 | **Loopback 게임 전판 실행 검증** — 가짜 피어 2명과 기본 모드 시작→라운드 진행→정답→점수→게임 종료→자유 그리기 복귀. 콘솔 에러 0 | eval 스크립트 | **PASS** — 아래 6-1 |
| G-3 | **드로잉 게이트** — 출제자 차례에 `fake_hand.py`로 실제 스트로크 생성, 가짜 피어(비출제자)의 StrokeStart는 중계·표시 안 됨 | fake_hand + eval | **PASS** — 아래 6-2 |
| G-4 | **정답 입력 실측** — Play 모드에서 InputField 한글 입력·Enter 제출 동작, 타이핑 중 WASD 이동·C 클리어 미발동 | 수동 확인 (사용자) 또는 eval로 InputFocus 게이트 검증 | **게이트 PASS / 텍스트 입력 자동화 불가 → 사용자 검증 대기** — 아래 6-3 |
| G-5 | **늦은 참가** — 게임 중 가짜 피어 참가 → GameStateSync 수신·관전 상태·다음 라운드 참여 | eval | **PASS** — 아래 6-4 |
| G-6 | 릴레이 모드 — 15초 교대·WordAssign 대상 반전·팀 점수 | eval (시간 가속: durationSec 주입) | **PASS** — 아래 6-5 |
| G-7 | 자유 그리기 무회귀 — 게임 미시작 상태에서 기존 P-2~P-6 경로 정상 (스모크) | eval | **PASS** — 아래 6-6 |
| G-8 | QUALITY_CHECKLIST ≥ 9.0 | 채점 보고 | 미착수 (Task 8) |

### 6-0. 검증 방법 (Task 7 공통)

- 대상 씬 `Netplay3D.unity`를 Play 모드로 띄우고 `unity cmd eval_file`로만 조작했다. **씬 파일은 무변경**(종료 시 `isDirty:false`, `git status` 클린) — 시간 단축 파라미터를 Inspector가 아니라 **Play 모드 런타임 리플렉션**으로 주입했다. 주입 후 readback: `intro=0.5 draw=10 reveal=0.5 gameEnd=1.5 relaySwap=2 cycles=1 state._gameEndSec=1.5` (G-2), `intro=0 draw=300 relaySwap=2 cycles=1` (G-3·G-5·G-6·G-7).
- 시간 전이는 `GameSession.TickForTest(dt)`를 리플렉션으로 호출해 주입했다 — 실시간 프레임에 의존하지 않아 결정적이다.
- 가짜 피어는 `LoopbackTransport.AddFakePeer` + `Hello`(EditMode `GameChannelTests.StartHost`와 같은 패턴). host의 실제 `NetSession`·`GameSession`·`GameUI`·`HandPointer`·`DrawingController`가 그대로 돈다.
- 실웹캠은 쓰지 않았다. 손 입력은 `PythonTracker/fake_hand.py` 합성 UDP(127.0.0.1:5052)뿐이다 (`--selfcheck` → `selfcheck OK  packet bytes: 868`).
- 캡처 파일을 만들지 않았으므로 `Assets/` 밑 정리 대상이 없다. 2D 씬 3개는 열지 않았다.

### 6-1. G-2 — Loopback 게임 전판 (PASS)

3인(로컬 host + 가짜 클라 `a`·`b`), Turns 모드, cycles=1 → `totalRounds=3`.

- 시작: 두 피어 모두 `GameStart{"gameId":0,"mode":0}` → `ClearCanvas{}` → `RoundBegin{"round":1,"totalRounds":3,"activeId":"local-host","wordLen":2,"introSec":0.5,"durationSec":10.0}` 수신. 라운드 1 출제자가 로컬이므로 `WordAssign`은 **와이어에 나가지 않고** 로컬만 적용(`localWord=바지`) — docs/12 §3 SendTo 규칙대로.
- 오답: `GuessFeed{"playerId":"a","text":"틀린답","correct":false}` (원문 보존).
- 정답: `GuessFeed{"playerId":"a","text":"","correct":true}` (**text 비움** — 유출 방지). 두 번째 정답(`"  바지  "` 공백 포함, 정규화 통과)에서 출제자 외 전원 정답 → `RoundEnd{"word":"바지","playerIds":["local-host","a","b"],"scores":[100,110,110],"reason":1}` (AllGuessed). 정답자 `100+floor(10.0)=110`, 출제자 `50x2=100`.
- 라운드 2·3은 타임아웃(`"reason":0`)으로 소화, 라운드마다 `ClearCanvas`+`RoundBegin`+출제자 1명에게만 `WordAssign`. 전이 로그: `1RoundReveal→2RoundIntro→2Drawing→2RoundReveal→3RoundIntro→3Drawing→3RoundReveal→3GameEnd→0Idle` (총 24.0s 주입, GameEnd→Idle이 정확히 1.5s = gameEndSec).
- 자유 그리기 복귀: Idle에서 `hp.StrokesEnabled=True`, `netSession.StrokeGate==null`.
- 타입 집계(피어 a 수신): `GameStart=1 ClearCanvas=3 RoundBegin=3 GuessFeed=3 RoundEnd=3 WordAssign=1 GameEnd=1` — **Turns 모드 `RelaySwap` 0건** (docs/14 §8-6 판정 확인).
- UI 실측(같은 씬, 별 게임의 라운드 1 Drawing): banner `라운드 1/4 — LocalHost님이 그립니다`, timer `300`, word `참새`(출제자 본인), feed `PeerA : 오답예시`, 시작 버튼 2개 `active=False`(게임 중). Idle에서는 시작 버튼 `active=True interactable=True`.
- **콘솔 Error 0 / Warning 0** (`get_console_logs --severity Error|Warning` → `total:0`). 전체 버퍼는 `[UdpHandReceiver] listening on 127.0.0.1:5052` Log 1건뿐.

### 6-2. G-3 — 드로잉 게이트 (PASS)

- 로컬 출제자 라운드(round 1, `activeId=drawer=local-host`, `StrokesEnabled=True`, `StrokeGate("local-host")=True`, `StrokeGate("a")=False`)에서 `fake_hand.py --target 0.5,0.5 --pinch-hold 25`(캔버스 중심 = 화면 960,540, 사전 레이캐스트 `DrawCanvas`/`isCanvasSurface=True`) → UDP `seq=628 hand0=Left pinch=0.150 palm=(0.500,0.500)` 수신 중 로컬 스트로크 **1개**: `activeStrokes=1 keys=[Left]`, `LineRenderer=Stroke_Left`, `host.strokes ids=[local-host:0]`, 피어 a에 `StrokeStart{"strokeId":"local-host:0","hand":"Left","x":0.5,...}` 1건 + `StrokePoints` 205건 중계.
- 비출제자 가짜 클라 `a`가 `StrokeStart{strokeId:"a:99"}` + `StrokePoints` 송신 → `host.strokes=0`, `contains(a:99)=False`, 다른 클라(b)가 받은 것은 **`CursorUpdate`뿐** (스트로크 4종만 게이트 대상 — docs/12 §2 표 #3 그대로).
- 로컬 비출제자 라운드(round 2, `drawer=a`, `StrokesEnabled=False`, `StrokeGate("local-host")=False`)에서 같은 `--target` 핀치를 다시 보냄 → UDP는 수신 중(`seq 185→236 pinch=0.150`)인데 `activeStrokes=0 finishedStrokes=0 host.strokes=0`, 피어의 `StrokeStart` 누적도 증가 없음(1 유지) → **스트로크 0개**.

### 6-3. G-4 — 입력 게이트 (게이트 PASS / 텍스트 입력 자동화 불가)

- 포커스: `guessInput.ActivateInputField()` → `isFocused=True`, `InputFocus.IsTyping=True`, `EventSystem.selected=GuessInput`, `interactable=True`(라운드 2에서 로컬이 게서 → `CanGuess=True`). 새 Input System의 `InputSystemUIInputModule` 아래에서도 **UI 포커스 경로는 동작한다**.
- WASD 게이트 A/B (같은 눌림 상태, 같은 호출 횟수):
  - `IsTyping=true` → `PlayerController.Update` x20 → `(0.4383,1.5,-2.3673)` → `(0.4383,1.5,-2.3673)`, **이동 0.0000**
  - `IsTyping=false` → 같은 Update x20 → `(0.4383,1.5,-2.3673)` → `(0.4383,1.5,-2.1390)`, **이동 0.2283**
  - → 이동을 막은 것이 `InputFocus.IsTyping` 게이트임이 대조로 확정. (Input System은 Editor/Player 상태 버퍼가 분리돼 `QueueStateEvent`로 만든 키가 게임 프레임에 도달하지 않는다 — 그래서 눌린 키를 그대로 두고 `Update`를 직접 구동했다.)
- C키 게이트: 런타임 구동 불가(`cKey.wasPressedThisFrame`가 editor 버퍼에서 잡히지 않는다 — queue + `InputSystem.Update()` 후에도 `False`). 계획서 지시대로 **코드 검사**로 확인: `DrawingController.cs:80` `if (clearKey != Key.None && keyboard != null && keyboard[clearKey].wasPressedThisFrame && !InputFocus.IsTyping)`, 씬의 `clearKey` 직렬화 값 `"C"` (`get_serialized_fields`).
- **legacy `InputField` 실타이핑은 자동화 불가 (사용자 검증 대기).** 시도한 것 2가지: (1) Input System `QueueStateEvent`(A/B 키) → `guessInput.text` 변화 없음. 단 위 버퍼 분리 때문에 이 결과는 InputField에 대한 판정 근거가 못 된다. (2) Game view에 키보드 포커스를 주고(`EditorWindow.FocusWindowIfItsOpen(UnityEditor.GameView)` → `focusedWindow=GameView`) Unity 창을 OS 전면으로 올린 뒤(`AppActivate(pid)` → `Application.isFocused=True` 확인) `WScript.Shell.SendKeys("abcd")` → `guessInput.text=[]`이고 `Keyboard.onTextInput` witness도 0문자. 그러나 읽는 시점에 `Application.isFocused=False`로 떨어져 **"키가 Unity에 전달되지 않았다"와 "InputField가 무시했다"를 구분할 수 없다.** → 에디터 Play에서의 실타이핑·한글 IME는 사용자가 직접 확인해야 한다.
- **주의(빌드 리스크 유지):** 에디터 Play에서 동작하더라도 빌드에서 안 될 수 있다. `ProjectSettings/ProjectSettings.asset:933`이 `activeInputHandler: 1`(Input System only)이고 legacy `InputField`의 문자 입력은 IMGUI 이벤트 큐(`Event.PopEvent`)에 의존한다 — 에디터는 EditorWindow가 IMGUI 이벤트를 펌프하지만 플레이어 빌드는 그렇지 않다. 폴백은 계획서 "깨지기 쉬운 것" 8번(`Keyboard.current.onTextInput` 자체 캡처). **`InputFocus` 게이트 자체는 `isFocused`만 보므로 문자 입력이 안 되더라도 위 게이트 결과는 유효하다** (docs/14 §1-3과 같은 판단).

### 6-4. G-5 — 늦은 참가 (PASS)

- 게임 중(round 2 Drawing, `activeId=a`, 3인) 4번째 피어 `c`가 `Hello` → `players=4`, `totalRounds` 3→4, `c`에게만 `GameStateSync{"phase":2,"gameId":0,"mode":0,"round":2,"totalRounds":4,"activeId":"a","wordLen":2,"remainingSec":246.97,"playerIds":["local-host","a","b","c"],"scores":[0,0,0,0]}` — **제시어 없음**(관전자).
- 그 payload를 실제 `GameClientState`에 적용 → `phase=Drawing round=2/4 active=a wordLen=2 localWord=(null) Spectator=True CanGuess(c)=False`.
- 다음 `RoundBegin` 적용 후 `Spectator=False`. 큐 등장: 같은 방식의 다른 세트에서 `RoundBegin{"round":4,"totalRounds":4,"activeId":"c",...}`로 순환 마지막에 붙는 것 확인.
- 정답 자격: 합류 라운드 제출 → 피드 **0건**(Ignored, 미자격). 다음 라운드 제출 → `GuessFeed{"playerId":"c","text":"다음라운드오답","correct":false}` 브로드캐스트.
- 부수 확인: 5번째 피어(`d`)의 `Hello`는 4인 상한으로 거부되어 `players`에 들어가지 않는다 (`NetSession.cs:420` 주석·colorIndex 고갈) — 늦은 참가도 4인까지다.

### 6-5. G-6 — 릴레이 모드 (PASS)

4인(host,a,b,c), `relaySwapSec=2`, cycles=1 → `totalRounds=4`.

- `StartGame(1)` → 전원 `GameStart{"gameId":0,"mode":1}`, `RoundBegin{"round":1,"totalRounds":4,"activeId":"local-host",...}`. **`WordAssign{"word":"레몬"}`이 ActiveId(=맞히는 사람) 제외 `a`·`b`·`c` 전원에게** 개별 전송되고, ActiveId인 로컬 host는 `localWord=(null)`·`wordLen=2`.
- Drawing 진입 시 `RelaySwap{"drawerId":"a"}` 1건 (docs/14 §8-6 판정대로).
- 2초씩 주입 → `RelaySwap` drawerId 순서 `b → c → a → b`, 매 교대마다 `StrokeGate`가 현재 drawer만 `True`(나머지와 ActiveId는 `False`).
- 비-ActiveId(`a`)가 정답을 제출 → 피드 **0건** (Relay는 ActiveId만 제출 가능).
- ActiveId(로컬 host)가 정답 → `GuessFeed{"playerId":"local-host","text":"","correct":true}` + `RoundEnd{"word":"레몬","playerIds":["local-host","a","b","c"],"scores":[391,391,391,391],"reason":1}` — **팀 전원 동일 증가** (`100+floor(291.96)=391`).

### 6-6. G-7 — 자유 그리기 무회귀 (PASS)

- Idle 복귀 상태 확인: `IsGameRunning=False`, `hp.StrokesEnabled=True`, `StrokeGate==null`.
- `fake_hand.py 7 --one`(원 궤적, 왼손) → 로컬 스트로크 **3건**(각 `points=29`, `Stroke_Left`), 피어 a 누적 중계 `StrokeStart=4 StrokePoints=272 StrokeEnd=4 CursorUpdate=715`. 사전 검사로 원 궤적 8점 전부 `DrawCanvas` 적중 확인.
- 팔레트 클릭 1건: `fake_hand.py --target 0.8269,0.3539`(=`/Palette/Color_4` 투영, 사전 레이캐스트 `Color_4`) → `ToolState.colorIndex 0→4`, `CurrentColor RGBA(0.100,0.100,0.120)→RGBA(0.200,0.500,0.950)`, 스트로크 증가 0(도구 클릭은 그리지 않는다).
- Loopback 원격 스트로크 재생: 피어 a가 `StrokeStart`/`StrokePoints`/`StrokeEnd`(`a:1`) 송신 → `host.strokes contains(a:1)=True`, `RemotePresenter`가 `RemoteStroke_a:1`(`positions=4`) 생성, 다른 클라에 3건 그대로 중계.
- `NetplayTest.unity`(2D) 등 나머지 씬 3개는 열지 않았고 `git status` 클린.

### 6-7. Task 7에서 새로 관측한 것 (수정하지 않음 — 판단은 메인 세션)

- **T7-① (Minor, 표시):** 게임 시작 직후부터 첫 `RoundEnd`까지 `scoreboardText`가 빈 문자열이다. `ApplyGameStart`가 `_scores`를 비우고 `GameClientState._scores`는 `RoundEnd`/`GameEnd`/`GameStateSync`로만 채워지므로 라운드 1 진행 중에는 점수판에 플레이어 목록조차 안 나온다 (`GameUI.RefreshScoreboard`는 `state.Scores`만 순회). 해결 방향은 `RoundBegin` 수신 시 `netSession.Players`로 0점 행을 채우거나 `GameStart` payload에 참가자 목록을 싣는 것.
- **T7-② (환경, 코드 무관):** 검증 중 Play 모드가 두 번 외부 요인으로 종료됐다(콘솔 예외 0건, Unity PID 동일, `[UdpHandReceiver] listening` 로그가 재진입 없이 1건). 그중 한 번은 host 세션 시작 직후 `게임 시작(기본)` 버튼이 눌린 것처럼 게임이 자동으로 시작됐다(`StartGame`을 호출하는 코드는 두 버튼의 `onClick`뿐). 원인 미확정 — 그래서 각 G 항목을 **한 번의 eval 안에서** 끝내도록 재구성했다. 재검증 시 Play 세션이 길어지면 같은 현상을 의심할 것.
- `--target` 고정 핀치처럼 손이 **정지**해 있으면 `DrawingController`의 `minPointDistance`가 점 추가를 막는데도 `NetSession`은 프레임마다 `StrokePoints`를 보낸다(로컬 1점 vs 와이어 205건). 실제 손은 항상 미세하게 움직여 정상 경로에서는 드러나지 않지만, 대역폭 관점의 알려진 천장으로 기록해 둔다.

## 7. 구현 분담

전역 규칙 §3: 메인 세션(Fable5)은 설계·계획·프롬프트·검수. 구현·검증은 sonnet/opus 하위 에이전트 위임 — 순수 로직·테스트는 sonnet, NetSession 수정·씬 배선·Loopback 검증은 opus. Unity Editor 조작은 `unity cmd`, docs/09 §4 함정 목록을 프롬프트에 포함할 것.

구현 계획(Task 단위 프롬프트): `docs/superpowers/plans/2026-08-27-phase3b-guess-game.md`
