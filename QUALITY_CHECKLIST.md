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
