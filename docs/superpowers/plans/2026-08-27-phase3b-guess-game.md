# Phase 3b+3c — 미니게임 프레임워크 + 릴레이 그림 맞추기 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 플레이어 2~4명이 3D 룸에서 릴레이 제시어 그림 맞추기를 한다 — host가 게임을 시작하면 라운드마다 출제자가 이젤에 그리고 나머지가 키보드로 정답을 입력하며, 점수판·타이머·정답 피드가 표시되고 게임이 끝나면 자유 그리기로 복귀한다.

**Architecture:** 게임 계층 분리. `NetSession`은 게임을 모른 채 "게임 메시지 통로"(화이트리스트 중계 + `OnGameMessage` + `SendGame*` + `StrokeGate`)만 열고, 신규 `GameSession`(플러밍) + 순수 로직(`GuessGameLogic` = host 권위 상태 머신, `GameClientState` = 전원 공통 표시 미러)이 그 위에 얹힌다. 판정·전이·점수는 전부 host가 결정해 브로드캐스트하고 클라이언트는 표시만 한다.

**Tech Stack:** Unity 6000.3.15f1 (URP, 새 Input System 전용), legacy uGUI (TMP 미사용), NUnit EditMode, Unity CLI (`unity cmd`), `PythonTracker/fake_hand.py`

**Spec:** `docs/12_phase3b_guess_game.md` (docs/08 §3 프로토콜, docs/09 §4 함정, docs/11 §2 참조 방향도 함께 읽을 것)

**대상 씬:** `Assets/_CameraCoop/Scenes/Netplay3D.unity` 만. 나머지 3개(`NetplayTest`/`DrawingTest`/`HandTrackingTest`)는 무수정.

## Global Constraints

- Unity Editor 자동화는 `unity cmd`만 사용. 시작 전 `unity pipeline list`로 Server Reachable 확인 — 안 뜨면 Editor 창 포커스 필요: `(New-Object -ComObject WScript.Shell).AppActivate(<Unity PID>)`
- **`unity cmd eval`/`eval_file`은 코드를 메서드 본문에 감싼다 — `using` 지시문 금지, 전부 전체 이름**(`CameraCoop.Game.GameSession`). `Object`는 `UnityEngine.Object`로 명시. `eval_file` 파라미터는 `--path`가 아니라 **`--file`**
- **`--timeout`은 command 앞에 온다**: `unity cmd --timeout 300 run_tests --mode EditMode`
- **Play 중 `recompile` 금지**(domain reload NRE burst) — `editor_stop` 먼저. **컴파일 중 `run_tests` 금지** — `editor_status`의 `compiling:false` 확인 후
- **dirty 씬을 열어둔 채 `run_tests` 금지** — test-framework가 무조건 저장한다(docs/09 §4). 씬 수정 후에는 저장하거나 `git checkout --`로 되돌린 뒤 테스트
- instanceId는 Play 진입/domain reload마다 무효화 — 매번 `get_scene_hierarchy`로 재취득
- `capture_game_view --save_path`는 실제로 `Assets/` 밑에 저장 — 검증 후 `.meta`와 함께 삭제. `--width`/`--height` 반드시 명시. `--source screen`은 Play 전용
- UI 버튼이 조용히 안 눌리면 Canvas의 `GraphicRaycaster`부터 확인 (docs/09 §4 실증 — 에러도 로그도 없다)
- 코드 스타일: 기존 파일 규칙 준수 — 핫패스 LINQ 금지, 한글 주석 + docs 섹션 참조, 순수 로직은 UnityEngine 무의존(또는 `internal static`) 분리, 테스트 namespace `CameraCoop.Tests`, 식별자는 영문, 신규 게임 코드 namespace는 `CameraCoop.Game`
- **JsonUtility 제약**: Dictionary 직렬화 불가 — 점수는 `string[] playerIds` + `int[] scores` 평행 배열. 다형성 불가 — payload는 기존처럼 2단 직렬화
- **ProjectSettings·2D 씬 3개·`NetplayUI.cs` 변경 금지**
- 각 Task 끝 `git status`로 의도치 않은 파일 변경 확인. 커밋 메시지: 본문 한글, 식별자 영문
- 이 계획의 모든 EditMode 실행 기준선: **기존 132건 전건 pass 유지** (2026-08-27 기준)

---

### Task 1 — GameProtocol + GuessJudge + WordBank (순수 데이터·함수) (model: sonnet)

프로토콜 payload와 순수 함수만 만든다. 아무도 참조하지 않으므로 씬 동작 불변. 이 Task 단독으로 테스트 전건 통과해야 한다.

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Game/GameProtocol.cs`
- Create: `Assets/_CameraCoop/Scripts/Game/GuessJudge.cs`
- Create: `Assets/_CameraCoop/Scripts/Game/WordBank.cs`
- Create: `Assets/_CameraCoop/Tests/EditMode/GuessJudgeTests.cs`, `WordBankTests.cs`, `GameProtocolTests.cs`

**Interfaces (Task 2~7이 의존 — 임의 변경 금지):**

```csharp
namespace CameraCoop.Game
{
    // 게임 메시지 type 상수 (docs/12 §3). NetProtocol의 드로잉 타입과 겹치지 않는다.
    public static class GameMsg
    {
        public const int GuessGameId = 0;

        public const string TypeGameStart = "GameStart";
        public const string TypeRoundBegin = "RoundBegin";
        public const string TypeWordAssign = "WordAssign";
        public const string TypeRelaySwap = "RelaySwap";
        public const string TypeGuessSubmit = "GuessSubmit";
        public const string TypeGuessFeed = "GuessFeed";
        public const string TypeRoundEnd = "RoundEnd";
        public const string TypeGameEnd = "GameEnd";
        public const string TypeGameAbort = "GameAbort";
        public const string TypeGameStateSync = "GameStateSync";
    }

