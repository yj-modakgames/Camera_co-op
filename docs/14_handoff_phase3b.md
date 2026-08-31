# 14. Phase 3b 미니게임 구현 판정 기록

> 작성 2026-08-27 · 상태 정정 2026-08-31
> 현재 요구·검증 결과의 기준은 [12_phase3b_guess_game.md](12_phase3b_guess_game.md)다. 이 문서는 source/test에서 참조하는 구현 판정과 환경 함정을 보존한다.

## 1. 현재 상태

- Task 1~6의 핵심 구현과 review가 완료됐다. 아래 §4~§8은 당시 수정·수용 판단의 근거다.
- commit `b121978`에서 Task 7 통합 결과가 `docs/12`에 기록됐다. G-2·G-3·G-5·G-6·G-7은 합성 UDP와 Loopback 환경에서 PASS다.
- G-4는 `InputFocus` 이동 gate만 PASS다. legacy `InputField`의 실제 keyboard·한글 IME와 Player build 동작은 사용자 검증 대기다.
- 실제 webcam 1 round, Steam 실제 2인 이상, G-8 `QUALITY_CHECKLIST` 평가는 남아 있다.
- 따라서 이 기록의 과거 `239/239` 기준선이나 Loopback PASS를 새 4인 3D 제품의 완료 증거로 사용하지 않는다.

## 2. 최신 검증과 잔여 finding

최신 시나리오·수치·제약은 `docs/12` §6을 따른다. Task 7에서 수정하지 않고 남긴 finding은 다음과 같다.

1. 첫 `RoundEnd` 전까지 scoreboard가 비어 있다.
2. 검증 중 Play 종료와 시작 버튼 자동 실행이 각각 관측됐지만 원인은 확정하지 못했다.
3. 정지 pinch는 local point 추가가 없어도 `StrokePoints`를 매 frame 보내 전송량이 커질 수 있다.
4. protocol v3는 이전 build와 호환되지 않는다. 실제 배포 전에 참여 기기를 같은 build로 갱신해야 한다.

## 3. 이 문서를 유지하는 이유

- runtime source와 test가 아래 §6의 권한·전이 판단을 직접 참조한다.
- §8은 `RelaySwap` 첫 drawer 통지와 `StrokeEnd` gate의 수용 경계를 설명한다.
- §9는 stale compile, dirty Scene test 저장, Unity 실행 순서 같은 재현 가능한 검증 함정을 보존한다.

오래된 branch·HEAD·working tree 상태와 재개 명령은 현재 작업 지시가 아니다. 실제 작업을 시작할 때는 현재 `git status`, `docs/12`, 활성 plan을 다시 확인한다.

## 4. 처리된 Important finding 기록

### 4-1. Task 3 (Important 1건 — `8d3c353`으로 수정, **2026-08-28 재리뷰 ADDRESSED**)

재리뷰 결과: 가드 위치가 `decode/version → 위조 가드 → Hello 분기(return) → 신규 멤버십 가드 → StrokeGate → 중계 → OnGameMessage → Apply` 순으로 정확하고, host 한정이 옳다(클라의 `players`는 `Apply`의 Welcome 분기 `NetSession.cs:484-487`에서 채워지므로 클라에 같은 검사를 걸면 Welcome 자체가 막힌다). host 자신은 `StartSession`(`NetSession.cs:87`)에서 등록되므로 self-message 경로 무손상. 테스트 비공허성도 확인 — `HandlePeerConnected`가 host에서 no-op이라 `AddFakePeer`만으로는 `players`에 안 들어가고, `Deliver`가 `sender == directSender`로 보내 위조 가드를 통과하므로 **신규 가드만이 폐기 주체**다.


리뷰어 판정 원문 요약:

> **미등록 sender의 게임 메시지가 `OnGameMessage`로 나간다** (NetSession.cs 수신 경로). 위조 가드(`env.sender != directSender`)는 있으나 `players` 멤버십 검사가 없어, Hello를 아직 안 보낸 피어나 4인 초과로 거부된 5번째 피어가 임의 게임 타입을 보내면 통로로 흘러 들어간다 — 미등록 피어가 정답을 제출해 라운드를 끝낼 수 있는 경로.

