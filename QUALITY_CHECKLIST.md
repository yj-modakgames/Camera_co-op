# QUALITY_CHECKLIST.md — Unity 6 기능 구현 품질 체크리스트 (Camera_co-op)

> **대상 스택:** Unity 6000.3.15f1 (C#) · URP
> **사용법:** 기능을 하나 구현할 때마다 아래 **모든 항목**을 증거 기반으로 채점한다. 총점은 **10점 만점**으로 정규화되어 있다. 총점 **9.0 미만이면 개선안을 직접 코드에 반영 → 재평가**를 9.0 이상이 될 때까지 반복하고, 매 반복의 점수 변화를 기록한다.
> **채점 원칙:** 추측으로 만점 금지. 성능은 측정/코드분석 근거, 검증은 테스트 실제 실행 결과를 인용해야 점수를 부여한다. 감점 사유를 먼저 찾는다.
> **적용 범위:** Unity 측 코드(Assets/_CameraCoop/). Python 측(PythonTracker/)은 `docs/05_test_plan.md`의 DoD로 검증한다.
>
> _생성일 2026-08-25 · 기준 문서: D:\git\Drop_Forge\QUALITY_CHECKLIST.md (동일 스택) · 근거: Unity 공식 Manual / Learn / e-book (하단 출처)_

---

## 총 배점 개요 (10.0)

| # | 카테고리 | 배점 |
|---|----------|------|
| 1 | 기능 (Functionality) | 2.0 |
| 2 | 성능 (Performance) | 2.0 |
| 3 | 검증 (Verification) | 2.0 |
| 4 | 코드 품질 (Code Quality) | 2.0 |
| 5 | 최적화 (Optimization) | 2.0 |
| | **합계** | **10.0** |

---

## 1. 기능 (Functionality) — 2.0

| 항목 | 배점 | 왜 필수인가 | 출처 |
|------|------|-------------|------|
| 1-1 요구사항 완전 충족 | 0.8 | 승인된 docs/ 설계 문서의 명세를 정확히·빠짐없이 구현해야 "완성"으로 인정된다. 부분 구현·임의 축소는 감점. | docs/01~05 설계 문서 |
| 1-2 엣지 케이스 처리 | 0.6 | null/경계값/빈 컬렉션/최소·최대·0 입력 등에서 깨지지 않아야 실제 플레이에서 버그가 없다. 이 프로젝트 특수 케이스: 잘못된 JSON, 손 0개, 패킷 역전, 서버 단절/재시작. | [Programming best practices](https://docs.unity3d.com/6000.4/Documentation/Manual/programming-best-practices.html) |
| 1-3 에러 핸들링 | 0.6 | 예외를 삼키지 않고(silent catch 금지) 실패를 graceful하게 처리해야 런타임 안정성이 확보된다. 예외: 소켓 Close에 의한 종료 경로 예외는 명시 주석 하에 허용. | [Programming best practices](https://docs.unity3d.com/6000.4/Documentation/Manual/programming-best-practices.html) |

## 2. 성능 (Performance) — 2.0

| 항목 | 배점 | 왜 필수인가 | 출처 |
|------|------|-------------|------|
| 2-1 핫패스 GC 할당 최소화 | 0.7 | 게임 루프에서 프레임당 힙 할당이 있으면 GC 스파이크로 프레임 드랍이 발생한다. boxing·Update 내 LINQ·문자열 연결·new 회피. 이 프로젝트 허용 예외: 30Hz JSON 파싱 할당 (docs/04 §2 명시, 측정 근거 필요). | [GC best practices](https://docs.unity3d.com/6000.3/Documentation/Manual/performance-garbage-collection-best-practices.html) · [Track GC allocations](https://docs.unity3d.com/6000.3/Documentation/Manual/performance-track-garbage-collection.html) |
| 2-2 Update 내 고비용 호출 제거 | 0.7 | `GetComponent`/`GameObject.Find`/`Camera.main`는 Update에서 반복 호출 시 CPU 낭비. Awake/Start에서 1회 캐싱 또는 Inspector 직접 할당. | [Camera.main](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera-main.html) · [Programming best practices](https://docs.unity3d.com/6000.4/Documentation/Manual/programming-best-practices.html) |
| 2-3 메모리 사용/누수 점검 | 0.6 | 이벤트 미구독 해제·미해제 리소스(스레드·소켓 포함)는 누수를 일으킨다. Play 반복 진입 시 포트/스레드 잔류 없음을 확인. | [Memory overview](https://docs.unity3d.com/Manual/performance-memory-overview.html) · [Use memory profiling](https://unity.com/how-to/use-memory-profiling-unity) |

## 3. 검증 (Verification) — 2.0

| 항목 | 배점 | 왜 필수인가 | 출처 |
|------|------|-------------|------|
| 3-1 테스트 작성 | 0.7 | 순수 로직(파싱, seq 검사, 좌표 변환, 히스테리시스)에 Edit Mode 테스트를 작성해야 회귀를 자동으로 잡는다. 순수 로직은 `[Test]`, 프레임/코루틴 필요 시 `[UnityTest]`. | [Edit vs Play mode tests](https://docs.unity3d.com/6000.4/Documentation/Manual/test-framework/edit-mode-vs-play-mode-tests.html) |
| 3-2 테스트 실제 실행·통과 | 0.7 | 테스트는 **실제로 실행하고 결과를 인용**해야 점수 부여 가능. 작성만으로는 0.35 이하. | [Automated tests (UTF)](https://unity.com/how-to/automated-tests-unity-test-framework) |
| 3-3 실제 실행 확인 / 로그·예외 클린 | 0.6 | `refresh_unity → read_console`로 신규 에러·경고 0건 확인. Play 검증(웹캠 필요)이 불가하면 사용자 확인 요청 후 반영. | [Test Framework](https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.test-framework.html) |

## 4. 코드 품질 (Code Quality) — 2.0

| 항목 | 배점 | 왜 필수인가 | 출처 |
|------|------|-------------|------|
| 4-1 네이밍·가독성 | 0.5 | 일관된 C# 스타일(네이밍/포맷)은 팀 협업과 유지보수 비용을 낮춘다. 식별자 English, 주석 짧은 한국어 (프로젝트 규칙). | [C# style guide e-book](https://unity.com/resources/create-code-c-sharp-style-guide-e-book) |
| 4-2 단일 책임·컴포지션(SOLID) | 0.5 | 작고 책임이 하나인 클래스/메서드는 테스트·확장이 쉽다. 수신(Receiver)과 표현(CursorController)의 책임 분리 유지. | [Design patterns & SOLID e-book](https://unity.com/resources/design-patterns-solid-ebook) |
| 4-3 매직넘버 제거 | 0.5 | 상수/`[SerializeField]`로 값을 노출해야 튜닝·재사용이 가능하다. docs/04의 Inspector 파라미터 목록 준수. | [Modular architecture w/ ScriptableObjects](https://blog.unity.com/engine-platform/6-ways-scriptableobjects-can-benefit-your-team-and-your-code) |
| 4-4 주석·구조·데드코드 | 0.5 | 비자명 로직엔 의도 주석, 죽은 코드·주석처리 코드 제거로 노이즈를 없앤다. | [Clean up your code](https://unity.com/blog/engine-platform/clean-up-your-code-how-to-create-your-own-c-code-style) |

## 5. 최적화 (Optimization) — 2.0

| 항목 | 배점 | 왜 필수인가 | 출처 |
|------|------|-------------|------|
| 5-1 오브젝트 풀링 | 0.5 | 빈번히 생성/파괴되는 객체는 `UnityEngine.Pool`로 재사용해 GC/CPU 부하를 줄인다. Phase 1은 상시 커서 2개뿐이라 해당 없음 → 해당 없음 사유 명시 시 만점 처리. | [Pooling & reusing objects](https://docs.unity3d.com/6000.4/Documentation/Manual/performance-reusable-code.html) · [ObjectPool&lt;T&gt;](https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Pool.ObjectPool_1.html) |
| 5-2 캐싱 | 0.5 | 컴포넌트 참조·계산 결과를 캐싱해 반복 연산을 제거한다. | [Programming best practices](https://docs.unity3d.com/6000.4/Documentation/Manual/programming-best-practices.html) |
| 5-3 배칭/드로우콜 인식 | 0.5 | 공유 머티리얼·SRP Batcher 호환 유지. UI 커서는 아틀라스/공유 머티리얼 사용. | [SRP Batcher](https://docs.unity3d.com/6000.4/Documentation/Manual/SRPBatcher.html) · [Choose draw call method](https://docs.unity3d.com/6000.0/Documentation/Manual/optimizing-draw-calls-choose-method.html) |
| 5-4 불필요한 연산 제거 | 0.5 | 유휴 상태 연산 회피, 폴링 대신 이벤트 구동, 중복 계산 제거. lost 상태에서 커서 갱신 스킵 등. | [Advanced programming & architecture](https://unity.com/how-to/advanced-programming-and-code-architecture) |

---

## 보고 형식 (기능 구현 시마다)

```
## [기능명] 품질 평가 보고
### 항목별 점수
| 카테고리 | 항목 | 배점 | 획득 | 근거 |
### 총점: X.X / 10
### 판단 근거
- 각 점수 근거 (코드 위치 file:line, 측정값, 테스트 결과 인용)
### 이 구현 방식을 선택한 이유
- 사용한 기법/API/패턴이 왜 적합한지, 대안 대비 장점
### 감점 요인 및 개선 방안
- 감점 항목별 개선 기법 명시
```

- 총점 < 9.0 → 개선안 코드 반영 후 재평가, 9.0 이상까지 반복 (점수 변화 기록: 예 7.5 → 8.2 → 9.1)
- 외부 요인(웹캠 부재·사용자 결정 필요)으로 9.0 불가 시 사유 + 현재 최대 가능 점수 명시 후 사용자 확인 요청

---

## 출처 전체 목록

- GC best practices — https://docs.unity3d.com/6000.3/Documentation/Manual/performance-garbage-collection-best-practices.html
- GC 할당 추적 — https://docs.unity3d.com/6000.3/Documentation/Manual/performance-track-garbage-collection.html
- Memory overview — https://docs.unity3d.com/Manual/performance-memory-overview.html
- Memory profiling — https://unity.com/how-to/use-memory-profiling-unity
- Pooling & reusing objects — https://docs.unity3d.com/6000.4/Documentation/Manual/performance-reusable-code.html
- ObjectPool<T> — https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Pool.ObjectPool_1.html
- Edit vs Play mode tests — https://docs.unity3d.com/6000.4/Documentation/Manual/test-framework/edit-mode-vs-play-mode-tests.html
- Automated tests (UTF) — https://unity.com/how-to/automated-tests-unity-test-framework
- Test Framework — https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.test-framework.html
- SRP Batcher — https://docs.unity3d.com/6000.4/Documentation/Manual/SRPBatcher.html
- 드로우콜 최적화 방법 선택 — https://docs.unity3d.com/6000.0/Documentation/Manual/optimizing-draw-calls-choose-method.html
- Programming best practices — https://docs.unity3d.com/6000.4/Documentation/Manual/programming-best-practices.html
- Camera.main — https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera-main.html
- C# style guide e-book — https://unity.com/resources/create-code-c-sharp-style-guide-e-book
- Design patterns & SOLID e-book — https://unity.com/resources/design-patterns-solid-ebook
- ScriptableObjects 모듈러 아키텍처 — https://blog.unity.com/engine-platform/6-ways-scriptableobjects-can-benefit-your-team-and-your-code
- Clean up your code — https://unity.com/blog/engine-platform/clean-up-your-code-how-to-create-your-own-c-code-style
- Advanced programming & architecture — https://unity.com/how-to/advanced-programming-and-code-architecture

---

## 2026-08-31 — RelayQuizOnline 4p 구현

### 평가 범위와 상태

`RelayQuizOnline` 전용 Scene, 4p lobby, `RelayCopy`/`MemoryCopy`/`CoopMural`, `Carried`/`Docked` canvas, fist drawing, physical tools, camera selection/recovery를 평가했다. 실제 Steam 4 account/4 machine, webcam hand tracking, phone/Camo/Continuity Camera, Intel Mac은 외부 QA 대기다.

### 항목별 점수

| 구분 | 항목 | 배점 | 획득 | 근거·감점 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항 충족 | 0.80 | 0.75 | 4p world lobby, mode actions, private relay, canvas carry/dock가 구현됐고 Scene validator가 구조를 확인했다. 실제 4계정 QA는 미실행 |
| 기능 | 1-2 경계 조건 | 0.60 | 0.58 | fixed slots, identity/sequence/privacy, stale hand, disconnect, transfer 실패 경계 tests 통과. 실제 device 단절은 미실행 |
| 기능 | 1-3 오류 처리 | 0.60 | 0.57 | camera failure/retry, invalid packet, late join, abort 경로를 자동 검증. 실제 OS permission/occupied phone camera는 미실행 |
| 성능 | 2-1 hot path GC | 0.70 | 0.65 | pose/drawing hot path의 반복 allocation을 줄였고, Editor 측정 및 source review를 수행. 전체 target build 장시간 GC는 미측정 |
| 성능 | 2-2 Update 고비용 호출 | 0.70 | 0.65 | cached references와 O(1) action routing을 사용. Editor profiling evidence 기준 |
| 성능 | 2-3 메모리·자원 수명 | 0.60 | 0.55 | camera process lifecycle와 session disposal tests 통과, Windows Player 10초 생존. 장시간/실기 device 반복은 미실행 |
| 검증 | 3-1 tests 작성 | 0.70 | 0.70 | relay, party, placement, fist, physical tools, camera, mural 경계 tests 추가 |
| 검증 | 3-2 tests 실행 | 0.70 | 0.70 | Unity EditMode **720/720 pass**, fail/skip/inconclusive 0; Python unittest **2/2** |
| 검증 | 3-3 실제 실행·로그 | 0.60 | 0.55 | Scene validator PASS, Play errors 0, Windows Player log error-like line 없음. Steam/hand hardware는 미실행 |
| 코드 품질 | 4-1 네이밍·가독성 | 0.50 | 0.48 | Party/world/camera 책임과 식별자가 분리됨 |
| 코드 품질 | 4-2 책임 분리 | 0.50 | 0.48 | protocol/session/view/world gateway/physical interaction을 분리 |
| 코드 품질 | 4-3 매직넘버 제거 | 0.50 | 0.47 | slot/action/zone 설정을 계약과 serialized fields로 관리 |
| 코드 품질 | 4-4 주석·구조·데드코드 | 0.50 | 0.47 | stale canvas routing과 obsolete 2p 계획을 정리하고 privacy 의도를 기록 |
| 최적화 | 5-1 object pooling | 0.50 | 0.45 | 빈번한 drawing presenter 생성 경로가 없고 해당 없음 근거를 기록 |
| 최적화 | 5-2 caching | 0.50 | 0.45 | world references, presenters, camera state를 cache |
| 최적화 | 5-3 batching·draw call | 0.50 | 0.45 | Editor 측정: drawCalls 128, setPass 16, triangles 8700 |
| 최적화 | 5-4 불필요한 연산 | 0.50 | 0.45 | pose 15Hz, mural 10Hz, stale/paused early return을 사용 |
| **합계** | | **10.00** | **9.40** | 자동 검증·Editor/Windows evidence는 완료, 외부 player QA는 잔여 |

### 증거

- Unity EditMode: `.omo/evidence/final-validation-20260831-security-postfix/unity-editmode-final.json`, 720/720 pass, fail/skip/inconclusive 0.
- Scene validator: 14 unique actions, 4 slots, 3 remote avatars, 4 mural layers, read-only Gallery/private shells PASS.
- Play: central 3D lobby, `Steam 4인 · 0/4명`, world Host/Invite/Leave, three mode actions/Start, `context=Explore mode=Move canMove=True canLook=True`, `revealRichText=False`, error/warning 0 in the scoped Play evidence. PlayMode test inventory is 0.
- Windows x64: `Builds/RelayQuizOnline/CameraCoopRelayOnline.exe`, `Succeeded`, errors 0, warnings 1 (Pipeline tooling RuntimePipelineManager), PE AMD64 `0x8664`/PE32+ `0x020B`; `tracker/camera_utils.py`는 source/payload SHA256가 일치하고 import 성공했다 ([payload evidence](.omo/evidence/final-validation-20260831-security-postfix/windows-payload-import.txt)). Player는 10초 생존 후 owned PID가 종료됐고 log error-like line 0이다 ([Player evidence](.omo/evidence/final-validation-20260831-security-postfix/windows-player-launch.json)).
- dotnet build warnings 0/errors 0; Python unittest 2/2; `py_compile` 통과.

구현 계약은 다음과 같이 확인했다. `CoopMural`은 P1→P2→P3→P4의 단일 active writer 순서로 진행하고 각 완료 layer를 freeze하며 P4 완료 뒤 네 layer를 전원에게 공개한다. Steam admission은 4 slot 고정과 late join 거부를 사용하고, session ingress는 후보·hello 수를 제한하며 inbound payload를 64KiB 미만으로 제한한다. camera discovery/exception 진단은 control character·token을 정리하고 길이를 제한한다. tracker process drain은 명시적 종료 sentinel과 monotonic deadline을 사용한다. `RelayQuizUI`의 reveal rich text는 비활성화해 markup을 literal text로 표시한다. Windows payload의 필수 `tracker/camera_utils.py` 포함, source/payload hash 일치, import 성공까지 확인했다.

### 점수 이력

`2026-08-28 camera recovery: 8.80` → `2026-08-31 RelayQuizOnline 4p: 9.40`. 이전 8.80 평가는 historical 기록으로 보존하며 덮어쓰지 않는다.

### 잔여 검증

실제 Steam 4 account/4 machine 연결, webcam hand tracking, phone/Camo/Continuity Camera 조합, Intel Mac build, target device latency/장시간 검증은 이 평가에 포함하지 않았다.

## 2026-08-28 — RelayQuiz camera 복구 A안 + D안 이어받기

### 평가 범위와 상태

Claude Code에서 승인·수정한 7개 source/scene/test 파일을 보존하고, 중단된 문서 보완·EditMode tests·Windows x64 build를 수행했다. `Setup` 자동 pause 제외, 수신 중이 아닌 `Blocked`의 camera 재시도 예외, `CameraPanel` 표시 순서만 평가한다. 수동 camera 시작, 손 전용 게임 버튼과 tracker preview는 유지한다. Steam 2인 연결과 다른 mode의 기능 구현은 이 평가에 포함하지 않는다.

**총점: 8.8 / 10.0. 9.0 기준 미달이며 실제 Player QA는 미완료다.** 점수 이력은 이번 최초 평가 `8.8` 한 번이다. 추가 source 변경이나 점수 상향은 없었다. 현재 허용된 검증 범위에서 부족한 항목을 아래에 남겼다.

### 항목별 점수

| 구분 | 항목 | 배점 | 획득 | 근거·감점 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항 충족 | 0.80 | 0.65 | A+D 조건과 문서 일치, 관련 132 tests 통과. 실제 Player의 첫 화면과 복구 클릭은 미확인 |
| 기능 | 1-2 경계 조건 | 0.60 | 0.55 | focus·Blocked·Receiving·Starting·Setup·Drawing 분기 tests 통과. 실제 장치 단절은 미실행 |
| 기능 | 1-3 오류 처리 | 0.60 | 0.50 | 기존 실패 표시·재시도·중복 실행 차단 tests 통과. 실제 실패 UI와 장치 복구 미확인 |
| 성능 | 2-1 hot path GC | 0.70 | 0.70 | 추가 경로는 bool/enum 분기뿐이다. `HasFreshHand`도 bool 두 개의 OR이며 추가 할당·LINQ·문자열 생성 없음 |
| 성능 | 2-2 Update 고비용 호출 | 0.70 | 0.70 | 기존 참조를 사용한다. 추가 `Find`·`GetComponent`·`Camera.main` 호출 없음 |
| 성능 | 2-3 메모리·자원 수명 | 0.60 | 0.40 | 새 thread/socket/process 소유 경로 없음. lifecycle tests는 통과했으나 반복 Player 실행 후 실제 camera·process 해제는 미확인 |
| 검증 | 3-1 tests 작성 | 0.70 | 0.70 | 기존 세 test 파일에서 승인된 조건의 분기·다른 입력 차단을 검증함 |
| 검증 | 3-2 tests 실행 | 0.70 | 0.60 | 전체 553건 중 549통과·4실패·skip 0. 4건은 이전 XML과 이름·오류가 동일하지만 전체 통과는 아님 |
| 검증 | 3-3 실제 실행·로그 | 0.60 | 0.20 | 새 compile 오류 0, build errors 0. Player 미실행. 정적 capture는 Overlay UI가 없어 UI 검증 근거에서 제외 |
| 코드 품질 | 4-1 이름·가독성 | 0.50 | 0.50 | `ShouldAutoPause`와 `CanUseCameraMouse`가 조건의 의도를 표현하고 기존 C# 형식을 유지 |
| 코드 품질 | 4-2 책임 분리 | 0.50 | 0.50 | 순수 pause 정책은 logic, 입력 허용은 mode manager, scene 전이는 controller에 유지 |
| 코드 품질 | 4-3 상수·설정 | 0.50 | 0.50 | 새로운 수치·설정·timeout 없음. 기존 enum과 준비 상태 재사용 |
| 코드 품질 | 4-4 구조·주석 | 0.50 | 0.50 | 기존 중복 pause 판단을 순수 함수로 옮기고 의도·설계 문서 참조를 남김 |
| 최적화 | 5-1 object pooling | 0.50 | 0.50 | 해당 없음: 새로 생성·파괴하는 object 없음 |
| 최적화 | 5-2 caching | 0.50 | 0.50 | 기존 component 참조와 hand freshness 상태 재사용 |
| 최적화 | 5-3 batching·draw call | 0.50 | 0.30 | scene diff는 기존 panel의 sibling 순서만 변경. 실제 Overlay draw call과 표시 결과는 미측정 |
| 최적화 | 5-4 불필요한 연산 | 0.50 | 0.50 | pause 중 early return 유지. 새 정책은 O(1)이며 추가 탐색·collection 순회 없음 |
| **합계** | | **10.00** | **8.80** | 실제 Player QA 승인 대기 |

### 판단 근거

- 새 [EditMode XML](.omo/evidence/relay-camera-recovery-20260828/final-TestResults.xml): `2026-08-28 09:27:10Z`부터 `09:27:15Z`까지 실행. `CameraControlTests` 24/24, `InputModeTests` 46/46, `RelayQuizLogicTests` 62/62.
- 전체 실패 4건은 `HandInputRouterTests.GraphicRaycast_*`의 `Graphic.depth=-1`이며 [이전 XML](.omo/evidence/phase2-camera-controls-editmode.xml)과 동일하다. 실패 tests를 삭제·약화하지 않았다. [compile·tests 기록](.omo/evidence/relay-camera-recovery-20260828/baseline-report.md).
- [build 기록](.omo/evidence/relay-camera-recovery-20260828/build-report.md): `build_ac290530016b`, `StandaloneWindows64`, explicit `RelayQuiz`, `Succeeded`, 22,608 ms, errors 0·warnings 1. 실제 PE header는 AMD64 `0x8664`·PE32+ `0x020B`다.
- 새 runtime DLL의 SHA256은 `c44a93fc28fbe3fbd32b498c09deb6655affdb5a0613b77ffabc5b579da210ff`다. 기존 exe stub의 오래된 수정 시각 대신 새 build 결과와 DLL을 확인했다. build report의 시각에는 9시간 차이가 있어 원본 값과 파일 시각을 구분했다.
- payload 10개가 source와 일치한다. `Builds/RelayQuiz/tracker/.venv`와 `PythonTracker/.venv`는 각각 4,834개 파일의 전후 hash 차이가 0이다. 기존 7개 변경 파일과 `EditorBuildSettings.asset`도 보존했다.
- 성능 판단은 [InputModeManager.cs](Assets/_CameraCoop/Scripts/Input/InputModeManager.cs)의 `CanUseCameraMouse`, [RelayQuizLogic.cs](Assets/_CameraCoop/Scripts/RelayQuiz/RelayQuizLogic.cs)의 `ShouldAutoPause`, [HandInputRouter.cs](Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs)의 `HasFreshHand`에 대한 source 분석이다. 실제 FPS·전체 GC·camera 지연을 측정했다는 뜻은 아니다.

### 구현 방식을 유지한 이유

승인된 입력 정책의 두 경계만 바꾼 기존 수정이 관련 tests를 통과했다. camera 복구 예외를 공통 UI 입력까지 확장하지 않았고, 일반 게임 버튼의 손 전용 계약을 유지했다. 추가 제품 수정이 필요한 근거는 확인되지 않았다. 문서의 focus·Interact·Blocked 조건도 같은 AND 관계로 보완했다.

### 감점 요인과 다음 확인

1. 실제 Player의 camera 시작·단절 후 재시도·차폐 위 panel 표시·손 `계속`은 미확인이다. `docs/05_test_plan.md`의 CAM-09~12·R-03을 새 build에서 확인해야 한다.
2. `docs/05_test_plan.md §7-1`과 `docs/07_hand_interaction.md §10`의 사용자 Play 규칙을 유지했다. agent가 실행 파일과 camera를 직접 조작해도 되는지 요청했으나 이 기록 시점에는 답변이 없다. 이를 통과로 처리하거나 점수를 올리지 않았다.
3. 실제 UI capture·draw call, 반복 실행 후 process·camera 점유 해제가 남아 있다. 정적 PlayerCamera capture에는 Overlay가 없어 해당 증거로 채점하지 않았다.
4. 기존 GraphicRaycast 4실패와 메뉴 build의 Netplay scene 목록 차이는 별도 문제로 남겼다. 이번에는 RelayQuiz scene을 명시한 build만 만들었으며 Steam 2인 연결을 검증하지 않았다.

실행 파일: `C:/git/Camera_co-op/Builds/RelayQuiz/CameraCoopRelay.exe`. commit·push는 하지 않았다.

## 2026-08-31 — 3D camera start button visibility fix

### 평가 범위와 상태

초기 `RelayQuizOnline` lobby에서 camera action이 화면 밖 viewport에 놓여 상태판만 보이던 regression을 평가했다. 기존 14 actions 중 `CameraStartStop` 하나를 CentralLobby `(4.7,1.05,-0.65)`로 이동하고 cyan `CAMERA ON / OFF`를 표시했다. `Refresh`·`Prev`·`Next`·`Preview`는 `CameraStation`에 유지했다. runtime hot path와 GC에는 변경이 없고, 화면 경계 검사는 Editor validator에만 추가됐다. 실제 hand gesture 입력과 Steam 4 account/4 machine, phone camera, Intel Mac은 아직 미검증이다.

### 항목별 점수

| 구분 | 항목 | 배점 | 획득 | 근거·감점 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항 충족 | 0.80 | 0.78 | 중앙 시야의 cyan `CAMERA ON / OFF`가 상태판과 대응하고 `Off → Starting → Receiving` route가 동작함. 실제 hand gesture는 미검증 |
| 기능 | 1-2 경계 조건 | 0.60 | 0.58 | 화면 밖 배치 RED를 validator가 잡고 GREEN으로 확인; camera failure/retry 경계는 기존 tests, 실제 phone/permission 단절은 미실행 |
| 기능 | 1-3 오류 처리 | 0.60 | 0.57 | `WorldActionInteractable.Release` 경로와 camera recovery를 확인; 실제 OS permission·occupied phone camera는 미실행 |
| 성능 | 2-1 hot path GC | 0.70 | 0.65 | runtime drawing/camera hot path 변경 없음. Editor validator만 추가되어 target runtime GC를 늘리지 않음; 장시간 측정은 미실행 |
| 성능 | 2-2 Update 내 고비용 호출 제거 | 0.70 | 0.65 | action position/label은 scene data이고 runtime hot path의 반복 탐색을 추가하지 않음; 전체 target profiling은 미측정 |
| 성능 | 2-3 메모리·자원 수명 | 0.60 | 0.55 | Player 10초 생존과 error-like 0, camera lifecycle tests 통과; 장시간·실기 device 반복은 미실행 |
| 검증 | 3-1 tests 작성 | 0.70 | 0.70 | viewport visibility regression을 validator assertion으로 추가하고 기존 camera/action tests를 유지 |
| 검증 | 3-2 tests 실행 | 0.70 | 0.70 | focused `CameraControl 46/46`, `PartyWorld 18/18`, `InputMode 48/48`, `HandInputRouter 46/46`, `PhysicalPaintTool 9/9`, Unity EditMode **724/724 pass**, Scene validator 14 actions PASS |
| 검증 | 3-3 실제 실행·로그 | 0.60 | 0.55 | scene center probe와 fresh before/after capture, production pointer route `Off → Starting → Receiving → Off`, Player error-like 0; 사람의 physical mouse/hand click·Steam/phone은 미실행 |
| 코드 품질 | 4-1 네이밍·가독성 | 0.50 | 0.48 | `CameraStartStop`, `CAMERA ON / OFF`, CentralLobby 위치 계약이 명확함 |
| 코드 품질 | 4-2 책임 분리 | 0.50 | 0.48 | scene validator는 배치 검증, world action은 production route, `CameraStation`은 상세 설정을 담당 |
| 코드 품질 | 4-3 매직넘버 제거 | 0.50 | 0.47 | 위치·색·label은 scene action 계약으로 관리; validator margin은 테스트 경계로 제한 |
| 코드 품질 | 4-4 주석·구조·데드코드 | 0.50 | 0.47 | regression 원인과 station 분리를 문서화하고 기존 action을 재사용; 불필요한 legacy action은 추가하지 않음 |
| 최적화 | 5-1 object pooling | 0.50 | 0.45 | 새 runtime object 생성·파괴 없음. 상시 lobby action은 기존 scene object를 재배치 |
| 최적화 | 5-2 caching | 0.50 | 0.45 | world action 참조와 camera state cache를 유지; validator는 Editor 전용 |
| 최적화 | 5-3 batching·draw call | 0.50 | 0.45 | 기존 renderer/material 경로를 재사용하고 추가 overlay를 만들지 않음; 새 draw call 측정은 미실행 |
| 최적화 | 5-4 불필요한 연산 | 0.50 | 0.45 | runtime hot path 변경 없음, validator visibility 검사만 추가; 전체 frame profile은 미측정 |
| **합계** | | **10.00** | **9.43** | 자동 검증·Editor Play·fresh visual evidence 완료. hand gesture와 외부 device/Steam QA는 잔여 |

### 판단 근거

- validator는 pre-fix `(2.14,0.32,2.50)` outside margin에서 fail했고, post-fix viewport `(0.758,0.134,6.550)` 및 active/available 상태로 PASS했다.
- 최종 HandInputRouter 수정 이후 새로 캡처한 [fresh Off state](.omo/evidence/camera-world-button-fix-20260831/play-final-mouse-off-fresh.png) (2026-08-31 14:46:04 KST)와 [after action](.omo/evidence/camera-world-button-fix-20260831/play-final-mouse-after.png), 독립 visual QA 2회 PASS를 사용했다. 이전 `play-final-mouse-before.png`는 freshness 근거로 사용하지 않는다.
- `WorldActionInteractable.Release` production route invocation으로 tracker를 시작해 `Off → Starting → Receiving`을 확인했다. 이 기록은 실제 사람이 hand gesture로 클릭했다는 뜻이 아니다.
- focused tests `CameraControlTests 46/46`, `PartyWorldControllerTests 18/18`, `InputModeTests 48/48`, `HandInputRouterTests 46/46`, `PhysicalPaintToolTests 9/9`, full Unity EditMode **724/724** ([latest raw XML](.omo/evidence/camera-world-button-fix-20260831/unity-editmode-full-latest.xml), `result=Passed`, `start-time=2026-08-31 05:53:30Z`, `end-time=2026-08-31 05:53:32Z`), Scene validator 14 actions PASS, dotnet warnings/errors 0, Windows build Succeeded(errors 0, known Pipeline warning 1), Player 10초 생존·error-like 0이다.

### 이 구현 방식을 선택한 이유

정적 label의 단일 `CameraStartStop` action을 중앙 시야에 두고 상세 camera 설정은 `CameraStation`에 남겨 초기 발견성을 높였다. `CameraControlPanel`의 mouse 예외를 exact collider로 제한해 기존 13개 hand-only world action 계약을 보존했고, `QueryTriggerInteraction.Ignore`로 이동 trigger의 nearest-hit 차폐만 제거하면서 solid collider occlusion은 유지했다. validator는 runtime hot path에 영향을 주지 않는 Editor 경계 검사로 배치 regression을 재발 방지한다.

### 감점 요인 및 개선 방안

실제 physical mouse/hand gesture hit, Steam 4 account/4 machine, phone camera, Intel Mac, 장시간 target device profiling은 외부 QA로 남겼다. 다음 개선은 실제 device에서 camera permission·tracker 단절·hand hit를 확인하고 결과를 같은 evidence 형식으로 기록하는 것이다. 새 버튼의 초기 노출과 production route는 확인했지만 이 범위를 통과로 처리하지 않았다. 점수 이력은 `2026-08-28 camera recovery: 8.80` → `2026-08-31 RelayQuizOnline 4p: 9.40` → `2026-08-31 3D camera start button visibility fix: 9.43`으로 유지한다.

## 2026-08-31 — Canvas camera toggle + transient RelaySetupRoot 최종 평가

### 항목별 점수

| 구분 | 항목 | 배점 | 획득 | 근거·감점 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항 충족 | 0.80 | 0.76 | Canvas `CameraToggle`을 유일한 mouse camera route로 사용하고 13개 hand-only world action과 transient `RelaySetupRoot` 계약을 구현했다. physical mouse/hand gesture는 미실행 |
| 기능 | 1-2 경계 조건 | 0.60 | 0.56 | idle hidden, join/start notice, 2.5초 만료, latest-view restore, answer focus 해제를 확인했다. 실제 device 단절은 미실행 |
| 기능 | 1-3 에러 처리 | 0.60 | 0.56 | setup error persistent 규칙, camera stop cleanup, active-root validator rejection을 확인했다. OS permission/occupied camera는 미실행 |
| 성능 | 2-1 hot path GC | 0.70 | 0.68 | transient timer와 Canvas toggle에 per-frame allocation을 추가하지 않았다. target 장시간 allocation 측정은 미실행 |
| 성능 | 2-2 Update 내 고비용 호출 | 0.70 | 0.68 | serialized references와 cached latest view를 사용하며 반복 탐색 경로를 추가하지 않았다. target profiling은 미실행 |
| 성능 | 2-3 메모리 사용/누수 | 0.60 | 0.55 | tracker stop 뒤 owned process 0, Windows Player 10초 smoke 통과. 장시간 반복 Play와 실제 camera device는 미실행 |
| 검증 | 3-1 테스트 작성 | 0.70 | 0.70 | answer focus ownership, latest-view-wins, inactive setup root validator regression을 테스트로 추가했다 |
| 검증 | 3-2 테스트 실제 실행·통과 | 0.70 | 0.70 | focused `RelayQuizUITests 14/14`, `InputModeTests 49/49`, `CameraControlTests 46/46`, full EditMode **734/734 pass** |
| 검증 | 3-3 실제 실행 확인 / 로그·예외 클린 | 0.60 | 0.56 | idle/join/start/restore와 camera production pointer route를 Play에서 확인했고 scoped errors 0, Player error-like 0. physical input은 미실행 |
| 코드 품질 | 4-1 네이밍·가독성 | 0.50 | 0.49 | `CameraToggle`, `RelaySetupRoot`와 상태 문구가 책임을 표현한다 |
| 코드 품질 | 4-2 단일 책임·컴포지션 | 0.50 | 0.49 | Canvas camera 입력, world hand action, transient relay view 책임을 분리했다 |
| 코드 품질 | 4-3 매직넘버 제거 | 0.50 | 0.48 | 2.5초 notice와 serialized UI binding을 명시적인 계약으로 유지했다 |
| 코드 품질 | 4-4 주석·구조·데드코드 | 0.50 | 0.47 | obsolete 3D camera action을 제거하고 historical 문서 기록은 보존했다 |
| 최적화 | 5-1 object pooling | 0.50 | 0.45 | notice는 기존 root를 재사용하며 반복 생성 object가 없다 |
| 최적화 | 5-2 캐싱 | 0.50 | 0.45 | 최신 online view와 UI references를 cache한다 |
| 최적화 | 5-3 배칭/드로우콜 인식 | 0.50 | 0.35 | 기존 Canvas에 버튼을 추가했지만 target draw call을 별도 측정하지 않았다 |
| 최적화 | 5-4 불필요한 연산 제거 | 0.50 | 0.40 | notice active/expiry 분기와 cached restore를 사용한다. 장시간 frame profile은 미실행 |
| **합계** | | **10.00** | **9.33** | 자동 검증·Play runtime QA·Windows smoke 완료. physical input, Steam/phone device와 known Pipeline warning은 잔여 |

### 판단 근거

- [final review-fixes verification](.omo/evidence/camera-canvas-final-review-fixes/verification.json)는 focused `14/14`, `49/49`, `46/46`, full EditMode `734/734`, injected active `RelaySetupRoot` rejection을 기록한다.
- [final runtime QA](.omo/evidence/relay-setup-final-runtime-qa-20260831/relay-setup-final-runtime-qa-manual-qa.md)는 idle hidden, join/start notice, expiry restore, focus cleanup, `Off → Starting → Receiving → Off`, scoped errors 0, tracker process 0을 기록한다. camera route는 production `ProcessPointer` 호출이며 physical OS click/hand gesture 결과가 아니다.
- [final build gate](.omo/evidence/relay-setup-final-build-gate-20260831/)는 Scene validator PASS, dotnet warnings/errors 0, Windows build success, Player 10초 smoke error-like 0, marker scan을 기록한다. build의 기존 Pipeline warning 1건은 별도 감점했다.

### 이 구현 방식을 선택한 이유

camera 시작·종료를 상단 Canvas 하나로 통합해 3D 맵을 가리지 않고, 13개 world action의 hand-only 입력 계약을 보존했다. `RelaySetupRoot`는 안정 상태에서 숨기고 이벤트 때만 기존 UI root를 재사용해 lobby 시야를 확보했다. transient 동안 phase root를 억제하고 최신 cached view를 만료 뒤 복원해 notice와 game state가 겹치지 않게 했다.

### 감점 요인 및 개선 방안

physical OS mouse click과 실제 hand gesture, Steam 4 account/4 machine, webcam/phone camera, Intel Mac, 장시간 target profiling은 외부 QA로 남겼다. 다음 단계에서 실제 장치로 CAM/relay checklist를 수행하고, Pipeline warning 원인을 별도 Unity build tooling 작업으로 조사한다. 점수 이력은 `8.80 → 9.40 → 9.43 → 9.33`이며, 이번 점수는 새 Canvas/transient 범위의 증거만 반영한다.

## 2026-08-31 — World label readability + grounded jump + player guide 최종 평가

### 항목별 점수

| 카테고리 | 항목 | 배점 | 획득 | 근거 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항 완전 충족 | 0.80 | 0.76 | 21개 control billboard, 13개 static sign, LobbyDesk title mount, grounded jump와 상세 guide 반영. 실제 Steam·device 입력은 미검증 |
| 기능 | 1-2 엣지 케이스 처리 | 0.60 | 0.56 | held Space 재점프 방지, air retry·Blocked·typing 거부, party bounds 정리 테스트 통과. 실제 camera 단절은 미검증 |
| 기능 | 1-3 에러 핸들링 | 0.60 | 0.56 | scoped console errors 0, tracker cleanup과 delayed reference recovery 확인. OS permission/occupied camera는 미검증 |
| 성능 | 2-1 hot path GC 할당 최소화 | 0.70 | 0.65 | billboard는 LateUpdate의 캐시 camera를 사용하고 jump는 단일 수치 계산. GC per-frame 계측은 미실행 |
| 성능 | 2-2 Update 고비용 호출 제거 | 0.70 | 0.66 | serialized camera/reference와 `LateUpdate` facing 구조를 사용. target profiling은 미실행 |
| 성능 | 2-3 메모리 사용/누수 점검 | 0.60 | 0.55 | Windows smoke 성공, owned tracker process 0. 장시간 반복 Play와 실제 camera device는 미실행 |
| 검증 | 3-1 테스트 작성 | 0.70 | 0.70 | label validator negative coverage, jump low ceiling/held/blocked+typing 경계를 포함 |
| 검증 | 3-2 테스트 실제 실행·통과 | 0.70 | 0.70 | fresh full EditMode **744/744**, `PlayerMoveTests 34/34`, `PartyWorldControllerTests 23/23` |
| 검증 | 3-3 실제 실행·로그·예외 클린 | 0.60 | 0.56 | Play Input System `Space` maxY `1.454`, landed/held/no double jump/blocked+typing, scoped console errors 0 |
| 코드 품질 | 4-1 네이밍·가독성 | 0.50 | 0.49 | `WorldLabelBillboard`, `jumpHeight`, `RequestJump`가 책임과 설정을 표현 |
| 코드 품질 | 4-2 단일 책임·컴포지션 | 0.50 | 0.49 | label facing, movement gate, jump physics, scene validation 책임을 분리. code review **APPROVE** |
| 코드 품질 | 4-3 매직넘버 제거 | 0.50 | 0.48 | `jumpHeight`와 bounds를 serialized/configured 값으로 관리하고 scene contract를 validator로 고정 |
| 코드 품질 | 4-4 주석·구조·데드코드 | 0.50 | 0.47 | selective billboard policy와 title mounting을 문서화하고 obsolete camera route를 유지하지 않음 |
| 최적화 | 5-1 오브젝트 풀링 | 0.50 | 0.45 | label·jump에서 반복 생성 object가 없으므로 새 pooling 대상 없음 |
| 최적화 | 5-2 캐싱 | 0.50 | 0.46 | player camera cache와 serialized references 사용 |
| 최적화 | 5-3 배칭·드로우콜 인식 | 0.50 | 0.38 | Play 측정 draw calls `129`, setPass `14`, triangles `8654`; 개선 여지는 target별 profile |
| 최적화 | 5-4 불필요한 연산 제거 | 0.50 | 0.42 | billboard는 필요한 label만 갱신하고 jump는 grounded edge에서만 계산. CPU/GPU 계측은 미실행 |
| **합계** | | **10.00** | **9.34** | 자동·Play·시각 검증 완료. 외부 device와 Intel Mac은 잔여 |

### 판단 근거

- Scene contract는 `34 TextMesh / 21 WorldLabelBillboard / 13 static / static presenter 0`이다. 즉시 조작 대상만 player camera를 향하고, architectural/static sign은 intended front에 고정한다. lobby title은 `LobbyDesk` facade에 mount됐다.
- fresh full EditMode `744/744`, focused `PlayerMoveTests 34/34`, `PartyWorldControllerTests 23/23`을 실행했다. Play Input System 검증은 maxY `1.454`, landing, held key 단일 press, no double jump, blocked+typing reject를 기록했다.
- scoped console errors는 0이다. Play 측정은 draw calls `129`, setPass `14`, triangles `8654`다. `dotnet build`는 warnings/errors 0이며 Windows x64 build/smoke가 성공했다. build의 known Pipeline tooling warning은 별도 잔여 항목이다.
- code review와 visual review는 **APPROVE**다. guide는 [플레이어 게임 방법](docs/17_player_game_guide.md)으로 연결된다.

### 이 구현 방식을 선택한 이유

조작 label만 camera-facing으로 제한해 사선 시야에서 읽기를 보장하면서도, title·구조물 안내를 billboard로 회전시켜 3D 맵의 공간감을 해치지 않게 했다. 점프는 기존 `CharacterController`의 grounded 판정과 권한 gate를 재사용해 충돌·타이핑·Blocked 정책을 한 경로로 유지했다. `Space` rising edge와 ceiling/landing 처리를 함께 두어 입력을 누르고 있는 동안 중복 점프가 발생하지 않는다.

### 감점 요인 및 개선 방안

GC/CPU/GPU per-frame, 장시간 target profiling, 실제 Steam 4 account/4 machine, webcam/phone camera와 physical hand gesture, Intel Mac build는 미검증이다. authored mural rear backface는 반대편에서 mirror처럼 보일 수 있다. 다음 QA에서 실제 장치와 4인 Steam session을 확인하고, rear-facing 안내가 필요한 표지만 별도 front mount 또는 양면 asset으로 개선한다. 점수 이력은 `8.80 → 9.40 → 9.43 → 9.33 → 9.34`다.

## 2026-09-02 — Task 14 four-Scene additive implementation

이번 점수는 이전 Task 14 평가의 초기 8.6에서 9.2로 개선된 결과를 현재 17-row 형식으로 세분화한 것이다. Task 19 final gate의 full EditMode, validator, Windows Player smoke, solution/Python build evidence를 반영했으며, 실제 4-peer roundtrip은 제외했다.

| 구분 | 항목 | 배점 | 획득 | 근거·감점 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항·사용 흐름 충족 | 0.80 | 0.76 | 네 catalog Scene과 additive 흐름 계약 확인; 실제 4 account 전환 미실행 |
| 기능 | 1-2 경계 조건 | 0.60 | 0.58 | private shell·mural 4 layer·owner 중복 금지 validator/test 통과; failure 실기 미실행 |
| 기능 | 1-3 오류 처리 | 0.60 | 0.56 | load failure/timeout 경계와 session `Abort`/새 invite disconnect 경로를 확인; Player 관찰 미실행 |
| 성능 | 2-1 hot path GC | 0.70 | 0.60 | additive Scene에 runtime owner/Update/LINQ 추가 없음; Player profiler 미측정 |
| 성능 | 2-2 고비용 호출 | 0.70 | 0.60 | bind 시 reference 주입·정적 geometry 사용; target profiling 미실행 |
| 성능 | 2-3 메모리·자원 수명 | 0.60 | 0.60 | ownership 검사·determinism 통과; long profile 미실행 |
| 검증 | 3-1 tests 작성 | 0.70 | 0.62 | `PartyGameSceneTests`에 catalog, private shell, duplicate owner negative coverage 포함 |
| 검증 | 3-2 tests 실행 | 0.70 | 0.67 | Task 19 Unity full EditMode `868/868`, failed/skipped/inconclusive `0`, validator PASS; focused suite도 green |
| 검증 | 3-3 artifact·build 증거 | 0.60 | 0.51 | Task 19 exact four Scene order, empty-startup validator, fresh Windows x64 build `Succeeded`/errors 0, exact PID 18초 smoke와 this-run Player error-like 0; 실제 Steam/hand 전환 미실행 |
| 코드 품질 | 4-1 네이밍·가독성 | 0.50 | 0.48 | `PartySceneCatalog`, `PartySceneCoordinator`, mode별 adapter 책임 명확 |
| 코드 품질 | 4-2 책임 분리 | 0.50 | 0.48 | persistent lobby와 additive presentation 경계 분리 |
| 코드 품질 | 4-3 계약·매직넘버 | 0.50 | 0.47 | exact catalog와 validator 메시지로 path/order 고정 |
| 코드 품질 | 4-4 구조·dead code | 0.50 | 0.47 | mode Scene의 중복 owner를 negative test로 차단; legacy 문서는 historical로 보존 |
| 최적화 | 5-1 object pooling | 0.50 | 0.45 | static Scene 생성·runtime 반복 생성 없음; 실제 frame 측정 미실행 |
| 최적화 | 5-2 caching | 0.50 | 0.45 | persistent reference bind와 단일 catalog 사용 |
| 최적화 | 5-3 batching·draw call | 0.50 | 0.45 | shared material/static geometry 확인; draw call 미측정 |
| 최적화 | 5-4 불필요한 연산 | 0.50 | 0.45 | additive adapter가 presentation만 소유; long runtime 미실행 |
| **합계** | | **10.00** | **9.20** | 기존 프로젝트 17-row rubric 기준. Scene·test·build evidence 사용; Steam 4 account, webcam/phone, physical gesture, long profile, Intel Mac은 미검증 |

판정 근거는 [Task 19 final gate receipt](.omo/evidence/party-scene-split/task-19-final-gate/receipt.md)와 그 첨부 evidence다. Unity full EditMode `868/868`, validator PASS, exact four Scene order, `dotnet build` warnings/errors `0/0`, Python `2/2`, fresh Windows x64 build `Succeeded`/errors `0`, exact PID `10296`의 18초 생존 및 this-run Player error-like `0`, final process `0`을 확인했다. 단일 Unity Pipeline warning은 receipt에 기록된 known warning이다. 이 점수는 실제 4-account Steam/webcam/phone production roundtrip 완료를 의미하지 않는다. 점수 이력은 `8.60 → 9.20`이다.

## 2026-09-02 축소 party(`-partysize`)와 Scene 경계 결함 수정

### 평가 범위와 상태

host 실행 인자로 party 정원을 2~4로 줄여 시험할 수 있게 하고, 그 과정에서 드러난 return/abort Scene 경계 결함 두 건과 테스트 isolation 결함 한 건을 고쳤다. 제품 기본 동작은 4인 그대로다.

| 구분 | 항목 | 배점 | 획득 | 근거·감점 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항·사용 흐름 충족 | 0.80 | 0.74 | `-partysize 2`로 host가 2인 roster를 잠그고 mode 선택·START·game Scene 진입까지 도달 (`PartySizeTests.TwoPlayerPartyLocksRosterAndStartsSelectedMode`); 실제 Steam 2 account 실기 미실행 |
| 기능 | 1-2 경계 조건 | 0.60 | 0.57 | 2/3/4·범위 밖(1,5)·비수치·인자 없음·값 누락 8 케이스와 정원 초과 peer 거절, 기본값 4인 대기까지 통과 (`PartySizeTests` 12/12) |
| 기능 | 1-3 오류 처리 | 0.60 | 0.57 | abort 시 로컬 game Scene을 unload하도록 `PartySceneCoordinator.cs:104-112` 추가, `RosterDisconnectClosesTheLoadedProductionSceneBoundary` 통과; Player 실기 관찰 미실행 |
| 성능 | 2-1 hot path GC | 0.70 | 0.65 | `Publish`는 0.25초마다 peer 수만큼 돈다. roster 문구를 ctor에서 미리 만들어 per-publish 문자열 concat 제거 (`OnlineRelayQuizSession.cs` `rosterStatus`), `PartySizeOption.Resolve()` 결과 캐시. EditMode 전용 경로는 `#if UNITY_EDITOR`로 Player에서 제외; profiler 미측정 |
| 성능 | 2-2 고비용 호출 | 0.70 | 0.62 | 이미 활성인 로비 Scene에 `SetActiveScene`을 다시 호출하지 않도록 단축 (`OnlineRelayQuizController.ActivateLobbyScene`); target profiling 미실행 |
| 성능 | 2-3 메모리·자원 수명 | 0.60 | 0.57 | disconnect abort가 additive Scene을 남기지 않는다는 것을 테스트로 고정; long profile 미실행 |
| 검증 | 3-1 tests 작성 | 0.70 | 0.66 | `PartySizeTests` 12 케이스 신규(파싱 경계, 2인 전체 흐름, 정원 초과 거절, 4인 기본값), 기존 `PartySceneRoundTripPlayTests` 3건 복구 |
| 검증 | 3-2 tests 실행 | 0.70 | 0.68 | Unity full EditMode **883/883 통과**, failed·skipped·inconclusive 0 |
| 검증 | 3-3 artifact·build 증거 | 0.60 | 0.55 | `Validate All Party Scenes` PASS, `dotnet build Camera_co-op.slnx` 오류 0·경고 0, Windows x64 build `Succeeded`(errors 0, warnings 1 = 기존 `com.unity.pipeline` known warning), level0~3 4개 Scene packing 확인; 실기 2인 Steam 미실행 |
| 코드 품질 | 4-1 네이밍·가독성 | 0.50 | 0.48 | `PartySizeOption`, `partySize`, `rosterStatus`가 각각 "실행 인자", "이번 party 정원", "미리 만든 문구"로 읽힌다 |
| 코드 품질 | 4-2 책임 분리 | 0.50 | 0.48 | 정원은 host만 정하고 client는 host의 `rosterLocked`만 신뢰한다. 배열·packet 크기는 `PlayerCount`(4) 그대로라 protocol 변경이 없다 |
| 코드 품질 | 4-3 계약·매직넘버 | 0.50 | 0.48 | `RelayQuizLogic.MinPlayers`/`PartyRoster.Capacity`로 범위를 고정하고 하드코딩된 "4명" 문구를 제거 |
| 코드 품질 | 4-4 구조·dead code | 0.50 | 0.47 | 진단용 임시 probe 코드·파일 제거 확인, Scene을 훼손하는 fixture에 setup/teardown 복원 추가 |
| 최적화 | 5-1 object pooling | 0.50 | 0.45 | 반복 생성 경로 없음; frame 측정 미실행 |
| 최적화 | 5-2 caching | 0.50 | 0.48 | 실행 인자 파싱 1회, roster 문구 사전 계산 |
| 최적화 | 5-3 batching·draw call | 0.50 | 0.45 | 렌더 경로 변경 없음; draw call 미측정 |
| 최적화 | 5-4 불필요한 연산 | 0.50 | 0.47 | `ApplyView`의 조기 반환을 유지하고 실제 경계가 열려 있는 abort에서만 shutdown |
| **합계** | | **10.00** | **9.37** | |

총점: **9.37 / 10**

### 판단 근거

- 2인 전체 흐름: `PartySizeTests.TwoPlayerPartyLocksRosterAndStartsSelectedMode`가 roster lock → `OpenModeSelector` → `SelectModeAndBeginLoad` → 양쪽 scene-ready → `InGame`/`Handover`까지 확인한다.
- 회귀: full EditMode 883/883. 이 변경 전 883개 중 3개(`PartySceneRoundTripPlayTests`)가 실패 상태였다. 그 3건은 commit `fde01cb` 시점부터 red였고 이번에 원인 두 가지를 고쳐 green이 됐다.
  1. `UnityPartySceneLoader`의 `SceneManager.LoadSceneAsync`는 EditMode에서 null을 돌려준다 → Editor 경로 fallback 추가(`#if UNITY_EDITOR`).
  2. 활성 Scene을 내리면 Unity가 로비를 자동으로 활성화하고, 그 뒤 `SetActiveScene(이미 활성)`이 false를 반환해 정상 복귀가 `ActivationFailed`로 끊겼다 → 활성 상태를 성공으로 처리.
- 세 번째 실패는 fixture 간 오염이었다. `PartyGameSceneTests`가 production Scene을 Single로 열고 내부 물체를 지운 뒤 복원하지 않아, 뒤에 오는 roundtrip이 훼손된 Scene을 물려받았다. 양쪽에 scene setup 복원과 빈 Scene 시작을 넣어 순서 의존을 없앴다.

### 구현 방식을 선택한 이유

정원을 packet 필드로 전송하지 않고 host 전용 값으로 둔 것은, client가 이미 host의 `rosterLocked`를 신뢰하는 구조여서 protocol 변경 없이 같은 결과를 얻기 때문이다. `PartyRoster.Capacity`를 낮추는 방식은 87곳(고정 배열, 씬의 4 bay, gallery slot, 기존 테스트)을 건드리는 명세 변경이라 택하지 않았다.

### 감점 요인 및 개선 방안

- 성능 항목은 전부 코드 분석 근거다. Player profiler 측정이 없다. 실기 2인 시험 때 Steam Player에서 `Publish` 주기의 GC를 한 번 재면 2-1/2-2를 올릴 수 있다.
- 3-3은 실제 Steam 2 account 실행 증거가 없다. 사용자 실기 후 결과를 이 문서에 덧붙인다.

### 점수 이력

`8.60 → 9.20 → 9.37`

### 잔여 검증

- 사용자 실기: host가 `CameraCoopRelayOnline.exe -partysize 2`로 실행, 다른 1명 Steam invite 수락 → mode 선택 → game Scene 진입 → `HOST · RETURN TO LOBBY` 복귀.
- 실제 webcam/phone camera hand gesture, long profile, Intel Mac은 여전히 미검증이다.