    [Serializable] public class GameStartPayload { public int gameId; public int mode; }
    [Serializable] public class RoundBeginPayload { public int round; public int totalRounds; public string activeId; public int wordLen; public float introSec; public float durationSec; }
    [Serializable] public class WordAssignPayload { public string word; }
    [Serializable] public class RelaySwapPayload { public string drawerId; }
    [Serializable] public class GuessSubmitPayload { public string text; }
    [Serializable] public class GuessFeedPayload { public string playerId; public string text; public bool correct; }
    [Serializable] public class RoundEndPayload { public string word; public string[] playerIds; public int[] scores; public int reason; } // reason: 0=Timeout 1=AllGuessed 2=ActiveLeft
    [Serializable] public class GameEndPayload { public string[] playerIds; public int[] scores; }
    [Serializable] public class GameStateSyncPayload { public int phase; public int gameId; public int mode; public int round; public int totalRounds; public string activeId; public int wordLen; public float remainingSec; public string[] playerIds; public int[] scores; }

    // 정답 정규화·판정 (docs/12 §1). 상태 없음.
    internal static class GuessJudge
    {
        public static string Normalize(string raw); // null→"". Trim 후 char.IsWhiteSpace 전부 제거, ToLowerInvariant
        public static bool IsMatch(string answer, string guess); // Normalize 양쪽 적용 후 완전 일치. 어느 쪽이든 정규화 결과가 빈 문자열이면 false
    }

    // 단어장: 1줄 1단어 텍스트 → 무중복 랜덤 추출, 소진 시 재셔플 (docs/12 §1)
    public class WordBank
    {
        public WordBank(string textContent, int seed); // 줄 단위 파싱: Trim, 빈 줄 제거, 중복 제거(첫 등장 유지)
        public int Count { get; }                      // 파싱된 고유 단어 수
        public string Next();                          // Count==0이면 null. 한 바퀴 안에서 중복 없음
    }
}
```

- [ ] **Step 1: 실패하는 테스트 작성** — 아래를 전부 덮을 것
  - `GuessJudge.Normalize`: `null`→`""` / `"  사과 "`→`"사과"` / `"사 과"`→`"사과"`(중간 공백 제거) / `"\t소방차\n"`→`"소방차"` / `"Apple"`→`"apple"` / 전각 공백 `"사　과"`→`"사과"`
  - `GuessJudge.IsMatch`: `("사과","사과")` true / `("사과"," 사 과 ")` true / `("사과","사과나무")` false / `("사과","")` false / `("","")` false / `(null,"사과")` false
  - `WordBank` 파싱: `"사과\n\n 바나나 \n사과\n포도"` → Count 3 (중복 사과 1회, 공백 trim)
  - `WordBank.Next` 무중복: Count==N일 때 N회 호출 결과가 전부 다르고 전체 집합과 일치
  - `WordBank` 재셔플: N+1회째 호출이 null이 아니고 집합 원소 중 하나
  - `WordBank` 시드 재현성: 같은 seed 두 인스턴스의 Next 순서 동일 / `WordBank("", 1).Next()` == null
  - `GameProtocol` 직렬화 왕복: `RoundBeginPayload`·`RoundEndPayload`(평행 배열 포함)·`GameStateSyncPayload`를 `JsonUtility.ToJson`→`FromJson` 왕복해 전 필드 보존. 한글 문자열(`WordAssignPayload { word = "소방차" }`) 왕복 보존
- [ ] **Step 2: 테스트 실행 → 컴파일 에러 또는 fail 확인** (`unity cmd --timeout 300 run_tests --mode EditMode`)
- [ ] **Step 3: 구현** — `WordBank`는 `System.Random(seed)` + Fisher-Yates. UnityEngine 참조는 GameProtocol(Serializable용 System만 필요하므로 사실상 불필요)까지 최소화. LINQ 금지
- [ ] **Step 4: 전체 테스트 통과** — 기존 132건 + 신규분. **결과 수치를 그대로 인용해 보고**
- [ ] **Step 5: Commit** — `feat: 게임 프로토콜 payload + 정답 판정 + 단어장 (Phase 3b Task 1)`

**Verification:** EditMode 전건 pass. `git status`에 신규 파일과 `.meta`만.

---

### Task 2 — GuessGameLogic 상태 머신 (host 권위 순수 로직) (model: opus)

게임의 두뇌. UnityEngine 무의존 — 시간은 `Tick(deltaTime)`으로 주입받는다. host의 GameSession(Task 5)만 이 클래스를 구동한다.

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Game/GuessGameLogic.cs`
- Create: `Assets/_CameraCoop/Tests/EditMode/GuessGameLogicTests.cs`

**Interfaces (Task 5가 의존 — 임의 변경 금지):**

```csharp
namespace CameraCoop.Game
{
    public class GuessGameLogic
    {
        public enum Phase { Idle = 0, RoundIntro = 1, Drawing = 2, RoundReveal = 3, GameEnd = 4 } // GameStateSyncPayload.phase와 같은 인코딩
        public enum GameMode { Turns = 0, Relay = 1 }
        public enum RoundEndReason { Timeout = 0, AllGuessed = 1, ActiveLeft = 2 }               // RoundEndPayload.reason과 같은 인코딩
        public enum Transition { None, ToRoundIntro, ToDrawing, ToRoundReveal, ToGameEnd, ToIdle, RelaySwap }
        public enum GuessResult { Ignored, Wrong, Correct, CorrectAndRoundEnd }

        public GuessGameLogic(WordBank words, float introSec, float drawSec, float revealSec, float gameEndSec, float relaySwapSec);

        public Phase CurrentPhase { get; }
        public GameMode Mode { get; }
        public int Round { get; }                 // 1-based. Idle에서 0
        public int TotalRounds { get; }           // 남은 큐 기준 — 이탈로 줄 수 있다
        public string ActiveId { get; }           // Turns: 출제자 / Relay: 맞히는 사람
        public string CurrentDrawerId { get; }    // Turns: ActiveId와 동일 / Relay: 현재 교대 그리는 사람
        public string CurrentWord { get; }        // Drawing·RoundReveal에서 유효
        public float PhaseRemaining { get; }      // 현재 phase 남은 초
        public RoundEndReason LastRoundEndReason { get; }
        public IReadOnlyDictionary<string, int> Scores { get; }

        public bool StartGame(IReadOnlyList<string> playerIds, GameMode mode, int cycles); // 성공 시 RoundIntro 진입 + 1라운드 세팅
        public Transition Tick(float deltaTime);       // 시간 전이. 호출당 전이 최대 1개
        public GuessResult SubmitGuess(string playerId, string text);
        public Transition PlayerLeft(string playerId);
        public void AddPlayer(string playerId);        // 늦은 참가: 점수 0 등록 + 순환 큐 끝에 1회 추가 + 다음 라운드부터 정답 자격
        public void Abort();                           // 어느 상태에서든 Idle로
    }
}
```