적용된 수정(`8d3c353`): host 수신 경로에서 Hello 분기 **뒤**, StrokeGate·중계·OnGameMessage **앞**에 `if (IsHost && !players.ContainsKey(env.sender)) return;` 가드 1건 (host 한정 — 클라의 players는 Welcome 전까지 비어 있어 같은 검사를 클라에 걸면 Welcome 자체가 막힌다). 커버 테스트: 미등록 피어의 `GuessSubmit`·`StrokeStart` 모두 폐기 (OnGameMessage 미발화·미중계·미반영). 부수 효과로 이탈한 피어의 잔여 메시지도 폐기된다. 남은 절차: 이 fix diff의 스코프 재리뷰.

### 4-2. Task 6 (Important 1건 — `4eece0a`로 수정)

> **GameEnd 최종 순위가 배너 높이에 잘려 2줄만 보인다.** `BuildFinalRankingText`(`GameUI.cs:255-266`)는 헤더 1줄 + 인원수만큼의 순위 줄을 만드는데, `BannerText`는 `m_SizeDelta {700,40}` · `m_FontSize 14` · `m_BestFit 0` · `m_VerticalOverflow 0`(Truncate)라 40px 박스에 2줄만 들어가고 넘치는 줄은 렌더 자체가 되지 않는다. 2인 게임(3줄)에서도 2위가 잘린다.

적용된 수정: 씬 `BannerText`(fileID `574219774`)의 **`m_VerticalOverflow: 0 → 1`(Overflow) 한 필드만**. 씬 diff 정확히 1줄.

`m_SizeDelta.y`를 늘리지 않은 이유 — 상단 3요소는 anchor·pivot 모두 top-center이고 `BannerText` y=-20/h=40(-20\~-60), `TimerText` y=-65/h=30(-65\~-95), `WordText` y=-100/h=50(-100\~-150)이라 **배너 아래 여유가 45px뿐이어서 높이를 늘리면 TimerText와 겹친다**. Overflow는 박스를 넘겨 아래로 렌더하지만, GameEnd phase에서는 `timerText`(`GameUI.cs:246-249`)와 `wordText`(`GameUI.cs:196-199`)가 둘 다 빈 문자열이 되고 다중 줄 배너를 만드는 phase는 GameEnd 하나뿐이라 겹칠 대상이 없다.

## 5. 리뷰 절차 참고 (레저 없이도 재현 가능하게)

- 리뷰 diff 생성: `git diff <BASE>..<HEAD>`를 파일로 떨궈 리뷰어에게 경로만 전달 (BASE = 해당 Task 파견 직전 HEAD).
- 리뷰어에게 줄 것: Task 브리프(계획서의 해당 Task 섹션) + 구현자 보고 + diff 파일 + 전역 제약.
- Minor는 수정 루프에 넣지 않고 이연 목록(§7)에 적립 — 최종 브랜치 리뷰가 triage.

## 6. 게임 계층 불변식 (Task 5에 전달·반영 완료 — Task 7 이후에도 유효)

> 6항목 전부 `2a63972`에 반영되고 리뷰에서 준수 확인됐다. Task 7에서 이 중 하나가 깨진 것처럼 보이면 회귀다.


1. **Relay의 `CurrentDrawerId`는 Drawing 밖에서 null** — `Tick`이 `ToDrawing`을 반환한 직후에 읽을 것 (Turns는 항상 ActiveId와 동일).
2. **`Scores`는 Idle 복귀 후에도 유지** — 다음 `StartGame`이 초기화 (최종 점수판 표시용).
3. **`RelaySwap` 브로드캐스트는 drawerId가 실제로 바뀐 경우에만** — 릴레이 인원이 2명으로 줄면 로직이 no-op RelaySwap을 반복 반환할 수 있다 (Task 2 리뷰 minor ⑥).
4. **게이트 플립 순서**: 라운드 전이는 반드시 `SendClear()` → 게이트 갱신 순서 유지 (Turns 모드에서 in-flight 스트로크 고아 방지 — Task 3 리뷰 minor ②의 천장).
5. **StrokeGate는 host에서만 설정** — 클라에 설정하면 host 정본과 어긋날 수 있다 (Task 3 리뷰 minor ③).
6. 클라 GameSession은 **`netSession.HostPlayerId` sender의 게임 메시지만 적용** (위조 방어) — host 자신은 `GuessSubmit` 타입만 처리.

