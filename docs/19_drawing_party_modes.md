# 로비에서 선택하는 그림 게임

## 사용자 확정 규칙

### 그림·글 릴레이 — PictureTelephone

제시어를 받은 A가 그린다. B는 A의 그림을 보고 글로 해석한다. C는 B가 쓴 글을 받아 그림을 그린다. D는 C의 그림을 보고 최종 답을 입력한다. 기존 4인 순차 진행과 최종 정답 판정 경로를 확장한다. Gartic Phone 전체의 동시 다중 이야기 순환을 복제하는 범위는 아니다.

C에게는 B의 글만 제공하고 최초 제시어나 A의 그림을 제공하지 않는다. 이 모드에는 첫 주자 관전 특례를 적용하지 않는다.

### 그림 끝말잇기 — DrawingWordChain

1. A가 제시어를 그림으로 표현한다.
2. B/C/D는 직전 그림의 단어를 추측하고 그 끝 글자로 시작하는 대상을 그린다.
3. 그리는 동안 단어 입력이나 단어 확인 기능은 없다.
4. 네 사람이 모두 그린 뒤 A/B/C/D가 차례대로 **자신이 그린 그림**의 단어를 입력한다.
5. 입력한 단어는 다른 사람에게 전달하거나 표시하지 않는다.
6. 모두 입력한 다음 인접 단어의 마지막 글자와 첫 글자가 모두 일치하면 성공이다.

예: 고등어 → 어항 → 항아리 → 리본. 의미 인식, 사전 조회, 두음법칙 예외는 이번 범위에 포함하지 않는다. 그림이 실제 그 단어를 나타내는지는 자동 판정하지 않는다.

그림 진행 중에는 앞서 확정한 A 관전·현재/직전 주자 상호 공개·미래 주자 비공개 규칙을 적용한다. 모든 그림 완료 후 자기 그림 이름을 적는 단계에서는 자기 그림을 참조하고 타인의 입력 단어는 계속 숨긴다.

이전의 '그리는 사람이 단어를 미리 비공개 입력' 선택은 사용자가 취소했다. 그 방식은 구현하지 않는다.

## 공간과 선택

- `RelayQuizOnline`: 기존 야외 모임·공용 연습 캔버스 유지.
- 기존 RelayCopy/MemoryCopy/CoopMural 유지.
- PictureTelephone=3, DrawingWordChain=4를 추가한다. 기존 serialized enum ID는 바꾸지 않는다.
- 전원 ReadyPad 준비 후 host가 START를 누르면 선택 버튼을 연다. host가 mode를 선택하면 해당 내부 공간을 불러온다. 준비 전에는 선택 버튼을 숨긴다.
- 공통 내부 배치와 기존 Scene adapter/host authority/전환 barrier를 재사용한다.

## 기준선

- 직전 전체 EditMode 942/942 통과 기록이 있다.
- 그러나 `PartyGameSceneValidator`의 'RelayCopy requires three remote blank paper shells' 오류가 남아 있다. 직전 validator 통과 보고는 잘못됐으며, 이번에 실제 예외와 Console까지 검증한다.
- 기존 dirty files는 직전 승인 작업이며 유지한다. `.codex`와 `Assets/BrokenVector`의 untracked 파일은 수정하지 않는다.

## 실행 계획

1. [완료] 두 모드의 순수 규칙과 tests, mode별 비공개 network 전달 구현.
2. [완료] 로비 선택·문구·입력·내부 Scene 연결 코드, validator 계약 수정.
3. [완료] compile → 관련 tests → 전체 tests → Scene validator → Play 상태별 화면 확인. 실제 Steam 4대 시험은 별도 미검증으로 남긴다.
4. [완료] 실행 근거와 QUALITY_CHECKLIST 평가(9.2/10), 실기 검증 한계 기록.

## 담당

