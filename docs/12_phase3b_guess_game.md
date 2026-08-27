# 12. Phase 3b+3c 설계 — 미니게임 프레임워크 + 릴레이 그림 맞추기

> 작성 2026-08-27 · 상태: **설계 승인, 구현 대기**
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

| # | 기준 | 확인 방법 |
|---|---|---|
| G-1 | EditMode: GuessGameLogic 상태 전이 전건·턴 순환(이탈 포함)·점수·조기 종료·릴레이 교대 / GuessJudge 정규화 / WordBank 무중복·재셔플 / NetSession 화이트리스트 중계·StrokeGate / 프로토콜 v3 인코딩 왕복 + 기존 전체 pass | `unity cmd run_tests --mode EditMode` |
| G-2 | **Loopback 게임 전판 실행 검증** — 가짜 피어 2명과 기본 모드 시작→라운드 진행→정답→점수→게임 종료→자유 그리기 복귀. 콘솔 에러 0 | eval 스크립트 |
| G-3 | **드로잉 게이트** — 출제자 차례에 `fake_hand.py`로 실제 스트로크 생성, 가짜 피어(비출제자)의 StrokeStart는 중계·표시 안 됨 | fake_hand + eval |
| G-4 | **정답 입력 실측** — Play 모드에서 InputField 한글 입력·Enter 제출 동작, 타이핑 중 WASD 이동·C 클리어 미발동 | 수동 확인 (사용자) 또는 eval로 InputFocus 게이트 검증 |
| G-5 | **늦은 참가** — 게임 중 가짜 피어 참가 → GameStateSync 수신·관전 상태·다음 라운드 참여 | eval |
| G-6 | 릴레이 모드 — 15초 교대·WordAssign 대상 반전·팀 점수 | eval (시간 가속: durationSec 주입) |
| G-7 | 자유 그리기 무회귀 — 게임 미시작 상태에서 기존 P-2~P-6 경로 정상 (스모크) | eval |
| G-8 | QUALITY_CHECKLIST ≥ 9.0 | 채점 보고 |

## 7. 구현 분담

전역 규칙 §3: 메인 세션(Fable5)은 설계·계획·프롬프트·검수. 구현·검증은 sonnet/opus 하위 에이전트 위임 — 순수 로직·테스트는 sonnet, NetSession 수정·씬 배선·Loopback 검증은 opus. Unity Editor 조작은 `unity cmd`, docs/09 §4 함정 목록을 프롬프트에 포함할 것.

구현 계획(Task 단위 프롬프트): `docs/superpowers/plans/2026-08-27-phase3b-guess-game.md`
