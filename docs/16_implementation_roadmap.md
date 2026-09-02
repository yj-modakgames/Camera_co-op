# 16. 3D party game 구현 순서와 완료 기준

> 작성: 2026-08-28 · **구현 상태와 완료 기준** · 2026-08-31 자동 검증 갱신
> 제품 요구: [3D hand tracking party game 설계](15_3d_party_game_design.md) · camera 경로: [phone camera 설계](13_phone_camera_input.md)

## 1. 진행 원칙

- 목표는 **4개의 PC/Steam 계정이 각자의 camera로 참여하는 게임**이다. local 4인이나 2p 테스트를 4p 검증으로 대체하지 않는다.
- 설계의 D1은 확정했다. `RelayCopy`의 “직전 그림을 보며 복사”와 fist drawing도 확정 요구다. 개인 canvas는 `Docked`로 시작하며 owner가 `Carried`로 들고 이동하거나 자기 zone 중앙에 다시 dock할 수 있다. 나머지 조작 방식과 수치는 승인된 범위만 구현한다.
- 기존 Steam 2p 계획은 historical 기록으로 취급한다. 현재 online entry는 `RelayQuizOnline` 4p 계약이다.
- 기존 `NetplayTest`, `Netplay3D`, local `RelayQuiz`를 유지한다. 제품 진입 Scene과 build 목록 전환은 별도 명시적 변경으로 다룬다.
- 기존 [QUALITY_CHECKLIST.md](../QUALITY_CHECKLIST.md)를 재생성하지 않는다. 기능 구현마다 전체 항목을 근거로 채점하고 9.0/10 이상까지 개선한다. 실행하지 못한 항목은 미확인·감점으로 기록한다.

## 2. 구현 단계

### M0. 요구 확정과 현재 상태 고정 — 완료

**산출물:** 승인된 D1·D3 세부안, source·Scene·build 경로별 기준 상태, 현재 실패와 미검증 목록.

1. 사용자가 말한 fist와 현재 pinch의 차이는 입력 계약으로 분리한다. fist는 drawing, pinch는 UI·tool·canvas placement selection에 사용한다.
2. 릴레이 개인 canvas는 `Docked`를 기본 상태로 한다. canvas handle pinch로 `Carried`가 되고 손을 놓아도 avatar에 latched된다. 자기 zone 중앙 dock를 pinch하면 `Docked`로 돌아간다. 두 상태는 같은 drawing data·revision과 private recipient 권한을 유지하며, transition 중 active stroke·capture를 취소한다.
3. `Carried` 중 WASD·look을 허용하고, round abort·reset·disconnect 때 canvas를 자기 dock으로 복귀시킨다.
4. 첫 출시 mode를 필수 `RelayCopy`로 고정하고, optional `MemoryCopy`가 기존 local 기억 규칙을 소급 변경하지 않게 구분한다.
5. 2p 작업의 완료 상태를 확인한 뒤 reusable 코드와 변경하면 안 되는 계약을 기록한다.

**완료 조건:** 요구에 두 해석이 남아 있지 않으며 기존 uncommitted 변경을 보존할 경계가 정해진다. D1의 carry·dock 전환 규칙과 reset 동작을 확정했다. 이 단계에서 기존 게임의 밸런스나 Scene을 바꾸지 않는다.

### M1. camera 선택과 phone 입력 — 구현 완료, 실제 phone QA 대기

**산출물:** camera 선택·preview·실패 복구, phone 연결 안내, 입력 상태 표시.

- 기존 Python camera index 경로에 `--camera`, `--list-cameras`, preview 선택 수단을 추가했다.
- process 실행, 정상 packet 수신, fresh hand 검출, player 준비를 각각 표시한다.
- phone을 PC camera로 노출하는 지원 경로를 실제 조합으로 검증한다. 외부 app·driver 설치는 자동 수행하거나 필수 의존성으로 숨기지 않는다.
- 손이 없어도 camera 시작·선택·재시도·종료에 접근할 수 있게 한다.

**완료 조건:** 선택한 장치가 바뀌지 않고 손 입력까지 이어진다. 틀린 장치, 검은 화면, 점유·권한 거부, 케이블 분리, 재연결을 처리한다. 실제 phone 조합은 기기·OS·연결 방식과 결과를 기록한다.

### M2. 3D 손 조작과 연습 공간 — 구현 완료, 실제 손 QA 대기

**산출물:** world 물체 선택, 붓 장착, 물감·굵기 변경, fist drawing, 반복 가능한 연습 절차.