**행동 규칙 (테스트가 이 표를 그대로 덮는다):**

| 항목 | 규칙 |
|---|---|
| StartGame 거부 | 인원 < 2 → false. `Relay`이고 인원 < 3 → false. 이미 진행 중(Phase != Idle) → false |
| 순환 큐 | StartGame 시 `playerIds` 순서 × cycles 회 반복으로 큐 구성. TotalRounds = 큐 길이. 라운드마다 큐 앞에서 ActiveId를 꺼낸다 |
| 시간 전이 | RoundIntro --introSec--> Drawing --drawSec--> RoundReveal --revealSec--> (큐 남음 ? ToRoundIntro : ToGameEnd) --gameEndSec--> ToIdle. Tick 반환값이 해당 Transition |
| 릴레이 교대 | Relay 모드 Drawing 중 relaySwapSec마다 `Transition.RelaySwap` 반환, CurrentDrawerId가 (참가자 − ActiveId) 목록을 순환. 첫 drawer는 Drawing 진입 시 결정 |
| 정답(Turns) | 정답자 += `100 + (int)PhaseRemaining`, ActiveId(출제자) += 50. 출제자 외 전원 정답 시 `CorrectAndRoundEnd` + 즉시 RoundReveal 진입(LastRoundEndReason=AllGuessed) |
| 정답(Relay) | ActiveId(맞히는 사람)만 제출 가능. 정답 시 **참가자 전원** += `100 + (int)PhaseRemaining`, `CorrectAndRoundEnd` + RoundReveal(AllGuessed) |
| Ignored 조건 | Phase != Drawing / Turns에서 ActiveId 본인 / Relay에서 ActiveId 아닌 사람 / 이미 맞힌 사람 / 미참가자(늦은 참가 당 라운드 포함) / 정규화 결과 빈 문자열 / 정규화 길이 > 64 |
| 오답 | `Wrong` 반환. 상태 변화 없음 (피드 브로드캐스트는 GameSession 몫) |
| 이탈: ActiveId | Drawing·RoundIntro 중 → 즉시 RoundReveal(ActiveLeft, 점수 없음), `Transition.ToRoundReveal` 반환. 큐에서 그 플레이어의 미래 항목 전부 제거 |
| 이탈: 일반 | 큐에서 미래 항목 제거 + Relay면 drawer 순환에서 제외(현재 drawer였으면 즉시 교대 = `RelaySwap` 반환). 그 외 `None` |
| 이탈: 인원 < 2 | 어느 phase든 Idle로, `Transition.ToIdle` 반환 (GameSession이 GameAbort 송신) |
| 늦은 참가 | AddPlayer는 Idle에서는 무시. 진행 중이면 Scores에 0 등록 + 큐 끝에 1회 추가(TotalRounds 증가) + `Round+1`부터 SubmitGuess 허용 |
| 단어 | 라운드 세팅 시 `words.Next()`. CurrentWord는 RoundReveal까지 유지, Idle 복귀 시 null |
| 점수 보존 | 이탈자의 Scores 항목은 지우지 않는다 (최종 점수판 표시용) |

- [ ] **Step 1: 실패하는 테스트 작성** — 위 표 전건 + 아래 시나리오 테스트
  - 4인 cycles=2 → TotalRounds 8, ActiveId가 `[A,B,C,D,A,B,C,D]` 순서로 순환
  - 점수 정확성: PhaseRemaining이 42.7일 때 정답 → 정답자 +142, 출제자 +50
  - 조기 종료: 3인 게임에서 게서 2명 모두 정답 → 두 번째 정답이 `CorrectAndRoundEnd`, 출제자 +100
  - Relay: introSec=0, drawSec=60, relaySwapSec=15로 Tick을 15초씩 → drawer가 (B,C,D,B...) 순환, ActiveId=A 고정
  - Relay 정답: A 정답 시 4인 전원 같은 점수 증가
  - 타임아웃: drawSec 경과 Tick → ToRoundReveal + LastRoundEndReason=Timeout
  - ActiveId 이탈 → ToRoundReveal + ActiveLeft + 다음 라운드에서 그 사람 안 나옴
  - 2인 게임에서 1명 이탈 → ToIdle
  - AddPlayer: Drawing 중 참가 → 당 라운드 SubmitGuess Ignored, 다음 라운드부터 Wrong/Correct, 큐 끝에 등장
  - Tick 호출당 전이 1개: introSec=1, deltaTime=100 한 번 → ToDrawing만 (RoundReveal 아님)
- [ ] **Step 2: 테스트 실행 → fail 확인**
- [ ] **Step 3: 구현** — 내부는 `List<string> upcomingQueue`, `HashSet<string> guessedThisRound`, `Dictionary<string,int> eligibleFromRound`. 프레임 핫패스 아님이지만 LINQ 금지 규칙은 동일
- [ ] **Step 4: 전체 테스트 통과** (수치 인용)
- [ ] **Step 5: Commit** — `feat: 그림 맞추기 게임 상태 머신 (Phase 3b Task 2)`

**Verification:** EditMode 전건 pass. UnityEngine using 0건 (`grep -n "UnityEngine" GuessGameLogic.cs` 결과 없음).

---

### Task 3 — NetProtocol v3 + NetSession 게임 통로 6건 (model: opus)

