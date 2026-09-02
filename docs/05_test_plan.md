# 05. 검증 계획

> 갱신: 2026-09-02 · §1~5와 §7~10은 historical Phase/local 기록이다. **현재 `RelayQuizOnline` 4p 검증 결과는 §11-5에 기록한다.**
> 기존 Phase 1 채점 프로토콜: `QUALITY_CHECKLIST.md` 기준 총점 ≥9.0. 과거 기록을 현재 릴레이 검증 통과로 재사용하지 않는다.

## 1. Step별 완료 판정 기준 (Definition of Done)

### Step 1 — Python 손 추적 서버

| # | 기준 | 확인 방법 |
|---|---|---|
| 1-1 | venv에서 `python hand_tracker.py` 실행 시 에러 없이 루프 진입 | 콘솔 출력 |
| 1-2 | 프리뷰 창에 랜드마크 21개가 손 위에 오버레이됨 | 육안 |
| 1-3 | 추론 완료 프레임마다 UDP v1 JSON 송신, 해당 환경의 실제 Hz 기록 | 아래 원라이너는 Unity 수신을 중지해 포트를 비운 상태에서만 사용: `python -c "import socket,json; s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM); s.bind(('127.0.0.1',5052)); [print(json.loads(s.recvfrom(65535)[0])['seq']) for _ in range(10)]"` |
| 1-4 | 손 미검출 시에도 `hands: []` 패킷이 계속 송신됨 (heartbeat) | 위 원라이너로 손 숨기고 확인 |
| 1-5 | pinch 값이 핀치 시 ≈0.15~0.25, 벌림 시 ≈0.8+ 범위 | 원라이너 출력 확인 |
| 1-6 | 카메라 부재/모델 부재 시 원인이 명시된 메시지로 종료 | 웹캠 분리 후 실행 |
| 1-7 | `q` / Ctrl+C로 리소스 정리 후 정상 종료 | 콘솔 확인 |

### Step 2 — Unity UDP 수신부

| # | 기준 | 확인 방법 |
|---|---|---|
| 2-1 | Play 모드 진입 시 수신 스레드 시작, 패킷 수신 확인 로그 | Console |
| 2-2 | 최신 슬롯의 패킷을 수용한 Update에서만 LatestPacket 갱신, 없으면 이전 값 유지 | 수용 seq·시각 관찰 |
| 2-3 | 마지막 수용 이후 0.5초 이상에서 IsServerLost, 재시작 새 패킷 수용 후 복구 | 사용자 Play 중 Python 껐다 켜기 |
| 2-4 | Play 종료 시 스레드·소켓 정리, Editor 잔류 스레드 없음 | Play 재진입 반복 시 포트 바인딩 에러 없음 |
| 2-5 | `refresh_unity → read_console`에서 신규 에러·경고 0건 | Unity-MCP |

### Step 3 — 손 커서 표시

| # | 기준 | 확인 방법 |
|---|---|---|
| 3-1 | 손바닥 중심(0·5·9·13·17 평균)이 미러 방향으로 추종하고 손가락 굽힘만으로 조준점이 바뀌지 않음 | 육안 |
| 3-2 | 좌/우 손 커서가 색으로 구분되고 handedness가 실제 손과 일치 | 육안 |
| 3-3 | 핀치 시 커서 축소+색 변화, 히스테리시스로 경계 떨림 없음 | 육안 |
| 3-4 | 손 하나 숨기면 그 커서만 fade out, 서버 종료 시 둘 다 fade out | 육안 |
| 3-5 | `refresh_unity → read_console` 신규 에러·경고 0건 | Unity-MCP |

## 2. 통합 테스트 시나리오 (Step 4)

전제: Python 서버 실행 + Unity Play 모드.

| 시나리오 | 절차 | 기대 결과 |
|---|---|---|
| S1 한 손 추적 | 오른손만 카메라에 노출, 사각형 궤적 이동 | 오른손 커서만 표시, 부드러운 추종(가시적 지터 없음) |
| S2 두 손 동시 | 양손 노출, 교차 이동 | 두 커서 색 구분 유지, handedness 스왑 없음 |
| S3 핀치 토글 | 각 손으로 핀치 10회 반복 | 10/10 인식, 히스테리시스로 중간 떨림 없음 |
| S4 손 lost | 한 손을 화면 밖으로 | 활성 pinch End 후 해당 커서만 0.2초 fade, 복귀 시 fade in |
| S5 서버 단절 | Python Ctrl+C 종료 | 마지막 수용 후 0.5초 이상에서 lost, controller Update에서 End·fade 시작. 완전 소멸까지 fade 시간이 별도로 필요 |
| S6 서버 재시작 | Python 재실행 | 재연결 절차 없이 커서 자동 복구 |
| S7 장시간 | 5분 방치 후 조작 | 메모리 증가·프레임 드랍·커서 밀림 없음 |

## 3. 레이턴시 측정 방법

- **수용 처리 간격:** UdpHandReceiver가 최근 N=100 패킷의 메인 스레드 수용 간격을 기록한다. 평균 ≈33ms 기대는 30Hz 합성 송신 조건에만 적용한다. 카메라 입력의 실제 Hz는 별도로 기록한다.
  - 이 지표는 **UDP 도착 간격이 아니라 Unity가 패킷을 처리한 간격**이다. `RecordInterval`이 메인 스레드 `Update()`에서 호출되므로 Unity 프레임 갭이 그대로 값에 들어간다. 프레임이 늦으면 그 사이 도착한 패킷은 최신 1슬롯 방식대로 폐기된다 (`docs/01_architecture.md` §3 의도된 동작).
- **추론 이후 처리 지연:** `LastLatencyMs = (Unity 수용 epoch − packet.timestamp) × 1000`. 필터·직렬화·UDP·최신 슬롯 대기·Unity 파싱/수용 구간이며 캡처·추론 시간은 제외된다. 전체 손동작→화면 지연의 검증을 대체하지 않는다.
  - `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`가 ms 단위로 내림하고 로그가 직전 패킷 기준이라 **-1ms 수준의 음수가 정상 범위**로 나온다.
- 측정 로그는 개발용 토글(Inspector bool) 뒤에 두고 기본 off.

### 실측 결과 (2026-08-26, 합성 UDP 송신기 30Hz, 웹캠 부재)

Editor Play 모드, 5분 연속 8843 패킷, 통계 window 88개 기준.

| 지표 | 실측 | 기대 | 판정 |
|---|---|---|---|
| 수신 간격 평균 | 33.8 ~ 34.0ms (중앙 33.9) | ≈33ms | PASS |
| 5분 경과 시점 평균 | 33.9ms | 드리프트 없음 | PASS |
| 수신 간격 최대 (중앙) | 38.1ms | < 100ms | PASS |
| 100ms 초과 window | 1 / 88 (1.1%), 최악 353ms | < 100ms | **부분 미달** |
| 합성 timestamp→Unity 수용 지연 (당시 end-to-end 표기) | -1.2 ~ 5.0ms | < 100ms | 당시 PASS (전체 카메라 지연 아님) |
| 예외·에러 | 0건 | 0건 | PASS |

- 100ms 초과 스파이크는 **에디터가 백그라운드일 때의 프레임 갭**이 원인이다. Unity CLI로 매초 폴링하며 측정하면 초과 비율이 22%(9 window 중 2건)까지 오르고, 폴링을 끊으면 1.1%로 떨어진다. 포커스된 빌드에서 재측정이 필요하다.
- **지연 값에 주의:** 이 합성 송신 기록에는 캡처·MediaPipe 추론이 없다. 실제 hand_tracker의 timestamp도 추론 뒤 생성되므로 전체 카메라 지연은 별도 측정해야 한다. 현재 장치 연결 여부와 전체 지연은 이 과거 기록에서 확인되지 않는다.

