# 야외 공용 로비와 내부 relay 공간

## 승인된 요구

- 야외 `RelayQuizOnline`의 외계 행성 분위기를 유지한다.
- 로비에는 4인 개인 그림 구역 대신 함께 그리는 큰 공용 캔버스 하나를 둔다.
- 초대 후 로비에서 모여 연습하고 START로 내부 게임 공간에 들어간다.
- 내부에서는 4명에게 개인 구역을 배정한다.
- 첫 주자는 후속 그림을 관전한다. 현재 주자와 직전 주자는 서로 그림을 볼 수 있다.
- 미래 주자는 자기 차례 전에 다른 사람의 그림을 보지 못한다. 예외로 사용자가 지정한 A 차례에는 B도 A의 그림을 본다.
- C 차례 B가 C의 그림을 볼 수 있다는 점은 사용자에게 확인되었다.

## 현재 게임과의 연결

기존 4인 relay는 A/B/C가 그림을 그리고 D가 정답을 입력한다. 이번 작업은 이 순서와 게임 모드를 유지한다.
각 라운드의 순서로 공개 범위를 계산하며 고정 slot 번호를 A로 간주하지 않는다.
자신의 그림은 아래 관전 표와 별도로 기존 입력 권한을 따른다.

| 단계 | 다른 사람의 그림 공개 |
|---|---|
| A 그림 | B에게 A 공개. C/D 비공개 |
| B 그림 | A/B 상호 공개. C/D 비공개 |
| C 그림 | A 관전. B/C 상호 공개. C에게 A 비공개. D 비공개 |
| D 정답 | A 관전. D에게 C 공개. D에게 A/B 비공개 |
| 결과 | 기존 결과 공개 규칙 유지 |

칸막이만으로 비대칭 공개를 구현할 수 없으므로 사용자별 그림 전달과 표시를 함께 제한한다. 내부 구조는 개방형 구획도 허용한 사용자 지시에 따라 선택한다.

## 작업 계획

1. [완료] runtime 공개 권한과 그림 전달 경로 구현 및 관련 tests.
2. [완료] 로비 공용 캔버스와 내부 4인 구역 생성, 기존 Scene binding 연결.
3. [완료] Unity compile, 당시 전체 EditMode 942/942. Play에서 additive 내부 로드와 C 차례 A/B별 실제 presenter 화면 확인. 당시 Scene validator 통과 보고는 잘못됐으며 후속 작업에서 수정·재검증했다(아래 정정 참조).
4. [완료] QUALITY_CHECKLIST 전 항목 평가: 8.65 → 9.15/10. 실기 검증 한계 기록.

## 검증 범위와 결과

정정: 당시 Console에 `RelayCopy requires three remote blank paper shells` 오류가 남아 있었다. 후속 두 게임 통합에서 validator 계약을 수정했으며, 최종 975/975 tests와 6개 Scene validator PASS 근거는 `docs/19_drawing_party_modes.md`에 기록했다. 아래 942개 결과는 당시 기록이다.

- 야외 공용판 16×4 하나와 내부 4인 구획을 생성하고 저장했다.
- 내부는 낮은 고정 칸막이와 사용자별 그림 표시를 사용한다. 칸막이 개폐 animation은 없다.
- `Temp/relay-interior-host-c.png`: C 차례 A 관전 화면, A/B/C 그림 표시.
- `Temp/relay-interior-b-c.png`: 같은 차례 B 화면, B/C만 표시.
- `Temp/relay-lobby-play-final.png`: 최종 야외 로비.
- 최종 EditMode 942/942, 실패 및 skip 0. `Temp/pipeline_test_status.json`.
- Play에서는 loopback 4 session 결과를 실제 Scene에 연결했다. 실제 Steam 4대와 START 버튼을 통한 왕복 조작은 미검증이다.
- game mode와 protocol version은 유지했다. 서로 다른 build의 호환성은 검증하지 않았다.
- 현재 Editor는 `RelayQuizOnline` EditMode, 저장 완료 상태다.

## 소유권

- runtime worker: 공개 규칙, session/controller, 관련 tests.
- scene worker: Editor builder, 생성 Scene, 배치 tests와 Unity Editor 실행.
- main: 계획, 통합 확인과 품질 평가.

`Assets/BrokenVector`와 `.codex`의 기존 untracked 파일은 작업 대상이 아니다.
