# 13. 폰을 PC 카메라로 쓰는 입력 설계

## 0. 범위와 결론

이 문서는 스마트폰 영상을 **PC의 카메라 장치로 노출**한 뒤, 기존 Python hand tracker를 유지하는 1차 경로를 정의한다.

```text
[phone camera] -> [OS/USB/virtual webcam device on PC]
                 -> PythonTracker/OpenCV
                 -> UDP 127.0.0.1:5052
                 -> Unity UdpHandReceiver -> HandInputRouter
```

폰 안에서 hand tracker를 실행하거나, 게임 peer끼리 영상을 전달하는 설계는 1차 범위가 아니다. 폰-PC 카메라 영상 경로와 초대/Steam의 game-peer 데이터 경로는 별개이며, 영상이나 landmarks를 초대/Steam으로 보내지 않는다. 카메라 영상은 해당 PC의 Python tracker가 소비하고, Unity에는 파생된 hand packet만 local UDP로 보낸다.

| 항목 | 현재 확인 상태 | 문서에서의 취급 |
|---|---|---|
| `PythonTracker/config.py`의 기본 `CAMERA_INDEX=0` | 소스 확인 | 기본값이며 `--camera N`으로 선택 가능 |
| `hand_tracker.py`의 OpenCV 입력 | 소스 확인 | PC에 camera device가 노출되면 기존 tracker를 유지하는 전제 |
| Python → UDP `127.0.0.1:5052` | 소스 확인 | 기존 경로 유지 |
| Unity fresh packet/hand, 끊김 시 취소·재무장 | 소스 확인 | `CameraControlPanel`, `HandInputRouter` 동작을 기준으로 안내 |
| Windows connected camera / Continuity Camera / Camo | 공식 문서 확인 | 사용 후보. 이 프로젝트에서 OpenCV 성공은 미실측 |
| phone camera → OpenCV → tracker | 기기 실측 미실행 | 지원 보장으로 표현하지 않음 |
| orientation, handedness, FPS, latency, 발열·전원 | 기기 실측 미실행 | 출시 전 검증 항목 |

640×480, 30fps는 목표 또는 시험 시작값으로 제안할 수 있지만 실측 결과가 아니다. 무료 여부, 모든 OS/기기 지원, 성능과 지연은 보장하지 않는다.

## 1. 1차 연결 선택

먼저 운영체제의 안전한 내장 경로를 확인하고, 되지 않으면 사용자가 승인한 USB 또는 virtual webcam 도구를 선택한다. 특정 앱을 필수 채택하거나 USB가 항상 우수하다고 단정하지 않는다.

### Windows connected camera