- 현재 `HandPointer`, `HandInputRouter`, `CanvasSurface`, `ToolState`의 책임을 대조한 뒤 필요한 부분만 확장한다.
- fist 판정은 실제 손으로 확인한다. pinch를 이름만 바꿔 fist라고 보고하지 않는다.
- 도구 선택·capture 취소·open 재무장과 stroke 종료를 연결한다.
- 사용자 저작 Scene은 허가된 변경만 수행한다. 물체형 UI의 크기·배치와 사용감은 실제 Game view에서 확인한다.

**완료 조건:** 한 손만으로 붓 선택부터 그림·굵기 변경까지 가능하다. 이동·회전·canvas 이탈·재검출 때 긴 선이나 의도치 않은 click이 생기지 않는다. 물체 앞에 가림판이 있으면 뒤를 조작하지 못한다.

### M3. Steam 4p lobby와 공동 시작 — 구현 완료, 실제 4계정 QA 대기

**산출물:** 친구 초대, body 표시, 연습 공유, 전원 준비, host 시작 후 mode 선택.

- `RelayQuizOnline`은 fixed 4 slot, identity mapping, roster lock, host-only mode/start를 사용한다. 기존 single remote slot 경로는 legacy로 유지한다.
- 4개의 고정 slot 배열과 `SteamId → playerIndex` 매핑, host identity, `rosterGeneration`을 정의한다. 실제 참가자의 identity와 표시 이름을 분리한다.
- 각 참가자의 초대 수락·연결 실패·lobby 만원·version 불일치·중복 참가를 검증한다.
- 공동 시작 장치를 4개의 준비 영역으로 구성하고 한 player의 양손을 두 명으로 세지 않는다.
- host의 시작 전에는 mode 선택을 확정할 수 없고, client가 host 명령을 보내도 거부한다.
- 참가자 변경·camera 단절 때 ready를 무효화한다. mode 선택 취소 후 lobby 연습으로 돌아갈 수 있게 한다.
- game start 시 roster를 잠그고, 이후 신규 peer는 handshake에서 거부한다. 이탈 slot을 같은 round에서 재사용하지 않는다.

**완료 조건:** 4개의 실제 game instance에서 body와 준비 상태가 일치한다. 4명 중 한 명이 준비하지 않으면 시작하지 않는다. host가 시작한 후에만 mode를 확정하고 같은 게임으로 진입한다.

### M4. private 4p `RelayCopy` — 구현 완료, 실제 4계정 privacy QA 대기

**산출물:** 개인 canvas 배정, P1→P2→P3→P4 진행, 시간 종료 전달, private snapshot, 정답과 gallery.

- 기존 shared `NetSession`의 broadcast 경로를 그대로 연결하지 않는다. recipient별 view와 canvas 소유권을 먼저 정한다. canvas transform은 `Carried`·`Docked` 중 하나로 동기화하되 drawing data·revision은 동일하게 유지한다.
- client의 쓰기 요청·제출과 host의 상태 변경을 분리한다. 실제 sender, session, 차례, generation을 검증한다.
- `WordSecret`, owner drawing, P1→P2/P2→P3/P3→P4 snapshot, answer, Reveal·Gallery의 recipient matrix를 packet contract와 test fixture의 기준으로 만든다.
- 전달을 `(session, round, turn, sourceSlot, destinationSlot, revision)`으로 식별한다. destination의 일치하는 준비 ack 뒤에만 timer를 시작하고 timeout·중복·늦은 ack를 처리한다.
- Gallery 공개 전에는 3개 그림의 배열을 보내지 않는다. 공개 snapshot은 순서·owner slot·revision을 포함하고 다음 game에서 cache를 지운다.
- P4 keyboard/IME 답 편집은 이동·그리기를 막고, 손 제출 또는 시간 만료 때 최종 답을 한 번만 확정한다.
- 차례가 끝나면 실제 renderer·capture와 데이터 수신 권한을 함께 바꾼다. gallery 전에는 전체 그림을 배포하지 않는다.
- 공개 world pose는 body root 위치·yaw·locomotion으로 제한한다. private turn에는 cursor·canvas 좌표·gesture·brush tip·stroke를 전송하지 않는다.
- 어떤 player가 이탈해도 첫 범위는 host가 새 generation으로 round 전체를 abort한다. turn 재배정·host migration은 하지 않는다.

**완료 조건:** 아래 Q1~Q12를 충족한다. privacy 검증은 화면 관찰뿐 아니라 수신 메시지·client 저장 상태를 확인한다. 로그에는 secret·camera 영상·계정 식별자를 남기지 않는다.