## 7. 이연된 Minor 목록 (최종 브랜치 리뷰가 triage)

- T1: `WordBank.ParseUnique`가 '\n' 분리 + Trim으로 CRLF를 우연히 처리 (WordBank.cs:196).
- T2-①: 플레이어 이탈 시 조기 종료 재평가 없음 → 드문 케이스에서 라운드가 타임아웃까지 지속 (수용 판정).
- T2-②: GameEnd 중 AddPlayer 시 TotalRounds 표시 일시 오류. T2-③: 재참가 시 이전 점수 유지(의도적, 무테스트). T2-④: 미커버 분기 3건(라운드 경계 guessedThisRound 리셋 / drawer 앞 인덱스 감소 / 미자격 늦은 참가 조기종료 분모). T2-⑤: Ignored 7종 단일 테스트 묶음. T2-⑥: no-op RelaySwap (→ §6-3으로 완화). T2-⑦: ResetToIdle이 _mode 미초기화(무해).
- T3-②: 게이트 플립 시 in-flight 스트로크가 host strokes에 미완결 고아로 잔존 — Turns는 SendClear가 덮고, Relay 교대 중 발생분(늦은 참가 스냅샷 누락)은 알려진 천장으로 수용. T3-③: StrokeGate가 클라 수신 경로에도 적용됨(브리프는 host 한정 — §6-5로 완화). T3-④: warnedGameRole이 StartSession에서 미리셋. T3-⑤: IsCoreType에 Hello 부재(현 순서상 안전). T3-⑥: GameChannelTests.cs.meta 최소 형태 — Unity 재작성 시 잡 diff 가능.

**Task 4 리뷰 (2026-08-28)**

- T4-①: `HandPointer.StrokesEnabled` setter 무테스트였으나 **Task 5에서 해소** (`StrokesEnabled_FalseWhileDrawing_EndsEveryHandsStroke`, 양손 케이스 포함).
- T4-②: legacy `InputField`는 `interactable=false`만으로 `isFocused`가 안 풀려 `IsTyping`이 true로 남을 수 있음 → **Task 6에서 해소** (`GameUI.OnDisable` 리셋 + `SetGuessInputInteractable`의 `DeactivateInputField()` 명시 호출).
- T4-③: `StrokeEnd`도 `StrokeGate` 대상이라(`NetSession.cs:429`) 라운드 타임아웃 시 클라 drawer의 회수 End가 host에서 폐기될 수 있다 → Task 5가 **천장 수용**으로 판정 (§8-7). T3-②와 동일 범위.
- T4-④: `HandPointer.cs:105`의 `HandlePinchEnd`가 3인자 `Decide`를 호출 — 현재는 End 분기가 `strokesEnabled`를 참조하지 않아 동작 동일하나, End 규칙이 바뀌면 이 호출만 암묵적 `true`로 남는다.

**Task 5 리뷰 (2026-08-28)**

- T5-①: `GameStateSyncPayload`에 Relay drawerId가 없어, Relay Drawing 중 합류한 관전자는 다음 `RelaySwap`(최대 relaySwapSec)까지 drawer 미상 — Task 6이 별도 문구로 표시 완화. 근본 해결은 payload 필드 추가.
- T5-②: `GameSession.OnDisable`이 게이트를 해제하지 않는다 — 게임 중 컴포넌트를 disable하면 `StrokesEnabled=false`와 `StrokeGate`가 남는다. 씬에 disable 경로가 없어 현재 도달 불가.
- T5-③: `HandleTransition`이 `SyncDrawer()` 반환값을 버려 릴레이 2인 잔존 시 no-op swap마다 `OnStateChanged`가 발화(송신은 0건). `HandlePlayersChanged:475`는 같은 헬퍼의 반환값을 제대로 쓴다 — 두 호출부 불일치.
- T5-④: 턴 순서가 `netSession.Players` Dictionary 열거 순서에 의존하고 **테스트 2건이 그 순서를 단정**한다 (`GameSessionTests.cs:332`, `:462`). `PeerLeft` 삭제 후에는 삽입 순서가 깨지므로 계약이 아니다. 흔들리면 `StartGame`에서 host 우선 + id 정렬로 결정화.
- T5-⑤: `revealSec`이 프로토콜에 없어 RoundReveal 구간 `RemainingSec == 0` → Task 6이 타이머 은닉으로 완화.
- T5-⑥: 구현자 보고의 파일 행수가 실제와 불일치(302 주장/실제 326, 455 주장/실제 548). 코드 영향 없음 — 이후 보고는 `wc -l` 인용.

