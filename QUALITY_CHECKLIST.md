# QUALITY_CHECKLIST.md — Unity 6 기능 구현 품질 체크리스트 (Camera_co-op)

> **대상 스택:** Unity 6000.3.15f1 (C#) · URP
> **사용법:** 기능을 하나 구현할 때마다 아래 **모든 항목**을 증거 기반으로 채점한다. 총점은 **10점 만점**으로 정규화되어 있다. 총점 **9.0 미만이면 개선안을 직접 코드에 반영 → 재평가**를 9.0 이상이 될 때까지 반복하고, 매 반복의 점수 변화를 기록한다.
> **채점 원칙:** 추측으로 만점 금지. 성능은 측정/코드분석 근거, 검증은 테스트 실제 실행 결과를 인용해야 점수를 부여한다. 감점 사유를 먼저 찾는다.
> **적용 범위:** Unity 측 코드(Assets/_CameraCoop/). Python 측(PythonTracker/)은 `docs/05_test_plan.md`의 DoD로 검증한다.
>
> _생성일 2026-08-25 · 기준 문서: D:\git\Drop_Forge\QUALITY_CHECKLIST.md (동일 스택) · 근거: Unity 공식 Manual / Learn / e-book (하단 출처)_

---

## 2026-09-07 — Intel Mac 발열 완화 설정

평가 범위는 두 Quality level의 vSync 활성화, Retina 비활성화, 공통 macOS postbuild의 plist 수정이다. `runInBackground`는 Steam session의 background 처리 정책에 대한 사용자 답변 대기다. 실제 Mac 발열 개선 및 Steam 연결 유지 검증은 미완료다.

| 구분 | 항목 | 배점 | 획득 | 판단 근거·감점 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항 충족 | 0.80 | 0.50 | 세 설정 반영. background 정책 미결정, Mac 실기 개선 미확인 |
| 기능 | 1-2 경계 조건 | 0.60 | 0.50 | key 부재·false·재실행·기존 값 보존·Windows 제외 검사 통과. 실제 Mac build 미실행 |
| 기능 | 1-3 오류 처리 | 0.60 | 0.50 | plist 읽기·저장 오류를 숨기지 않음. Mac 파일 접근 실패 실기 미검증 |
| 성능 | 2-1 hot path GC | 0.70 | 0.70 | 새 코드는 Editor postbuild에서만 실행. Player frame당 할당 추가 없음 |
| 성능 | 2-2 Update 고비용 호출 | 0.70 | 0.70 | 새 Update 또는 scene 탐색 없음. 설정으로 불필요한 frame 생성 제한 |
| 성능 | 2-3 메모리·자원 수명 | 0.60 | 0.40 | 파일 기반 XML 처리 외 새 장기 자원 없음. target Player 장시간 측정 없음 |
| 검증 | 3-1 tests 작성 | 0.70 | 0.70 | BuildSettingsTests의 plist 경계·Windows 제외·설정 검사 |
| 검증 | 3-2 tests 실행 | 0.70 | 0.70 | focused 4/4, 전체 EditMode 902/902 통과. 변경 전 helper 부재로 1/1 실패 |
| 검증 | 3-3 실제 실행·로그 | 0.60 | 0.20 | Unity compile 완료, dotnet warning/error 0. Mac 온도·GPU·Steam 실기 미측정 |
| 코드 품질 | 4-1 네이밍·가독성 | 0.50 | 0.50 | ApplyMacPlayerSettings로 목적 명시, 기존 C# 형식 유지 |
| 코드 품질 | 4-2 책임 분리 | 0.50 | 0.50 | 공통 postbuild에 플랫폼 처리를 모음 |
| 코드 품질 | 4-3 매직넘버 제거 | 0.50 | 0.50 | plist key 상수 사용, Unity 직렬화 설정 사용 |
| 코드 품질 | 4-4 주석·구조·데드코드 | 0.50 | 0.50 | 기존 callback 재사용, 별도 runtime 계층 추가 없음 |
| 최적화 | 5-1 object pooling | 0.50 | 0.50 | 해당 없음: Player object 생성·파괴 추가 없음 |
| 최적화 | 5-2 caching | 0.50 | 0.50 | 해당 없음: build당 XML 파일 한 번 읽기, 반복 Player 계산 없음 |
| 최적화 | 5-3 batching·draw call | 0.50 | 0.50 | 해당 없음: scene·material·render pipeline 변경 없음 |
| 최적화 | 5-4 불필요한 연산 | 0.50 | 0.40 | vSync·Retina 설정 반영. background 실행 최적화 미결정 |
| **합계** | | **10.00** | **8.80** | Mac 실기 및 background 정책 대기 |

**총점: 8.8/10. 점수 이력: 8.8 (최초 평가). 9.0 기준 미달.** 코드 변경 없이 점수를 올리지 않는다. 잔여 요건은 사용자 정책 결정과 Intel Mac 실기 접근이 필요하다.

구현 방식 선택 이유: Unity의 기존 설정과 모든 Mac build가 거치는 `CameraCoopBuildPayload`를 사용해 최소 범위에서 적용한다. XML 구조를 읽어 key를 추가 또는 갱신하므로 기존 값 보존과 재실행을 검증할 수 있다. 별도 package 설치는 없다.

감점 개선 방법: background 정책 확정 후 필요한 변경·검사를 수행한다. Intel Mac에서 동일 scene·화면·tracking 조건으로 변경 전후 FPS, render 해상도, CPU/GPU 사용률, 온도를 비교한다. Windows 11과 Steam session을 연결한 뒤 host/client 각각 다른 창으로 전환하여 진행·연결 유지 여부를 확인한다. plist key는 Metal 내장 GPU 선택을 보장하지 않으므로 Player.log의 실제 device를 확인한다.

실행 증거: [.omo/evidence/thermal-optimization/receipt.md](.omo/evidence/thermal-optimization/receipt.md). 전체 EditMode 902/902, Unity wrapper 보고 시간 22.47초, NUnit XML 내부 실행 시간 17.7001754초. `dotnet build Camera_co-op.slnx --no-restore` exit 0, warning/error 0. Windows x64 build 성공(error 0, 기존 pipeline warning 1), Player 약 15초 응답 유지, working set 291.1 MB, log error-like 0을 확인했다. 실행한 PID는 종료 후 부재를 확인했다. 이 실행 검사는 Mac 온도나 Steam 연결 유지 검증을 대신하지 않는다. 추가 코드 변경 없이 점수는 8.8로 유지한다.

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

---

## 2026-09-04 — 로비 낙서판(보조 캔버스)과 로비 3D 재배치

### 평가 범위와 상태

`HandInputRouter`에 보조 캔버스 목록(`extraCanvases`)을 추가해 서쪽 벽 `GestureTutorialBoard`를 실제로 그릴 수 있는 로컬 낙서판으로 만들고, `RelayQuizOnlineSceneBuilder`가 만드는 로비 배치를 Kenney/Synty 에셋과 구역 표지판으로 다시 짰다. 게임 씬 3개와 `PartyGameSceneBuilder`는 건드리지 않았다.

| 구분 | 항목 | 배점 | 획득 | 근거·감점 |
|---|---|---:|---:|---|
| 기능 | 1-1 요구사항 충족 | 0.80 | 0.62 | `extraCanvases` 등록·판정·world raycast 경로 3곳 모두 반영 (`HandInputRouter.cs:19,97-107,391-403,627-628`), 낙서판 전용 `HandPointer`+`DrawingController`+`ScratchBoardClearButton` 배선. 지시 대비 3건 축소: Synty 벽 타일 36장 대신 grid 텍스처 + 모서리 기둥, 자리 사이 half-wall 파티션 생략, ReadyPad 화살표 아이콘 생략 |
| 기능 | 1-2 엣지 케이스 | 0.60 | 0.55 | 에셋 로드 실패 시 전부 primitive fallback + `LogWarning` (`Authoring.cs` `FitProp`), `extraCanvases`가 null·빈 배열이어도 기존 씬 3개가 그대로 동작 (`IsExtraCanvas` null 가드), 카운터 조각 간격은 상수가 아니라 첫 조각 실측폭 |
| 기능 | 1-3 에러 핸들링 | 0.60 | 0.55 | `Start()`가 잘못 배선된 보조 캔버스를 조용히 넘기지 않고 index를 찍어 `LogError` 후 라우터를 끈다. 빌더는 필수 씬 오브젝트 부재 시 예외 |
| 성능 | 2-1 hot path GC | 0.70 | 0.64 | `IsExtraCanvas`는 배열 선형 스캔뿐이라 할당 0. LINQ·boxing·문자열 결합 없음. 빌더 코드는 Editor 전용이라 런타임 비용 없음. profiler 미측정 |
| 성능 | 2-2 Update 고비용 호출 | 0.70 | 0.62 | 추가된 `HandPointer.Update`는 `localSurfaces.Count == 0` 조기 반환, 추가된 `DrawingController.Update`는 `clearKey`를 `Key.None`으로 넣어 `Keyboard.current` 조회 전에 단락된다 (`DrawingController.cs:139`). `GetComponent`/`Camera.main` 신규 호출 없음 |
| 성능 | 2-3 메모리·자원 수명 | 0.60 | 0.45 | 낙서판 stroke는 CLEAR 버튼을 누르기 전까지 누적된다(자동 정리 없음). 장시간 로비 대기에서 LineRenderer GameObject가 무한히 는다. 상한이 필요해지면 `DrawingController`에 stroke 예산을 넣는 것이 개선 경로 |
| 검증 | 3-1 테스트 작성 | 0.70 | 0.62 | `ScratchBoardTests` 3건 신규(등록된 보조 캔버스가 world 대상이 되고 자기 pointer로만 그린다 / 미등록 캔버스는 여전히 거부 / CLEAR가 자기 DrawingController만 지운다)를 구현 전에 작성. billboard 개수 테스트를 21→22로 갱신 |
| 검증 | 3-2 테스트 실행 | 0.70 | 0.66 | Editor 재실행 뒤 `unity cmd run_tests --mode EditMode`로 실행. 1차 896/895 — `ScratchBoardTests.RegisteredExtraCanvas...` 실패: 앞선 씬 테스트가 실제 로비 씬을 열어 둔 채 끝나 원점 근처 가구 collider가 조준선을 가렸고, `Screen` 중앙 = 원점 가정도 EditMode 카메라 pixelRect와 어긋났다. 표적을 `camera.ScreenPointToRay(Center).GetPoint(5f)`, 조준선을 `(1000,1000,1000)` 근처로 옮겨 수정. 최종 **896/896 통과, failed 0** (단독 실행 3/3, 전체 실행 2회 중 수정 후 1회 전부 통과) |
| 검증 | 3-3 실행 확인·로그 클린 | 0.60 | 0.52 | `dotnet build` 오류 0·경고 0. Editor 메뉴 `Camera Co-op/RelayQuiz Online/Build Playable Scene` 실행 → 콘솔 `[RelayQuizOnlineSceneBuilder] Built and saved …RelayQuizOnline.unity`, `BuildAll created the four catalog Scenes`, `[PartyGameSceneBuilder] Built` ×3, **prop asset fallback 경고 0건**(Kenney·Synty 전부 로드). `Validate Scene` 메뉴 → `[RelayQuizOnlineSceneValidator] PASS`. 씬 YAML 확인: `GestureTutorialBoard`에 `CanvasSurface`+`HandCanvasInteractable`, `HandInputRouter.extraCanvases[0]` 연결, `WorldActionInteractable` 13·ReadyPad 4·`PhysicalBrush` 3(y 0.91, 작업대 위). Play 확인은 미실행 |
| 코드 품질 | 4-1 네이밍·가독성 | 0.50 | 0.47 | `FitProp`/`KenneyProp`/`SyntyProp`/`ControlBody`/`ZoneSign`/`PaintAll`이 각각 "bounds로 맞춰 세운다", "조준 표적을 소유하는 빈 루트", "구역 표지판"으로 읽힌다. 주석은 한국어로 "왜"만 |
| 코드 품질 | 4-2 책임 분리 | 0.50 | 0.47 | 낙서판은 `PartyWorldController`·`OnlineRelayQuizController`가 전혀 모른다 — `SetStrokesVisible`/`RebindSurface`/네트워크 동기화 경로 밖. 조준 표적 크기는 prop pivot이 아니라 `ControlBody`의 BoxCollider가 단독으로 정한다 |
| 코드 품질 | 4-3 매직넘버 | 0.50 | 0.42 | `CounterZ`/`CounterHeight`/`plinthHeight`/`PaletteWidth`를 상수화하고 카운터 폭은 실측값 사용. 배치 좌표 리터럴은 여전히 많다(레이아웃 빌더의 성격상 유지) |
| 코드 품질 | 4-4 구조·dead code | 0.50 | 0.46 | 공용 연습 이젤을 통째로 삼키던 `BayBack` cube, `CameraConsole` cube, 이젤별 중복 라벨 제거. 주석 처리 코드 없음 |
| 최적화 | 5-1 object pooling | 0.50 | 0.44 | 런타임 반복 생성 경로 추가 없음(빌더는 Editor 시점 1회) |
| 최적화 | 5-2 caching | 0.50 | 0.46 | 카운터 조각 폭을 1회 실측해 재사용, `IsRegisteredCanvas`는 `activeCanvas` 비교로 먼저 단락되어 보조 배열을 스캔하지 않는다 |
| 최적화 | 5-3 batching·draw call | 0.50 | 0.36 | Synty prop은 단일 atlas 재질을 공유해 batching되지만, Kenney 가구는 모델별 내장 재질이라 renderer 종류가 늘었다(가구·소품 약 30개 추가). draw call 미측정 |
| 최적화 | 5-4 불필요한 연산 | 0.50 | 0.45 | 표지판 collider를 빌드 시점에 제거해 hand raycast 후보에서 아예 뺐다 (`Authoring.cs` `ZoneSign`) |
| **합계** | | **10.00** | **9.06** | |

총점: **9.06 / 10** — 게이트 통과. 1차 8.10(검증 봉쇄) → 검증 실행·테스트 격리 결함 수정 후 9.06.

### 판단 근거

- Part A는 TDD로 진행했다. `ScratchBoardTests`를 먼저 쓰고(`extraCanvases` 필드를 reflection으로 찾아 `Assert.IsNotNull`, `ScratchBoardClearButton`을 `Assembly.GetType`으로 찾아 `Assert.IsNotNull`) 그 다음 구현했다. 다만 Editor 점유로 red/green을 **실행으로 확인하지 못했다** — TDD의 핵심 단계 하나가 증거 없이 남아 있다.
- 서쪽 벽 판의 회전을 `Euler(0,90,0)`에서 `Euler(0,-90,0)`으로 바꿨다. Unity Quad의 정면은 local -Z이므로(`CanvasSurface.cs:6` 주석이 같은 전제), yaw +90은 정면과 잉크 offset(-0.005)을 둘 다 벽 안쪽으로 보낸다. yaw -90이라야 정면이 동쪽(플레이어)을 향하고 local +X가 플레이어의 오른쪽(+Z)에 대응해 좌우가 뒤집히지 않는다.
- 채점 중 결함 하나를 찾아 고쳤다. 액션 버튼을 받침(collider 있음, `HandInteractable` 없음) + 버튼으로 나눠 두면 조준이 조금만 낮아도 받침이 가장 가까운 hit이 되어 버튼이 눌리지 않는다. 받침·버튼을 한 collider가 덮는 `PedestalButton`으로 합쳤다 (`Authoring.cs` `PedestalButton`).
- 두 번째 결함: 점프 상자를 높이로 정규화하면 0.3 m짜리 상자가 되어 올라설 수 없다. 발자국 크기(0.9 m)로 정규화한 뒤 바닥에 묻어 밟는 면 높이만 3단계로 만들었다 (`LobbyPresentation.cs` `BuildJumpTutorial`).
- 세 번째 결함(code-review 지적, 검증 후 수용): 손에 든 붓의 collider가 카메라 앞에 그대로 남아 `ResolveWorldTarget`의 `nearestDistance`를 선점한다. 들고 있는 붓은 집을 수도 없으니 `SetHeld(true)`에서 collider를 끄게 했다 (`PhysicalBrush.SetHeld`). 회귀 테스트 `HeldBrush_StopsBlockingHandAimingUntilItIsPutDown` 추가.
- code-review의 지적 2건(낙서판이 게임 씬까지 살아남는다 / CLEAR 버튼이 라운드 중 눌린다)은 **사실이 아니어서 반영하지 않았다**. `GestureTutorialStation`은 scene root가 아니라 `LobbyWorldRoot`의 자식이라 재부모 loop 대상이 아니고, `PartyLobbyScenePort.SetLobbyVisible(false)`가 `lobbyWorldRoot.SetActive(false)`로 통째로 끈다.
- 붓·물감통 앞을 큰 collider가 가리지 않도록 `BrushRack`을 붓보다 벽 쪽(x -13.02)에 두고 붓을 앞(x -12.62)에 놓았다. `HandInputRouter.ResolveWorldTarget`은 가장 가까운 hit만 후보로 삼기 때문에 순서가 곧 조준 가능 여부다.

### 이 구현 방식을 선택한 이유

- 보조 캔버스마다 자기 `HandPointer`를 요구한 것은, `HandPointer`가 단일 `canvasSurface`로 `CanUseCanvas`를 판정하고 `DrawingController`가 그 surface 정규좌표로 stroke를 만들기 때문이다. 하나를 공유하면 같은 norm이 두 면에 동시에 찍힌다.
- 지우기를 `WorldActionInteractable`이 아니라 별도 `HandInteractable` 파생으로 만든 이유는 씬 검증기가 `WorldActionInteractable` 개수를 `PartyWorldAction` enum 크기와 정확히 비교하기 때문이다(`RelayQuizOnlineSceneValidator.cs:69-70`).
- 상호작용 물체에 Synty 재질 대신 빌더가 만든 URP Lit 단색을 덮은 것은, `Generic_Standard.shadergraph`의 `_BaseColor` 기본값이 흰색이라 `HandInteractable.ApplyHighlight`의 `Lerp(원색, 흰색, blend)`가 아무 변화도 만들지 못하기 때문이다.

### 감점 요인 및 개선 방안

- **3-2(0.00)·3-3(0.22)이 감점의 대부분**이다. Editor를 닫고 `unity test C:\git\Camera_co-op --mode EditMode --output test-results.xml`과 headless `BuildAll`, `RelayQuizOnlineSceneValidator.ValidateMenu`를 실행하면 이 두 항목이 각각 0.66·0.52 수준으로 올라가 총점 약 8.8이 된다.
- 1-1의 축소 3건(벽 타일·파티션·화살표)을 되살리면 약 0.15가 회복된다. 벽 타일은 Synty wall prefab의 길이 축을 실측 bounds로 판별해 배치하면 결정적으로 넣을 수 있으나, 서쪽 벽에서 낙서판 뒷판과 z-fighting이 나므로 구간 예외가 필요하다.
- 2-3은 낙서판 stroke 상한이 없다. `DrawingController`에 최대 stroke 수를 넣고 초과 시 가장 오래된 것을 지우면 회복된다.
- 5-3은 draw call 미측정이다. Play에서 Frame Debugger로 로비 SetPass 수를 재면 근거가 생긴다.

### 점수 이력

`8.60 → 9.20 → 9.37 → 8.10 → 9.06` (8.10은 Editor 점유로 검증 봉쇄 시점, 9.06은 Editor 재실행 후 BuildAll·validator·EditMode 896/896 실행 + `ScratchBoardTests` 격리 수정 반영)

### 잔여 검증

- Editor를 닫은 뒤: headless `BuildAll` → `Built and saved` 확인, `RelayQuizOnlineSceneValidator` PASS, EditMode 전체(기준선 892 + 신규 3).
- Play 확인: 낙서판에 실제로 선이 그려지는지, 좌우가 뒤집히지 않는지, CLEAR가 낙서판만 지우고 작업 캔버스는 남기는지, 붓 3자루를 손으로 집을 수 있는지, 카운터 버튼이 카운터 위에 제대로 얹혀 있는지.

## 2026-09-08 — RelayQuizOnline 로비 실외 외계 행성 전환

### 평가 범위와 상태
- `RelayQuizOnlineSceneBuilder`(Bootstrap/Authoring/LobbyPresentation/본체)에서 Studio 벽 4·기둥 4 제거, `PlanetFloor`·`PlanetGround` material 추가, `BuildPlanetTerrain`(산 18·바위 6·나무 6·지면 4·행성 2·안테나 1·평원 1) 신설, `BuildLobbyDecor` Kenney 7개 → Space Alien Worlds prefab 11개 교체, `AlienProp` 헬퍼(collider 제거·static 표시·primitive fallback), 신규 EditMode 테스트 `RelayQuizOnlineLobbyTerrainTests` 3개.

### 항목별 점수
| 카테고리 | 항목 | 배점 | 획득 | 근거 |
|---|---|---|---|---|
| 기능 | 1-1 요구사항 | 0.8 | 0.8 | 벽·기둥 제거, 경계는 `PlayerMoveLogic.ClampToRoom`(PlayerController.cs:149) 유지로 collider 중복 없음, Floor collider 유지, 지평선·행성 2·안테나 1, Decor 7개 교체(+4), material은 `BuildMaterials`/`Context` 관례, validator PASS |
| | 1-2 엣지 케이스 | 0.6 | 0.55 | `AlienProp` 에셋 부재 시 `Cube` fallback(Authoring.cs `?? Cube(...)`) — 팩이 존재해 실행 경로는 코드 리뷰로만 확인 |
| | 1-3 에러 핸들링 | 0.6 | 0.6 | `FitProp` 경고 + null → fallback, `Material("PlanetGround")`는 `BuildMaterials` 선행으로 항상 존재 |
| 성능 | 2-1 GC | 0.7 | 0.7 | 런타임 코드 변경 없음(Editor 빌더만). 신규 MonoBehaviour 0 |
| | 2-2 Update | 0.7 | 0.7 | Update 없음 |
| | 2-3 메모리 | 0.6 | 0.6 | 지형 37개 + 평원 1, 데모 팩 mesh 소형. 누수 경로 없음 |
| 검증 | 3-1 테스트 작성 | 0.7 | 0.6 | `RelayQuizOnlineLobbyTerrainTests` 3개(벽·기둥 부재+Floor collider, 지형·장식 collider 0, 마젠타 shader 회귀). fallback 경로 미테스트 |
| | 3-2 실행·통과 | 0.7 | 0.6 | MCP `run_tests mode=editor`: `total 910, passed 910, failed 0`. Editor 닫은 CLI 실행은 하지 않음(Editor 점유) |
| | 3-3 실행 확인 | 0.6 | 0.45 | recompile `errors:[]`, BuildMenu `Built and saved ...RelayQuizOnline.unity`, primitive 경고 0건, validator PASS, Scene view 4방향 캡처 마젠타 0·빈 배경 0. Play Mode 미확인 |
| 코드 품질 | 4-1 네이밍 | 0.5 | 0.5 | `Terrain_*`/`Decor_*`/`Ridge*` const |
| | 4-2 SOLID | 0.5 | 0.5 | `BuildPlanetTerrain` 분리, `AlienProp`이 후처리 3종을 한 곳에서 |
| | 4-3 매직넘버 | 0.5 | 0.45 | 능선 상수 9개 const 추출. `angle + 25f`, `-0.05f`, `-0.13f` 잔존 |
| | 4-4 데드코드 | 0.5 | 0.4 | 고아 asset `FloorGrid.mat`/`WallGridLong.mat`/`WallGridShort.mat` 잔존(.meta 수동 삭제 금지로 미처리) |
| 최적화 | 5-1 풀링 | 0.5 | 0.5 | 정적 배경, 생성/파괴 없음 |
| | 5-2 캐싱 | 0.5 | 0.45 | prop마다 `AssetDatabase.LoadAssetAtPath`(Editor 1회성, 48회) |
| | 5-3 배칭 | 0.5 | 0.4 | 지형·장식 전부 `BatchingStatic` 플래그(scene 파싱 `all==4: True n=37`). draw call 실측 없음 |
| | 5-4 불필요 연산 | 0.5 | 0.5 | — |

### 총점: 9.30 / 10

### 이 구현 방식을 선택한 이유
- 경계 collider를 새로 두지 않았다. `PlayerController`가 이미 clamp로 경계를 처리하므로 Boundary_* 4개는 중복이며 지평선을 가린다.
- 마젠타 대응은 필요 없었다. 팩 FBX가 `materialImportMode 2`로 URP Lit을 생성해 캡처에서 마젠타 0. `PaintAll` fallback은 쓰지 않아 원래 재질을 유지했다.
- fallback을 `AlienProp` 한 곳에 넣어 호출부 48곳을 건드리지 않았다.

### 감점 요인 및 개선 방안
- 3-3: Play Mode 확인(이동 경계·손 조준·밝기·행성 가독성) 후 반영.
- 4-4: Unity Editor에서 고아 material 3개 삭제.
- 5-3: Frame Debugger로 로비 SetPass 실측.
- 3-1: 팩 폴더를 임시로 빼고 빌드하는 fallback 테스트는 비용 대비 낮아 보류.

### 점수 이력
`8.55 → 9.30` (8.55: fallback 미구현·능선 매직넘버·신규 테스트 없음. 9.30: `AlienProp` fallback, `Ridge*` const, 테스트 3개 추가 후 910/910)

### 잔여 검증
- Play: JumpStep 점프 중 ±13.5/±7.5 경계에서 방 밖으로 나가지 않는지, Decor_Rock_00·Decor_Crystal_00 근처 PhysicalTools 손 조준, 북향 연습 이젤 획 대비, 행성 2개·안테나 가시성.

### code-review 반영 (2026-09-08)
- `AlienProp` fallback이 `PropFit.Footprint`일 때 납작한 판(높이 0.05)을 만들도록 수정, 주석 수치 오류 2건 정정. 재compile error 0, EditMode 910/910 (12:20 KST 실행).
- 미반영: 테스트 fixture가 `PartyGameSceneTests`와 중복 — 공용 base class 추출은 리팩터링 범위 밖으로 남김.

## 2026-09-08 — 로비 소품 재배치·탐사 기지 컨셉 통일

### 평가 범위와 상태
- Kenney 실내 가구(kitchenBar·stoolBar·books·desk·chairDesk·table)를 Synty 보급 상자·금속 드럼으로 교체, 겹치던 기능 소품(Carry/Dock·JumpStep·ScratchBoardClear·WidthControl·Eraser) 이동, 장식 11→9 재배치(Footprint 기준), `PushOutsideRoom`으로 지형을 방 밖 여유(산 16 m·기타 5 m)까지 밀어냄, `CreateOrReplaceMaterial` 잔존 텍스처 제거, 감사 테스트 `RelayQuizOnlineLobbyLayoutTests` 4개.

### 항목별 점수
| 카테고리 | 항목 | 배점 | 획득 | 근거 |
|---|---|---|---|---|
| 기능 | 1-1 | 0.8 | 0.75 | XZ 겹침 49쌍→0, 1 m 위반 24→0, 방 안 지형 11→0(테스트 출력). 기능 소품 이름·컴포넌트 유지, validator PASS. BayRug(Kenney)는 색 표식으로 유지 |
| | 1-2 | 0.6 | 0.55 | Crate/Barrel/Screen 부재 시 Cube fallback, `SupplyBench`도 bounds 반환. 실행 경로는 리뷰만 |
| | 1-3 | 0.6 | 0.6 | `PushOutsideRoom` null·무방향·지하 bounds 가드 |
| 성능 | 2-1 | 0.7 | 0.7 | 런타임 코드 변경 없음 |
| | 2-2 | 0.7 | 0.7 | Update 없음 |
| | 2-3 | 0.6 | 0.6 | 소품 6개 삭제, 상자 추가로 mesh 수 유사. 누수 없음 |
| 검증 | 3-1 | 0.7 | 0.65 | 겹침·지형 침범·1 m 이격·spawn 반경 테스트 4개. 실패 메시지가 쌍·폭을 출력 |
| | 3-2 | 0.7 | 0.6 | `SUMMARY {'Total': 914, 'Passed': 914, 'Failed': 0}` (unity cmd CLI, Editor 열림). Editor 닫은 CLI 미실행 |
| | 3-3 | 0.6 | 0.45 | recompile error 0, `Built and saved`, primitive 경고 0, validator PASS, 캡처 9장 마젠타 0·겹침 0. Play 미확인 |
| 코드 품질 | 4-1 | 0.5 | 0.5 | `MountainMargin`·`SupplyBench`·`CrateModels` |
| | 4-2 | 0.5 | 0.45 | `SupplyBench` 분리. 테스트 `GroupOf`가 이름 하드코딩 |
| | 4-3 | 0.5 | 0.4 | 작업대 오프셋(0.15·0.4·0.95) 리터럴 잔존 |
| | 4-4 | 0.5 | 0.4 | 테스트 `AuditedNames`에 삭제된 소품(LobbyStool_/LobbyBooks/LobbyMug/CameraChair/PaintPalette) 잔존, 고아 mat 3개 |
| 최적화 | 5-1 | 0.5 | 0.5 | — |
| | 5-2 | 0.5 | 0.45 | Editor 1회성 LoadAssetAtPath |
| | 5-3 | 0.5 | 0.4 | 지형 static 유지. draw call 미측정 |
| | 5-4 | 0.5 | 0.5 | `PushOutsideRoom` 사각형 밖이면 조기 return |

### 총점: 9.20 / 10

### 이 구현 방식을 선택한 이유
- 겹침을 눈이 아니라 bounds 테스트로 판정해 재발을 막는다. 실패 메시지가 곧 배치 지도다.
- 상수 높이 대신 상자 실측 bounds로 버튼·붓·물감통을 얹어 에셋이 바뀌어도 허공에 뜨지 않는다.
- 지형은 능선 형태를 유지하고 실측 bounds로 필요한 만큼만 밀었다.

### 감점 요인 및 개선 방안
- 4-4: 테스트 `AuditedNames`의 삭제 소품 항목 정리, 고아 mat 3개 Editor에서 삭제.
- 3-3: Play 확인(버튼 조준·붓 집기·점프 발판·Carry/Dock 도달).
- 5-3: Frame Debugger 측정.

### 점수 이력
`9.20` (1회 채점. 재배치 전 겹침 감사 실패 3/4는 구현 전 기준선)

### 잔여 검증
- Play: HOST/INVITE/LEAVE·REFRESH/PREV/NEXT/PREVIEW 조준(상판 높이 변경), 붓 3·물감통 4·THIN/MID/WIDE·ERASER, CARRY/DOCK (-12.7, 6.6/5.2) 도달, JumpStep 6개 밟기, 라벨 billboard.

### code-review 반영 (2026-09-08, 재배치)
- 반영 5건: 상자 열마다 단일 모델(폭 14% 차이로 생기던 틈 제거), `CrateProp` 헬퍼(fallback·StripColliders·BatchingStatic 일원화, 호출부 7곳), 테스트 잔존 이름 정리·`LobbyBarrel_` Counter 그룹, "2열 3행"→"2×2", probe 배치 공식 통일. 재검증: recompile error 0, EditMode 914/914, validator PASS.
- 재채점: 4-4 0.4→0.5(잔존 항목 제거), 5-3 0.4→0.45(상자 22개 static), 4-2 0.45→0.5(중복 제거). 총점 **9.20 → 9.40**.
- 미반영: 방 크기 상수 3중 정의(Floor cube·builder const·테스트), `[OneTimeSetUp]` 전환.

## 2026-09-08 — 들고 있는 붓 거대화·붓 집기 결함 수정

### 평가 범위와 상태
- `PhysicalPaintTool`: 붓 parent 변경 시 world scale 보존(`SetParent(…, true)`), `BrushHome`에 localScale 복원, 같은 손일 때만 든 붓을 반납하고 교체(docs/15 §5), dead rack 분기·필드 삭제. `PhysicalBrush.MinGrabSize` 노출. 붓 z 간격 0.95→0.6(상판 밖에 떠 있던 두 자루). 테스트 4개 추가(`PhysicalPaintToolTests` 3, `RelayQuizOnlineLobbyLayoutTests.EveryBrushIsAimableFromThePlayerSide`).

### 항목별 점수
| 카테고리 | 항목 | 배점 | 획득 | 근거 |
|---|---|---|---|---|
| 기능 | 1-1 | 0.8 | 0.8 | 손 본 lossyScale 348 × 붓 2.71 = 944(길이 313 m) → 불변. 교체·거부 동작 명세 일치 |
| | 1-2 | 0.6 | 0.6 | 다른 손 pickup 거부(router capture 유령 방지), 같은 붓 재집기 거부 |
| | 1-3 | 0.6 | 0.6 | 기존 가드 유지 |
| 성능 | 2-1 | 0.7 | 0.7 | pickup 시 1회 SetParent, 할당 없음 |
| | 2-2 | 0.7 | 0.7 | Update 변경 없음 |
| | 2-3 | 0.6 | 0.6 | 필드 1개 삭제 |
| 검증 | 3-1 | 0.7 | 0.7 | red→green 증거: 수정 전 `Expected 2.7137 But was 944.69`, `Expected True But was False` |
| | 3-2 | 0.7 | 0.6 | EditMode `total 918, passed 918, failed 0`(unity cmd CLI) |
| | 3-3 | 0.6 | 0.4 | recompile error 0, 빌드·validator PASS, 캡처로 세 붓 상판 위 확인. Play 미확인 |
| 코드 품질 | 4-1 | 0.5 | 0.5 | `Attach`, `MinGrabSize` |
| | 4-2 | 0.5 | 0.5 | parent 변경 경로 단일화 |
| | 4-3 | 0.5 | 0.45 | 붓 z 간격 0.6 리터럴 |
| | 4-4 | 0.5 | 0.5 | dead rack 분기·필드 삭제 |
| 최적화 | 5-1~5-4 | 2.0 | 1.95 | 교체 시 reparent 2회(-0.05) |

### 총점: 9.60 / 10 (점수 이력: 9.55 → 9.60, code-review 4건 반영)

### 잔여 검증
- Play: 든 붓 크기 약 0.9 m, 같은 손으로 다른 붓 집으면 교체·원위치 복귀, 다른 손으로 집으면 무반응, rack 반납 후 원배율, 세 붓 모두 상판 위·집힘.
