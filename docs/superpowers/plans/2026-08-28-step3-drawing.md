# Step 3: 손 캔버스 드로잉

2026-08-28 사용자가 카메라와 현재 화면 테스트를 마쳤다고 보고하고 다음 단계 구현을 요청했다. 이어 커밋도 요청했다. 이전 Step 5 전 커밋 대기 방침은 이번 요청으로 변경한다. Step 4는 구현하지 않는다.

## 범위와 제약

- 승인된 docs/07·08의 캔버스, 팔레트, 메모리 저장·복원, 읽기 전용 프리뷰를 구현한다.
- 현재 RelayQuiz 체크아웃을 사용한다. Unity Play와 실제 카메라 검증은 사용자만 수행한다.
- 씬은 Unity MCP 전용 도구로만 수정한다. execute_code와 .unity 텍스트 편집은 사용하지 않는다.
- 기존 LegacyCursorEvents, 온라인 씬, NetSession·GameSession·Python·패키지는 보존한다.
- 카메라 컨트롤만 마우스를 허용한다. 팔레트·Undo·Clear·보관·복원은 손 전용이고 clearKey는 None이다.
- 기존 GraphicRaycaster 네 실패는 별도로 기록한다. 테스트를 삭제·완화하지 않는다.
- 사용자 문서 docs/12_phase3b_guess_game.md와 기존 패키지 변경은 커밋에서 제외한다.

## 구현 순서

- [x] 사용자 Step 2·카메라 테스트 보고, Step 3 승인, 커밋 요청 기록
- [x] 설계·기존 코드·씬·브랜치·작업 트리 확인 및 변경 전 해시 저장
- [x] DrawingTests에 저장·복원·순서·깊은 복사 경계 추가 후 실패 확인
- [x] DrawingController의 로컬 정본과 CanvasDrawingData·CanvasDrawingPresenter 구현
- [x] HandPointer의 입력 경로 분리, HandCanvasInteractable·Router 월드 입력 연결
- [x] ToolState 선택 조회, HandSliderInteractable·HandToolPalette 구현
- [x] 손 전용 작업 명령과 보관·복원·프리뷰 시험 화면 구성
- [x] RelayQuiz Inspector 배선·저장·정적 검사
- [x] refresh_unity → read_console, 관련·전체 EditMode 검증
- [x] 최신 화면 캡처와 독립 UI 검토, 구현·검증 기록 갱신 (표시 안정성은 사용자 확인 대기)
- [x] 승인된 선행 Phase 2와 Step 3의 커밋 범위 분리·사용자 Play 절차 작성

## 경계 계약

- HandPointerInputSource: LegacyCursorEvents=0, HandRouter=1. 새 로컬 경로만 InputModeManager.CanDraw와 현재 작업 surface를 검사한다.
- CanvasDrawingData.TryCopy(source, brushCount, out copy, out error)는 전체 검증 후 order 정렬과 깊은 복사를 수행한다.
- ToolState의 BrushCount와 GetBrushMaterial(index)는 로컬 복원의 브러시 검증·표시에 사용한다.
- 모든 작업 명령은 canvas capture 취소 → 활성 선 종료 → Undo/Clear/Export/Load 순서다.
- 보관 프리뷰는 별도 surface와 presenter를 사용한다. collider·HandCanvasInteractable은 붙이지 않는다.
- Step 3 화면에서는 Drawing context를 사용한다. 실제 릴레이의 context 전환과 N-1 갤러리 배치는 Step 4 범위다.

## 검증 기록

변경 전 파일 해시는 임시 `CameraCoop-Step3-20260828/baseline.json`에 저장했다. 기존 전체 EditMode 기준은 436건 중 432통과·4실패다. 사용자의 이번 보고는 카메라·현재 화면 수동 확인이며 개별 시나리오 횟수나 자동 실패 해결을 뜻하지 않는다.

최종 전체 EditMode job `5c3f25dc6ea8400f92fddb8baacd4c59`: 484건 중 480통과·기존 4실패·skip 0, 새 48건 모두 통과. Drawing 51/51·HandCanvasRouting 10/10·ToolState 13/13. `.omo/evidence/phase2-step3/editmode.xml`과 소스 해시 요약을 보관했다. 코드 검토 승인, 씬 정적 구조 승인. CJK 검토는 정상 프레임 배치만 승인하고 후속 캡처의 글자 누락·깨짐 때문에 무조건적인 화면 승인을 거부했다. Play는 사용자만 수행하며 Step 4 승인을 기다린다.

커밋은 플레이어·입력 모드 API/테스트와 손 UI·드로잉·씬 통합의 두 그룹으로 나눈다. 패키지 2개·docs/12의 기존 변경과 `.omo` 검증 자료는 제외한다. 실제 커밋 식별자는 git log와 최종 보고에서 확인한다.