Microsoft의 현재 안내는 Windows 11, Android 10 이상, Link to Windows app 1.24022.0 이상을 요구한다. `Settings > Bluetooth & devices > Mobile devices > Manage devices > Use as connected camera`에서 기능을 설정하며 앱과 카메라 권한이 필요하다. 기존 문서의 Android 9 이상·앱 불필요 조건은 폐기한다. [Microsoft: Use your mobile device's camera](https://support.microsoft.com/en-us/windows/apps/phonelink/use-your-mobile-device-s-camera)

### Apple Continuity Camera

Apple의 공식 조건은 iPhone XR 이상, iOS 16 이상, macOS Ventura 13 이상, 동일 Apple Account와 2FA, Wi-Fi와 Bluetooth다. USB 연결 시 Mac에서 Trust 절차가 필요하다. 이 조건은 카메라 노출 조건이며, Windows 또는 이 프로젝트의 OpenCV에서 열리는 것까지 의미하지 않는다. [Apple: Continuity Camera](https://support.apple.com/en-us/102546)

### USB 또는 virtual webcam

OS 기능이 없거나 카메라 장치로 나타나지 않을 때만 사용자가 선택한 도구를 검토한다. Camo는 PC/Mac의 Camo Studio와 iOS/Android의 Camo Camera를 USB 또는 Wi-Fi로 연결하는 후보이며, Windows virtual camera 호환 add-on과 관리자 권한이 필요할 수 있다. Android Windows USB에는 USB debugging 요구가 있으므로 설치·권한을 먼저 확인한다. Camo는 검증 후보이지 확정 채택이나 무료 보장이 아니다. [Camo: Getting started](https://camo.com/support/camo/camo-getting-started)

기기와 PC에 표시된 virtual webcam 장치가 OpenCV에서 실제로 열리는지는 별도 시험한다. 온보딩에 앱 이름이나 낡은 화면 캡처를 고정하지 않는다.

## 2. 카메라 시작 UX와 입력 경계

카메라 시작에 필요한 선택과 권한 확인만 mouse를 허용한다. 게임 그림, 물감 선택, 시작·준비 입력은 hand input으로만 처리한다. 마우스/키보드 폴백을 카메라 실패 복구 수단으로 추가하지 않는다.

`hand_tracker.py`는 `--camera N`, `--list-cameras`, `--preview/--no-preview`를 제공한다. Unity의 `CameraDeviceCatalog`와 `CameraControlPanel`은 후보 탐색·preview·재시도·world camera action을 연결한다. phone/Camo/Continuity Camera의 실제 OpenCV 호환성은 별도 기기 시험 대상이다.

preview에서 다음을 확인해야 한다.

- 전면 카메라의 좌우 미러와 화면 orientation이 tracker/Unity 좌표와 일치하는가
- 왼손·오른손 handedness가 실제 손과 일치하는가
- 손과 그림 영역이 동시에 들어오는 거치 위치인가

orientation과 handedness는 문서 추정값이 아니라 Android·iOS 실기기에서 각각 확인한다.

## 3. 실패·복구 계약

다음 상태는 모두 사용자에게 읽을 수 있는 상태로 표시하고, 재시도 경로를 제공한다.

| 상황 | 처리 원칙 |
|---|---|
| 장치가 여러 개 | 후보 preview로 선택. `CameraDeviceCatalog`와 world camera action이 선택·재시도를 제공 |
| 프레임 없음/권한 거부 | tracker 시작 실패로 표시하고 권한·장치 상태 확인 후 재시도 |
| 다른 앱이 카메라 점유 | 점유 앱을 닫도록 안내 후 재시도 |
| virtual webcam 연결 끊김 | tracker/Unity 수신 상태를 실패로 표시. 자동 복구를 보장하지 않음 |
| tracker 프로세스 종료 | `CameraControlPanel`의 fresh packet 상태를 무효화하고 재시작 선택 제공 |
| 재시작 후 오래된 입력 | 현재 packet을 폐기하고 fresh packet을 기다림. 손 입력은 다시 open 상태를 관찰해 재무장 |
| active turn 또는 readiness 변경 | 카메라 준비 상태를 무효화하고 해당 turn의 입력을 일시정지. 새 fresh hand와 준비 절차 필요 |

Unity 소스상 `CameraControlPanel`은 fresh packet과 프로세스 상태를 구분하고, 상태가 바뀌면 `HandInputRouter.CancelAll(TrackingLost)`를 호출한다. `HandInputRouter`는 sample freshness를 검사하며 stale/invalid sample을 취소하고, 새 입력 뒤 open hand 관찰로 재무장한다. 이 문서는 소스에서 확인한 동작을 설명하며, 제품 UI와 자동 시작의 최종 상태는 다른 작업의 변경을 반영해 다시 확인해야 한다.

## 4. 네 명 동시 사용과 개인정보 경계

4-player 목표는 **각 player의 PC가 자기 camera와 tracker 하나를 소유하는 4개의 독립 pipeline**이다. 현재 Python tracker의 local UDP 목적지 `127.0.0.1:5052`는 각 PC 안에서만 사용하므로 서로 다른 PC끼리 port가 충돌하지 않는다. 한 PC에서 tracker 여러 개를 실행하는 구성은 이 목표가 아니며 구현되었다고 가정하지 않는다. 각 PC의 장치 매핑, CPU/GPU, 유선·무선 지연은 실제 4개 instance에서 확인한다.

초대/Steam에는 camera 영상, hand landmarks, QR 연결 정보, PC의 LAN 주소를 넣지 않는다. 전화기 영상 전송은 phone과 해당 PC 사이의 입력 장치 연결이고, game-peer 통신은 게임 상태 전송이다. 두 경로를 섞지 않는 것이 개인정보 경계다.

## 5. 검증 계획

아래 표에서 자동 검증과 외부 기기 검증을 구분한다. Python unit test와 Unity camera discovery test는 통과했지만, 실제 phone/Camo/Continuity Camera 기기 시험은 아직 미실행이다.

| 검증 | 방법 | 통과 기준 |
|---|---|---|
| 장치 노출 | 지원 OS/도구별로 PC 카메라 목록과 OpenCV `VideoCapture` frame 획득 확인 | preview에 연속 frame 표시 |
| orientation/handedness | Android·iOS 각 1대에서 좌우 손과 화면 회전 시험 | Unity cursor 위치와 handedness 일치 |
| no frame/권한/점유 | 권한 거부, 장치 제거, 다른 앱 점유를 각각 재현 | 실패 문구, 입력 취소, 재시도 가능 |
| 연결 끊김/restart | USB·Wi-Fi를 끊고 tracker를 재시작 | stale packet 사용 금지, fresh packet 후 재무장 |
| 4-player | 네 PC에서 각자 camera·tracker·game instance를 실행 | 서로의 local port와 장치를 공유하지 않고 각 player의 hand 입력 유지 |
| latency | 손 동작과 Unity 반응을 같은 시간 기준의 고속 촬영으로 측정 | 유선/무선 및 장치별 분포 기록 |
| 장시간 | 유선·무선 각각 장시간 실행하며 발열·배터리·절전·연결 유지 기록 | 지원 조건과 제한을 문서화 |

phone timestamp와 PC 수신 timestamp의 단순 차이로 절대 latency를 주장하지 않는다. clock sync가 없으면 시계 오프셋이 포함되므로, 고속 촬영 또는 동기화된 측정 장치가 필요하다.

## 6. 후속 검토

폰 내 tracker, 자체 pairing QR, 브라우저 bridge, WebSocket/WebRTC 전송은 1차 경로의 구현 항목이 아니다. 나중에 검토할 때도 영상 대신 landmarks만 보내는지, secure context와 인증, 재연결, 개인정보 경계를 별도 설계하고 실제 기기로 검증한 뒤 결정한다. 현재 `phone_bridge.py`와 폰용 페이지는 존재하지 않으며, 이는 삭제된 기능이 아니라 아직 구현 전인 기능이다.

## 7. 결정 기록

1. 1차는 phone을 PC camera device로 노출하고 기존 Python/OpenCV tracker와 UDP loopback 경로를 유지한다.
2. 초대/Steam game-peer 경로에는 camera 영상이나 landmarks를 보내지 않는다.
3. camera 시작의 선택·권한·실패 복구에만 mouse를 사용한다. 게임 조작은 hand input만 사용한다.
4. camera 선택과 preview 경로는 구현됐다. phone/Camo/Continuity Camera의 장치 노출, orientation, latency, 장시간 지원 범위는 실측 후 확정한다.