### 실측 결과 — S7 장시간 (5분)

| 지표 | 시작 | 5분 후 | 판정 |
|---|---|---|---|
| totalAllocatedBytes | 2095MB | 2055MB | 누수 없음 |
| monoUsedBytes | 849MB | 944MB (+95MB) | GC 대기 garbage. 8843패킷 × 약 10KB ≈ 88MB와 일치 (`docs/04_unity_client.md` §2가 인지·허용한 JsonUtility 할당) |
| drawCalls / triangles | 7 / 1686 | 7 / 1686 | 오브젝트 누적 없음 |
| cpuFrameTimeMs | 4.16 | 3.08 | 악화 없음 |

절대값은 Editor 프로세스 기준이라 게임 실측치가 아니다. 추세만 유효하다.

## 4. Step 4 절차

1. 위 S1~S7 수행, 결과를 표로 기록 (통과/실패 + 관찰값).
2. `refresh_unity → read_console`로 에러·워닝 확인 (`execute_code` 금지).
3. 구현–문서 차이 발견 시: **문서 갱신 → 승인 → 코드 반영** 순서. 차이점을 보고서에 명시.
4. 전 항목 통과 + 체크리스트 ≥9.0 시 커밋: `feat: Phase 1 hand tracking pipeline`.

## 5. 알려진 전제·리스크

- 2026-08-26 측정 당시에는 웹캠이 없어 합성 송신기를 사용했다. 현재 장치 연결 여부는 별도로 확인해야 하며, 실손 검증에는 웹캠이 필요하다.
- MediaPipe 첫 실행 시 모델 초기화로 수 초 지연될 수 있다 — lost 판정과 무관 (Unity는 수신 시작 전 상태를 lost가 아닌 "미수신"으로 취급).

## 6. 로컬 릴레이 검증 절차와 책임

대상 설계: [06_player_controller](06_player_controller.md), [07_hand_interaction](07_hand_interaction.md), [08_drawing_canvas](08_drawing_canvas.md), [09_relay_quiz_mode](09_relay_quiz_mode.md). 로컬 씬과 Phase 기록은 historical 범위다. 현재 4p online 결과는 §11-5를 우선한다.

| 순서 | 담당·절차 | 완료 판정 |
|---|---|---|
| 문서 승인 | 사용자 | 승인된 문서 버전 확인 후 Step 1 착수 |
| 코드 변경 검증 | 에이전트: Unity MCP `refresh_unity → read_console` | 대상 프로젝트·컴파일 완료 확인, 오류 0, 신규 경고 0 또는 원인·영향 보고 |
| 자동 테스트 | 에이전트: MCP EditMode 테스트 | 기존 관련 테스트 유지, 변경 경계의 테스트 결과·실패 원인 기록 |
| Python 검증 | 에이전트: 현재 venv·모델·카메라 상태 확인 후 명시적인 Python 경로로 실행 | 정상 캡처·추론·UDP 로그. 카메라 권한·장치 부재는 실제 장애로 보고 |
| Play 검증 | **사용자가 Play 진입·조작·결과 전달** | 아래 체크리스트의 관찰 결과, 실패 시 단계·스크린샷·Console |
| Step 종료 | 에이전트 보고, 사용자 승인 | 승인 없이는 다음 Step으로 진행하지 않음 |

- Play 자동 진입, MCP `execute_code`, `.unity` 텍스트 편집, 승인 없는 패키지 추가는 금지한다. 씬 조작은 전용 MCP 도구만 사용한다.
- 테스트 실행이 씬 재로드·Play를 요구하면 현재 씬을 임의 저장하지 않고 사용자에게 필요한 조작을 안내한다. EditMode 테스트의 결과를 실제 Play 통과로 표시하지 않는다.
- Python 송신기는 한 개만 실행한다. 기존 프로세스를 임의 종료하지 말고 포트·카메라 점유를 확인한다. `fake_hand.py`의 성공은 실제 웹캠·IME·게임 한 판의 성공을 대신하지 않는다.
- Phase S에서 확인한 기존 경고는 `com.unity.pipeline`의 automated mode 관련 경고 1건이다. 이후 검증에서 현재 경고를 다시 읽어 비교한다. 과거 오류 0을 새 변경의 컴파일 결과로 인용하지 않는다.
- MCP가 끊긴 경우에만 Unity 배치 컴파일로 폴백한다. 같은 프로젝트를 연 Editor가 실행 중이면 배치 실행하지 않고 먼저 사용자에게 Editor 종료를 요청한다.

배치 폴백은 설치된 Unity.exe 경로를 확인한 뒤 아래 인자를 사용한다. 프로젝트 루트는 반드시 명시한다.

```text
Unity.exe -batchmode -quit -projectPath "C:\git\Camera_co-op" -logFile "C:\git\Camera_co-op\build_check.log"
```

종료 코드와 로그의 `error CS`, `Exception`을 확인한다. Unity CLI 래퍼를 별도로 사용할 때도 반드시 `--projectPath C:\git\Camera_co-op`를 지정한다. 파괴적인 대량 변경·삭제·구조 변경 전에는 사용자의 변경을 섞지 않는 git checkpoint를 먼저 만든다.

## 7. Phase 2 Step별 완료 판정

| Step | 자동·정적 확인 | 사용자 Play 확인 | 다음 승인 |
|---|---|---|---|
| 1 플레이어 | PlayerMoveTests 유지, InputModeTests의 권한 표·포커스·타이핑, 참조 할당·Legacy 유지, 컴파일 | WASD·대각선·벽 충돌·pitch·Tab·모드 HUD | Step 2 |
| 2 손 UI | ProtocolTests·PointerRouteTests 유지, HandInteractionTests의 up/cancel·freshness·재무장·양손 캡처, UI 입력 경로 | 버튼 3개·호버·눌림·음·정상 클릭·추적 상실·마우스/키보드 차단 | Step 3 |
| 3 드로잉 | 기존 DrawingTests 확장: 필터·분할·깊은 복사·undo·clear·load 원자성·스타일, 프리뷰 읽기 전용 | 양손 선·팔레트·굵기·undo·clear·UI 뒤 그리기 차단·복원 | Step 4 |
| 4 릴레이 | RelayQuizLogicTests: 2/3/4인 순서·타이머·중복 전이·정답·일시정지, 20개 단어·Inspector 연결 | 2인 한 판 + 3인 기억 재그리기 + 4인 순서, IME·차폐·갤러리 | Step 5 |
| 5 통합 | 최종 컴파일·관련 전체 EditMode·Python 로그·구현/문서 차이·기존 온라인 회귀 확인 | 아래 E2E와 실패 수정 후 재시험, Windows 빌드 IME | 최종 결과 |

새 테스트 파일은 새 입력 상태와 새 릴레이 로직에만 만든다. 드로잉 데이터 검증은 기존 `Assets/_CameraCoop/Tests/EditMode/DrawingTests.cs`를 확장하고 `DrawingArchiveTests.cs`를 중복 생성하지 않는다.

### 7-0. Step 1 실행 기록 — 2026-08-28