`NetSession`이 게임을 모른 채 통로만 여는 Task. docs/12 §2 표의 6건을 그대로 구현한다.

**Files:**
- Modify: `Assets/_CameraCoop/Scripts/Netplay/NetProtocol.cs`
- Modify: `Assets/_CameraCoop/Scripts/Netplay/NetSession.cs`
- Modify: `Assets/_CameraCoop/Tests/EditMode/ProtocolTests.cs` 또는 `ProtocolV2Tests.cs` (v3 반영), `NetplayTests.cs`

**Interfaces (Task 5가 의존 — 임의 변경 금지):**

```csharp
// NetProtocol.cs
public const int Version = 3; // v3: 게임 메시지 타입 도입 (docs/12 §3). 구버전 envelope는 기존 규칙대로 폐기

// NetSession.cs 추가분
public event Action<string, string, string> OnGameMessage; // (type, senderPlayerId, payloadJson) — 화이트리스트 밖 타입 전부
public event Action<string> OnPeerJoinedSession;           // host: HandleHello 직후 / 클라: PeerJoined 적용 직후
public string HostPlayerId { get; }                        // host: LocalPlayerId / 클라: Welcome sender / 세션 없음: null
public Func<string, bool> StrokeGate { get; set; }         // null = 전원 허용. 스트로크 4종에만 적용, 커서엔 미적용
public void BroadcastGameMsg<T>(string type, T payload, string exceptId = null); // host 전용, reliable
public void SendGameTo<T>(string playerId, string type, T payload);              // host 전용, reliable
public void SendGameToHost<T>(string type, T payload);                           // 클라 전용, reliable
```

- [ ] **Step 1: 실패하는 테스트 작성** — LoopbackTransport 기반 기존 NetplayTests 패턴 재사용
  - v2 envelope 폐기: `v=2`로 인코딩한 바이트 → `Decode` null
  - 화이트리스트 중계: 가짜 클라가 host에 `StrokeStart` 송신 → 다른 클라에 중계됨 / 임의 타입 `"GuessSubmit"` 송신 → **다른 클라에 중계 안 됨** + host의 `OnGameMessage`가 (type, sender, payload)로 발화
  - 클라 수신: host가 임의 타입 브로드캐스트 → 클라 `OnGameMessage` 발화
  - `StrokeGate`: host에 `p => p == "allowed"` 설정 → "denied" 클라의 `StrokeStart`가 중계·`OnRemoteStrokeStart` 모두 없음 / "allowed"는 통과 / **`CursorUpdate`는 gate 무관 통과**
  - `StrokeGate` 로컬: host 로컬 스트로크 시작 시 gate가 자기 id를 거부하면 `strokes`에 안 쌓이고 송신 없음
  - `HostPlayerId`: host 세션 시작 → 자기 id / 클라 Welcome 적용 후 → host id / StopSession → null
  - `OnPeerJoinedSession`: host에서 Hello 처리 시 새 피어 id로 발화
  - `SendGameTo`/`BroadcastGameMsg`/`SendGameToHost` 왕복: 임의 payload가 수신 측 `OnGameMessage`에 JSON 그대로 도착
- [ ] **Step 2: 테스트 실행 → fail 확인**
- [ ] **Step 3: 구현**
  - `HandleMessage`의 host 중계 분기를 `RelayRaw(무조건)` → 화이트리스트로 교체: `static readonly HashSet<string> RelayTypes = { TypeCursor, TypeStrokeStart, TypeStrokePoints, TypeStrokeEnd, TypeStrokeErase, TypeClear }`
  - 스트로크 4종(`StrokeStart/Points/End/Erase`)은 중계·Apply 전에 `StrokeGate` 검사 — 거부면 조용히 폐기(스팸 방지 위해 로그 없음, 주석으로 이유 명시)
  - 화이트리스트 밖 타입: 중계하지 않고 `OnGameMessage?.Invoke(env.type, env.sender, env.payload)` 후 return (Apply의 switch에 안 들어간다)
  - `HandleLocalStrokeStart` 첫 줄에 `if (StrokeGate != null && !StrokeGate(transport.LocalPlayerId)) return;`
  - 송신 3종은 기존 `Broadcast`/`SendToHostMsg`/`transport.SendTo` 재사용. host/클라 역할이 안 맞으면 `Debug.LogWarning` 1회 후 무시(조용한 실패 금지)
  - `HostPlayerId`: StartSession(host면 자기 id) / Apply의 Welcome 케이스(`env.sender`) / StopSession(null)
  - `OnPeerJoinedSession`: `HandleHello` 끝 + Apply `PeerJoined` 케이스 끝에서 발화
  - **주의: `NetplayUI`·`RemotePresenter`·transport 3종은 무수정** — `git diff --stat`로 확인
- [ ] **Step 4: 전체 테스트 통과** (수치 인용). 기존 테스트 중 v2를 하드코딩한 것은 v3로 갱신하되 **검증 의도는 유지**
- [ ] **Step 5: Commit** — `feat: 프로토콜 v3 — 게임 메시지 통로 + 중계 화이트리스트 + StrokeGate (Phase 3b Task 3)`

**Verification:** EditMode 전건 pass. `git diff --stat`에 NetProtocol/NetSession/테스트만.

---

### Task 4 — 입력 게이트: InputFocus + HandPointer.StrokesEnabled + WASD/C키 차단 (model: sonnet)

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Input/InputFocus.cs`
- Modify: `Assets/_CameraCoop/Scripts/Input/HandPointer.cs`, `PointerRouteLogic.cs`, `PlayerController.cs`
- Modify: `Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs`
- Modify: `Assets/_CameraCoop/Tests/EditMode/PointerRouteTests.cs`

**Interfaces (Task 5·6이 의존):**

```csharp
// InputFocus.cs — 타이핑 중 게임플레이 키 차단의 단일 출처 (docs/12 §2). GameUI가 쓰고 PlayerController·DrawingController가 읽는다
namespace CameraCoop { public static class InputFocus { public static bool IsTyping; } }