### M5. 추가 mode와 공통 경계 확인 — 구현 완료, 실제 mode 전환 QA 대기

**산출물:** 승인된 추가 mode, lobby 전시대, mode별 결과 흐름.

1. `MemoryCopy`: 관찰 종료 뒤 이전 그림의 실제 presentation과 입력 표면이 사라지는지 확인한다.
2. `CoopMural`: P1→P2→P3→P4 순서로 한 번에 한 명만 쓰고, 완료한 layer는 freeze되어 앞사람의 선을 지울 수 없게 한다. P4 완료 뒤 네 layer를 전원에게 공개하며, 공개 그림이라는 권한 차이를 명시한다.
3. `WordPictureRelay`: 채택 시 문장·그림 네 차례와 결과 공개를 정의한다. 처음부터 여러 chain을 동시에 실행하는 구조는 요구하지 않는다.

**완료 조건:** mode를 두 번 이상 오가도 이전 제시어·그림·입력 권한·도구 점유가 남지 않는다. mode 추가를 위해 camera·Steam 초대·공통 붓 동작을 복제하지 않는다.

### M6. 실제 Player와 배포 경로 검증 — 외부 검증 대기

**산출물:** 실제 대상 build, 지원 환경 표, 4p QA 기록, 기능별 품질 보고.

- Editor에서 보인 Scene과 build의 첫 Scene이 같은지 확인한다. `EditorBuildSettings`와 build helper의 명시 Scene 목록을 각각 확인한다.
- 실제 Steam 4계정, camera 입력 4개, 채택한 phone 연결 경로를 포함한 플레이를 수행한다.
- title/lobby부터 재시작까지 한 session으로 관찰한다. 실제 webcam과 phone 환경에서 input 지연·오검출·피로·가독성도 확인한다.
- Python runtime·model·OS별 의존성과 Steam native library를 target별로 확인한다. 다른 PC의 `.venv`를 복사한 것을 이식 가능한 배포 방식으로 간주하지 않는다.

**완료 조건:** 실제 Player 증거와 재현 절차가 있고, 미검증 환경을 지원 완료로 표시하지 않는다. 기능별 품질 점수는 실제 코드·실행 결과에 근거한다.

## 3. 4p 핵심 검증 항목

| ID | 시나리오 | 합격 관찰 |
|---|---|---|
| Q1 | camera 없는 참가자가 phone으로 설정 | mouse로 연결 복구 가능, hand 검출 전 ready/drawing 불가 |
| Q2 | 전원 준비와 host 시작 | 서로 다른 4명만 집계, host 시작 전 mode 확정 불가 |
| Q3 | 제시어 전달 | P1 view에만 단어. P2~P4의 메시지·공개 데이터에 단어 없음 |
| Q4 | P1→P2→P3 복사 | 해당 player에게 직전 frozen 그림만 도착. 자기 작업은 빈 canvas에서 시작. canvas는 `Carried` 또는 `Docked`로 그릴 수 있음 |
| Q5 | 비참가자 엿보기 | 이동·회전·종이 뒷면·remote 손 동작·thumbnail·수신 message·client cache로 그림 노출 없음 |
| Q6 | 제한 시간과 수동 완료 동시 발생 | 그림 확정·기록과 차례 변경이 각각 한 번. 다음 timer는 일치하는 transfer 준비 확인 이후 시작 |
| Q7 | 큰 그림·전송 실패 | 한도 초과/누락/중복을 처리. 전송 실패를 정상 빈 그림으로 기록하지 않음 |
| Q8 | P4 마지막 글자·IME·timeout | 편집 focus가 이동을 막고 확정 답이 유실·중복 제출되지 않음 |
| Q9 | 손 상실·재검출·tool 변경·canvas carry/dock | active stroke·capture 종료, open 재무장, 이전 gesture로 새 차례나 canvas placement 자동 입력 없음 |
| Q10 | 대기자가 손을 내리거나 focus를 잃음 | 자기 private 화면만 숨김. active player timer에 영향 없음 |
| Q11 | player 이탈·host 종료·late join | 중간 차례에 새 사람을 끼워 넣지 않음. 실패 안내 후 안전하게 종료·재초대 |
| Q12 | 결과·다음 게임 | 3개 그림 순서·owner 정확, 공개 전 전체 배포 없음, 다음 round secret과 기록 분리 |

## 4. 검증 방법과 근거