| 확인 | 결과 |
|---|---|
| 대상 | Unity 6000.3.15f1, `C:/git/Camera_co-op`, `codex/phase2-step1` |
| 테스트 우선 RED | 56건 중 기존 동작 9 통과, 미구현 기능 47 실패. 누락된 타입·필드·메서드가 실패 원인 |
| 구현 후 관련 테스트 | InputModeTests 28 + PlayerMoveTests 28 = **56/56 통과**. 기존 이동 계산 8건 유지 |
| 최종 전체 EditMode | **287/287 통과**, 실패·건너뜀 0. 테스트의 공개 API 호출을 타입 기반으로 정리한 뒤 재실행 |
| 최종 컴파일 | 13:30 KST `refresh_unity → read_console`, 오류 0. 기존 `com.unity.pipeline` automated mode 경고 1건 |
| 씬 검사 | MCP `validate`: 문제 0, missing script 0, broken prefab 0. 직접 참조·6개 루트·방 치수 확인 |
| 정적 화면 | Unity 창 캡처 1/1에서 한글 HUD·배치 확인. 구조·참조 검토와 CJK 검토 통과 |
| 사용자 Play | **사용자 보고 통과**. 2026-08-28 “Play 확인했어. 다 괜찮아 다음 단계 진행해”로 확인하고 Step 2 진행 승인. 에이전트의 직접 Play 관찰은 아님 |

최종 테스트 job은 `d7e89f61cc5e4a5bb7075924934f5d0c`다. 실제 MCP 응답, `red-TestResults.xml`, `final-TestResults.xml`, `final-source-manifest.json`, `unity-window-only.png`는 `C:/Users/yunji/AppData/Local/Temp/CameraCoop-Step1-20260828/`에 보관했다. 최종 XML의 실행 시각은 2026-08-28 04:30:53 UTC다.

검증 중 MCP 도메인 리로드의 WebSocket 경고와 Transform 속성 조회의 serializer 경고가 일시적으로 관찰됐다. 후속 도구 호출은 성공했고, 최종 콘솔에는 기존 pipeline 경고만 남았다. MCP 게임 캡처에는 Overlay HUD가 빠져 정적 UI 검토에 사용하지 않았으며, Unity 창 자체의 캡처를 사용했다. 편집 화면 확인은 Play 통과를 뜻하지 않는다.

### 7-1. Step 1 사용자 체크리스트

`Assets/_CameraCoop/Scenes/RelayQuiz.unity`를 열고 직접 Play를 시작한다. Game 뷰에 포커스를 둔다. 포커스 복귀로 손 조작 모드가 되었다면 Tab으로 이동 모드에 들어간다. Step 1에는 손 커서와 버튼이 아직 없으며 웹캠·Python 실행이 필요하지 않다.

- [ ] Move에서 WASD가 카메라 yaw 기준으로 움직이고 대각선이 더 빠르지 않다.
- [ ] 벽·이젤과 충돌하고 바닥 아래로 내려가지 않는다. 마우스는 우클릭 없이 회전하며 pitch가 뒤집히지 않는다.
- [ ] Tab 한 번에 Interact로 바뀌고 WASD·마우스 룩이 멈춘다. 다시 Tab하면 Move다.
- [ ] HUD·커서 잠금 상태가 일치하고 앱 포커스 복귀 때 갑자기 회전하지 않는다.
- [ ] Alt+Tab으로 나갔다 돌아오면 손 조작 모드로 유지되고, Tab을 다시 누르기 전에는 이동·회전하지 않는다.
- [ ] 기존 `Netplay3D`에서는 우클릭 유지 시에만 회전하고 기존 이동 경계가 유지된다.

2026-08-28 사용자가 Play 전체 결과를 통과로 보고하고 Step 2 진행을 승인했다. 항목별 상세 기록은 따로 받지 않았으므로 위 개별 칸은 일괄 체크하지 않는다. 이후 실패가 발견되면 씬·입력 순서·HUD 문구·Console 메시지를 함께 남긴다.

### 7-2. Step 2 실행 기록 — 2026-08-28