- party_mode_rules: RelayQuizLogic와 순수 규칙 tests.
- party_mode_network: Session/View/Protocol과 다중 session 전달 tests.
- party_mode_ui_scene: catalogs/선택/입력/UI/Scene builders·validators, Unity Editor 실행 전담.
- main: 요구 정리, 변경 통합 검토, 품질 평가.

## 완료 확인 기준

| 기준 | 필요 근거 |
|---|---|
| 로비에서 두 신규 모드 선택·시작 | 실제 Scene 연결 및 Play 확인 |
| 그림·글 교대 | A 그림/B 글/C 그림/D 글의 session 실행 |
| 전화식 relay의 이전 정보 차단 | recipient별 view/payload tests |
| 끝말잇기 그리는 동안 글 입력 없음 | phase별 입력 권한 tests 및 UI 확인 |
| 마지막 자기 그림의 비공개 단어 입력 | 순차 입력과 타인 정보 미노출 tests |
| 전원 제출 후 연결 성공/실패 | 올바른 연결·끊긴 연결·빈 입력 tests |
| 기존 게임 유지 | 기존 tests, 실제 validator |
| 실기 범위 정직한 보고 | loopback와 Steam 실기 구분 |

## 통합 검사 중 발견·수정

- 첫 전체 EditMode 실행: 975개 중 972개 통과, 3개 실패. 아래는 당시 결과에서 직접 확인한 실패와 수정 사항이다. 결과 파일은 이후 최종 통과 결과로 갱신됐다.
- 새 선택 항목 두 개로 world label 수가 늘어 검사 기대값을 갱신했다.
- START가 spawn의 2 m 안전 반경을 침범해 버튼을 기존 안전 위치로 복구했다. 공간 검사의 기준은 완화하지 않았다.
- 기존 RelayCopy/MemoryCopy 결과에서 client가 최초 제시어를 지우던 수신 경로를 수정했다. 새 게임의 진행 중 정보 차단은 유지한다.
- 기존 RelayCopy의 B/C Drawing 단계에서 참고 그림이 사라질 수 있던 분기를 복구했다. MemoryCopy는 그리는 동안 참고 그림을 숨기는 규칙을 유지한다.
- Play 화면에서 Telephone C의 글 prompt가 중앙 WordReveal panel에 계속 표시돼 그림판을 가리는 문제를 발견했다. Drawing 중에는 작은 안내로 제공하도록 수정했다. 전달 글과 prompt label은 rich text 해석을 끄고 UI 회귀 검사를 통과했다.

## 실행 검증

- 첫 통합 수정 후 Unity 전체 EditMode: **975/975 통과**, 실패·skip·inconclusive 0, 26.54초. `Temp/new-modes-editmode-status.json`의 중첩 결과와 `Temp/pipeline_test_status.json`에서 확인했다. 이후 Play에서 발견한 prompt 가림 수정은 최종 실행 결과를 별도로 확인한다.
- 별도 Scene 관련 검사 25/25, 앞서 실패한 세 경로 관련 검사 7/7 통과 보고를 받았다. 전체 실행에도 해당 경로가 포함된다.
- `PictureTelephoneFourPlayersCompletesWithoutLeakingIntermediateHistory`: 4 session의 그림/글 교대와 recipient별 중간 정보 차단.
- `DrawingWordChainFourPlayersCompletesAndKeepsEveryLabelPrivate`: 전체 그림 후 자기 그림 이름 입력, 전원 제출 후 성공 및 단어 비공개.
- `DrawingWordChainBrokenLinkFailsWithoutPublishingLabels`, `WordChainRejectsWrongOwnerStaleDrawingPhaseAndDuplicateTextSubmissions`: 끊긴 연결 실패, 잘못된 주자·지난 Drawing 단계·중복 제출 차단.
- 검사 범위는 deterministic loopback이다. 실제 Steam 4대의 latency, disconnect, 창 전환, 장시간 입력은 이 결과로 검증됐다고 보지 않는다.
- Scene별 실제 validator: `RelayQuizOnline`, `RelayCopy`, `MemoryCopy`, `CoopMural`, `PictureTelephone`, `DrawingWordChain` 모두 `True`, error 빈 문자열. Console seq 4195의 `PartyGameSceneValidator PASS`와 일치한다(`Temp/new-modes-validator.txt`). 최초 일괄 호출은 5초 제한으로 실패했으며, 그 실패 응답을 PASS 근거로 쓰지 않았다.
- **최종 source 기준**: Unity recompile errors 0, UI focused 1/1, 전체 **975/975 통과**(NUnit 27.737초, 2026-09-09 02:27:16Z~02:27:43Z), 재생성 Scene validator PASS(seq 5056). `Temp/new-modes-editmode-full-final.xml`과 `Temp/new-modes-validation-final.txt`를 직접 읽어 확인했다. 이 결과가 prompt 수정 전 실행을 대체한다.