// HandPointer.cs 추가
public bool StrokesEnabled { get; set; } // 기본 true. false 설정 순간 진행 중 스트로크 전부 End 발행. 도구 클릭은 계속 허용

// PointerRouteLogic.cs — 4번째 인자 오버로드 (기존 3인자는 strokesEnabled:true로 위임 유지)
public static RouteAction Decide(HitKind hit, StrokeLogic.PinchKind kind, bool isDrawing, bool strokesEnabled);
```

- [ ] **Step 1: 실패하는 테스트 작성**
  - `Decide` 4인자 분기표: `strokesEnabled=false`일 때 — Start+Canvas → **None** / Start+Tool → ClickTool(도구는 허용) / Move+Canvas+isDrawing → **EndStroke** / End+isDrawing → EndStroke / 기존 3인자 호출이 4인자 `true`와 전 조합 동일(회귀 가드)
- [ ] **Step 2: 테스트 실행 → fail 확인**
- [ ] **Step 3: 구현**
  - `PointerRouteLogic.Decide` 오버로드: `!strokesEnabled`면 스트로크 신규·계속을 차단하되 진행 중이던 것은 End로 회수. 기존 3인자는 `Decide(hit, kind, isDrawing, true)` 위임
  - `HandPointer.Route`가 4인자 버전 호출. `StrokesEnabled` setter: false 전환 시 `isDrawing`이 true인 손 전부에 `EndStroke(hand)` 호출 (라운드 종료 시 그리다 만 선 고아 방지 — docs/12 §5)
  - `PlayerController.Update`: keyboard null 가드 다음에 `if (InputFocus.IsTyping) return;` — **마우스 룩(ApplyLook)은 그 앞에 둬 유지** (우클릭 홀드라 타이핑과 충돌 없음)
  - `DrawingController.Update`: clearKey 검사에 `!InputFocus.IsTyping` 조건 추가. 주석: `// 정답 타이핑 중 C키가 캔버스를 지우는 사고 방지 (docs/12 §2)`
- [ ] **Step 4: 전체 테스트 통과** (수치 인용)
- [ ] **Step 5: Commit** — `feat: 타이핑 게이트 + 스트로크 게이트 (Phase 3b Task 4)`

**Verification:** EditMode 전건 pass. `Netplay3D.unity` 무변경(`git status`).

---

### Task 5 — GameClientState + GameSession (플러밍) (model: opus)

표시 상태는 **host 포함 전원이 `GameClientState` 한 경로**를 쓴다 — host는 브로드캐스트 직후 같은 payload를 자기 미러에 적용한다. 씬 배선 전이므로 이 Task까지 씬 동작 불변.

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Game/GameClientState.cs`
- Create: `Assets/_CameraCoop/Scripts/Game/GameSession.cs`
- Create: `Assets/_CameraCoop/Tests/EditMode/GameClientStateTests.cs`, `GameSessionTests.cs`

**Interfaces (Task 6이 의존 — 임의 변경 금지):**

```csharp
namespace CameraCoop.Game
{
    // 수신 메시지 → 표시 상태 미러 (순수 — UnityEngine의 Mathf 정도만 허용). 판단하지 않는다: host가 보낸 것을 그대로 반영
    public class GameClientState
    {
        public GuessGameLogic.Phase CurrentPhase { get; }
        public int Mode { get; }
        public int Round { get; }
        public int TotalRounds { get; }
        public string ActiveId { get; }
        public string CurrentDrawerId { get; }      // Turns면 ActiveId와 동일, Relay면 RelaySwap 반영
        public string LocalWord { get; }            // WordAssign 수신 시. 못 받았으면 null → UI는 wordLen 힌트 표시
        public int WordLen { get; }
        public float RemainingSec { get; }          // 표시용 로컬 카운트다운
        public bool Spectator { get; }              // GameStateSync로 합류 → 다음 RoundBegin까지 true
        public IReadOnlyList<GuessFeedPayload> Feed { get; }        // 최근 32개 유지
        public IReadOnlyDictionary<string, int> Scores { get; }

        // 각 Apply는 "표시 상태가 바뀌었으면 true" — GameSession이 이벤트 발행 조건으로 쓴다
        public bool ApplyGameStart(GameStartPayload p);
        public bool ApplyRoundBegin(RoundBeginPayload p);   // Spectator 해제, LocalWord/피드 리셋, RoundIntro 진입 + introSec 카운트다운
        public bool ApplyWordAssign(WordAssignPayload p);
        public bool ApplyRelaySwap(RelaySwapPayload p);
        public bool ApplyGuessFeed(GuessFeedPayload p);
        public bool ApplyRoundEnd(RoundEndPayload p);       // 점수 평행 배열 → Scores 갱신, RoundReveal 진입
        public bool ApplyGameEnd(GameEndPayload p);
        public bool ApplyGameAbort();                        // 즉시 Idle
        public bool ApplyGameStateSync(GameStateSyncPayload p); // 늦은 참가: 전체 미러 + Spectator=true
        public bool Tick(float deltaTime);                   // RoundIntro→Drawing 자체 전환, RemainingSec 감소, GameEnd→(gameEndSec 후)Idle
        public bool CanGuess(string localPlayerId);          // Drawing && !Spectator && localPlayerId가 정답 자격 (Turns: ActiveId 아님 / Relay: ActiveId 본인) && 아직 안 맞힘
    }

    // MonoBehaviour 플러밍: NetSession 게임 통로 ↔ (host) GuessGameLogic / (전원) GameClientState (docs/12 §2)
    public class GameSession : MonoBehaviour
    {
        // [SerializeField]: NetSession netSession, HandPointer handPointer, TextAsset wordAsset,
        //                   float introSec=3, drawSec=90, revealSec=5, gameEndSec=8, relaySwapSec=15, int cycles=2
        public GameClientState State { get; }       // UI가 읽는 표시 상태 (항상 non-null)
        public bool IsGameRunning { get; }           // State.CurrentPhase != Idle
        public event Action OnStateChanged;          // 표시 상태 변화 통지 (덩어리 1개 — 세분화 YAGNI)