**Task 6 리뷰 (2026-08-28)**

- T6-①: 상단 3개 Text(`BannerText`/`TimerText`/`WordText`)가 `m_Alignment: 0`(UpperLeft)이라 anchor가 top-center인데 글자는 박스 왼쪽 끝에서 시작 — docs/12 §4의 "상단 중앙"과 어긋난다. 1600×900에서는 겹침 없으나 **창 폭이 약 1100px 미만이면 배너 글자가 `NetplayUI.StatusText`와 같은 픽셀에 겹친다**. 수정은 세 Text의 `m_Alignment`를 1(UpperCenter)로.
- T6-②: 새 시작 버튼 2개가 기존 버튼 스택과 스타일 불일치(내장 `UISprite` + 흰색, 라벨 font 14 / 기존은 `m_Sprite 0` + 어두운 반투명, font 20). 배치·크기·겹침은 정상.
- T6-③: Enter 제출 프레임에 `InputFocus.IsTyping`이 1프레임 false로 떨어진다 — legacy `InputField`가 Enter에서 스스로 `DeactivateInputField()`한 뒤 `onEndEdit`를 발행하고, `ActivateInputField()`의 실제 활성화는 `LateUpdate`이므로 그 프레임 폴링이 false를 쓴다. 같은 프레임에 물리 C키가 눌려 있으면 캔버스가 지워지고 WASD면 1프레임 이동(한글 ㅈ/ㅁ/ㄴ/ㅇ이 물리 W/A/S/D). 대안은 제출 직후 1프레임 IsTyping 래치.
- T6-④: 라운드가 바뀌어도 `guessInput.text`가 비워지지 않아 이전 라운드의 미제출 문자열이 남고, 그대로 Enter를 누르면 제출된다.
- T6-⑤: `feedText`가 `m_RichText: 1`이라 원격 피어의 오답 원문에 담긴 `<size=200>`·`<color=…>`가 렌더된다(길이 clamp도 없음). 수정은 `FeedText`의 `m_RichText`를 0으로.
- T6-⑥: RoundReveal에서 배너 `"정답: X"`와 `wordText`가 같은 정답을 중복 표시. `GameUI` 루트에 Graphic 없는 죽은 `CanvasRenderer` 1개(레이캐스트 영향 없음 — 확인됨). 동점 처리 없음(`List.Sort` 불안정 + Dictionary 열거라 동점자 순서가 Refresh마다 흔들릴 수 있고 동점에도 `1위/2위`를 붙인다). Idle에도 하단 입력창이 계속 보인다(스펙은 "비활성"만 요구 — 위반 아님).

## 8. 판정(Ruling) 기록

