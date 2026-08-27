# 14. 인계 — Phase 3b 미니게임 구현 진행 상태

> 작성 2026-08-27 · 이 문서는 **다른 세션이 Phase 3b 구현을 이어받기 위한** 인계 문서다.
> 스펙: `docs/12_phase3b_guess_game.md` (구속력 있는 진실 원천)
> 계획: `docs/superpowers/plans/2026-08-27-phase3b-guess-game.md` (Task 1~8, 체크박스)
> 브랜치: **`phase3b-guess-game`** (main은 깨끗함. worktree 대신 브랜치 — Unity Editor가 이 디렉터리에 바인딩)

## 1. 즉시 확인할 것 (재개 첫 단계)

1. `git log --oneline -7` — 아래 §2 표와 대조해 어디까지 커밋됐는지 확인. 인계 시점 HEAD 부근: `8d3c353`(Task 3 fix) / `a2b3907`(이 문서). working tree는 인계 시점에 clean.
2. **Task 3 수정 라운드 1은 완료·커밋됨** (`8d3c353`, EditMode 197/197, 신규 테스트 `Host_UnregisteredPeer_MessagesAreDropped` 포함). 남은 것은 **스코프 재리뷰 1건**: `8d3c353` diff만 놓고 §4 finding이 ADDRESSED인지 + fix diff의 신규 파손 여부만 확인 (§5 절차). ADDRESSED면 Task 3 종료 → Task 4부터 진행.
   - 참고(재리뷰어에게 전달): 이 fix 라운드는 가드 제거 상태의 실행 RED 증거가 없다(recompile 권한 차단). 논리 증명 + 신규 테스트 GREEN + 전체 197/197로 갈음 — task-3-report.md의 fix 부록 참조.
3. SDD 레저(git-ignored, 이 기기 한정): `.superpowers/sdd/2026-08-27-phase3b-guess-game/progress.md` — 존재하면 그것이 상세 기록. 없으면 이 문서 §2~§6이 대체본.

## 2. Task 진행 상태 (2026-08-27 인계 시점)

| Task | 내용 | 상태 | 커밋 | 테스트 |
|---|---|---|---|---|
| 1 | GameProtocol + GuessJudge + WordBank | **완료** (리뷰 clean) | `a1b434b` | 153/153 |
| 2 | GuessGameLogic 상태 머신 | **완료** (리뷰 clean, minor 7 이연) | `8d5bc78` | 178/178 |
| 3 | NetProtocol v3 + NetSession 통로 6건 | 구현+리뷰 fix 커밋됨, **스코프 재리뷰만 남음** (§1-2) | `b7d851a` + `8d3c353` | 197/197 |
| 4 | 입력 게이트 (InputFocus·StrokesEnabled·WASD/C 차단) | 미착수 | — | — |
| 5 | GameClientState + GameSession | 미착수 (§6 인계 노트 필수 반영) | — | — |
| 6 | GameUI + words_ko.txt + 씬 배선 | 미착수 | — | — |
| 7 | 통합 검증 G-2~G-7 | 미착수 | — | — |
| 8 | 품질 채점 + 문서 마감 | 미착수 | — | — |

참고: `3c6b331`(docs/13 폰 카메라 설계)은 **사용자가 직접 만든 커밋** — 이 계획과 무관, 건드리지 말 것.

## 3. 재개 방법

superpowers:subagent-driven-development 스킬로 계획을 이어서 실행한다:

```
docs/superpowers/plans/2026-08-27-phase3b-guess-game.md 를 subagent-driven-development로 이어서 실행.
브랜치 phase3b-guess-game. Task 1·2 완료, Task 3는 docs/14_handoff_phase3b.md §4의 open finding 처리부터.
스펙 docs/12, 인계 노트는 docs/14 §6.
```