        public bool CanStartGame(int mode);          // host && 세션 중 && !IsGameRunning && 인원>=2 (Relay는 >=3)
        public void StartGame(int mode);             // host 전용 (UI 버튼)
        public void SubmitGuess(string text);        // 로컬 입력 (UI 입력창). host면 로직 직접, 클라면 SendGameToHost
    }
}
```

**host 전이 → 송신 매핑 (GameSession 내부 규칙 — 테스트가 덮는다):**

| 로직 이벤트 | host가 하는 일 |
|---|---|
| StartGame 성공 | `BroadcastGameMsg(GameStart)` → 라운드 세팅 공통(아래) |
| 라운드 세팅 공통 (StartGame 직후·ToRoundIntro) | `netSession.SendClear()` → `BroadcastGameMsg(RoundBegin{...})` → WordAssign: Turns면 출제자 1명에게 `SendGameTo`(출제자==로컬이면 로컬 적용만), Relay면 ActiveId 제외 전원에게 각각 `SendGameTo`(+로컬이 drawer면 로컬 적용) → 게이트 갱신 |
| Tick=ToDrawing | 메시지 없음 (클라 자체 전환). 게이트 갱신 |
| Tick=RelaySwap | `BroadcastGameMsg(RelaySwap{drawerId})` + 게이트 갱신 |
| SubmitGuess=Wrong | `BroadcastGameMsg(GuessFeed{playerId, text, false})` |
| SubmitGuess=Correct | `BroadcastGameMsg(GuessFeed{playerId, "", true})` — **text를 비운다 (정답 유출 방지, docs/12 §2)** |
| SubmitGuess=CorrectAndRoundEnd | 위 GuessFeed + `BroadcastGameMsg(RoundEnd{word, ids, scores, AllGuessed})` |
| Tick=ToRoundReveal | `BroadcastGameMsg(RoundEnd{word, ids, scores, LastRoundEndReason})` |
| Tick=ToGameEnd | `BroadcastGameMsg(GameEnd{ids, scores})` |
| Tick=ToIdle | 메시지 없음. 게이트 전부 해제 (StrokeGate=null, StrokesEnabled=true) |
| PlayerLeft→ToIdle | `BroadcastGameMsg(GameAbort)` |
| OnPeerJoinedSession && 게임 중 | `logic.AddPlayer(id)` → `SendGameTo(id, GameStateSync{현재 상태})` |
| 세션 종료 (host 이탈·StopSession — `OnPlayersChanged` 시 `!netSession.IsRunning`) | host·클라 공통: State를 Idle로 리셋 + 게이트 전부 해제 (docs/12 §5 "host 이탈 — 게임도 함께 끝") |

**게이트 갱신 규칙 (host·클라 공통 함수 1개로):**
- `handPointer.StrokesEnabled` = `!IsGameRunning || (State.CurrentPhase == Drawing && State.CurrentDrawerId == netSession.LocalPlayerId)`
- host만: `netSession.StrokeGate` = 게임 중이면 `p => State.CurrentPhase == Drawing && p == State.CurrentDrawerId`, Idle이면 `null`
- 세션 자체가 없으면(자유 로컬) 항상 전부 허용

**클라 수신 규칙:** `OnGameMessage(type, sender, json)`에서 **`sender != netSession.HostPlayerId`면 무시** (위조 방어, docs/12 §5). type별 `JsonUtility.FromJson<T>` → `State.ApplyX` → true면 `OnStateChanged`. host는 `GuessSubmit` 타입만 처리(sender = 제출자).

- [ ] **Step 1: GameClientState 실패하는 테스트 작성**
  - RoundBegin 적용 → RoundIntro, introSec 카운트다운, Tick(introSec) 후 Drawing + RemainingSec=durationSec
  - WordAssign → LocalWord 설정, 다음 RoundBegin에서 null로 리셋
  - GuessFeed 32개 초과 → 오래된 것부터 버림
  - RoundEnd 평행 배열 → Scores 사전 일치, RoundReveal 진입
  - GameStateSync → Spectator true + 상태 미러, 다음 RoundBegin에서 Spectator false
  - CanGuess: Turns에서 (게서, Drawing) true / 출제자 false / Spectator false / 이미 correct 피드에 자기 id 있으면 false / Relay에서 ActiveId만 true
  - GameEnd 후 Tick(gameEndSec) → Idle
- [ ] **Step 2: GameSession 실패하는 테스트 작성** — LoopbackTransport로 host NetSession + GameObject.AddComponent 패턴(기존 NetplayTests 참조). Update 대신 내부 tick을 직접 호출할 수 있게 `internal void TickForTest(float dt)` 노출
  - host StartGame(Turns) → 가짜 클라가 GameStart·RoundBegin 수신, 출제자만 WordAssign 수신
  - 가짜 클라 GuessSubmit(오답) → 전원 GuessFeed(text 보존) / 정답 → GuessFeed text=="" + RoundEnd 도착
  - 비출제자 클라의 StrokeStart가 다른 클라에 안 감 (StrokeGate 배선 확인)
  - 게임 중 신규 Hello → 그 피어만 GameStateSync 수신
  - 클라 역할 GameSession: host 아닌 sender의 RoundBegin 위조 → State 무변화
  - 게임 중 StopSession → State가 Idle로 리셋되고 StrokesEnabled 복원
- [ ] **Step 3: 테스트 실행 → fail 확인**
- [ ] **Step 4: 구현** — GameSession.Update: host면 `logic.Tick(Time.deltaTime)` 전이 처리, 전원 `State.Tick`. `wordAsset` null이면 `Debug.LogError` + StartGame 거부(조용한 실패 금지). WordBank seed는 `Environment.TickCount`
- [ ] **Step 5: 전체 테스트 통과** (수치 인용)
- [ ] **Step 6: Commit** — `feat: GameSession 플러밍 + 표시 미러 (Phase 3b Task 5)`

**Verification:** EditMode 전건 pass. 씬 파일 무변경.

---

### Task 6 — GameUI + 단어장 + Netplay3D 씬 배선 (model: sonnet)

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Game/GameUI.cs`
- Create: `Assets/_CameraCoop/Data/words_ko.txt`
- Modify: `Assets/_CameraCoop/Scenes/Netplay3D.unity`