- 사용자 승인: Step 1 Play 통과 보고와 함께 Step 2 진행을 승인했다.
- 대상: Unity 6000.3.15f1, 로컬 RelayQuiz 씬, 손 샘플·라우터·버튼 3개. 패키지와 Python UDP 형식은 변경하지 않는다.
- 씬 조작은 전용 Unity MCP로 수행했다. `uiRaycasters` 배열은 도구 성공 응답뿐 아니라 실제 저장된 원소 참조도 대조했다.
- 최신 MCP `refresh_unity → read_console`에서 C# 컴파일 오류는 없었다. 기존 MCP WebSocket 재연결 경고와 pipeline 비자동 실행 경고 2종은 남아 있다.
- 2026-08-28 14:27 KST 전체 EditMode: **379건 중 375통과·4실패·skip 0**, 기존 287건 모두 통과. 신규 샘플 30/30, 버튼 16/16, 라우터 42/46. [전체 실행 XML](../.omo/evidence/phase2-step2-editmode.xml).
- 실패 4건은 실제 GraphicRaycaster의 임시 Canvas 깊이가 -1인 경우다. 대기·Game 뷰 활성화·즉시 repaint로 해결하지 못했다. 테스트는 그대로 유지하며 전체 통과로 기록하지 않는다. 자세한 관측은 [손 UI 기록](07_hand_interaction.md#미해결-검증)에 남겼다.
- 코드·명세 리뷰에서 발견한 참조 누락과 비활성화/선택 콜백 문제는 수정하고 회귀 테스트 통과를 확인했다. 초기 정적 화면은 두 독립 검토를 통과했으나, 마지막 자동 테스트 뒤 Overlay 전체 미표시를 재현했다. 저장된 씬 재열기로도 복구되지 않아 최종 화면 검증은 미통과다. [검증 기록](../.omo/evidence/phase2-step2-verification.md).
- Play 모드는 에이전트가 실행하지 않았다. 아래 사용자 확인 결과를 받은 뒤 Step 3 승인을 기다린다.
- 후속 사용자 캡처에서 Overlay·A/B/C·손 조작 안내 표시를 확인했다. 핀치 클릭 성공은 아직 보고받지 않았다. 캠 시작 버튼 누락에 대한 [보완안](07_hand_interaction.md#10-캠-시작-버튼-보완안-승인-완료)은 2026-08-28 사용자가 승인했다. 카메라 컨트롤의 시작·재시도·종료에만 마우스 왼쪽 클릭 예외를 구현한다.
- 카메라 보완 구현 후 관련 EditMode **85/85 통과**. 최종 전체는 **436건 중 432통과·기존 레이캐스트 4실패·skip 0**이며 새 57건은 모두 통과했다. [전체 XML](../.omo/evidence/phase2-camera-controls-editmode.xml), [보완 검증 기록](../.omo/evidence/phase2-camera-controls-verification.md). 테스트 뒤 Overlay 미표시가 있었으나 RelayQuiz를 다시 연 최신 Full HD 기본 화면에서 버튼·한글을 직접 확인하고 두 정적 검토가 통과했다. 기존 표시 문제의 원인, 다른 해상도와 실제 카메라 검증은 아직 미해결/대기다.
- 후속 사용자 보고: “카메라 되는거 확인했고, 이 화면에서 테스트할 수 있는건 다 했어. 다음 단계 구현해줘.” Step 2·카메라 화면 확인과 Step 3 진행 승인을 받았다. 개별 시나리오 횟수나 자동 실패 해결을 뜻하지는 않는다. 이어 “커밋도해줘” 요청을 받아 Step 5 전 커밋 대기를 해제했다.

### 7-3. 기존 Step 2 화면의 사용자 Play 체크리스트

아래는 앞서 확인한 A/B/C 시험 화면 기준이다. 현재 Step 3 화면은 §7-5를 사용한다.

1. `Assets/_CameraCoop/Scenes/RelayQuiz.unity`를 연다. 기존 Netplay3D와 다른 씬이다. 자동 테스트 뒤 UI가 표시되지 않으면 Unity를 재시작한다. A/B/C와 오른쪽 위 `캠 켜기`가 보이지 않으면 화면과 Console을 전달한다.
2. 사용자가 Play를 누른다. 최초 준비는 Interact·마우스 포인터 표시·이동 잠금이다. 오른쪽 위 `캠 켜기`를 마우스로 누르고 `시작 중…`에서 `송신 수신 중`으로 바뀌는지 확인한다. 별도 터미널 실행은 필요 없다. 이미 외부 Python 송신 중이면 `외부 캠 사용 중`으로 표시하며 종료는 해당 터미널에서 한다.
3. 연결되면 원래 게임 컨텍스트로 돌아간다. Explore/Move이면 Game 뷰에서 Tab으로 Interact에 들어가 손을 0.1초 이상 편다. A/B/C 위에서 엄지·검지를 모았다가 펴면 정상 클릭이다. 카메라가 꺼져 있거나 실패한 상태에서는 포인터와 준비 잠금을 유지해 재시도할 수 있다.

| ID | 확인할 동작 | 기대 결과 |
|---|---|---|
| S2-01 | A/B/C에서 각각 정상 핀치 10회 | 각 버튼의 확인 횟수 10회. 대상 문구와 서로 다른 높이의 확정음. 누른 채 유지해도 반복 실행 없음 |
| S2-02 | 버튼에 올리기 → 누름 → 해제 | 밝은 테두리·대상명, 눌림 축소와 손 색, 짧은 확정 강조. 왼손은 파랑, 오른손은 주황이며 L/R도 표시 |
| S2-03 | 누른 채 손을 숨기거나 Python 송신 중단 | 클릭·성공음 0회, 눌림 복구, 추적 안내. 복구 뒤 손을 펴고 새로 핀치해야 실행 |
| S2-04 | A를 누른 채 밖으로 이동하거나 B 위에서 해제 | A/B 모두 추가 클릭 0회. 누른 채 다시 들어와도 실행 없음 |
| S2-05 | 누른 채 Tab 전환 또는 다른 창으로 포커스 이동 | 클릭 0회. 복귀 뒤 held pinch로 실행되지 않으며 새 open·pinch 필요 |
| S2-06 | 양손이 같은 버튼을 동시에 누름 | 먼저 누른 손 하나만 소유. 한 번만 확정. 서로 다른 버튼의 hover는 독립적 |
| S2-07 | A/B/C에 마우스 좌/우 클릭·휠, Enter·Space·방향키 시도 | 확인 횟수 변화 없음. 카메라 컨트롤에만 왼쪽 클릭 허용. 연결된 Explore에서는 WASD·Tab 허용 |
| S2-08 | 1920×1080, 1280×720과 16:10에서 확인 | 버튼·글자·상태 안내가 겹치거나 잘리지 않음 |

결과는 `S2-01~08 통과` 또는 실패 ID·입력 순서·스크린샷·Console 메시지로 전달한다. 손/오디오 실측과 다른 해상도는 자동 EditMode 통과로 대신하지 않는다. 캔버스·팔레트·정답 입력창은 이번 테스트 씬에 아직 없다.

#### 카메라 보완 체크리스트 (historical 3D action 기록)

> 계약 보완일: 2026-08-28.

| ID | 확인할 동작 | 기대 결과 |
|---|---|---|
| CAM-01 | Play 후 버튼을 누르지 않고 대기 | 카메라 자동 실행 없음, 포인터 표시, 준비·이동 잠금 |
| CAM-02 | 캠 켜기, 시작 중 반복 클릭 | 송신기 한 번만 시작. 첫 유효 패킷 전에는 `시작 중…`, 수신 뒤에만 `송신 수신 중` |
| CAM-03 | 연결된 상태에서 손을 카메라 밖으로 내림 | 카메라 연결 표시는 유지, 손 추적 안내만 표시 |
| CAM-04 | Interact에서 캠 끄기 | 직접 시작한 Python 트리와 카메라 점유 해제, 손 캡처 취소, 준비 포인터 복구 |
| CAM-05 | 실행 실패 또는 연결 중단 뒤 재시도 | 오류가 유지되고 재시도 가능. 복구 후 손을 펴고 새로 핀치해야 클릭 |
| CAM-06 | 외부 Python 송신 중 Play | 외부 연결 표시, 중복 실행·외부 프로세스 종료 없음 |
| CAM-07 | 캠을 켠 뒤 Play 종료 | 직접 시작한 프로세스와 웹캠 점유 해제. 다음 Play에서 자동 실행하지 않음 |
| CAM-08 | 카메라 버튼 밖 클릭, 누른 뒤 영역 이탈, 포커스 변경, Enter/Space | 캠 실행·종료 없음. 영역 안에서 새 왼쪽 press/release만 동작 |
| CAM-09 | 카메라 준비 중 focus loss 후 복귀, `Blocked`에서 재시도 | focus 상실 중 클릭은 거부된다. 카메라 control이 available이고 app focus와 `Interact`가 모두 성립해야 하며, `Blocked`에서는 추가로 수신 중이 아닌 준비 상태(`IsCameraPreparing`)일 때만 카메라 패널 왼쪽 클릭으로 재시도 가능 |
| CAM-10 | 카메라가 수신 중인 상태에서 `Blocked` 진입 후 패널 클릭 | 카메라 mouse도 거부. 일반 게임 입력과 손 UI는 계속 차단 |
| CAM-11 | Handover·Pause 차폐가 켜진 상태에서 카메라 패널 클릭 | `CameraPanel`은 시각적으로 차폐 위에 있지만 실제 mouse 허용은 CAM-09/10 조건을 따른다. 다른 게임 버튼은 계속 손 전용이며 실행되지 않음 |
| CAM-12 | Setup에서 focus 상실·손 부재, 다른 상태에서 focus 상실·손 부재 | Setup은 자동 pause·차폐·secret/timer 생성 없음. 다른 상태 focus 상실은 pause, 손 부재 자동 pause는 Drawing에서만 발생 |

### 7-4. Step 3 실행 기록 — 2026-08-28

| 확인 | 결과 |
|---|---|
| 구현 | 양손 핀치 드로잉, 정규화 데이터·깊은 복사, 선 단위 지우개, Undo/Clear, 메모리 보관·복원, 읽기 전용 프리뷰 |
| 팔레트 | 색 6개·브러시 3개·굵기 3단계 Slider. 모든 작업 명령은 손 전용. 카메라 컨트롤의 마우스 예외 유지 |
| 테스트 우선 | 최초 관련 68건 중 26통과·42실패. 미구현 API 경계의 실패를 확인한 뒤 구현 |
| 최종 전체 EditMode | **484건 중 480통과·4실패·skip 0**. 기준 436건에 추가한 **48건 모두 통과** |
| 관련 최종 결과 | DrawingTests 51/51, HandCanvasRoutingTests 10/10, ToolStateTests 13/13 |
| 실행 증거 | job `5c3f25dc6ea8400f92fddb8baacd4c59`, 2026-08-28 16:07:30~35 KST. [XML](../.omo/evidence/phase2-step3/editmode.xml), [요약·소스 해시](../.omo/evidence/phase2-step3/test-summary.json) |
| 컴파일 | `refresh_unity → read_console` 후 새 도메인과 직렬화 필드 확인. C# 오류 없음. 기존 MCP 재연결·pipeline 비자동 모드 경고는 별도 유지 |
| 씬 | MCP 배선·저장, validate 문제 0. 작업 surface 하나, 읽기 전용 preview·gallery에 collider/손 입력 어댑터 없음 |
| 정적 화면 | Full HD 정상 한글 프레임 확인. 그러나 이후 저장·재활성화 캡처에서 글자 누락/atlas 깨짐 재현. **안정적인 최종 CJK 표시 판정은 보류** |
| 사용자 Play | **미실행**. 에이전트가 Play나 실제 카메라를 켜지 않았다. 아래 체크리스트 결과 필요 |

기존 실패는 `HandInputRouterTests.GraphicRaycast_`의 DisabledAdapterStillBlocks, HigherOverlaySortOrderWinsRegardlessOfArrayOrder, NonInteractableButtonStillBlocks, TopNonTargetGraphicBlocksUnderlyingTarget 네 건이다. 임시 Canvas의 `Graphic.depth == -1`이 남으며 테스트를 삭제·완화하지 않았다. 전체 통과로 기록하지 않는다.

초기 후속 컴파일 실패 때 이전 어셈블리로 실행된 결과와 잘못된 어셈블리 필터로 0건 실행된 결과는 검증 근거에서 제외했다. 최종 결과는 수정된 팔레트 fixture·프리뷰 크기 조정 테스트까지 포함한다. `.omo/evidence`는 로컬 검증 자료이며 제품 코드와 구분한다.

### 7-5. Step 3 사용자 Play 체크리스트

`RelayQuiz`를 열고 사용자가 Play를 누른다. A/B/C 패널 대신 캔버스·팔레트·아래 명령 버튼이 보여야 한다. 오른쪽 위 `캠 켜기`만 마우스로 누르고, 연결 후 손을 0.1초 이상 편다. 이 시험 화면은 Drawing context라 WASD·Tab 이동 전환이 잠긴다.

| ID | 확인할 동작 | 기대 결과 |
|---|---|---|
| S3-01 | 첫 화면과 캠 연결 확인 | 한글·버튼 정상 표시, 수신 후 `그리기` 상태. 글자가 깨지거나 빠지면 화면·Console 전달 |
| S3-02 | 캔버스에서 한 손·양손으로 핀치 유지 후 해제 | 손별 선이 독립적으로 이어지고 해제하면 멈춤. 손 숨김·캔버스 이탈 뒤 held pinch로 재개하지 않음 |
| S3-03 | 색 6개·펜/마커/세필·굵기 Slider 조작 | 선택 표시와 새 선 스타일 일치. 다른 손으로 그리는 중인 선은 시작 스타일 유지 |
| S3-04 | UI 위에서 누르기·밖에서 해제·두 손 동시 버튼 누르기 | 취소된 버튼 실행 없음, 같은 버튼 중복 확정 없음, 버튼 뒤 작업 선 없음 |
| S3-05 | 지우개로 완성 선을 가로지르기 | 닿은 선 하나를 통째로 제거. 보관 그림은 유지 |
| S3-06 | 실행 취소·전체 지우기, 다른 손은 그리기 유지 | 모든 활성 선이 먼저 종료됨. Undo는 마지막 시작 선 하나, Clear는 작업 전체 제거. 손을 다시 펴야 재개 |
| S3-07 | 그림 보관 → 작업 수정/지우기 → 보관 복원 | 보관 당시 좌표·색·굵기·순서로 복원. 수정·Undo·Clear가 보관본을 바꾸지 않음 |
| S3-08 | 프리뷰 숨김/보기, 프리뷰 위 핀치·지우개 | 보관본 보존, 프리뷰에는 새 선이나 지우기 없음. 실제 겹친 선·반투명 브러시 표시도 비교 |
| S3-09 | 마우스 클릭·휠·Enter/Space/방향키·C로 팔레트/명령 시도 | 변화 없음. 카메라 버튼의 왼쪽 클릭만 예외 |
| S3-10 | 1920×1080, 1280×720, 16:10으로 변경 | 팔레트·명령·한글이 겹치거나 잘리지 않음. 보관 프리뷰가 오른쪽 영역을 따라가고 선·폭 비율 유지 |

제시어·타이머·정답·턴 전환·갤러리 진행은 아직 없다. Step 3 확인과 다음 단계 승인을 받은 뒤 Step 4를 구현한다.

## 8. 2인 핫시트 end-to-end 체크리스트

전제: 웹캠과 Python 송신 상태 확인, 사용자만 Play 실행, 기본 타이머 5/60/5/30초, 손 외 UI 조작 금지. 플레이어 1·2의 물리 순서를 합의하고 다른 사람 차례에는 화면을 보지 않는다.

| ID | 사용자 절차 | 기대 결과 |
|---|---|---|
| H2-01 | 손으로 2인 선택·시작 | 인계 차폐와 플레이어 1 준비만 표시 |
| H2-02 | 손을 편 뒤 준비 핀치·해제 | 첫 사람에게만 제시어 5초 표시 |
| H2-03 | 첫 그림 작성, 팔레트·undo·clear 시험 | 시점 고정·손 입력만 동작, 제시어는 더 보이지 않음 |
| H2-04 | 손 `그림 완료` 또는 시간 만료 | 그림 한 번 저장, 실제 그림·제시어 숨김, 플레이어 2 인계 |
| H2-05 | 이전 핀치를 유지하며 화면 인계 | 다음 준비 버튼이 자동으로 눌리지 않음 |
| H2-06 | 플레이어 2가 새 준비 핀치·해제 | 직전 그림과 답변창 표시, 추가 드로잉·중간 관찰 상태 없음 |
| H2-07 | 손으로 답변창 선택, 손을 카메라 밖으로 내려 한글·WASD/C 문자를 입력·편집 | 타이핑 가능, 타이머 계속, 이동·회전·clear·Tab 탐색 없음 |
| H2-08 | 한글 조합 중 제출 시도 후 조합 확정, 손으로 제출 | 조합 중 제출 차단, Enter는 텍스트 확정만, 최종 손 제출은 1회 |
| H2-09 | 정답 또는 오답·빈 답으로 종료 | 공백·대소문자 정규화 후 결과 공개. 빈 답은 오답 |
| H2-10 | 손 `갤러리`, Move로 이동·시점 회전 | 첫 그림 1장이 원본대로 표시되고 손으로 편집되지 않음 |
| H2-11 | Tab으로 Interact, 손 `다시 시작` | Setup 복귀, 이전 그림·제시어·답·타이머·캡처 잔류 없음 |

같은 판의 정답과 오답을 동시에 확인했다고 기록하지 않는다. 서로 다른 판으로 정답·오답·시간 만료를 각각 확인한다.

## 9. 추가 필수 시나리오

2인으로는 기억 재그리기 구간을 검증할 수 없으므로 다음 시나리오도 수행한다.

| ID | 절차 | 기대 결과 |
|---|---|---|
| H3-01 | 3인: 1번 그림 → 2번 준비·열람 | 제시어 없이 직전 그림 한 장만 5초 표시 |
| H3-02 | 2번 열람 시간 만료 | 이전 실제 렌더·표면 숨김, 빈 캔버스. 시점·이동으로 몰래 볼 수 없음 |
| H3-03 | 2번이 다른 그림 작성 → 3번 답변 → 갤러리 | 답변자는 2번 그림만 보고, 갤러리는 1번·2번 원본을 나란히 표시 |
| H4-01 | 4인 정상 한 판 | 중간 두 사람은 각각 직전 한 장만 관찰, 최종 갤러리 3장·작성자 순서 일치 |
| R-01 | Drawing에서 손 하나 숨김 / 둘 다 숨김 | 하나는 해당 선만 종료, 둘 다 유효하지 않으면 차폐·일시정지. 기존 그림 유지 |
| R-02 | WordReveal·ObservePrevious·Guessing에서 손을 내림 | 손 UI만 차단되고 단계 타이머는 계속. 키보드 답변을 위해 손을 내릴 수 있음 |
| R-03 | 각 상태에서 앱 포커스 상실·복귀 | `Setup`은 자동 pause·차폐 없음. 그 밖의 상태는 실제 비공개 표시 숨김·차폐·타이머 정지, 새 손 `계속` 전에는 자동 복귀 없음. 수신 중이 아닌 준비 상태이면 focus 복귀 후 카메라 패널만 복구용 mouse 허용 |
| R-04 | Python 중단·재시작, seq 재시작 | stale UI 클릭 0회, Drawing pause, 복구 시 새 open·핀치 필요 |
| R-05 | 그림 완료와 timeout 동시 / 오래된 버튼 release | record 추가와 player 증가 각각 한 번. 새 화면 동작으로 바뀌지 않음 |
| R-06 | 캔버스 겹침 선·브러시 반투명·다른 크기 재생 | 저장 전후 스타일·겹침 순서·폭 비율 일치 |
| R-07 | Windows 빌드에서 한글 조합·삭제·caret·재포커스·손 제출 | 글자 손실·중복 제출 없음. Editor만 통과하면 빌드 통과로 기록하지 않음 |
| R-08 | 사용자에게 기존 Netplay3D 회귀 확인 요청 | 기존 Legacy 이동·우클릭·팔레트·네트워크 게이트 동작 유지. 실제 연결 검증 불가 시 그 범위를 명시 |

관찰·제시어 시간은 `Setup`을 제외한 상태에서 앱 포커스 상실 때 멈춘다. 손 추적만으로 중단하면 화면을 읽거나 답을 입력하기 어려우므로 Drawing 외에는 자동 pause하지 않는다. `Setup`에는 아직 secret·timer가 없으므로 focus loss나 손 부재로 멈출 대상이 없다.

## 11. Historical Steam online RelayQuiz 2인 실기 검증

이 절차는 이전 2p 승인 범위의 historical 기록이다. 현재 4p online 검증은 §11-5를 따른다. synthetic packet이나 synthetic webcam은 real-device equivalence로 기록하지 않는다.

### 11-1. 실행 전제와 build evidence

서로 다른 Steam account 두 개, 서로 다른 device 두 대, matching online build, compatible game/version을 준비한다. 두 build가 이미 실행된 상태에서 host가 invite하고 guest가 accept한 뒤 두 player가 모두 ready인지 기록한다. app cold-start, deployment, store distribution은 이 절차의 보장 범위가 아니다.

Windows 예정 output은 `C:/git/Camera_co-op/Builds/RelayQuizOnline/CameraCoopRelayOnline.exe` (`StandaloneWindows64`, PE x64)이고, Intel Mac 예정 output은 `Builds/RelayQuizOnlineMac/CameraCoopRelayOnline.app` (`StandaloneOSX`, Intel x64 Mach-O)이다. build report, 실제 architecture, payload를 각 device에서 증거로 남긴다. 현재 Windows Unity에는 `windowsstandalonesupport`만 있으며 Mac Build Support 설치 승인 전이므로 Mac 항목을 통과로 처리하지 않는다.

### 11-2. 실기 checklist

| ID | 확인 동작 | 기대 결과·기록 |
|---|---|---|
| ON-01 | 각 device에서 scene 진입 후 manual `CameraToggle` 관찰 | 신규 scene은 camera auto-start를 하지 않는다. mouse로 `CameraToggle`을 눌러 시작하고 missing `.venv`, dependency 오류, OS camera permission, occupied camera를 표시하며 stderr/exit 원인을 기록한다. |
| ON-02 | camera 실패 뒤 retry, permission 허용 뒤 재시도 | continued recovery가 가능하고, 새 시도 뒤 fresh hand 수신을 기록한다. local `RelayQuiz`의 manual start와 혼동하지 않는다. |
| ON-03 | host invite, guest accept, 두 player ready | 두 peer만 연결되고 ready 이후 round가 시작된다. 세 번째 peer·stale session은 허용하지 않는다. |
| ON-04 | WordReveal/Drawing에서 host와 guest의 화면 확인 | 제시어·완료 그림이 recipient별로 비공개다. camera raw video와 hand landmarks가 network payload에 없다. 각 recipient screenshot을 남긴다. |
| ON-05 | 수동 `drawing complete`와 drawing timeout을 각각 실행 | active stroke가 종료되고 drawing이 정확히 한 번 archive된다. 상대 timer는 최종 snapshot 전 시작하지 않는다. 두 결과를 별도 round로 기록한다. |
| ON-06 | Guessing에서 keyboard answer와 hand `제출` | keyboard/IME answer가 동작하고, hand game buttons/drawing 규칙이 유지된다. mouse는 connection/invite, camera recovery, pause `계속` 예외만 확인한다. |
| ON-07 | 다음 game 진행 | drawing/guessing role이 교대되고 두 player가 다시 ready해야 한다. |
| ON-08 | active player와 waiting player가 각각 focus를 잃고 복귀 | active player는 pause shield 후 focus와 fresh hand가 모두 있어야 `계속`으로 resume한다. waiting player 차폐는 상대 timer를 멈추지 않는다. |
| ON-09 | 연결 중 disconnect 후 re-invite | round가 abort되고 timer/input/private render가 중단된다. 새 invite로만 다시 시작하며 reconnect restoration/host migration으로 기록하지 않는다. |
| ON-10 | Windows output 검사 | 실제 실행 파일이 PE x64인지 확인하고 build report·Player.log·payload 경로를 기록한다. |
| ON-11 | Intel Mac output 검사 | 실제 app이 Intel x64 Mach-O인지 확인한다. Mac Build Support 미설치 상태에서는 `미실행`으로 남긴다. |
| ON-12 | tracker 환경과 기존 파일 확인 | Windows와 Intel Mac dependency를 분리하고, 기존 Windows `.venv`가 설치·수정·삭제되지 않았음을 hash/목록으로 기록한다. Windows `.venv`를 Mac에 복사하지 않는다. |

각 항목에는 날짜, commit/code state, Unity·OS·build version, device/account 식별자(비밀값 제외), 실제 입력 방식, PASS/FAIL/미실행, log·screenshot 경로를 남긴다. synthetic packet/webcam 또는 Editor-only 결과는 ON-04~ON-12의 real-device PASS를 대신하지 않는다.

## 11-3. Historical RelayQuizOnline 4p verification — 2026-08-31

> 이 절의 `724/724`, `734/734` 및 cyan `CAMERA ON / OFF` 수치는 당시 camera/setup regression의 historical 결과다. 최종 label·jump 기준과 현재 수치는 §11-4의 fresh `744/744` 기록을 우선한다.

#### 11-3-1. Historical Canvas camera and transient RelaySetupRoot contract

이 historical section에 남아 있는 cyan 3D `CAMERA ON / OFF`와 `CameraStartStop` 기록은 historical regression evidence이며 현재 계약으로 사용하지 않는다. 현재 camera 시작·종료는 오른쪽 위 Canvas `CameraToggle`의 mouse press/release만 담당한다. 버튼 상태는 `캠 켜기`·`시작 중…`·`캠 끄기`이고, 나머지 13개 world action은 hand-only다. `CameraStation`의 `Refresh`·`Prev`·`Next`·`Preview`는 세부 설정용으로 유지한다.

`RelaySetupRoot`는 Scene load 시 inactive이며 안정된 lobby에서 계속 숨겨진다. join/leave와 game start 때만 notice를 2.5 unscaled seconds 동안 표시하고, 표시 중 phase overlay를 억제한 뒤 timeout 시 가장 최신으로 수신한 online view를 복원한다. setup error는 persistent 상태로 남긴다.

최종 runtime QA에서 idle hidden, join notice, game-start notice, latest-view restore, answer focus cleanup, Canvas production pointer route `Off → Starting → Receiving → Off`를 확인했다. camera route는 실제 serialized button center에 `CameraControlPanel.ProcessPointer`를 호출한 것이며 physical OS mouse click이나 hand gesture를 수행했다고 주장하지 않는다. scoped Unity errors는 0이고 tracker process도 stop 뒤 0이다. [runtime QA 기록](../.omo/evidence/relay-setup-final-runtime-qa-20260831/relay-setup-final-runtime-qa-manual-qa.md)

자동 검증은 focused `RelayQuizUITests 14/14`, `InputModeTests 49/49`, `CameraControlTests 46/46`, full EditMode **734/734 pass**이며, validator는 injected active `RelaySetupRoot`를 거부한다. [final review-fixes verification](../.omo/evidence/camera-canvas-final-review-fixes/verification.json)

Scene validator는 PASS, `dotnet build`는 warnings/errors 0, Windows build는 success다. build에는 기존 `com.unity.pipeline` `RuntimePipelineManager` warning 1건이 남아 있으며, Windows Player 10초 smoke의 error-like line은 0이다. [final build gate evidence](../.omo/evidence/relay-setup-final-build-gate-20260831/)

`Assets/_CameraCoop/Scenes/RelayQuizOnline.unity`와 `RelayQuizOnlineBuild`를 기준으로 확인했다. Unity EditMode는 **724/724 pass, fail/skip/inconclusive 0** ([latest raw XML](../.omo/evidence/camera-world-button-fix-20260831/unity-editmode-full-latest.xml))이다. XML attributes로 `result=Passed`, `total=724`, `passed=724`, `failed=0`, `skipped=0`, `inconclusive=0`, `start-time=2026-08-31 05:53:30Z`, `end-time=2026-08-31 05:53:32Z`를 직접 확인했다. PlayMode test inventory는 0이며, Editor Play에서 중앙 3D lobby, `Steam 4인 · 0/4명`, world `Host/Invite/Leave`, cyan `CAMERA ON / OFF`, `RelayCopy`/`MemoryCopy`/`CoopMural`/`Start`, 2D Ready action 부재를 확인했다. `context=Explore mode=Move canMove=True canLook=True`, `revealRichText=False`이며 scoped Play error/warning은 0이다.

이번 regression 시나리오는 기존 camera action이 scene에는 active/available로 있었지만 초기 runtime viewport `(2.284,-0.502)`로 화면 밖에 놓여 사용자가 camera 시작 버튼을 찾을 수 없었던 경우다. 이후 `RoomBounds` trigger가 nearest hit로 target을 가리는 두 번째 regression도 확인했다. `HandInputRouter` world raycast를 `QueryTriggerInteraction.Ignore`로 수정하고 RelayQuizOnline의 HandInteractable에 trigger collider가 없음을 확인했으며, solid collider occlusion은 유지했다. `CameraStartStop` action을 중앙 lobby `(4.7,1.05,-0.65)`로 이동하고 cyan `CAMERA ON / OFF` label을 부여한 뒤 validator의 RED `(2.14,0.32,2.50)` outside margin이 GREEN PASS로 바뀌었다. initial scene center probe는 `screen=(1456.0,144.3,6.6)|target=Action_CameraStartStop|uiBlocked=False|state=Off|canMouse=True`다. post-fix runtime viewport는 `(0.758,0.134,6.550)`, `active=True`, `available=True`다. production `CameraControlPanel.ProcessPointer`에 scene screen coordinate press/release를 주입하고 application focus를 복구해 `Off → Starting (running=True) → Receiving`을 확인했다. 이는 사람이 실제 mouse나 hand gesture로 누른 결과를 뜻하지 않는다. 동일 route로 `Receiving → Off/running=False` shutdown과 tracker process 없음도 확인했다. 최종 HandInputRouter 수정 이후 새로 캡처한 [fresh Off state](../.omo/evidence/camera-world-button-fix-20260831/play-final-mouse-off-fresh.png)는 2026-08-31 14:46:04 KST 기준이며, 이전 `play-final-mouse-before.png`는 freshness 근거로 사용하지 않는다. after action 확인은 [after action](../.omo/evidence/camera-world-button-fix-20260831/play-final-mouse-after.png)이다.

Scene validator는 14 unique actions, 4 slots, 3 remote avatars, 4 mural layers, read-only Gallery/private shells를 PASS했다. focused tests는 `CameraControlTests 46/46`, `PartyWorldControllerTests 18/18`, `InputModeTests 48/48`, `HandInputRouterTests 46/46`, `PhysicalPaintToolTests 9/9`다. Editor 측정값은 drawCalls 128, setPass 16, triangles 8700, CPU 7.7669ms, GPU 2.1504ms, main thread 1.7934ms다.

Windows x64 build는 `Succeeded`, errors 0, warnings 1이며 warning은 Pipeline tooling RuntimePipelineManager warning이다 ([latest build evidence](../.omo/evidence/camera-world-button-fix-20260831/windows-build.json)). 산출물은 `Builds/RelayQuizOnline/CameraCoopRelayOnline.exe`, PE AMD64 `0x8664`/PE32+ `0x020B`다. `tracker/camera_utils.py`는 source/payload SHA256가 일치하고 import 성공했다 ([payload evidence](../.omo/evidence/final-validation-20260831-security-postfix/windows-payload-import.txt)). Player는 10초 생존 후 owned PID가 종료됐고 log error-like line은 0이다 ([Player evidence](../.omo/evidence/final-validation-20260831-security-postfix/windows-player-launch.json)). camera button fix의 최신 Player 실행도 10초 생존·owned PID 종료·error-like 0으로 확인했다 ([bootstrap log](../.omo/evidence/camera-world-button-fix-20260831/windows-player-mouse-bootstrap.log), [log audit](../.omo/evidence/camera-world-button-fix-20260831/windows-player-log-audit.json)). `dotnet build`는 warnings 0/errors 0, Python unittest는 **2/2**, `py_compile`도 통과했다.

아직 실행하지 않은 범위는 실제 Steam 4 account/4 machine 연결, 실제 physical mouse/hand gesture, 실제 webcam hand tracking, phone/Camo/Continuity Camera 조합, Intel Mac build다. 따라서 자동·Editor 검증 완료를 실제 player 지원 완료로 해석하지 않는다.

## 10. 결과 기록과 최종 종료 조건

각 시험은 날짜, 코드 상태, Unity·Python 환경, Editor/빌드 구분, 시나리오 ID, 입력 방식(실손/합성), 실제 결과, PASS/FAIL/미실행, 관련 로그·스크린샷 경로를 함께 기록한다. 사용자의 응답이 없으면 사용자 검증 대기로 남기며 통과 처리하지 않는다.

## 11-4. World label readability + grounded jump final verification — 2026-08-31

최종 기준은 `RelayQuizOnline` Scene의 선택적 billboard 정책과 `PlayerController`의 grounded `Space` jump다. 전체 EditMode는 **744/744 pass**이며 focused `PlayerMoveTests 34/34`, `PartyWorldControllerTests 23/23`이다. Scene contract는 `34 TextMesh / 21 WorldLabelBillboard / 13 static / static presenter 0`이다. 21개는 즉시 조작해야 하는 control label만 player camera를 향하고, architectural/static sign은 의도한 정면에 고정한다. lobby title은 `LobbyDesk` front facade에 mount되어 있다.

| ID | 시나리오 | 결과 |
|---|---|---|
| WL-01 | 중앙·사선 시야에서 21개 control label이 camera를 향하고 readable | PASS, dot `1.0000..1.0000` |
| WL-02 | 13개 static sign이 billboard로 오염되지 않고 intended front에 mount | PASS, static presenter 0 |
| WL-03 | lobby title이 `LobbyDesk` facade에 부착되고 control과 겹치지 않음 | PASS |
| JP-01 | grounded 상태에서 rising edge `Space` 입력 | PASS, Play Input System maxY `1.454` |
| JP-02 | 착지 후 held `Space`가 재점프하지 않음 | PASS, press transition 1 / landing 1 |
| JP-03 | 공중 재점프·Blocked·typing 입력 거부 | PASS |
| JP-04 | 바닥 button/structure collider를 jump로 통과하고 착지 | PASS |
| QA-01 | Play scoped console error, build, smoke | PASS, scoped errors 0 |

Play 측정값은 draw calls `129`, setPass `14`, triangles `8654`다. GC/CPU/GPU per-frame 측정은 이 QA surface에서 제공되지 않아 미실행으로 남긴다. `dotnet build Camera_co-op.slnx --no-restore`는 warnings/errors 0이며 Windows x64 build와 smoke가 성공했다. 기존 Pipeline tooling warning은 알려진 잔여 warning으로 기록한다. code review와 visual review는 모두 **APPROVE**다. 실제 Steam 4 account, webcam/phone camera, physical hand gesture, Intel Mac은 미검증이며 authored mural의 rear backface는 반대편에서 mirror처럼 보일 수 있다. 상세 guide는 [플레이어 게임 방법](17_player_game_guide.md)을 따른다.

## 11-5. Task 14 four-Scene transition verification — 2026-09-02

현재 기준의 사용자 순서는 `CameraToggle` mouse press/release → 손으로 `Host` 또는 `Invite` → lobby 자유 연습(brush pickup, paint·width·eraser, fist draw, jump) → 네 `ReadyPad` dwell → Host `START` → `ModeSelectorRoot` 표시 → Host mode 선택(`SelectModeAndBeginLoad`, `startSignal` 증가) → 선택한 `RelayCopy`/`MemoryCopy`/`CoopMural` additive Scene → Host `RETURN TO LOBBY`이다. mode Scene은 persistent camera·input·network owner를 중복 생성하지 않는다.

| 확인 항목 | 결과·증거 | 범위 한계 |
|---|---|---|
| 네 Scene path/order 및 build catalog | PASS, Task 19 Unity full EditMode `868/868`, empty-startup `PartySceneValidator PASS`, Build Settings exact four ([receipt](../.omo/evidence/party-scene-split/task-19-final-gate/receipt.md)) | 실제 4 peer 전환은 미실행 |
| private paper shell·CoopMural 4 layer·adapter ownership | PASS, Task 19 full EditMode `868/868`, failed/skipped/inconclusive `0` ([receipt](../.omo/evidence/party-scene-split/task-19-final-gate/receipt.md)) | 실제 privacy 관찰은 미실행 |
| four-scene Editor determinism | PASS, Task 19 empty-startup validator exit `0`과 exact four Build Settings order ([validator summary](../.omo/evidence/party-scene-split/task-19-final-gate/party-scenes-validator-summary.txt), [order](../.omo/evidence/party-scene-split/task-19-final-gate/build-settings-order.txt)) | long profile 미실행 |
| protocol v4 transition fields | source contract 확인: `startSignal`, `transitionGeneration`, `transitionPhase`, `sceneReadyMask`; `CoopMural`은 `startSignal`을 mural epoch로 사용하고 layer `revision`으로 중복을 막음 | Steam transport 실기 미실행 |

Scene load failure와 timeout은 round를 정상 완료로 처리하지 않고 private render/input을 정리한 뒤 lobby 복귀 안내를 표시해야 한다. disconnect는 정상 lobby return이 아니라 `Abort`이며 새 invite가 필요하다. 실제 Steam 4 account/4 machine, webcam/phone camera, physical gesture, long profile, Intel Mac은 미검증이며 이 표의 PASS를 대체하지 않는다.

현재 runtime 보완 계약: `CameraControlPanel.autoStartCamera=false`이며 camera는 오른쪽 위 `CameraToggle`의 manual mouse 입력으로만 시작한다. lobby는 `Explore/Move`에서 자유 이동하고, `Explore/Interact`에서는 active registered lobby paper에 fist drawing을 할 수 있다. camera 연결과 fresh hand 수신 후에는 owner handover가 자동으로 진행된다. lobby gallery는 결과를 즉시 렌더링하지 않고 deferred 상태를 허용한다. game Scene 결과는 3개 read-only gallery slot이며, `RETURN TO LOBBY` world action은 Host만 수행한다. `CoopMural`은 final root와 Host-only Return action을 갖는다. Task 19 fresh Windows Player smoke는 exact PID가 18초 생존했고 this-run Player error-like count가 0이었으며, 종료 후 Unity/Player/tracker process가 0이었다. 세 game Scene은 별도 additive asset이다. 최신 full 결과는 Unity EditMode `868/868`, `dotnet build` `0 warnings/0 errors`, Python `2/2`이다. 실제 Steam 4 account, webcam/phone, physical gesture, long profile, Intel Mac은 미검증이다.

Task 17의 이전 validator 실패 기록은 Task 19의 empty-startup validator exit `0`/PASS와 full EditMode `868/868` 재실행으로 대체되었으며, 최신 판정에는 사용하지 않는다. 전체 gate 상세와 개별 로그는 [Task 19 final gate receipt](../.omo/evidence/party-scene-split/task-19-final-gate/receipt.md)를 기준으로 한다.

실패 수정이 설계 변경을 요구하면 **문서 수정 → 사용자 승인 → 코드 반영** 순서를 따른다. 다음 Step 승인은 이전 Step의 실행 결과 보고와 별개로 명시적으로 받는다.

최종 종료 조건은 신규 컴파일 오류 없음, 관련 자동 테스트 결과 확인, 2인·3인·4인과 차폐·IME 체크리스트의 사용자 결과 반영, 문서와 구현 일치다. 검증 불가 범위는 원인과 함께 보고하고 완료로 숨기지 않는다. 문제가 없을 때만 사용자 요청의 최종 커밋 `feat: Phase 2 player control + hand UI + relay quiz mode`를 만든다. 기존 사용자 변경은 그 커밋에 임의로 포함하지 않는다.
