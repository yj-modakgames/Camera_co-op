# Camera Co-op 플레이어 게임 방법

> 대상: `RelayQuizOnline` 현재 build
> 기준: 2026-08-31 source와 문서 계약. 실제 Steam 4계정, physical webcam/phone camera, 실제 hand gesture는 별도 확인이 필요하다.

## 1. 시작 전 준비

1. Steam client를 실행하고 로그인한다.
2. `RelayQuizOnline` build를 실행한다. Steam이 실행 중이 아니거나 로그인되지 않으면 lobby 연결을 재시도할 수 없다.
3. webcam을 PC에 연결한다. webcam이 없으면 phone camera를 PC가 camera 장치로 인식하도록 연결한다. phone 연결 방식과 장치 선택은 [phone camera 안내](13_phone_camera_input.md)를 따른다.
4. camera toggle을 누르기 전에 HUD가 `손 조작 · Tab: 이동`인지 확인한다. `이동 · Tab: 손 조작`이면 `Tab`으로 Interact mode에 들어간다. 앱에 focus가 있고 답변 input을 편집 중이 아니어야 한다.
5. 게임 화면 오른쪽 위의 `캠 켜기` 버튼을 **mouse 왼쪽 버튼으로 누른다**.
6. 버튼이 `시작 중…`으로 바뀌면 tracker 시작을 요청한 상태다. 실제 입력 사용은 첫 fresh packet을 받아 `송신 수신 중`이 된 뒤 가능하다.
7. 상태가 `송신 수신 중`으로 바뀌고 손 안내가 사라지면 camera 입력을 사용할 수 있다. 버튼은 `캠 끄기`로 바뀐다.
8. 종료할 때는 같은 위치의 `캠 끄기`를 mouse 왼쪽 버튼으로 누른다. `CameraToggle`은 현재 build에서 camera를 시작·종료하는 유일한 mouse control이다.

`Host`, `Invite`, `Leave`, game 선택, `START`, `ReadyPad`, drawing 도구와 relay 진행 버튼은 mouse로 누르지 않는다. camera toggle을 제외한 3D action은 손으로 조작한다.

## 2. 이동과 시점

1. 로비에서 `Tab`을 눌러 HUD가 `이동 · Tab: 손 조작`이 되면 이동 mode다.
2. `W`, `A`, `S`, `D`로 현재 player 시선 기준으로 이동한다.
3. 이동 중 mouse를 움직여 시점을 회전한다. 벽, 바닥, 버튼과 구조물에는 collider가 있으므로 통과할 수 없다.
4. **Space**는 바닥에 닿아 있을 때만 점프한다. 공중에서 다시 누른 Space는 무시한다. 점프의 목표 최고점은 약 1.5m이며, 실제 height와 조작감은 Player QA에서 확인할 항목이다.
5. `Tab`을 다시 누르면 `손 조작 · Tab: 이동`으로 바뀐다. 이때 cursor가 표시되고 손으로 3D 물체를 겨냥할 수 있다.
6. relay의 `Setup`, `Handover`, `WordReveal`, `ObservePrevious`, `Guessing`, `Reveal`에서는 game context가 이동을 잠근다. Drawing에서도 canvas가 `Docked`인 동안은 이동이 잠기지만, 자기 canvas를 `Carried`로 든 동안에는 `WASD`와 mouse look으로 이동·회전할 수 있다. `Gallery`에서는 다시 이동하며 결과를 둘러볼 수 있다.
7. 답변 input이 선택되어 글자를 입력하는 동안에는 `WASD`, mouse look, `Tab`, drawing이 차단된다. 답변은 keyboard로 입력한다.

### 조작 도구 요약

| 입력 | 용도 | 사용 시점 |
|---|---|---|
| mouse 왼쪽 클릭 | `CameraToggle` 시작·종료 | camera panel 조건을 만족할 때 |
| `W/A/S/D` | 이동 | `Explore`의 Move mode, 허용된 drawing 이동 |
| mouse 이동 | 시점 회전 | 이동 mode |
| `Space` | grounded jump | 이동 중 바닥에 있을 때 |
| `Tab` | Move ↔ Interact | `Explore`에서만 |
| 손 hover | 물체 조준·hover | camera 수신 후 Interact |
| 손 pinch 후 release | 일반 3D button/action·canvas handle 선택 | release를 확정 조건으로 사용하는 손 action |
| 손 pinch press | brush pickup·paint/width/eraser station 선택 | brush를 들고 있는 Drawing 중 |
| 손 fist 유지 | canvas에 그리기·지우기 | active drawing canvas |
| 손 open | 선 종료·다음 gesture 재무장 | fist drawing 종료 |
| keyboard | 최종 답 편집·한글 IME | 마지막 player의 답변창 |