| 방법 | 확인할 것 | 대체할 수 없는 것 |
|---|---|---|
| 기존 EditMode suite와 필요한 경계 테스트 | state 전이, 권한, 중복 방지, snapshot 검증, gesture 재무장 | 실제 camera 인식, Steam 연결, 손으로 누르는 감각 |
| 합성 hand 입력·Loopback | 잘못된 packet, 4명 identity, 순서·유실·끊김 재현 | 실제 4계정·4기기의 연결과 device 지연 |
| Game view/Player 화면 관찰 | 물체 조작, 한글 표시, 가림, 시야·동선, 결과 | 수신되지 않아야 하는 비밀 데이터의 부재 |
| recipient별 message 검사 | 누가 어떤 종류의 데이터를 받는지 | 변조된 host memory까지의 보안 |
| Profiler와 장시간 실행 | frame time, GC, stroke 증가, memory, 전송량 | 측정하지 않은 기기의 성능 |
| 실제 손·phone 검증 | gesture 분리, orientation, 입력 지연·안정성 | 합성 입력만으로 합격 판정 불가 |

지연 측정은 같은 관측 장치의 고속 영상 또는 검증된 clock 측정으로 수행한다. phone timestamp와 PC 시각을 단순히 빼서 절대 latency로 보고하지 않는다. 30fps, 특정 dwell 시간, 낮은 전송량은 측정 전 목표·제안이며 실측 결과가 아니다.

구현 검증은 기존 테스트부터 사용한다. meaningful한 미보호 경계에만 테스트를 추가한다. 알려진 실패를 삭제·skip·완화해서 전체 성공으로 표시하지 않는다. Unity test 실행 전에 compile 완료와 Scene dirty 상태를 확인하고, 사용자 변경을 임의 저장·복구하지 않는다.

## 5. 품질 보고와 역할

기능마다 기존 품질 기준의 기능·성능·검증·코드 품질·최적화 **전체 17개 항목**을 채점한다. 보고에는 항목별 점수 표, 총점, 선택한 구현 방식과 이유, 감점 요인·개선 방안, 실제 테스트 결과·측정 경로를 넣는다. 코드 변경 없이 점수만 올리지 않는다. 9.0 미만이면 근거가 있는 개선과 재검증을 수행하고 점수 이력을 남긴다.

메인 session은 요구 정리·계획·agent 작업 경계와 품질 기준을 맡는다. 실제 구현·검증은 하위 agent에 위임한다. 단순 작업은 `gpt-5.6-luna + medium`, 권한·상태 전이·network 경계 등 어려운 작업은 `gpt-5.6-sol + high`로 배정한다. 각 agent에게 소유 파일과 관련 skill 지침을 주고 기존 변경 보존을 명시한다.

이번 문서 작업에서는 Unity build·Play·camera·Steam 실기를 실행하지 않았다. 기존 문서의 과거 검증 결과는 그 당시 범위의 기록이며 새 목표의 통과 증거가 아니다.

## 6. Task 14 — four-Scene additive split 완료 (2026-09-02)

`PartySceneCatalog`가 build와 runtime의 단일 source of truth다. 정확한 Scene 순서는 `RelayQuizOnline` lobby, `RelayCopy`, `MemoryCopy`, `CoopMural`이며 각 경로는 [10_build.md](10_build.md)에 고정한다. 최신 `PartyGameSceneTests` 15/15와 `PartySceneValidator PASS`로 Scene 존재·순서·private shell·mural layer·persistent owner 중복 금지를 확인했다. BuildAll 재생성 결과도 evidence에 남아 있다.

완료된 사용자 흐름은 camera button → Host/Invite → 자유 연습 → four ReadyPads → Host START → `ModeSelectorRoot` 표시 → Host mode 선택(`SelectModeAndBeginLoad`, `startSignal` 증가) → 세 mode 중 하나 additive load → Host RETURN TO LOBBY다. `PartySceneCoordinator`가 bind/unbind와 load failure/timeout 경계를 담당하며, 실패 시 private render/input을 정리하고 lobby 복귀를 안내한다. disconnect는 `Abort` 후 새 invite가 필요하다. 정상적인 mode return에서만 Steam party/session과 camera process를 유지하고, mode Scene은 별도 camera·input·network owner를 만들지 않는다.

Task 14 자동·Editor 결과는 실제 Steam 4 account/4 machine, webcam/phone, physical gesture, long profile, Intel Mac 검증을 포함하지 않는다. 이 항목들은 M6 외부 QA로 유지한다.
