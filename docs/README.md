# Camera Co-op 문서 지도

> 갱신: 2026-08-31 · 이 문서는 `docs/`의 진입점이다.

## 먼저 읽을 문서

| 목적 | 문서 | 상태 |
|---|---|---|
| 제품 방향과 4인 gameplay | [15_3d_party_game_design.md](15_3d_party_game_design.md) | 구현 계약과 현재 범위. 실제 4계정·기기 QA 대기 |
| 구현 순서와 4p 완료 기준 | [16_implementation_roadmap.md](16_implementation_roadmap.md) | M0~M5 구현 완료, M6 외부 QA 대기 |
| phone을 PC camera로 사용 | [13_phone_camera_input.md](13_phone_camera_input.md) | camera 선택 경로 구현. 실제 phone/Camo 기기 검증 대기 |
| Python→Unity 입력 구조 | [01_architecture.md](01_architecture.md)~[05_test_plan.md](05_test_plan.md) | 현재 구현 reference와 검증 기록 |
| local player·손·drawing | [06_player_controller.md](06_player_controller.md)~[09_relay_quiz_mode.md](09_relay_quiz_mode.md) | 현재 local 구현 reference |
| Steam·3D·게임 framework | [08_netplay.md](08_netplay.md), [10_phase3d_world_canvas.md](10_phase3d_world_canvas.md), [11_phase3e_paint_tools.md](11_phase3e_paint_tools.md), [12_phase3b_guess_game.md](12_phase3b_guess_game.md) | 기존 공유 online 게임 reference |
| 실제 플레이 순서 | [17_player_game_guide.md](17_player_game_guide.md) | 현재 `RelayQuizOnline` build의 camera·손·Steam·Relay Copy 순서 |
| build | [10_build.md](10_build.md) | build 조건 reference. 동시 작업 변경 보존 |

## 문서 우선순위

서로 다른 게임 경로를 섞지 않는다.

1. 새 4인 3D 제품 목표는 [15](15_3d_party_game_design.md)와 [16](16_implementation_roadmap.md)을 따른다.
2. local 한 화면 RelayQuiz는 [09_relay_quiz_mode.md](09_relay_quiz_mode.md)를 따른다.
3. 기존 2p RelayQuiz 기록은 현재 4p 구현의 역사적 근거로만 취급한다. 현재 online entry는 `RelayQuizOnline` 4p 계약을 따른다.
4. 기존 `Netplay3D`와 `GameMode.Relay`는 새 `RelayCopy`와 규칙이 다르다. 같은 이름 때문에 완료된 기능으로 간주하지 않는다.

## 유지한 역사·검증 문서

- [06_handoff_macos.md](06_handoff_macos.md): `PythonTracker/README.md`와 Intel Mac requirements가 직접 참조한다. camera·권한·방화벽·실제 손 검증의 주요 기존 기록이므로 유지한다.
- [09_handoff_windows.md](09_handoff_windows.md): Steam 2인 실기, build Scene, shared network의 잔여 위험과 Unity 검증 함정을 source/tests가 참조하므로 유지한다.
- [14_handoff_phase3b.md](14_handoff_phase3b.md): source/tests의 protocol·권한 결정 근거가 남아 있다. 최신 통합 결과는 [12_phase3b_guess_game.md](12_phase3b_guess_game.md)를 우선한다.
- `superpowers/plans/2026-08-26-phase3a-netplay.md`, `2026-08-27-phase3b-guess-game.md`, `2026-08-27-phase3e-paint-tools.md`: Steam·IME·실기·품질 평가 등 남은 항목이나 현행 source의 결정 근거가 있어 유지한다.

## 이번에 제거한 문서

기능 자체가 사라진 문서는 확인되지 않았다. 아래 완료 작업 절차와 2p 전용 계획 문서는 현재 4p 계약·검증 결과로 대체되어 제거했다.

- `superpowers/plans/2026-08-26-phase2-drawing.md`
- `superpowers/plans/2026-08-27-phase3d-world-canvas.md`
- `superpowers/plans/2026-08-28-camera-controls.md`
- `superpowers/plans/2026-08-28-step3-drawing.md`
- `superpowers/plans/2026-08-28-relayquiz-steam-2p.md`
- `superpowers/specs/2026-08-28-relayquiz-steam-2p-design.md`

old commit, session 재개 명령, 당시 작업 tree 상태는 제품 reference로 옮기지 않았다. 기능 삭제나 source 삭제는 수행하지 않았다.

## 확인이 남은 항목

- [15](15_3d_party_game_design.md)의 D1은 완료됐다. 개인 canvas는 `Docked`로 시작하고 owner가 `Carried`로 들고 이동하거나 자기 zone 중앙에 다시 dock할 수 있다.
- fist drawing, world tool interaction, 4p lobby, mode selection, private relay는 구현 및 자동 검증이 완료됐다. 실제 손 입력·Steam 4계정 실기는 대기 중이다.
- phone camera는 camera 선택 UI, Windows/Android·Mac/iPhone 실제 연결, orientation·handedness·latency·장시간 실행 검증이 남았다.
- 완료된 camera control과 local drawing도 실제 Player의 다른 해상도·CJK 표시·camera 복구 범위는 기존 검증 문서에 남은 항목으로 취급한다.
- 새 4인 목표는 실제 Steam 4계정·4개 instance·각자 camera를 사용한 Player QA가 필요하다. local 4인, Loopback, Steam 2p 결과로 대체하지 않는다.

## 문서 정리 규칙

- source가 존재하는 기능 문서는 오래됐다는 이유만으로 삭제하지 않는다.
- handoff를 삭제할 때는 source/test의 참조와 유일한 실기·보안·build 기록을 현재 reference로 먼저 옮긴다.
- 완료된 계획은 구현 계약과 미검증 항목이 다른 문서에 남았는지 확인한 뒤 제거한다.
- 문서의 `구현 완료`는 해당 범위만 뜻한다. 새 4인 목표의 완료를 뜻하지 않는다.
