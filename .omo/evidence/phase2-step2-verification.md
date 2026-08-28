# Phase 2 Step 2 검증 기록

2026-08-28 · C:/git/Camera_co-op · Unity 6000.3.15f1 · 판정: **검증 미완료, 사용자 Play 관측 대기**

## 승인·범위

사용자 “Play 확인했어. 다 괜찮아 다음 단계 진행해”를 Step 1 Play 통과 보고와 Step 2 승인으로 기록했다. Step 3~5, 온라인 경로, 패키지 추가, 커밋은 수행하지 않았다. 씬은 전용 Unity MCP로만 조작했다. execute_code와 자동 Play는 사용하지 않았다.

## 구현

- HandInputSample·HandInputState·HandClickContext·취소 이유, 검증된 샘플 발행.
- 손별 신선도·재무장·캡처·취소·화면 세대·대상 소유권 라우팅.
- uGUI Button/InputField 어댑터, 정상 해제만 OnHandClick 확정, native onClick 미사용.
- A/B/C 버튼, L/R 커서, 상태 문구, 자체 생성 hover/click 음원과 Inspector 배선.
- 선택 콜백 중 비활성화·재활성화·파괴를 취소하는 lifecycle 회귀 수정.

## 실제 실행 증거

| 실행 | 결과 |
|---|---|
| 초기 RED | 80/80 실패. 미구현 샘플과 Editor-only probe·EventSystem 등록 fixture 문제를 분리 |
| fixture 수정 후 router/adapter RED | 51건 중 6통과·45실패. 기존 fixture 오류 제거 |
| 첫 전체 | 368건 중 363통과·5실패. 색상 복구 1건과 native raycast 4건 |
| lifecycle RED | 377건 중 368통과·9실패. 콜백·수명 5건, native raycast 4건 |
| lifecycle 수정·렌더 관측 | 92건 중 86통과·6실패. lifecycle 5건 GREEN, 새 label guard 2건 RED, raycast 4건 |
| 최종 전체 | **379건 중 375통과·4실패·skip 0**, 2026-08-28 05:27:11~16 UTC |

최종 job: `52cbee4a1fb84f158f22cc30ffa56fa5`. [원본 내용의 XML](phase2-step2-editmode.xml). 기존 287건은 모두 통과했다. 신규 HandSample 30/30, HandButton 16/16, HandInputRouter 42/46이다. 실패 테스트나 assertion을 삭제·skip·완화하지 않았다.

최신 코드의 MCP refresh_unity → read_console 뒤 editor/state는 idle, is_compiling=false, is_playing=false였다. C# 컴파일 오류는 없었다. 기존 WebSocket 재연결 경고와 pipeline의 비자동 실행 경고는 남았다. 씬 validate는 missingScripts=0, brokenPrefabs=0, repaired=0이었다.

## 미해결 렌더 경계

다음 네 실제 GraphicRaycaster 테스트가 `Graphic.depth=-1`로 실패한다.

- HigherOverlaySortOrderWinsRegardlessOfArrayOrder
- TopNonTargetGraphicBlocksUnderlyingTarget
- DisabledAdapterStillBlocks
- NonInteractableButtonStillBlocks

Canvas force update와 30 Editor 프레임 대기, Game 메뉴 활성화, 즉시 native repaint를 순서대로 관측했다. 마지막 probe는 Game 카메라 31회·Scene 카메라 30회, Main Camera 활성·Game·targetTexture=null·display 0을 기록했다. 대상 Graphic은 정점 4개·material 1개·cull=false인데 depth가 계속 -1이었다. 따라서 단순 대기 부족·mesh 미생성으로 원인을 확정하지 않는다. 임시 렌더 계측은 제거했고 세 번째 접근 뒤 추가 추측 수정을 멈췄다.

마지막 전체 테스트 후 실제 RelayQuiz Overlay도 Game 뷰에서 사라졌다. Scene isDirty=false, 루트 6개와 참조는 유지됐다. 저장 씬 재열기로도 표시가 복구되지 않았다. 앞선 에디터 재시작 직후에는 UI가 표시됐다. 이 차이를 구분하기 위해 사용자에게 Unity 재시작 후 직접 Play에서 A/B/C 표시·핀치 클릭 확인을 요청한다. 사용자 관측은 자동 테스트 실패를 면제하지 않는다.

## 화면 검토 범위

- 초기 `static-ui-after-restart.png`: 기본 FullHD 화면, 한글·배치·실제 uGUI 계층 두 독립 검토 통과.
- 최신 `static-ui-final.png`, `static-ui-after-reload.png`: Overlay 미표시. **최종 UI 게이트 미통과**. 초기 PASS를 최신 화면 승인으로 재사용하지 않는다.
- 손 추적 하드웨어·소리·동적 hover/press/click·다른 해상도·InputField IME는 사용자 Play 전까지 미확인이다.

위 PNG와 RED/probe XML, 각 MCP 전체 응답은 `C:/Users/yunji/AppData/Local/Temp/CameraCoop-Step2-20260828/`에 보관했다. 코드·명세 검토 보고서는 같은 evidence 폴더의 `phase2-step2-code-review.md`, `phase2-step2-spec-review.md`다. 정적 코드 승인과 전체 실행 승인 여부를 구분한다.

## 다음 확인

Unity 재시작 → RelayQuiz 열기 → 사용자가 Play → Tab → A/B/C 표시와 새 핀치 클릭 확인. 표시되지 않으면 Game 뷰와 Console을 기록한다. 이후 검증 문제를 해결하고 Step 2 결과를 확정하기 전에는 Step 3를 시작하지 않는다.