- [ ] **Step 1: words_ko.txt 생성** — 1줄 1단어, 아래 150개 그대로:

```
사과 바나나 포도 수박 딸기 복숭아 감 배 귤 레몬
당근 오이 양파 감자 고구마 버섯 옥수수 호박 가지 배추
강아지 고양이 토끼 사자 호랑이 코끼리 기린 원숭이 곰 판다
여우 늑대 사슴 말 소 돼지 닭 오리 펭귄 부엉이
독수리 참새 고래 상어 문어 오징어 새우 게 거북이 개구리
뱀 나비 벌 잠자리 달팽이 거미 개미 모기 병아리 공룡
자동차 버스 트럭 기차 지하철 비행기 헬리콥터 돛단배 자전거 오토바이
소방차 경찰차 구급차 로켓 잠수함 집 학교 병원 성 탑
다리 등대 텐트 의자 책상 침대 소파 냉장고 세탁기 텔레비전
컴퓨터 휴대폰 시계 카메라 선풍기 우산 안경 모자 장갑 목도리
양말 신발 치마 바지 넥타이 가방 반지 왕관 칼 가위
망치 톱 삽 빗자루 국자 냄비 주전자 접시 컵 숟가락
젓가락 포크 칫솔 비누 수건 거울 열쇠 연필 지우개 책
붓 풀 피아노 기타 바이올린 드럼 마이크 축구공 야구방망이 농구공
스키 낚싯대 해 달 별 구름 눈사람 무지개 번개 산
바다 나무 꽃 선인장 피자 햄버거 김밥 라면 케이크 아이스크림
사탕 도넛 계란 빵 치즈 로봇 풍선 인형 연 주사위
소방관 의사 요리사 화가 가수 공주 마법사 해적 유령 산타클로스
```

  (한 줄에 하나씩 개행으로 분리해 저장할 것 — 위 표기는 지면 절약용. 실제 파일은 180개 = 150 목표 초과분 포함, `WordBank` 파싱이 개수를 확정한다)

- [ ] **Step 2: GameUI 작성** — 로직 없음, 표시와 입력만. `[SerializeField]`: `GameSession gameSession`, `NetSession netSession`, `Text bannerText`(라운드·안내), `Text timerText`, `Text wordText`(제시어/힌트), `Text scoreboardText`, `Text feedText`(최근 6줄), `InputField guessInput`, `Button startTurnsButton`, `Button startRelayButton`
  - `OnEnable/OnDisable`: `gameSession.OnStateChanged` + `netSession.OnPlayersChanged` 대칭 구독 → `Refresh()`
  - `Update()`: ① `InputFocus.IsTyping = guessInput != null && guessInput.isFocused` (단일 출처 — 이벤트 아닌 폴링이 포커스 상실 누락이 없다) ② 타이머 텍스트 갱신(`State.RemainingSec`, 0.1초 간격) ③ `Keyboard.current.enterKey.wasPressedThisFrame && !guessInput.isFocused && CanGuess`면 `guessInput.ActivateInputField()`
  - 제출: `guessInput.onEndEdit` 리스너에서 `Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame`일 때만 `gameSession.SubmitGuess(text)` → 입력창 비우고 재포커스. (포커스만 잃은 onEndEdit는 제출 아님)
  - `Refresh()` 표시 규칙: 시작 버튼 = `CanStartGame(mode)`일 때만 interactable+표시 / wordText = 내가 그리는 쪽이면 `State.LocalWord`, 아니면 `"◯"×WordLen` / 배너 = phase별(`라운드 2/8 — Alice님이 그립니다`, `정답: 사과`, 최종 순위) / 점수판 = `netSession.Players` 이름과 `State.Scores` 병합, StringBuilder(NetplayUI.Refresh 패턴) / 입력창 활성 = `State.CanGuess(netSession.LocalPlayerId)`
  - 플레이어 이름 조회는 `netSession.Players`에서 — 없으면 id 그대로 표시(이탈자)
- [ ] **Step 3: 컴파일 확인** — `unity cmd recompile` 후 콘솔 에러 0
- [ ] **Step 4: 씬 배선** (docs/09 §4 절차 준수 — 배선 후 씬 저장, 그 다음에만 run_tests)
  - `GameSession` 빈 오브젝트 생성 + 컴포넌트 부착: `netSession`·`handPointer`·`wordAsset`(words_ko.txt) 배선
  - 기존 UI Canvas 아래 `GameUI` 오브젝트 + 자식 UI 요소들 생성: 상단 중앙 banner/timer/word, 우측 scoreboard/feed, 하단 guessInput(InputField + 자식 Text·Placeholder, 폰트 `LegacyRuntime.ttf`), host 버튼 2개(기존 Host 버튼 옆). **Canvas에 `GraphicRaycaster` 존재 확인** (docs/09 §4)
  - GameUI 참조 전부 배선 후 `SerializedObject`로 각 필드 non-null 하나씩 출력 (Phase 3d Task 4 방식 — 증거 남김)
  - 씬 저장 → EditMode 전건 (`NetplaySceneTests` 포함)
- [ ] **Step 5: Commit** — `feat: 게임 UI + 단어장 + Netplay3D 배선 (Phase 3b Task 6)`

**Verification:** `git status`에 신규 파일·`Netplay3D.unity`만. 다른 씬 3개 무변경. EditMode 전건 pass.

---

### Task 7 — 통합 검증 G-2 ~ G-7 (model: opus)

docs/12 §6 DoD를 실제로 실행해 증거를 남긴다. **추측 금지 — 실행 출력·캡처만 인용.** 검증용 eval 스크립트는 스크래치 디렉터리에 두고 커밋하지 않는다.