## 최종 Play 화면 확인

1280×720에서 다음 8개 화면을 main과 별도 read-only 검토자가 직접 열어 확인했다.

| 화면 | 최종 capture | 확인 |
|---|---|---|
| 로비 | `Temp/new-modes-lobby-five-modes-final.png` | 기존 3개와 신규 2개 선택, 연결 대기 안내 |
| Telephone B | `Temp/new-modes-telephone-b-input-final.png` | 이전 그림의 획, 비공개 그림 설명 입력 |
| Telephone C | `Temp/new-modes-telephone-c-prompt-final.png` | 큰 중앙 panel 없음, 작은 전달 글과 그릴 공간 |
| Telephone D | `Temp/new-modes-telephone-d-answer-final.png` | 다른 참고 획, 최종 답 입력 |
| Telephone 결과 | `Temp/new-modes-telephone-result-final.png` | 최초 제시어·그림 설명·최종 답과 판정 |
| Chain Drawing | `Temp/new-modes-chain-drawing-final.png` | 허용된 그림 pair, 단어 입력창 없음 |
| Chain 자기 그림 입력 | `Temp/new-modes-chain-own-picture-input-final.png` | 자기 그림 참조와 비공개 입력 안내 |
| Chain 결과 | `Temp/new-modes-chain-result-final.png` | 입력 단어 없이 성공 여부만 표시 |

화면에는 synthetic authorized View와 검증용 획을 실제 Scene presenter에 적용했다. 실제 Steam session의 연속 입력 재생은 아니다. 초기 촬영이 ScreenSpaceOverlay를 누락해 임시 Canvas 전환을 시도했으나 해당 Play를 종료해 복구했다. **최종 8장은 새 Play에서 원본 ScreenSpaceOverlay를 유지하고 `capture_game_view source=screen`으로 촬영**했다. 임시 변경은 제품 Scene에 저장하지 않았다. 장비 카메라가 꺼져 있어 손 입력/이동 잠금 안내가 표시된다.

독립 검토 결과: 기능성 한국어 안내의 clipping·주요 가림 없음, text 역할과 privacy 요구 일치. 작은 배경 English 표지 일부는 겹침·낮은 대비가 남아 cosmetic 감점으로 기록한다. 단일 해상도 확인이며 다른 화면 비율, 실제 손 추적, HOST/INVITE/START 왕복, 4대 Steam 접속과 장시간 성능은 미검증이다.

성능 참고: DrawingWordChain **결과 화면**의 단일 Editor sample은 drawCalls 23, SetPass 10, CPU 7.6918 ms, main thread 1.8381 ms, GPU 1.97632 ms였다(`Temp/new-modes-play-evidence.txt`). 획이 늘어나는 실제 4인 Drawing 부하를 대표하지 않는다.

마무리: Play 종료 후 `RelayQuizOnline` 단일 Scene, dirty=false로 복원했다. 검증용 임시 asset/meta는 제거했으며 제품 Scene에는 저장하지 않았다. C#/Markdown diff 공백 검사 통과. 기존 `.codex`와 `Assets/BrokenVector`는 보존했다. commit/publish 및 Player 배포 build는 수행하지 않았다.