## 3. 손으로 3D 물체 누르기

1. camera가 `송신 수신 중`인지 확인한다.
2. `Tab`으로 Interact mode에 들어간다.
3. 손을 대상 collider 위에 올려 hover한다. 대상 이름과 hover 반응을 확인한다.
4. 손을 **펼친 상태로 최소 0.10초** 유지해 새 gesture를 준비한다.
5. 대상 위에서 pinch를 시작한다. canvas가 아닌 일반 3D 물체는 pinch가 press다.
6. 같은 대상을 계속 겨냥한 채 pinch를 푼다. 대상 밖에서 풀면 action은 취소될 수 있다.
7. 대상이 바뀌거나 camera가 끊기거나 phase가 바뀌면 capture가 취소된다. 다시 손을 펴고 0.10초 이상 유지한 뒤 새 pinch를 해야 한다.

ReadyPad는 pinch/release 버튼이 아니라 손 presence dwell로 동작한다. 일반 world action과 손 UI button은 pinch press 뒤 같은 대상을 겨냥한 release에서 한 번 실행된다. PhysicalBrush pickup과 paint/width/eraser station은 pinch press 시점에 적용된다. 양손이 같은 exclusive 물체를 누르면 먼저 capture한 손만 소유한다.

## 4. Steam lobby 만들기와 참가하기

### Host

1. camera 수신을 켠다.
2. `Tab`으로 Interact mode에 들어간다.
3. 로비의 `Host` 물체를 손 hover한다.
4. pinch를 시작하고 같은 물체 위에서 release한다.
5. Steam lobby가 만들어지면 화면의 인원과 연결 상태를 확인한다.

### Steam Invite

1. Host인 player가 `Invite` 물체를 손 pinch 후 release한다.
2. Steam invite overlay가 열리면 친구를 초대한다.
3. Host는 lobby가 4명으로 찰 때까지 기다린다.

### 초대받은 친구

1. Steam invite를 수락한다.
2. 같은 `RelayQuizOnline` game/version인지 확인한 뒤 lobby에 들어온다.
3. 자신의 player slot과 zone을 확인한다.
4. 연결 notice가 사라진 뒤 자신의 `ReadyPad`로 이동한다.

### 나가기

1. lobby 또는 gallery에서 `Leave` 물체를 손 pinch 후 release한다.
2. 게임 중 이탈하면 진행 중 round가 중단될 수 있다.
3. Host가 종료하면 자동 host migration은 제공되지 않는다.

## 5. 4명 준비와 mode 선택

1. 네 player가 각각 자신의 zone을 확인한다. zone과 canvas dock 순서는 player slot 순서와 같다.
2. 각자 자기 zone 중앙의 `ReadyPad` 위에 손을 올려 hover한다.
3. camera가 연결되고 fresh hand가 감지된 상태로 약 1초 동안 Pad 위에 손을 유지한다.
4. Pad가 준비 완료로 바뀌면 다음 player도 자기 Pad에서 같은 동작을 한다.
5. 4명 모두 ready가 되면 Host의 mode 전시대가 열린다.
6. Host가 `Relay Copy`, `Memory Copy`, `Coop Mural` 중 하나를 손으로 선택한다.
7. Host가 중앙의 `START` 물체를 손 pinch 후 release한다.

| mode | 현재 동작 | 검증 상태 |
|---|---|---|
| `Relay Copy` | 4인 private 그림 전달, 마지막 player keyboard answer, 결과 gallery | 자동·Editor 검증, 실제 Steam 4인 대기 |
| `Memory Copy` | 직전 그림을 약 5초 본 뒤 숨기고 복사 | code/session 계약, 실제 Steam 4인 대기 |
| `Coop Mural` | 공개 canvas에 slot 순서대로 한 layer씩 그리는 공동 그림 | code/session 계약, 실제 Steam 4인 대기 |

## 6. Relay Copy 한 판 진행

Host가 `Relay Copy`를 선택하고 `START`를 실행하면 player 0부터 한 줄 순서로 진행한다. setup notice는 약 2.5초 뒤 사라지고, 안정된 lobby에서 계속 떠 있지 않는다.

### 첫 번째 player

