Camera Co-op — Steam 2인 테스트용 빌드
=====================================

■ 준비
  1. Steam을 실행하고 로그인해 둔다 (본인 계정).
  2. 초대를 주고받을 상대와 Steam 친구 관계여야 한다.

■ 실행 순서 (중요)
  1. **먼저 게임을 실행한다.**
     - Windows: CameraCoop.exe
     - macOS:   CameraCoop.app
     - Steam을 켠 상태에서 실행할 것. 화면에 "세션 없음 — Steam 연결됨: <내 계정명>"이
       뜨면 정상이다. "Steam 미연결"이 뜨면 Steam 로그인부터 확인.
     - 반드시 게임을 먼저 켜야 한다. 안 켜고 초대를 수락하면 Steam이 엉뚱한 게임
       (Spacewar)을 실행하려 한다. 이 빌드는 개발용 AppID 480을 쓰기 때문이다.

  2. 방장(host) 쪽만 [Host Steam] 버튼을 누른다.
     - Steam 초대창이 게임 화면 위에 겹쳐서 뜬다. 거기서 친구를 선택해 초대.
     - 초대창이 안 뜨면 Shift+Tab으로 Steam overlay를 열고
       친구 목록 -> 친구 우클릭 -> "게임 초대" 로도 된다.

  3. 참가자는 [Host Steam]을 누르지 않는다. 초대만 수락하면 자동으로 들어간다.
     - 화면 왼쪽 위가 "[CLIENT] players: 2"로 바뀌면 참가 성공.

■ 웹캠으로 그리기 (양쪽 다 각자 설정해야 함)
  게임 자체에는 손 추적이 없다. 같은 PC에서 tracker를 따로 띄워야
  커서가 나오고 그림이 그려진다.

  최초 1회 설치:
    - Windows: tracker 폴더의 setup_tracker.bat 실행
    - macOS:   터미널에서  cd <tracker 폴더> && ./setup_tracker.sh
    - Python이 없으면 안내가 뜬다. https://www.python.org/downloads/ 에서 설치할 것.
      Windows는 설치할 때 "Add python.exe to PATH" 를 반드시 체크한다.
      Intel Mac은 Python 3.12 이하가 필요하다 (mediapipe 0.10.21 제약).

  설치가 끝나면:
    게임 화면의 [캠 켜기] 버튼을 누르면 tracker가 자동으로 실행된다.
    [캠 끄기]를 누르면 캠이 꺼진다. 게임을 닫아도 함께 꺼진다.
    버튼이 "캠: setup 먼저 실행"으로 바뀌면 위 설치가 아직 안 된 것이다.

  수동으로 띄우려면 run_tracker.bat (Windows) / run_tracker.sh (macOS).

  손을 카메라에 비추면 커서가 나오고, 엄지와 검지를 붙이면 선이 그려진다.
  왼손 = 파랑, 오른손 = 주황. 프리뷰 창에서 q 를 누르면 tracker가 종료된다.

  * 웹캠이 없거나 설치가 번거로우면: Python만 있으면
        python fake_hand.py 600
    으로 가짜 손을 움직일 수 있다 (설치 불필요, 600은 실행 시간(초)).

■ 확인할 것
  - 서로의 커서가 실시간으로 보이는가
  - 한쪽이 그린 선이 다른 쪽에 즉시 나타나는가
  - 나중에 들어온 사람에게 기존 그림이 한 번에 채워지는가
  - 방장이 종료하면 참가자 세션이 끊기는가
  - [Clear]는 방장만 동작한다 (참가자가 눌러도 무반응이 정상)

■ 종료
  창을 닫으면 된다. Windows는 Alt+F4, macOS는 Cmd+Q.