- [ ] **Step 1: G-2 Loopback 게임 전판** — Play → Host Loopback → eval로 가짜 클라 2명 접속(`LoopbackTransport(isHost:false, ...)` — NetplayTests 패턴) → `GameSession.StartGame(0)` → 출제자 확인 → 가짜 클라로 오답 1회·정답 제출 → 피드·점수 확인 → 전 라운드 소화(테스트 편의로 Inspector에서 `drawSec` 10으로 낮춰 진행 가능) → GameEnd → Idle 복귀 → 자유 그리기 동작. **콘솔 에러 0**
- [ ] **Step 2: G-3 드로잉 게이트** — 로컬이 출제자인 라운드에 `fake_hand.py --target`으로 실제 UDP 스트로크 1개 생성(스트로크 오브젝트 존재 인용). 가짜 클라(비출제자)가 `StrokeStart` 송신 → host `strokes`에 없고 다른 클라에 미중계 인용. 로컬이 비출제자일 때 fake_hand 핀치 → 스트로크 0개
- [ ] **Step 3: G-4 입력 게이트** — Play 중 eval로 `guessInput.ActivateInputField()` → `InputFocus.IsTyping == true` 인용 → `PlayerController.Step` 경로가 Update에서 안 불리는지(위치 불변) + C키 시뮬레이션 대신 코드 검사로 게이트 조건 인용. **한글 IME 실타이핑은 자동화 불가 — 최종 보고에 "사용자 확인 필요" 항목으로 명시**
- [ ] **Step 4: G-5 늦은 참가** — 게임 중 가짜 클라 3번째 접속 → `GameStateSync` 수신 payload 인용, Spectator true, 다음 라운드 RoundBegin 후 false + 큐에 등장
- [ ] **Step 5: G-6 릴레이 모드** — `StartGame(1)` (introSec·relaySwapSec 축소 주입) → RelaySwap 브로드캐스트 순서·WordAssign 대상(ActiveId 제외 전원) ·팀 점수 동시 증가 인용
- [ ] **Step 6: G-7 무회귀** — 게임 미시작 상태에서 fake_hand 원 궤적 드로잉 + 팔레트 클릭 1건 + Loopback 원격 스트로크 재생 smoke. `NetplayTest.unity`(2D)는 열지 않는다 — 무수정 확인만 (`git status`)
- [ ] **Step 7: 정리** — `Assets/` 밑 캡처·임시 파일 `.meta`와 함께 삭제, `git status` 클린
- [ ] **Step 8: docs/12 §6 표에 결과 기입** (PASS/FAIL + 인용 근거)

**Verification:** G-2~G-7 전건 PASS (G-4 IME 항목만 "사용자 확인 대기" 허용). 실패 항목은 해당 Task로 되돌아가 수정 후 재실행 — **부분 통과로 넘기지 않는다.**

---

### Task 8 — QUALITY_CHECKLIST 채점 + 문서 마감 (model: opus)

- [ ] **Step 1:** `QUALITY_CHECKLIST.md` 전 항목 채점 — 추측 만점 금지, 감점 사유 우선 탐색, 검증 점수는 Task 7 실행 결과 인용
- [ ] **Step 2:** 9.0 미만이면 코드 개선 → 재채점 (점수 이력 기록)
- [ ] **Step 3:** `docs/12_phase3b_guess_game.md` 상태를 "구현 완료"로 갱신 + 채점 섹션(항목별 점수표/총점/근거/감점 요인) 추가
- [ ] **Step 4:** `docs/09_handoff_windows.md` §1 표에 3b 행 갱신 (미니게임 프레임워크 + 첫 게임 완료, park 항목 중 "중계 경로 권한 검사 없음"·"로컬 C키 Clear 네트워크 우회" 해소 여부 정정 — C키는 게이트만 추가됐고 네트워크 통합은 여전히 미해결이면 그대로 남길 것)
- [ ] **Step 5:** Commit — `docs: Phase 3b 검증 결과 + 채점 기록`

**Verification:** 총점 ≥ 9.0. 문서와 코드 불일치 0.

---

## 주의: 이 Phase에서 깨지기 쉬운 것

1. **`GuessSubmit`은 절대 중계되면 안 된다** — 화이트리스트가 이것을 보장한다. 중계되는 순간 오답·정답이 전원에게 유출된다.
2. **`WordAssign`은 `SendGameTo`만** — `BroadcastGameMsg`로 보내는 순간 게임이 성립하지 않는다. Turns는 출제자 1명, Relay는 ActiveId **제외** 전원.
3. **정답 피드는 text를 비워 보낸다** — `GuessFeed{correct:true}`에 원문을 실으면 아직 못 맞힌 사람에게 정답이 보인다.
4. **클라는 host sender의 게임 메시지만 적용** — `HostPlayerId` 비교. 이게 없으면 아무 클라나 RoundBegin을 위조해 전원 화면을 바꾼다.
5. **StrokeGate는 스트로크 4종에만** — `CursorUpdate`에 적용하면 게임 중 커서가 전부 사라진다.
6. **타이머 권위는 host** — 클라 카운트다운은 표시 전용. 클라 시계로 라운드를 끝내는 코드를 쓰지 말 것.
7. **JsonUtility에 Dictionary 금지** — 점수는 평행 배열. 파싱 후 길이 불일치면 짧은 쪽 기준으로 자르고 LogWarning.
8. **legacy InputField + 새 Input System** — 프로젝트가 Input System 전용이라 InputField 텍스트 입력이 환경에 따라 안 될 수 있다. G-4에서 실측하고, 안 되면 `Keyboard.current.onTextInput` 기반 자체 캡처로 폴백 (Task 6 재작업).
9. **프로토콜 v3 = 기존 빌드와 비호환** — `Builds/` 기존 플레이어와 접속 불가. 다음 빌드 갱신 시 양쪽 다 새 빌드.
10. **dirty 씬 + `run_tests` = 씬 무단 저장** (docs/09 §4) — Task 6에서 특히 주의.