1. 첫 번째 player가 자신의 `ReadyPad` 위에 fresh open hand를 올린다.
2. pinch하지 않은 채 손을 Pad 위에 약 1초 유지한다. dwell이 끝나면 자동으로 준비가 확정된다. 손을 Pad 밖으로 빼거나 tracking이 stale되면 준비되지 않는다.
3. 첫 번째 player에게만 제시어가 표시된다.
4. 제시어 화면이 끝나면 자기 zone의 빈 canvas와 도구가 열린다.
5. 붓 거치대의 brush 위에서 pinch하면 즉시 brush를 든다. pinch를 풀거나 손을 펴도 brush는 유지된다. 다시 놓으려면 rack/dock station 위에서 pinch한다.
6. brush를 든 채 paint station에서 색을 고르고, width station에서 굵기를 고른다.
7. canvas 위에서 **fist를 유지**해 선을 그린다. canvas drawing은 pinch가 아니라 fist다.
8. fist를 풀면 현재 선이 끝난다. 이어서 손을 펴고 최소 0.10초 유지해야 다음 gesture가 rearm된다. canvas 밖으로 나가도 현재 선은 종료된다.
9. 지우개 station을 고르면 stroke erase mode로 바뀐다.
10. 제한 시간이 끝나거나 `그림 완료`를 손으로 release하면 그림이 확정된다.

### canvas 들기와 거치하기

1. 자기 canvas가 `Docked` 상태라면 canvas handle을 pinch 후 release해 `Carried`로 바꾼다.
2. 손을 놓아도 canvas는 player에게 계속 들려 있다. `WASD`와 mouse look으로 canvas를 들고 이동·회전할 수 있다.
3. 자기 zone 중앙의 dock 위치로 돌아온다.
4. `Dock Paper` 또는 dock 영역을 손으로 pinch 후 release한다.
5. 자기 canvas만 `Docked`로 돌아간다. 다른 zone에는 거치할 수 없다.

### 다음 player

1. 시간이 끝나면 그림은 다음 player에게만 전달된다.
2. 다음 player는 fresh open hand를 자기 `ReadyPad` 위에 올리고 pinch하지 않은 채 약 1초 dwell해 `준비`를 실행한다.
3. 다음 player에게만 직전 player의 frozen 그림이 보인다. 다른 player는 private data를 받지 않는다.
4. 참고 그림을 보며 자기 빈 canvas에 다시 그린다. 이전 그림 위에 이어 그리지 않는다.
5. P2, P3도 같은 brush·paint·width·eraser 절차를 사용한다.
6. 제한 시간이 끝나거나 `그림 완료`를 release하면 다음 player에게 넘긴다.

### 마지막 player

1. 네 번째 player는 직전 그림만 보고 제시어는 볼 수 없다.
2. 손으로 답변 input의 `입력 포커스`를 pinch 후 release한다.
3. keyboard로 답을 입력한다. 한글은 IME 조합을 사용한다.
4. `Enter`는 제출을 대신하지 않는다. 손으로 `제출`을 pinch 후 release한다.
5. 시간이 끝나면 현재 답으로 판정되며 빈 답은 오답이다.
6. `Reveal`에서 전원에게 정답, 입력값, 정오가 공개된다.
7. `갤러리`를 손으로 선택하면 P1, P2, P3 그림이 순서대로 공개된다.
8. Gallery에서는 `WASD`와 mouse look으로 전시물을 둘러본다.
9. Host가 `다시 시작`을 손으로 선택하면 이전 그림·답·제시어·timer·capture를 reset하고 새 setup으로 돌아간다.

## 7. camera 문제 해결

- 버튼이 `시작 중…`에서 멈추면 camera 권한, Python tracker 실행, 선택된 device를 확인하고 실패 문구를 읽는다.
- `송신 수신 중`인데 손이 안 보이면 camera 앞에서 손을 화면 안에 두고, 손을 편 상태로 잠시 기다린 뒤 다시 pinch한다.
- camera가 끊기면 Drawing 중인 선은 종료되고 입력이 차단될 수 있다. camera를 복구한 뒤 손을 새로 펴서 rearm하고 다시 누른다.
- phone camera가 목록에 없으면 phone이 OS camera device로 노출됐는지, 다른 프로그램이 독점하고 있지 않은지 확인한다. `Refresh`, `Prev`, `Next`, `Preview`는 CameraStation에서 손으로 사용한다.
- `Host`와 `START`는 mouse로 누르지 않는다. camera toggle만 mouse control이다.

## 8. 검증 범위

자동 test와 Editor runtime에서 camera 상태 전환, relay private visibility, canvas `Carried`/`Docked`, ReadyPad·mode·phase 전이를 확인했다. `실제 Steam 4인 대기`라고 표시한 항목은 source contract와 자동 test는 있지만 실제 webcam hand gesture, phone camera, Steam 4 account/4 machine 결과를 의미하지 않는다.

관련 source: `PlayerController`, `InputModeManager`, `HandInputRouter`, `PartyWorldController`, `WorldReadyPadInteractable`, `PhysicalPaintTool`, `PersonalCanvasPlacement`, `OnlineRelayQuizController`.