1. **worktree 대신 같은 트리의 브랜치** — Unity Editor 바인딩 때문. 비용: main 오염 없음, 병합은 사용자 확인 후.
2. **계획 사전 스캔 결함 수정** (`454c19f`) — 중계 화이트리스트(RelayTypes 6종)와 코어 Apply 타입(+Welcome/PeerJoined/PeerLeft)을 분리. 안 하면 클라 세션이 통째로 깨짐.
3. **외부 커밋 `3c6b331`은 사용자 작업** — 유지, 리뷰 범위에서만 제외 (사용자 확인됨).
4. **이탈 시 조기 종료 재평가 미구현 수용** — 스펙이 명시하지 않고 타임아웃 종료는 안전. 비용: 드문 케이스 라운드 지연.
5. **검증 환경 제약** (사용자 지시): 실웹캠·실기 멀티 이연 — G-2~G-7은 `fake_hand.py` 합성 UDP + Loopback 가짜 피어로만. 한글 IME 실타이핑·실웹캠 스모크·Steam 실기는 사용자 검증 대기 (docs/12 §6 주석 참조).
6. **계획서 매핑 표의 `Tick=ToDrawing` "메시지 없음"은 Relay 모드에서 틀렸다** (2026-08-28, Task 5). 게이트 규칙이 `State.CurrentDrawerId`를 쓰는데 `ApplyRoundBegin`은 Relay에서 drawer를 null로 두므로, 표대로 구현하면 **첫 relaySwapSec 동안 host의 `StrokeGate`도 로컬 게이트도 전원을 차단**한다(클라는 Relay drawer를 계산할 정보가 없다). 그래서 Drawing 진입 시 `RelaySwap{drawerId}`를 1건 송신한다 — `SyncDrawer` 헬퍼가 §6-3(drawerId가 실제로 바뀐 경우에만)을 만족시켜 **Turns 모드에서는 RelaySwap이 0건**이고, 와이어 테스트 2건이 이를 단정한다. 리뷰 판정: docs/12 §3의 `RelaySwap` 의미와 충돌하지 않으므로 **스펙 위반이 아니고 계획서 표만 현실과 어긋난 것**. 계획서 Task 5 표에 이 판정을 주석으로 박아 뒀다 — 되돌리지 말 것.
7. **`StrokeEnd`의 게이트 폐기는 천장으로 수용** (2026-08-28, Task 5). 게이트 적용 대상은 docs/12 §2 표 #3이 스트로크 4종으로 못 박고, `StrokeGate`가 `Func<string,bool>`(`NetSession.cs:29`)로 타입을 못 보므로 `StrokeEnd`만 통과시키려면 리뷰까지 마친 `NetSession.cs`를 고쳐야 한다. 대신 `SendClear()` → 게이트 갱신 순서(§6-4)를 지켜 다음 라운드가 반드시 덮게 했다. 잔여 영향은 RoundReveal 구간 표시 잔여뿐.
8. **Task 6의 Important를 씬 1필드로 수정** (2026-08-28, §4-2). 사용자 저작 씬을 다시 여는 비용을 감수한 이유는 코드 쪽 대안(최종 순위를 `scoreboardText`로 이동)이 계획서가 지정한 배너 표시 규칙을 바꾸기 때문. 씬 무손상은 블록 단위 + line multiset 대조로 검증됨.

## 9. 환경 메모

- EditMode 테스트: `unity cmd --timeout 300 run_tests --mode EditMode` (`--timeout`은 command 앞). 함정 목록: docs/09 §4.
- 현재 기준선: **239/239** (`4eece0a` 기준, 메인 세션 직접 실측).
- **파일을 편집해도 Unity가 자동 재컴파일하지 않는다** (2026-08-28 Task 4·5 실측). `run_tests`가 stale 결과를 반환한다 — 편집 후 `unity cmd --timeout 120 recompile`을 명시 호출하고 `recompile_status`로 확인한 뒤에 테스트할 것. Task 4에서 이것 때문에 구현 전 첫 `run_tests`가 197/197 GREEN을 반환해 RED 증거를 놓칠 뻔했다.
- 씬을 만지는 Task는 **씬 수정 → 명시 저장 → `git diff` 확인 → 그 다음에만 `run_tests`** 순서를 지킬 것. 씬 diff 검증은 `git show <commit> -- <씬>`을 `--- !u!<class> &<fileID>` 블록 단위로 파싱해 (a) 삭제된 fileID, (b) 기존 fileID 중 내용이 바뀐 것, (c) old 라인 multiset이 new에 전부 존재하는지 세 가지를 본다 — raw line diff의 삭제 수치는 git의 행 매칭 artifact라 그대로 믿으면 오판한다 (Task 6에서 `-158`이 실제로는 손상 0이었다).
- 프로토콜이 v3로 올라 기존 `Builds/`의 플레이어와 비호환 — 다음 빌드 갱신 시 양쪽 재빌드.