- 모델 배분(전역 규칙 §3): 계획의 각 Task 헤더에 명시 (sonnet=단순, opus=난이도 높음). 메인 세션은 구현하지 않고 위임·검수만.
- Task당 절차: 구현자 파견(TDD) → 리뷰어 파견(스펙+품질) → 필요 시 수정 라운드(최대 5) → 레저 기록 → 다음 Task.
- 전체 완료 후: 최종 브랜치 리뷰 → G-8 채점(≥9.0) → main 병합은 **사용자 확인 후**.

## 4. Task 3 리뷰 finding (Important 1건 — `8d3c353`으로 수정됨, 재리뷰 대기)

리뷰어 판정 원문 요약:

> **미등록 sender의 게임 메시지가 `OnGameMessage`로 나간다** (NetSession.cs 수신 경로). 위조 가드(`env.sender != directSender`)는 있으나 `players` 멤버십 검사가 없어, Hello를 아직 안 보낸 피어나 4인 초과로 거부된 5번째 피어가 임의 게임 타입을 보내면 통로로 흘러 들어간다 — 미등록 피어가 정답을 제출해 라운드를 끝낼 수 있는 경로.

적용된 수정(`8d3c353`): host 수신 경로에서 Hello 분기 **뒤**, StrokeGate·중계·OnGameMessage **앞**에 `if (IsHost && !players.ContainsKey(env.sender)) return;` 가드 1건 (host 한정 — 클라의 players는 Welcome 전까지 비어 있어 같은 검사를 클라에 걸면 Welcome 자체가 막힌다). 커버 테스트: 미등록 피어의 `GuessSubmit`·`StrokeStart` 모두 폐기 (OnGameMessage 미발화·미중계·미반영). 부수 효과로 이탈한 피어의 잔여 메시지도 폐기된다. 남은 절차: 이 fix diff의 스코프 재리뷰.

## 5. 리뷰 절차 참고 (레저 없이도 재현 가능하게)

- 리뷰 diff 생성: `git diff <BASE>..<HEAD>`를 파일로 떨궈 리뷰어에게 경로만 전달 (BASE = 해당 Task 파견 직전 HEAD).
- 리뷰어에게 줄 것: Task 브리프(계획서의 해당 Task 섹션) + 구현자 보고 + diff 파일 + 전역 제약.
- Minor는 수정 루프에 넣지 않고 이연 목록(§7)에 적립 — 최종 브랜치 리뷰가 triage.

## 6. Task 5 파견 시 반드시 전달할 인계 노트

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

## 8. 판정(Ruling) 기록

1. **worktree 대신 같은 트리의 브랜치** — Unity Editor 바인딩 때문. 비용: main 오염 없음, 병합은 사용자 확인 후.
2. **계획 사전 스캔 결함 수정** (`454c19f`) — 중계 화이트리스트(RelayTypes 6종)와 코어 Apply 타입(+Welcome/PeerJoined/PeerLeft)을 분리. 안 하면 클라 세션이 통째로 깨짐.
3. **외부 커밋 `3c6b331`은 사용자 작업** — 유지, 리뷰 범위에서만 제외 (사용자 확인됨).
4. **이탈 시 조기 종료 재평가 미구현 수용** — 스펙이 명시하지 않고 타임아웃 종료는 안전. 비용: 드문 케이스 라운드 지연.
5. **검증 환경 제약** (사용자 지시): 실웹캠·실기 멀티 이연 — G-2~G-7은 `fake_hand.py` 합성 UDP + Loopback 가짜 피어로만. 한글 IME 실타이핑·실웹캠 스모크·Steam 실기는 사용자 검증 대기 (docs/12 §6 주석 참조).

## 9. 환경 메모

- EditMode 테스트: `unity cmd --timeout 300 run_tests --mode EditMode` (`--timeout`은 command 앞). 함정 목록: docs/09 §4.
- 현재 기준선: **196/196** (Task 3 fix 커밋 후에는 +α — fix의 신규 테스트 포함).
- 프로토콜이 v3로 올라 기존 `Builds/`의 플레이어와 비호환 — 다음 빌드 갱신 시 양쪽 재빌드.
