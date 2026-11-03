# 다른 세션을 위한 작업 인계

기록일: **2026-11-03**. 최신 작업은 **왕의 회랑 종 완주 판정·공중 체크포인트 저장 수정**입니다. 수정 버전 **1.0.4 / 프로토콜 39**는 배포 준비 중이며, 기존 배포는 1.0.3 / 38입니다. 사용자 맵 스키마는 1입니다. 맵 지형·종 위치·이동 물리와 다른 공식 맵은 유지합니다.

## 지금 이어받을 상태

사용자는 Unity 없이 게임 안에서 할리갈리 데스런 맵을 제작하고, 방에서 자동 공유하며, 파일 하나로 다른 사람에게 전달하기를 요청했습니다. 공식 아이템 레이스에 이어 고정 패턴 장애물 맵 태엽 요새를 추가했습니다. 기존 카드·종·승패 규칙을 유지하며 공개 맵 자료실과 범용 규칙 편집기는 선택하지 않았습니다. 저장소는 `alphamon1114/halligalli-deathrun`의 `main`입니다. 사용자가 Unity 저장·Git 커밋·GitHub 반영을 요청했으므로 아래 검증을 마친 프로젝트 상태를 전달 대상으로 삼습니다.

### 왕의 회랑 종·공중 체크포인트 수정 · 2026-11-03

- 사용자 보고: 2인에서 봇은 종을 누르지만 본인은 안내가 없고, 체크포인트에 직접 착지하지 않으면 R로 처음으로 돌아갑니다. 종 모델 중심과 판정 좌표의 수평 위치는 같았습니다. 원인은 모든 서프 발판의 접촉 기록을 요구하는 완주/체크포인트 조건입니다. 카드상 정상 기립·종 앞 접지 상태에서도 접촉 기록이 부족하면 종 안내·입력이 막히는 실패를 재현했습니다.
- 왕의 회랑만 `CourseCheckpoints.orderedPassage`를 켭니다. 네 광장을 순서대로 지면 높이 이상에서 통과하면 공중에서도 호스트가 저장하며, 뒤로 돌아가도 저장 지점을 낮추지 않습니다. R/낙하는 마지막 광장으로 복귀합니다. 종은 네 체크포인트와 기존 거리·접지·카드 자격으로 판정하며 개별 발판 접촉을 모두 요구하지 않습니다. 직행·체크포인트 순서 생략·광장 아래 통과·오기립은 계속 차단합니다. HUD 점 네 개는 체크포인트 진행 상태입니다.
- 검증(2026-11-03): 2인 실제 카드 규칙·플레이어 HUD/E 입력·카드 획득·자동 착석·공중 저장/R 복귀 **30/30**, 왕의 회랑 전체 물리 완주·복귀 **38/38**, 기존 서프 수로 물리·복귀·우회 차단 **57/57**. `TestResults/pyramid-bell-checkpoints.json`, `pyramid-validation.json`, `surf-validation.json`, 실제 안내 화면 `Previews/Pyramid_BellPrompt.png`. 도착 상태의 격리 회귀는 위치/진행 상태를 설정하며 전체 주행 검사는 별도로 실제 이동합니다.
- 미완료: 1.0.4 빌드·게시·두 프로세스 네트워크 검증. 인터넷 Relay·인위적 지연은 이번 수정에서 미검증입니다. 지형 빌더는 실행하지 않았으며 저장된 왕의 회랑 씬에는 체크포인트 정책 필드만 추가했습니다.

### 맵 BGM 생성 · 2026-11-03

- VARCO Sound 공식 7개 맵별 음악 후보 생성·사용자 선택이 완료됐습니다. 리뷰 ID `r_4e25035b`, 선택 파일 저장 위치 `C:/Users/mbc/Music/VARCO Sound/HalliGalli_MapBGM`. MP3 7개 파일 존재·크기를 확인했습니다. 첫걸음/정상 쟁탈/왕의 회랑/아이템 레이스/태엽 요새는 후보 1, 서프 수로/폐공장은 후보 2입니다. 유료 생성은 이미 실행했으므로 다시 요청하지 않습니다. 게임 적용·루프 경계 검증은 아직 하지 않았습니다.

### 태엽 요새 복합 코스 · 2026-11-03

- 기존 직선 128m 통로를 네 개의 40m 구간으로 재구성했습니다. A: 1.5m 간격 점프 두 곳·창 → 우회전, B: 엇박 프레스 두 곳·화살 → 좌회전, C: 대기·탑승·운반·하차하는 이동 발판·화살 → 좌회전, D: 창·화살·창·마지막 점프·종. 코스 폭 8m, 코너 안전 광장 폭 10m, 체크포인트 3곳입니다.
- 코너/체크포인트 좌표는 `(0,0,40)`, `(40,0,40)`, `(40,0,80)`, 종은 `(0,.7,80)`입니다. 이동 발판은 `(40,-.3,58)`을 중심으로 ±6m, 10초 주기로 왕복하며 16m 빈 구간을 건넙니다. 안전 착지대와 함정을 분리하고 낙하 구간 밑의 숨은 바닥은 두지 않았습니다. 장식은 비충돌이며 원래 카드 테이블을 유지합니다.
- `ClockworkCourse`가 방향에 독립적인 프레스 대기와 실제 이동 발판의 도착 예측·탑승·하차를 처리합니다. 이동 운반은 기존 `FactorySurface`/모터를 사용하며 물리는 조정하지 않습니다. `FactoryPress.contactSession`을 지정한 태엽 요새 프레스만 잘못 기립·복귀 상태에서도 피격합니다. 기존 폐공장·유즈맵 프레스 동작은 유지합니다. 창·화살 3연발과 기존 리스폰 수정도 유지합니다.
- 검증(2026-11-03): Unity 실제 플레이 **132/132**. 순간이동 없는 완주 **50.895초/낙하 0회**, 모든 점프·세 방향 전환·프레스 2개·함정 6곳·체크포인트 3곳 통과. 기계 앞 안전 대기 약 8.0초, 발판 도착 대기 약 4.5초, 실제 운반 약 11.2m, 먼 착지대 하차 확인. 정상/잘못 기립/좌석 복귀의 피격·리스폰 및 이전 고속 3연발 회귀도 포함합니다. `TestResults/clockwork-validation.json`.
- `ClockworkCourseBuilder.Build()`로 태엽 요새만 재생성했습니다. 기존 다른 씬·맵 설정·이동 프로필/소스 **19개 SHA-256 일치**. `Previews/ClockworkMixedOverview.png`는 새 전체 배치, `ClockworkPresses.png`/`ClockworkFerry.png`는 실제 플레이 화면입니다.
- Windows **1.0.3 / 프로토콜 38** 빌드·패키징·[GitHub Release](https://github.com/alphamon1114/halligalli-deathrun/releases/tag/v1.0.3) 게시 완료. 배포 소스 `c5b3054`. 실제 두 플레이어 검증 **33/33**: 방향이 바뀐 모든 함정 피격·복귀, 프레스 오기립 피격, 세 코너 체크포인트, 참가자 무입력 발판 운반·동기화, AI 물리 완주 **46.319초/낙하 0회**, 종·6인 카드 테이블 확인. `TestResults/clockwork-network.json`, `clockwork-mixed-release.json`, `clockwork-mixed-preservation.json`.
- 미검증: 인터넷 Relay·인위적 지연·실사용자 난이도 평가. 별도 미구현 기능은 없습니다. 사용자의 실행 중 게임·런처는 건드리지 않았으며, 게임 종료 후 런처에서 1.0.3으로 업데이트해야 합니다.

### 함정 리스폰·고속 화살 3연발 · 2026-11-03

- 사용자 보고: 창·화살에 닿아도 리스폰되지 않았습니다. 원인은 `ClockworkTrap`이 `participant.Racing`으로 피격을 제한하여 잘못 기립한 사람과 좌석 복귀 중인 사람을 제외한 것입니다. 이제 앉아 있거나 이미 리스폰 중인 사람만 제외하며, 카드 경주 자격·`RunActive`와 무관하게 실제 함정 접촉 시 기존 `BeginFall()`로 복귀합니다.
- 화살은 **0.4초 간격 3연발**, 각 발의 통로 횡단은 **0.2초/50m/s**로 기존보다 6배 빠릅니다. 공격 1.2초 안에서 세 번 발사하고 녹색 안전 3.5초·황색 예고 0.9초를 유지합니다. 발사 사이 숨겨진 화살은 피격하지 않으며, 각 발을 분리해 연속 충돌을 검사하므로 발사점으로 되돌아가는 순간에 역방향 판정이 생기지 않습니다. 창 패턴과 맵 지형은 변경하지 않았습니다.
- 검증: 기존 구현에서 실제 잘못 기립한 창/화살 피해자·종 획득 후 복귀·경주 비활성 상태의 **4개 리스폰 회귀 실패**를 기록했습니다. 수정 후 Unity 실제 플레이 **92/92**: 해당 네 회귀, 세 발 각각의 표시·명중, 발사 사이 비충돌, 50m/s 이동, 안전 시간 통과, 기존 완주/체크포인트를 확인했습니다. `TestResults/clockwork-respawn-regression.json`, `clockwork-validation.json`.
- Windows **1.0.2 / 프로토콜 37** 빌드·패키징·[GitHub Release](https://github.com/alphamon1114/halligalli-deathrun/releases/tag/v1.0.2) 게시 완료. 소스 `23b0b1f`. 사용자가 실행 중인 기존 게임·런처는 종료하거나 덮어쓰지 않았고 별도 출력 경로에서 동일 런처를 패키징했습니다. 사용자는 게임 종료 후 런처에서 최신 확인·업데이트해야 합니다.
- 배포된 버전의 두 플레이어 루프백 **28/28**: 양쪽 잘못 기립 상태의 창·화살 피격/리스폰, 정상 경주 피격·체크포인트 복귀, 여섯 구간 실제 이동 완주 **36.057초/추가 낙하 0회**, 종·6인 테이블 확인. 관측 최대 패턴 시각 차이 **0.159초 미만**, 실제 고속 화살 비행 화면 확인. `TestResults/clockwork-network.json`, `clockwork-patch-release.json`. 인터넷 Relay·인위적 지연은 이번 변경에서 미검증입니다.

### 태엽 요새 · 2026-11-03

- 공식 맵 `clockwork`를 마지막에 추가했습니다. 128m 석조 요새, 창 2곳 → 화살 2곳 → 혼합 2곳, 체크포인트 45m/82m입니다. 녹색 안전 3.5초 → 황색 예고 0.9초 → 적색 공격 1.2초의 5.6초 주기를 사용하며 이웃 함정은 2.8초 어긋납니다. 실제 창과 화살에 닿을 때만 기존 체크포인트 복귀를 적용합니다.
- `ClockworkCourseBuilder.Build()`만 새 씬을 생성합니다. 저장된 다른 공식 씬·이동 설정은 재생성하지 않습니다. 새 머티리얼·창/화살/기어 메시·썸네일과 카탈로그·빌드 목록을 연결했습니다. 장식은 비충돌, 통로 외벽은 충돌합니다. 기존 테이블·카드·종 규칙과 6인 출발을 유지합니다.
- `ClockworkTrap`은 호스트 공통 시간으로 패턴을 표시하고 호스트에서만 연속 캡슐/개별 함정 형상 교차를 판정합니다. 숨은 창·화살 리셋·예고·복귀 중에는 피격하지 않습니다. AI는 다음 구간이 위험하면 앞에서 기다립니다. 공식 맵 선택은 대기방 4개/페이지, 오프라인 6개/페이지로 일곱 번째 맵도 접근할 수 있습니다.
- 검증(2026-11-03): Unity 실제 플레이 **58/58**. 단독 AI가 순간이동 없이 **34.245초, 낙하 0회**로 여섯 구간과 두 체크포인트를 통과해 종을 울렸습니다. 안전/예고/실제 피격/복귀·여섯 출발 위치·연속 충돌·비경주 면역을 확인했습니다. 기존 씬·맵 설정·이동 프로필/소스 **19개 SHA-256 일치**. `TestResults/clockwork-validation.json`, `clockwork-preservation.json`.
- Windows 두 플레이어 루프백 **23/23**: 참가자 실제 이동 완주 **38.978초/낙하 0회**, 종 획득·재착석, 양쪽 창/화살 피격·복귀, 두 체크포인트, 안전 대기, 6인 시작. 관측한 최대 패턴 시각 차이 **0.109초 미만**. 실제 마우스로 오프라인/대기방 다음 페이지와 일곱 번째 맵 선택 **2/2**. `TestResults/clockwork-network.json`, `clockwork-ui.json`.
- Windows **1.0.1 / 프로토콜 36** 빌드·패키징·[GitHub Release](https://github.com/alphamon1114/halligalli-deathrun/releases/tag/v1.0.1) 게시 완료. 배포 소스 `e7f6c55`. 실제 런처 UI에서 설치된 1.0.0 실행 차단 → 다운로드·검증·1.0.1 설치 → 실행 버튼 활성화 확인. 이전 설치는 보존했습니다. 다운로드된 실행 파일의 최신 검사 ALLOWED와 태엽 요새 6인 카드 공개 후 전체 테이블 화면도 확인했습니다. `TestResults/clockwork-release.json`, `Previews/Clockwork_{Spears,Arrows,Table}.png`.
- 남은 검증 범위: 인터넷 Relay·인위적 지연·실사용자 난이도 평가는 이번 작업에서 수행하지 않았습니다. 별도 미구현 기능은 없습니다. 모든 참가자는 런처에서 1.0.1로 업데이트해야 합니다.

### 최신 버전 필수 Windows 런처 · 2026-11-02

- `Launcher/`의 .NET 10 WinForms 런처는 자체 런타임을 포함한 Windows x64 단일 EXE입니다. 공식 Releases의 `BellRun-release.json`을 새 요청으로 확인하고, SHA-256/크기 검증·안전한 압축 해제·파일별 검증 후 버전별 디렉터리로 설치합니다. 최신 확인 실패나 설치 중 새 배포 발견 시 실행하지 않습니다. 실패한 설치는 기존 설치를 덮어쓰지 않습니다.
- `GameReleaseGate`는 에디터 밖에서 게임 시작 전 씬 루트를 비활성화하고 컨트롤·네트워크·프로브 시작을 보류합니다. 최신 버전·프로토콜이 맞을 때만 정상 시작합니다. 직접 EXE 실행도 동일하며 런타임 우회 옵션은 없습니다. 오프라인도 차단하고 재확인/배포 페이지/종료를 표시합니다. 이미 시작한 경기 도중 새 버전 때문에 강제 종료하지는 않습니다.
- 설치 경로는 `%LOCALAPPDATA%/BellRunLauncher`이며 사용자 맵의 기존 LocalLow 경로와 PlayerPrefs는 건드리지 않습니다. 구버전 디렉터리를 자동 삭제하지 않습니다. 과거 검사 기능 없는 배포본이나 변조 실행 파일까지 통제하는 보안/DRM 기능은 아닙니다.
- 배포 계약: schemaVersion 1, version, protocol, archiveName, downloadUrl, sha256, size, entryPoint. `GameRelease.Version`을 올리면 빌드 훅이 bundleVersion과 `BellRun-build.json`을 기록합니다. `Tools/Release/Build-Release.ps1` → 소스 커밋/푸시 → `Publish-Release.ps1 -Version <버전>` 순서입니다. GitHub draft에 ZIP·런처·메타데이터를 모두 올린 뒤 latest로 게시합니다. 공식 씬은 재생성하지 않습니다.
- 검증(2026-11-02): 런처 HTTP/압축/손상 복구/오프라인·버전 변경 차단 등 **31/31**, Unity 응답 계약 **32/32**. Windows 게임·self-contained 런처 빌드 및 패키징 성공. 첫 배포 전 실제 게임은 HTTP 404에서 BLOCKED(씬 루트 93개 보류), 실제 런처도 실행/설치 버튼 비활성화를 확인했습니다. 두 실제 차단 화면을 확인했습니다.
- 실제 배포: 소스 `6f7f421`로 [v1.0.0](https://github.com/alphamon1114/halligalli-deathrun/releases/tag/v1.0.0)을 게시했습니다. 게임 ZIP 408,317,886바이트, 독립 런처 116,137,790바이트이며 GitHub 에셋 SHA-256과 로컬 결과가 일치합니다.
- 배포 후 실제 런처 UI에서 GitHub 다운로드→검증→설치→게임 실행을 수행하고 설치된 게임의 정상 홈 화면과 ALLOWED(93개 루트 복구)를 확인했습니다. 별도 실제 구버전 0.9.9는 최신 1.0.0 응답 HTTP 200에서도 VersionMismatch로 차단되며 네트워크 프로브도 시작하지 않았습니다. 프로세스에만 끊어진 프록시를 적용하자 최신 게임은 HTTP 0/BLOCKED, 이미 최신 게임이 설치된 런처도 실행 버튼 비활성화였습니다.
- 실제 설치에 임시 예상 밖 파일을 넣으면 실행이 막히고 업데이트 버튼만 활성화됩니다. 공식 ZIP을 다시 받아 새 설치 폴더로 전환하며 기존 폴더를 보존하는 것을 확인했고 임시 파일은 제거했습니다. 사용자 맵/받은 맵/클리어 데이터 **24개 해시 유지**. 최신 버전 검사 후 실제 3인 게임플레이 **21/21**(`TestResults/missile-aim.json`)도 통과했습니다. 전체 증거는 `TestResults/launcher-release.json`입니다. 구버전 실행기·UI 자동화 등 임시 검증 파일은 삭제했습니다.
- 미완료/제한: 런처 자동 자체 교체·증분 패치·코드 서명은 포함하지 않습니다. 인터넷 연결과 GitHub Releases 접근이 필수이며, Windows SmartScreen 경고가 나타날 수 있습니다. 이번 변경의 인터넷 Relay/인위적 지연 게임플레이는 재검증하지 않았습니다.

### GitHub 공개 전환·업데이트 런처 요청 · 2026-11-02

- 사용자 요청으로 `alphamon1114/halligalli-deathrun`을 PRIVATE에서 **PUBLIC**으로 전환했고 GitHub API로 확인했습니다.
- 공개 전 Gitleaks 8.30.1로 전체 Git 이력 4개 커밋과 현재 스테이징 변경을 검사하여 탐지 0건을 확인했습니다. 이는 모든 비밀정보 부재를 보증하지는 않습니다. 빌드·캐시·로컬 인증정보 제외 규칙을 유지합니다.
- 누적된 서프맵·1인칭 유즈맵 제작·전투·티셔츠·프로토콜 35 아이템 개선과 검증 기록을 이번 커밋 대상으로 모았습니다. 이번 공개 작업에서는 게임 코드 변경이나 게임 테스트 재실행을 하지 않았습니다.
- 누적 작업 커밋 `f97b954`를 `origin/main`에 푸시했고 GitHub API로 원격 커밋을 확인했습니다. LFS 에셋 71개(79MB) 업로드도 완료했습니다. 공개 전환에 맞춰 README의 비공개 접근 안내를 수정합니다.
- 다음 요청: GitHub에서 새 게임 버전을 내려받아 업데이트하고 실행하는 런처. 이번에는 우선 공개 전환과 커밋/푸시만 진행하며 런처·Release 배포는 아직 구현하지 않았습니다. 게임 배포 파일은 소스 저장소 대신 GitHub Releases를 사용하는 방향입니다.

### 순위 제한 없는 아이템·공중 설치·실시간 조준 락온 · 2026-11-02

- 모든 순위에서 미사일·바나나·바리게이트를 각각 1/3 확률로 획득합니다. 순위 표시는 유지하며 선두도 뒤돌아 미사일을 발사할 수 있습니다.
- `RaceCourse.LockTarget`은 시점에서 조준 방향과 이루는 각도가 가장 작은 상대를 선택합니다. 기존 거리 가중치를 제거했고, 같은 각도에서만 가까운 상대를 우선합니다. 45m 사거리·조준 범위·장애물 검사는 유지합니다. 보유 중 마우스 조준이나 상대 위치가 바뀌면 HUD 락온도 갱신하며, 발사 후에는 발사 순간의 대상을 유지합니다.
- 바나나/바리게이트는 점프·낙하 중에도 즉시 설치됩니다. 자기 몸에 끼이지 않도록 기존 뒤쪽 거리 1.15m/3m를 유지하고 해당 위치 아래 실제 바닥을 찾습니다. 바닥 없음·경사/공간 부적합·우회 공간 부족이면 아이템을 소비하지 않습니다. 사용자 맵에도 같은 호스트 권위 규칙을 적용합니다.
- 검증(2026-11-02): 저장 씬 Windows 빌드 성공(`Logs/rank-free-items-player.log`). 최종 빌드에서 공식 레이스 **34/34**(`TestResults/race-network.json`), 회전된 사용자 맵·실제 공중 설치·510개 박스 공유·5인 시작 **38/38**(`TestResults/usermap-items.json`), 실제 3인 조준 전환/먼 중앙 대상 우선/사거리/무효 발사/발사 후 대상 유지/호스트·참가자 명중 **21/21**(`TestResults/missile-aim.json`).
- 실제 화면에서 먼 중앙 상대와 가까운 옆 상대 사이의 LOCK ON 이동을 확인했습니다: `Tools/Network/Runs/missile-aim-1793608003309/host-far-centered-beats-near-off-axis.png`, `host-aim-switches-to-near.png`. 기존 레이스 실행에서도 선두의 후방 미사일 명중과 공중 바리게이트 설치 화면을 확인했습니다.
- 보존: 씬·이동 프로필·SourceMotor·모션·원본 모델/재질 등 **64개 해시 일치**(`TestResults/rank-free-items-preservation.json`). 공식 맵을 재생성하지 않았습니다. 네트워크 프로토콜 **35**로 모두 같은 최신 빌드를 사용해야 합니다.
- 미완료/제한: 이번 변경의 인터넷 Relay·인위적 지연·사람끼리 체감 밸런스는 미검증입니다. 이번 작업의 Git 커밋/푸시는 하지 않았습니다.

### 티셔츠 8색·밀침 강도 조정 · 2026-11-02

- 홈/방 입장 화면과 대기방에서 파랑·빨강·주황·노랑·초록·하늘·보라·분홍을 선택합니다. `PlayerShirtColors`가 로컬 선호색을 저장하고, 호스트가 사람/AI/승인 대기 연결까지 포함해 방 안의 색 중복을 막습니다. 입장 시 선호색이 사용 중이면 빈 색을 배정하되 선호색은 덮어쓰지 않습니다. 플레이/로딩 중 변경은 차단합니다.
- `PlayerShirtAppearance`가 전신 Body의 티셔츠 마스크만 재색칠합니다. 공유 재질 + 렌더러별 MaterialPropertyBlock을 사용하며 원본 피부·바지·신발과 1인칭 손/발 모델은 유지합니다. 새 Resources 마스크·재질·셰이더는 `PlayerShirtAssets.Install`로 설치하고 `CaptureColors`로 앞/뒤 16장을 비교합니다. 씬/원본 재질/모션을 재생성하지 않습니다.
- 사용자 추가 요청: 기존 발차기 9m/s의 절반을 좌클릭에 적용하여 **4.5m/s**, F는 2.5배인 **22.5m/s**로 변경했습니다. 위로 띄우기·입력 중단 없음, .65초 피격 보호, 발 모션·사거리·타격 시점·F 2초 쿨타임은 그대로입니다.
- 검증(2026-11-02): 저장 씬 Windows 빌드 성공(`Logs/shirt-player.log`), 실제 호스트/참가자+AI 4명 색상·충돌 배정·실제 솔로 완주 후 사용자 맵 공유·라운드·재실행/재접속 **59/59**(`TestResults/shirt-colors.json`, `--api-only`). 테스트 후 원래 티셔츠 선호값을 정확히 복원했습니다. 실제 홈/대기방 네이티브 클릭과 화면은 별도 실행에서 확인했으나 전체 네이티브 자동화는 Windows 포커스 이동 때문에 불안정합니다.
- 실제 렌더링: `Previews/Shirt_EightColors_FrontBack.png` 8색 앞/뒤, `shirt-appearance.json`에서 색 전환 시 비티셔츠 영역 변화 0픽셀 및 손/발/메시 보존. `Tools/Network/Runs/shirt-colors-1793605927220/shirt-gameplay-six-avatars.png`에서 서로 다른 색의 실제 상대 5명과 원래 손을 확인했습니다.
- 밀침 검증: 실제 플레이 모드 **40/40**(`stronger-kick-physics.json`), 두 플레이어 실제 타격 **28/28**(`push-network.json`). 정지 상대의 평지 이동량은 좌클릭 약 **0.42m**, F 약 **2.72m**이며 양쪽 위치가 일치하고 수직 띄움은 없습니다. 고정 이동 거리는 아니며 기존 속도·지형에 따라 달라집니다.
- 보존: 씬 9개·프로필 2개·SourceMotor·기존 모션·프리팹·원본 Body 재질/텍스처 등 **64개 해시 일치**(`shirt-preservation.json`). 임시 전투 실행기는 삭제했습니다. 프로토콜 **34**로 모두 같은 최신 빌드를 사용해야 합니다.
- 미완료/제한: 이번 변경의 인터넷 Relay/인위적 지연과 사람끼리 체감 밸런스는 미검증입니다. Git 커밋/푸시는 하지 않았습니다.

### 최신 전투 키 안내·2초 쿨타임·발차기 복원 · 2026-11-02

- 사용자 최종 결정: F의 웃긴 발차기 모션을 유지합니다. 양손 밀기는 폐기하고 1인칭 바지/신발과 전신 오른발 동작을 복원했습니다. 효과는 수평 9m/s·수직 0·입력 중단 없음, 전체 1.1초/접촉 .32~.42초 그대로입니다. 좌클릭 손바닥 밀치기와 .65초 피격 보호도 유지합니다.
- `CourseHUD.Presentation.cs`: 오른쪽 아래에 ‘밀치기 / 좌클릭’, ‘발차기 / F’, ‘밧줄 / Q’를 같은 원형 안내로 표시합니다. F/Q는 남은 시간과 게이지, Q 연결 시 ‘당기기’를 표시합니다. 변경된 키 바인딩을 반영하며 연습실 왼쪽 아래의 중복 공격 안내는 제거했습니다.
- `MeleeCombat.KickCooldown=2`: 실제 발차기 시작부터 계산하며 거절/예약 입력은 시간을 소비하거나 연장하지 않습니다. 동작이 끝나면 쿨타임 중에도 좌클릭이 가능합니다. R 복귀·개별 착석·취소는 쿨타임을 보존하고 새 라운드/경기만 명시적으로 초기화합니다. 호스트가 승인하고 Pose에 남은 시간을 보내므로 프로토콜 **33**입니다. API/입력/진단은 `Kick`, `TryKick`, `KickCooldownRemaining`, `kickCooldown`으로 통일했습니다.
- `CombatAssetBuilder.InstallKickMotions()`로 F 클립 2개만 Kick으로 이동/재생성해 GUID를 보존했습니다. SourcePlayer 프리팹과 MovementLab의 발 모델/연결만 YAML에 추가했고, 나머지 8개 게임 씬은 프리팹을 그대로 상속합니다. 기존 좌클릭 클립 4개와 비공격 클립 42개는 그대로입니다. 삭제됐던 발 표시 모델 생성기도 복원했습니다.
- 최종 검증: 저장 씬 Windows 빌드 성공(`Logs/kick-cooldown-player.log`), 실제 플레이 모드 **40/40**(`TestResults/push-cooldown-physics.json`), 실제 호스트/참가자 쿨타임·거절·좌클릭 병행·복귀·동기화 **80/80**(`push-cooldown.json`), 기존 수평 밀침 회귀 **28/28**(`push-network.json`). 측정된 정지 상대 이동량은 좌클릭 약 .29m/F 약 .99m이며 띄움은 없습니다.
- 실제 화면: 1280×720 양쪽 플레이어의 준비/카운트다운을 네 장 확인했습니다(`Tools/Network/Runs/push-cooldown-1793602339196/`). `Previews/Push_FP_Kick_Contact.png`, `Push_Body_Kick_Contact.png`에서 발차기를 확인했고 접촉 후 발 표시가 사라져 Idle로 복귀합니다. 최신 `push-preview.json`은 밀치기와 Kick 12프레임입니다. `CombatAssetBuilder.CaptureRuntime()`는 시작 화면을 건너뛰고 실제 모션을 캡처합니다.
- 보존: 8개 게임 씬·이동 프로필 2개·SourceMotor·기존 클립 46개, 총 **57개 해시 일치**. F 클립 2개와 MovementLab의 발 표시 추가만 승인된 변경입니다(`kick-cooldown-preservation.json`). 프리팹/씬 비관련 문서와 보호 에셋 99개 보존은 `kick-motion-migration.json`에 기록했습니다. 임시 스모크 실행기는 제거했습니다.
- 미완료/제한: 인터넷 Relay/인위적 지연과 사람끼리의 체감 밸런스는 이번에 검증하지 않았습니다. 이번 작업에서 Git 커밋/푸시는 하지 않았습니다.

### 앞선 밀치기 전환 — 양손 F 모션은 이후 발차기로 복원 · 2026-11-02

- 좌클릭은 좌우 한 손 교대, F는 양손과 상체를 내미는 강한 밀치기입니다. 키 설정의 기존 숫자 슬롯과 F 바인딩은 유지합니다. `MeleeAttack.PushLeft/PushRight/StrongPush`, `TryPush/TryStrongPush`, 네트워크·AI·연습실·HUD·진단 입력을 함께 전환했습니다.
- 빠른 밀치기: 전체 0.55초, 접촉 0.12~0.20초, 수평 추가 속도 3.5m/s. 강한 밀치기: 전체 1.1초, 접촉 0.32~0.42초, 수평 9m/s. 수직 추가 속도는 0이며 실제 평지 정지 상대 이동량은 약 0.29m/0.99m입니다. 지형·기존 속도에 따라 달라지므로 고정 거리로 취급하지 않습니다.
- `MeleeReceiver`는 호스트가 승인한 피격부터 0.65초 보호합니다. 무시한 타격은 보호 시간을 갱신하거나 충격/모션/소리를 추가하지 않습니다. 시야 가림·카메라 회전·입력 중단·공격/예약 취소를 제거해 즉시 이동·점프·웅크리기·반격이 가능합니다. 참가자는 승인된 연출과 모터 스냅샷만 받고 충격을 중복 적용하지 않습니다.
- `CombatAssetBuilder.InstallPushMotions()`로 전신/손 공격 클립 6개만 재생성하고 GUID를 보존했습니다. `MovementLab.unity`와 `SourcePlayer.prefab`의 폐기한 1인칭 다리 모델을 제거했으며, 9개 씬과 플레이어 프리팹의 예전 피격 필드는 YAML의 해당 필드만 삭제했습니다. 지형을 다시 만들거나 씬 전체를 재직렬화하지 않았습니다. 기존 WAV를 새 밀치기 음원 필드에서 재사용합니다.
- 검증: 저장 씬 Windows 빌드 성공(`Logs/push-player.log`), 실제 플레이 모드 `CombatValidation`/`ReactionValidation` **35/35**(`TestResults/push-physics.json`), 실제 호스트·참가자 `check-push.mjs` **28/28**(`TestResults/push-network.json`). 양쪽 공격, 더 먼 강한 밀림, 수직 띄움/중복 충격 없음, 보호 중 입력과 만료, 사거리·뒤쪽·착석·복귀 차단을 확인했습니다.
- 보존: 기존 비공격 클립 42개·프로필 2개·SourceMotor 45개 해시 일치. 나머지 8개 공식/사용자 씬은 폐기 피격 필드 10개씩만 삭제됐으며 다른 문서는 보존했습니다(`TestResults/push-preservation.json`). 공격 GUID 6개, 보호 에셋 90개와 전체 직렬화 정리 상세는 `push-motion-migration.json`.
- 화면: 실제 런타임 카메라의 왼손/오른손/양손 접촉·복귀를 1인칭/전신 각각 캡처했습니다(`Previews/Push_*.png`, `TestResults/push-preview.json`, 12프레임 모두 접촉 구간/Idle 복귀 일치). Windows 조작키 화면에서도 ‘밀치기 / 카드 공개’, ‘강하게 밀치기’, 기존 LMB/F 바인딩을 확인했습니다(`Tools/Network/Runs/push-ui-1793600667155/push-controls.png`). 임시 실행기는 제거했습니다.
- 에디터 로그에는 Unity 자체 `UnityEditor.Search.SearchDatabase` 인덱스 예외가 있었지만 물리 검사와 캡처는 정상 완료됐습니다. 실제 두 Windows 플레이어 로그에는 런타임 예외가 없었습니다.
- 미완료/제한: 사람끼리의 밀치기 체감 밸런스와 인터넷 Relay/인위적 지연은 이번에 검증하지 않았습니다. 이전 레이스·유즈맵 전용 검사는 재실행하지 않았으며 과거 결과를 아래에 구분해 둡니다. 이번 밀치기 작업에서 Git 커밋/푸시는 하지 않았습니다.

### 앞선 1인칭·공중 제작·걷기 전환 · 2026-11-02

- 기본 1인칭 제작: Tab/맵 클릭으로 커서 잠금, Tab/Esc/메뉴/포커스 이탈로 해제. WASD+마우스, 노클립 Space/Ctrl 상승·하강, 비행 속도 3/8/18m/s. 우클릭 조준 부품 선택 후 설정 패널, Delete 조준 대상만 삭제. 전체 보기는 기존 직교 회전/팬이며 1인칭 위치와 회전을 별도 보존합니다.
- `UserMapStudio.FirstPerson.cs`: V로 비행/걷기 전환. IgnoreRaycast 레이어의 편집 전용 CharacterController/SourceMotor가 실제 로컬 플레이어의 프로필을 참조합니다. 실제 모터 고정 틱으로 걷기·점프·앉기를 처리하고, 몸이 솔리드와 겹치면 전환을 거절합니다. 커서 해제 시 입력/속도 정지, 추락 시 출발점 복귀, F8/씬 전환/종료 시 편집 폰 제거. 참가자·종·인증 이벤트와 연결하지 않았습니다.
- Q 공중 배치는 레이 거리 2~60m(기본 8m), 휠 한 칸 2m, 모든 축 0.5m 스냅입니다. 면 충돌로 모드를 자동 변경하지 않으며 배치 원점/바닥 기준을 보존합니다. 점선 바닥 안내는 실제 지면 또는 명시적 Y0 기준을 표시합니다. 미리보기와 실제 배치가 같은 검증/좌표를 사용합니다.
- 기존 `UserMapStudio.Clearance.cs.LateUpdate`에서 편집 카메라 보간을 호출해 Unity 메시지 중복을 제거했습니다. F8 인증 조건은 바꾸지 않았습니다. 편집 걷기로 종에 도달해도 인증할 참가자/완주 이벤트가 없습니다.
- 검증: 저장 씬 Windows 빌드 성공(`Logs/first-person-player.log`), `check-first-person-builder.mjs` **13/13씩**(1440×900,1280×720), 실제 OS 입력 **16/16**(`TestResults/first-person-native.json`): 잠금/해제, 비행·Q/휠·설치·우클릭 선택·Delete/복구, V 걷기·실제 점프/착지·재비행, 벽 내부 전환 거절, 걷기에서 F8 진입/복귀. 기존 전체 보기 `check-easy-builder.mjs` 1440×900 **24/24**, 실제 제작자 클리어·저장 `check-creator-clear.mjs` **37/37** 재실행.
- 실제 화면: `Previews/UserMap_FirstPersonAir.png`, `UserMap_FirstPersonWalking.png`, `UserMap_FirstPersonProperties.png`. 9개 씬·프로필 2개·SourceMotor 총 12개 해시 보존(`TestResults/first-person-preservation.json`). 빌드는 씬을 재생성하지 않았습니다.
- 미완료/제한: 초등학생 대상 사용성 평가와 이번 변경의 인터넷 Relay 재검증은 하지 않았습니다. 움직이는 장치는 편집 상태에서 정지하므로 최종 타이밍은 F8로 확인해야 합니다. 이번 작업에서 Git 커밋/푸시는 수행하지 않았습니다.

### 앞선 쉬운 맵 만들기 · 2026-11-02

- 기본 UI는 아래 큰 6개 부품 슬롯과 선택/짓기/지우개·크기 3단계·90도 회전입니다. 전체 11종 목록은 모든 부품 안에, 수치·축 손잡이는 자세히 안에 숨깁니다. 색/장치 패널도 필요할 때만 엽니다.
- `UserMapStudio.EasyBuild.cs`: 기본 블록 면 붙이기·위로 쌓기, 회전된 크기로 접촉면 계산, 미리보기와 실제 배치의 동일 검증, 명시적 지우개. 크기 변경은 밑면 높이를 보존합니다. 바닥 기준 장치는 옆면 배치를 거절하며 잘못된 위치를 자동으로 다른 곳에 옮기지 않습니다. 출발 데크·마지막 종·ItemBox 고정 크기와 실행 취소를 유지합니다.
- `UserMapStudio.UI.cs`: 화면 방향에 맞춘 이동 버튼, 이동 발판의 월드 축 방향·거리(2/4/8)·빠르기(주기 8/4/2초) 버튼. 스크롤 부품 아이콘은 GUI 행렬 회전 대신 캐시한 선 텍스처를 사용해 클리핑 왜곡을 제거했습니다. 선택 크기는 실제 부품 크기와 일치할 때만 강조합니다.
- 기본 카메라는 사선으로 시작하지만 우클릭 드래그로 자유 회전합니다. 가운데 버튼/빈 바닥 드래그로 팬, 휠 줌, F/더블클릭/중심 되찾기/전체 맵으로 복구합니다. 위/사선/옆은 선택 사항입니다. 고급 비행과 구분하며, 카메라만 조작하면 맵 해시/인증이 변하지 않습니다.
- 실제 Windows 검증: `Tools/Network/check-easy-builder.mjs`를 1440×900·1280×720에서 각각 **24/24**, OS 마우스 회전·팬·줌과 실제 이동 발판 방향/거리/속도 버튼 **4/4**, 기존 `check-creator-clear.mjs` **37/37**, 기존 저장 데이터 검증 **14/14**. 저장 씬 빌드 성공(`Logs/easy-builder-player.log`). 원시 기록은 각 `TestResults/easy-builder-*.json`, `usermap-creator-clear.json`의 folder에 있습니다.
- 실제 화면: `Previews/UserMap_EasyBuilder.png`, `UserMap_EasyParts.png`, `UserMap_EasyPlatform.png`. 9개 씬·이동 프로필 2개·`SourceMotor.cs` 총 12개 해시 보존(`TestResults/easy-builder-preservation.json`). 씬/템플릿을 재생성하지 않았습니다.
- 미완료/제한: 초등학생 대상 실제 사용성 평가와 인터넷 Relay 재검증은 하지 않았습니다. 이번 변경은 편집기/공유 검증 함수만 변경하며 전송 형식과 클리어 인증 정책은 그대로입니다. 이번 작업에서 Git 커밋/푸시는 수행하지 않았습니다.

### 앞선 아이템 변경 · 2026-11-02

- 최종 enum은 `None=0, Missile=1, Banana=2, Barricade=3`입니다. 레이저·실드·굴리는 공의 획득/효과/AI/네트워크 필드/시각 자원/HUD를 제거했습니다. 선두는 바나나·바리게이트 각 50%, 추격자는 미사일 90%·바나나 10%입니다.
- 미사일은 `SourceMotor.ApplyRaceLaunch(11.5f, 1.4f)`로 기존 속도를 초기화하고 수직 발사합니다. `RaceLaunchRemaining`을 예측/복원 상태에 포함해 체공 중에도 수평 이동을 차단합니다. 실측 최고 발 높이는 약 3.3m였으며 몸만 회전하고 카메라는 뒤집지 않습니다. 바나나 미끄러짐은 유지했습니다.
- `RaceCourse.Barricades.cs`: 시선 뒤 3m, 연결된 현지 길 너비 약 1/3, 높이 2.2m/두께 .6m/8초. 시각과 분리한 8개 BoxCollider 풀을 호스트 생성·클라이언트 수신 즉시 동기화합니다. 만료·설치자 복귀·착석·라운드·맵 전환 시 충돌도 제거합니다. 빠른 회전 직후 사용은 오래된 참가자 자세가 아니라 요청의 조준 방향을 사용합니다.
- 설치는 평탄한 지면과 통로/인원/장애물 여유를 검사하며 실패하면 소비하지 않습니다. 좌우 .25m 간격으로 연결 지면을 측정하고 양옆 우회를 남깁니다. 높이 변화, 작은 틈, 128m 안에서 가장자리를 찾을 수 없는 지형은 보수적으로 제한합니다.
- 최종 빌드 성공(`Logs/item-impact-player.log`). 실제 Windows 두 플레이어 `race-network.json` **27/27**: 미사일 달리기 속도 제거·체공 입력 차단·회복, 바리게이트 양쪽 충돌·우회·만료·잘못된 설치 보존·설치자 복귀 정리, 확률별 허용 아이템, 사람/AI 완주와 종 규칙을 확인했습니다. `usermap-items.json` **26/26**: 제작·솔로 클리어·파일 전달·510개 박스 자동 다운로드, 공유 맵 미사일과 90도 회전 바리게이트 충돌, 5인 시작을 확인했습니다.
- 원시 기록: `Tools/Network/Runs/race-1793584422336/`, `usermap-items-1793584373514/`. 대표 실제 화면: `Previews/ItemRace_Barricade.png`, `Previews/ItemRace_Somersault.png`. 9개 씬과 이동 프로필 2개 해시 보존: `TestResults/item-preservation.json`. 씬을 재생성하지 않았습니다.
- 미완료/제한: 인터넷 Relay·인위적 지연과 실제 여러 사람의 밸런스 평가는 미검증입니다. 이번 아이템 작업에서 Git 커밋/푸시는 수행하지 않았습니다. 아래 피라미드 38/38·26/26은 앞선 프로토콜 30 결과이며 이번에 재실행한 것이 아닙니다.

### 앞선 왕의 회랑 변경

앞선 요청은 너무 일직선인 왕의 회랑에 높낮이와 90도 전환을 추가하고 길이를 약 70%로 줄이며 상승 서프의 중앙 골을 없애는 것이었습니다. 당시 왕의 회랑만 재생성했고 다른 공식/사용자 맵 씬과 기본 이동 물리는 유지했습니다. 바닥 면적 테두리는 복구하지 않았습니다.

- 25개 발판은 실제 충돌체를 가진 **∧ 모양의 양면 삼각형**입니다. 가운데가 높고 양옆으로 내려갑니다. 마주 보는 벽 두 개로 만든 ∨ 모양이 아닙니다.
- 왼쪽 또는 오른쪽 면으로 완주할 수 있습니다. 방 진행 방향은 0→90→0→90→0도이며, 20×20m 체크포인트에서 네 번 직각 전환합니다. 서프 중 강제 면 전환이나 좌우 나선은 없습니다.
- 5개 방과 25개 구간을 유지했습니다. 서프 수평 길이 합계는 **644→450m(69.9%)**, 틈·착지대 연결을 포함한 진행 거리는 약 **619m**입니다. 내부 틈 2.2~3.2m, 높은 착지 +0.65~0.75m 여섯 곳/낮은 착지 −1.2~1.6m 열네 곳. 난도 표시는 3입니다.
- 4개 체크포인트의 완료 구간 수는 **4, 9, 14, 20**입니다. 25개 구간을 순서대로 완료해야 종을 누를 수 있습니다.
- `SurfCourse.Section.twoSided`가 양면을 구분합니다. 기존 **서프 수로는 11개 편측 면 그대로**이며 이번 변경 대상이 아니었습니다.
- 기본 이동 4.5m/s, 자동 버니합 꺼짐, 별도 스크립트 가속 없음. 기존 서프 이동 프로필을 유지했습니다.

### 단축·고저차·직각 전환 재설계 근거·검증

- 참고: [beginner](https://steamcommunity.com/sharedfiles/filedetails/?id=3070321829)의 Tier 1/7단계, [kitsune](https://steamcommunity.com/sharedfiles/filedetails/?id=3076153623)의 Tier 1/9단계, [utopia](https://steamcommunity.com/sharedfiles/filedetails?id=156054906)와 [boreas](https://steamcommunity.com/sharedfiles/filedetails/?id=3133346713)의 공개 램프 이미지, [mesa_aether](https://steamcommunity.com/sharedfiles/filedetails/?id=3125360522)의 분리된 착지 구역. 외부 맵을 직접 플레이하거나 원본 에셋을 복제하지 않았습니다.
- `PyramidRouteLayout`은 단조 하강 가속면과 단조 상승 도약면을 별도 발판으로 분리했습니다. 상승 높이식은 `rise*(0.1*t+0.9*t^4)`로 가운데 골 없이 올라갑니다. `PyramidCourse.Chamber`의 origin/heading/local bounds로 건축물·pit·장식·미리보기를 함께 회전합니다. `SurfCourse.Guide`, `SourceMotor`, 이동 프로필은 변경하지 않았습니다.
- 첫 실제 주행은 높은 착지 앞턱에 막혔습니다. 상승 끝의 접선이 충분하도록 곡률만 수정해 **38/38** 통과. 좌측 두 출발점과 우측 한 출발점에서 무낙하 완주 **55.680/56.085/55.065초**, CP 네 곳 모두 재출발 완주했습니다. 자동 조향의 물리 결과이지 초보자의 완주 시간 보장은 아닙니다.
- `Pyramid decoration`과 `Pyramid course cues`는 충돌체 0개. 기둥·석상·석문·벽화·화로·석관·가시를 장식으로 분리하고 문턱/중앙 나선축/가로막는 석문은 제거했습니다. 외벽·천장·낙하 pit/SurfDivider·실제 서프·착지대는 충돌 유지. 벽쪽 장식은 렌더 경계까지 바깥 3m 띠에 맞춰 도약 시야를 비웁니다.
- 금색 도약/청록 착지 표시는 방마다 긴 틈과 반대 고저차 틈에만 둡니다. 체크포인트에 작은 직각 방향 화살표를 추가했으며 바닥 면적 네모선은 없습니다. 기존 6종 VARCO 모델을 재사용했고 유료 생성은 없습니다.
- 2026-11-02: `pyramid-validation.json` **38/38**, `pyramid-network-loopback.json` **26/26**, Windows 빌드 성공. 실제 플레이어 시작/상승면/직각 전환/마지막 방과 전체 배치도·단면을 확인했습니다. 네트워크 CP/종 검증은 진단 위치·통과 기록 설정을 사용하며, 물리 완주는 별도의 실제 입력 조향 검사입니다.
- `pyramid-preservation.json`: 다른 8개 씬과 SurfMovement/DefaultMovement 프로필 해시 모두 동일. 공식 지형이 바뀌므로 구버전과 섞이지 않게 프로토콜 **30**으로 올렸습니다.
- 남은 검증: 실제 초보자 체감, 인터넷 Relay/인위적 지연. 이번 요청에서 Git 커밋/푸시는 수행하지 않았습니다. 실제 화면은 `Tools/Network/Runs/pyramid-compact-1793581869007/`, 보존한 대표 화면은 `Previews/Pyramid_Runtime_Launch.png`, 전체 배치는 `Previews/Pyramid_Overview.png`입니다.
- 에디터 배치 실행에는 Unity SearchDatabase 인덱싱의 `ArgumentOutOfRangeException`이 기록됐습니다. 이후 주행 검증과 빌드는 정상 완료했고 실제 두 플레이어 로그에는 런타임 예외가 없습니다. Unity 검색 인덱스 문제는 별도 점검 대상으로 남깁니다.


## 사용자 맵 스튜디오 · 현재 동작

- 시작 화면 **맵 만들기** → 편집 → **F8 클리어 검증** → 혼자 출발점부터 도착 종을 E로 울리기 → F8 편집 복귀 → 저장 → 홈 → 방의 **사용자 맵** 선택으로 플레이합니다. 검증 중 AI/카드/체크포인트를 끄며 낙하/R은 중앙 출발점으로 돌아갑니다. 초안과 편집 카메라 위치는 유지합니다.
- 11종 부품: Block, Ramp, Surf, Checkpoint, MovingPlatform, Conveyor, Press, Hazard, Bell, Waypoint, ItemBox. 출발 책상/자리/규칙은 보호합니다. 배치·선택·축 드래그·수치 변환·장치 설정·복제·삭제·64단계 실행 취소/다시 실행을 제공합니다.
- 기본 1인칭은 WASD/마우스·V 비행/걷기·Tab 커서 전환을 사용합니다. 전체 보기는 기존 사선 직교 카메라에서 우클릭 드래그 회전, 가운데/빈 바닥 드래그 팬, 휠 줌입니다. 위/사선/옆은 선택 사항이며 전체/선택/출발/종 버튼과 별도 고급 비행을 유지합니다. 공중 배치 중 휠은 거리를 조절합니다. 카메라만 조작하면 맵 데이터와 인증은 바뀌지 않습니다.
- `UserMapStudio.Camera.cs`가 편집 뷰포트의 실제 픽셀 영역에 맞춰 직교 카메라를 배치하고 회전된 부품/이동 발판 경로를 포함해 화면을 맞춥니다. F8 왕복에서 중심·배율·회전을 유지합니다. 편집 카메라 안개는 해당 카메라 렌더 전후로만 비활성화/복원합니다.
- 아래 큰 6개 부품 슬롯을 기본으로 표시하며 나머지는 모든 부품 메뉴에서 고릅니다. 목록은 선택/버튼/Esc/맵 클릭으로 닫히며 닫는 맵 클릭은 소비해 오배치를 막습니다. 닫힌 동안 패널 폭은 맵에 반환합니다. 색/장치·고급 수치 패널은 필요할 때만 엽니다.
- 과거 햄버거 메뉴 8항목 검증은 이전 배치 기록입니다. 2026-11-02 하단 슬롯/패널 배치로 교체한 화면은 1440×900·1280×720에서 실제 플레이어로 확인했습니다.
- **내보내기/가져오기**는 Windows 파일 선택 창을 사용합니다. `.bellmap` UTF-8 JSON 한 파일에 모든 배치 데이터를 저장합니다. 가져오기는 새 GUID로 복사하여 기존 내 맵을 덮어쓰지 않습니다. 맵은 `Application.persistentDataPath/UserMaps`에 저장하며 게임 설치 폴더와 분리합니다.
- 제작 초안은 클리어 전 저장·내보내기·방 선택을 차단합니다. 수정/실행 취소/다시 실행은 현재 편집 인증을 해제합니다. 파일 가져오기는 원본을 받은 맵 보관함에 저장하고 별도 GUID의 편집 초안을 엽니다. 받은 원본은 재클리어 없이 방 선택·원본 내보내기가 가능하며, 편집 사본은 직접 클리어해야 저장됩니다. 원본 수신은 솔로 클리어 인증을 만들지 않습니다.
- `UserMapClearance.cs`는 맵 ID와 정규화 JSON SHA256에 묶인 로컬 기록을 `persistentDataPath/UserMapClearance`에 원자적으로 저장합니다. 실제 솔로 종 완료 이벤트에서만 기록하며 휴대 파일/네트워크 스키마에는 인증을 넣지 않습니다. 재실행 후 같은 저장 맵 인증은 유지됩니다. 로컬 파일/수정된 실행 파일에 대한 안티치트나 서버 공인 인증은 아닙니다.
- 스키마 1, 최대 512개/524288바이트. 이름 60자, 설명 1000자. 위치 X±500/Y−100~300/Z0~1000, 이동 경로와 회전 후 경계도 검증합니다. 크기 0.1~100m(Y≤60), 낙하 높이 −200~−2. 종은 정확히 하나이며 크기 0.5~2. 시작 데크의 윗면은 Y=0/Z≤0입니다.
- 지형 position은 중심, Bell/Press/ItemBox는 바닥 기준, Waypoint는 플레이어 발 위치입니다. Checkpoint/MovingPlatform/Conveyor/Press/Bell/Waypoint/ItemBox는 Y축 회전만 허용합니다. ItemBox는 단위 scale 고정입니다. 이동 발판 travel은 월드 축 진폭, period는 초, 컨베이어 speed는 로컬 +Z 방향 m/s입니다.
- 외부 코드·에셋 로딩은 없습니다. 엄격한 스키마/유한 수치/고유 ID/크기 검사, SHA256, 원자적 저장을 사용합니다. `UserMapDownloads.cs`는 받은 원본을 `persistentDataPath/DownloadedMaps/<canonical-sha256>.bellmap`에 보관하며 제작 맵을 덮어쓰지 않습니다. 같은 ID/이름도 내용이 다르면 별도 버전입니다. 로비는 내 완주 맵과 받은 원본을 병합 표시하고 해시로 선택을 구분합니다.
- 호스트는 선택 revision/hash를 고정하고 1KiB 조각을 전송합니다. 참가자는 검증된 수신 파일을 보관한 뒤 ACK합니다. 정확한 캐시가 있으면 내용을 다시 받지 않고 ACK하며, 호스트는 초기 캐시 ACK와 전송 큐의 순서 차이를 처리합니다. 맵 변경 시 준비를 해제하고 전송을 무효화합니다. 현재 프로토콜은 발차기 쿨타임 동기화 후 **33**이며 같은 최신 빌드를 사용해야 합니다.
- 구현: `Runtime/UserMapData.cs`, `UserMapStorage.cs`, `UserMapRuntime.cs`, `UserMapStudio*.cs`, `CourseNetwork.UserMaps.cs`. 일반 로스터·체크포인트·전투·카드 판정을 재사용합니다. 움직이는 장치는 공유 서버 시각을 사용합니다.
- 전용 씬: `Demo/UserMapCourse.unity`. `Editor/UserMapTemplateBuilder.Build()`는 저장된 첫걸음 씬의 복사본에서 코스 지형을 제거하고 원래 책상·캐릭터·자체 모션·종·소리·프로필을 보존합니다. 공식 씬을 다시 만들지 않습니다. `CourseMapsBuilder.BuildScenePaths()`에 전용 씬을 포함했습니다.
- 저장·인증 최신 재검증 **2026-11-02**: `TestResults/easy-builder-storage.json` **14/14**, `usermap-creator-clear.json` 실제 플레이어 **37/37**. 클리어 전 저장/내보내기 차단, 실제 이동·낙하·R 재시작, CP 무효화, 종 접근만으로 인증되지 않음, 실제 종 완료 후 저장, 수정/실행 취소 후 재검증, 파일 가져오기 격리, 프로세스 재실행 후 인증 유지를 확인했습니다. `usermap-storage-build.json` 15항목은 이전 결과입니다.
- 최신 네트워크: `TestResults/usermap-network-loopback.json` 27항목 통과. 미검증 호스트 맵 거절, 인증된 맵 자동 전달, 동일 스냅샷 로딩, 실제 대결 CP/종 동작, 큰 맵 전송 취소/재선택 및 공식 맵 복귀를 확인했습니다. 이전 스튜디오 18항목/Unity Relay 26항목 결과는 인증 기능 추가 전 기록이며 이번에는 Relay를 다시 실행하지 않았습니다.
- 다운로드 보관 추가 검증: `TestResults/usermap-downloads.json` 27항목 통과. 실제 파일 가져오기 후 수신자 방 생성, 캐시 없는 참가자 다운로드·영구 보관·실제 경기, 프로세스 재시작 후 재호스팅, 수정본 거절, 다음 참가자의 캐시 재사용과 실제 경기를 확인했습니다. 로비 화면도 캡처했습니다. 제작자 37항목/네트워크 27항목을 최신 빌드에서 재검증했습니다. 기존 네트워크 테스트의 일시적인 두 프로세스 보고 시점 차이 조건은 실제 다운로드 미완료/준비 차단 조건으로 교체했습니다.
- 카메라 변경 검증: `TestResults/usermap-camera-1440.json`, `usermap-camera-1280.json` 각각 실제 Windows 플레이어 24항목 통과. 이동 정지·줌 한계·시점·목적지·먼 부품·클릭/더블클릭 공통 처리 경로·데이터 불변·고급 모드·F8 복귀를 검사했습니다. 1440×900/1280×720 캡처에서 버튼과 편집 뷰포트를 확인했습니다. 입력은 플레이어의 실제 카메라/선택 메서드를 진단 드라이버로 호출했으며 OS 마우스 이벤트 자동화는 하지 않았습니다. Windows 빌드 및 기존 저장 경계 검사도 통과했습니다.
- 재현: Unity batchmode `-executeMethod HalliGalli.EditorTools.UserMapVerification.Build -quit` 후 `node Tools/Network/check-creator-clear.mjs .`, 이어서 `node Tools/Network/check-usermaps.mjs .`, `node Tools/Network/check-map-downloads.mjs .`. 네트워크 검증은 앞선 실제 클리어 시나리오의 파일을 사용합니다. Node.js 또는 호환 Bun이 필요합니다. `check-usermaps.mjs . --relay`는 실제 Relay 서비스를 사용합니다.
- 제한: 공개 자료실·외부 모델·임의 스크립트·공동 편집은 범위 밖입니다. 솔로 클리어 검증은 멀티플레이 난이도나 AI 동선의 보증이 아닙니다. 복잡한 AI 동선은 제작자가 경로점을 지정하고 실제 방에서 확인해야 합니다. Windows 파일 창의 직접 클릭 검증은 이전 작업에서 사용자 마우스 입력과 겹쳐 중단했으며 파일 저장/불러오기 동작은 실제 플레이어 API로 검증했습니다.

### 사용자 맵 아이템 박스 · 2026-10-30

- `UserMapPart.ItemBox=10`을 기존 enum 뒤에 추가했습니다. 스키마 1과 이전 부품 번호/맵 파일을 유지합니다. 위치는 바닥 앵커, scale은 정확히 `(1,1,1)`, 수평 회전만 허용합니다. 유효 영역은 애니메이션까지 포함한 중심 `(0,.8,0)`, 크기 `(1.28,1.6,1.28)` 경계로 검사합니다.
- 부품 메뉴 아래에서 선택하며 이동/수평 회전/색 변경·복제·삭제·실행 취소를 지원합니다. 확대/축소와 기울이기는 잠급니다. 충돌체 없이 렌더러 경계로 클릭 선택하되 더 가까운 지형에 가려진 박스는 선택하지 않습니다.
- `UserMapRuntime`이 박스가 있는 맵에만 `RaceCourse`를 구성합니다. 편집 중에는 박스만 정적으로 보이고, 실제 플레이/솔로 검증 때 미사일·바나나·바리게이트 3종을 사용합니다. 생성물 교체/비활성화는 이벤트 구독·효과·Active를 정리합니다. 사용자 맵 순위는 종까지 직선거리, HUD 표기는 ‘종 거리’; 공식 레이스는 기존 +Z 순위입니다. 아이템 확률과 쿨다운은 동일합니다.
- 네트워크 버퍼는 헤더·최대 6명·40개 엔티티와 실제 박스 타이머 수에 맞게 계산합니다. 512개 부품 제한 안의 510개 박스+바닥+종 맵도 지원합니다. 최초 부품 추가는 프로토콜 28, 3종 아이템/체공 제동 상태는 31에서 도입했으며 현재 프로토콜은 **33**입니다.
- 이전 검증 **2026-10-30, 프로토콜 28**: Windows 저장 씬 빌드 성공. 실제 플레이어 `check-usermap-items.mjs` **23/23**, `check-race.mjs` **26/26**, `check-creator-clear.mjs` **37/37** 통과. 최신 3종 아이템 검증은 문서 위쪽의 2026-11-02 결과를 사용하세요.
- 실제 편집 UI·솔로 HUD·5인 카드 화면 캡처: `Tools/Network/Runs/usermap-items-1793349499613/`. 대표 이미지 `Previews/UserMap_ItemBox.png`, `Previews/ItemRace_FivePlayerTable.png`. 입력은 진단 드라이버의 실제 게임 경로이며 OS 마우스/키보드 자동화가 아닙니다. Relay/인위적 지연/다운로드 재호스팅 전용 시나리오는 이번에 재실행하지 않았습니다.


### 시인성·부품 외형·바닥 표시 정리

- 부품 메뉴 위치는 두 번째 줄을 유지하고 금색 채움/굵은 아이콘으로 강조했습니다. 안내문·속성 제목·입력 테두리·선택 색상 대비를 높였습니다. `BellUI.Text`는 Unity GUI 비활성 색상 상속을 차단하고 원래 상태를 복구합니다. 실제 비활성 버튼은 계속 동작하지 않습니다.
- `UserMapRuntime.Visuals.cs`와 `Resources/UserMapSurface.shader`, `UserMapSurfaceTemplate.mat`가 콘크리트·금속·고무 재질, 측면 패널/체결부, 컨베이어 리브/진행 화살표, 프레스 경고 띠/램프, 위험 구역 환기구/X 표시를 생성합니다. 원본 절차적 텍스처와 13개 공유 재질을 사용하며 외부 에셋 다운로드는 없습니다. 선택 색상은 장식 포인트에 적용합니다.
- 부품 크기/충돌체/서 있는 높이/이동 규칙은 그대로입니다. 체크포인트만 바닥 테두리를 유지합니다. 편집용 선택 상자와 손잡이는 플레이 중 바닥 표시가 아닙니다. 셰이더의 `worldNormal` 입력은 `WorldNormalVector` 계산에 필요하며 제거하면 플레이어에서 검게 렌더링될 수 있습니다.
- `Editor/FloorMarkingCleanup.Apply()`가 저장된 공식 씬에서 정확히 식별한 장식만 삭제합니다. 씬 전체 재생성은 하지 않습니다. 최초 249개 제거, 체크포인트 테두리 11개 보존, 충돌/서프/경로/체크포인트 불변 확인: `TestResults/floor-marking-initial-removal.json`. 재실행 보고서 `floor-marking-cleanup.json`은 제거 0으로 정상입니다. 빌더에서도 해당 장식 생성을 제거했고 `UserMapVerification.Build()`가 정리 후 사용자 맵 템플릿을 갱신합니다.
- 최종 Windows 빌드 성공 및 `TestResults/visual-quality-smoke.json` 11항목 통과. 실제 플레이어에서 10종 부품, 최소/긴 부품, 이동 발판 움직임, 착지, 공식 5종 맵을 확인했습니다. `Tools/Network/Runs/visual-quality-1793343399060/`에 실제 UI/부품/1인칭/공식 맵 캡처가 있습니다. 검사 입력은 진단 드라이버이며 OS 마우스 자동화는 아닙니다.
- 외형 변경 후 제작자 클리어 37항목, 루프백 네트워크 27항목 재통과. 이번 변경에서 Relay 및 다운로드 보관 전용 시나리오는 재실행하지 않았습니다. 임시 외형 검사/빌드 스크립트는 제거하고 보고서·캡처를 남겼습니다.

## 마지막 공식 맵 · 아이템 레이스

- `Demo/RaceCourse.unity`, 카탈로그 ID `item-race`, `CourseMapStyle.Race`. 220m 연속 평지, 폭 14m, 8줄×4개 아이템 박스, 기존 6석 책상/캐릭터/종을 사용합니다. 카드 공개·기립·종 보상·승패 규칙과 기본 이동 프로필은 유지합니다.
- `Editor/RaceCourseBuilder.Build()`는 저장된 첫걸음 씬의 사본으로 새 씬만 생성하며 기존 공식 씬을 재생성하지 않습니다. 그래픽이 켜진 Unity에서 실제 코스 썸네일 `Resources/UI/Maps/Race.png`도 렌더링합니다. `BuildPlayer()`는 저장된 씬을 Windows로 패키징하며 씬을 다시 만들지 않습니다.
- `RaceCourse.cs`가 호스트 권위로 박스 접촉·순위·획득·발사·명중을 판정합니다. 선두는 바나나/바리게이트 각 50%, 추격자는 미사일 90%/바나나 10%입니다. 한 칸 보관, 박스 8초 재등장, 미사일 사거리 45m/속도 21m/s/최대 5초, 바나나 최대 22초/설치자 보호 .85초. AI도 같은 박스를 획득하고 상황에 맞춰 사용합니다.
- 미사일은 기존 속도 초기화 후 수직 11.5m/s 발사·1.4초 수평 제동, 바나나는 1.1초 관성 미끄러짐입니다. `SourceMotor`의 아이템 전용 상태만 변경했으며 기본 이동 프로필은 유지했습니다. `RaceCourse.Visuals.cs`는 몸 메시만 회전하고 카메라·참가자 루트는 뒤집지 않습니다. 미사일/바나나/바리게이트 외형을 풀링합니다.
- `CourseNetwork.Race.cs`의 10Hz 호스트 스냅샷은 기존 자세 채널과 분리합니다. epoch/revision/round/respawn/itemSequence로 이전 라운드·생명의 사용 요청을 차단합니다. 현재 프로토콜 **33**은 바리게이트 크기와 미사일 체공 제동 상태를 포함합니다. 착석·복귀·종·맵 전환 시 아이템/효과/엔티티/충돌체를 정리합니다.
- 기본 사용 키 **C**, 기존 V 전신 보기 유지. `CourseHUD.Race.cs`에 순위·남은 거리·아이템·락온을 표시합니다. 기존 12행 조작키 저장은 보존해 새 행을 추가하고, C를 이미 사용 중이면 기존 설정을 빼앗지 않고 아이템 키를 미지정으로 둡니다.
- 재현: Unity batchmode에서 `-executeMethod HalliGalli.EditorTools.RaceCourseBuilder.Build`(새 맵 생성이 필요할 때만), 이후 `-executeMethod HalliGalli.EditorTools.RaceCourseBuilder.BuildPlayer`; `node Tools/Network/check-race.mjs .`로 실제 두 플레이어를 실행합니다. 진단 시 박스의 실제 접촉으로 획득하며 아이템을 강제로 지급하지 않습니다.
- 최초 구현 검증 **2026-10-30, 프로토콜 26**: `TestResults/race-build.json` Windows 빌드/조작키 보존/키 충돌 처리 통과. 당시 레이스 18/18, 제작자 클리어 37/37, 사용자 맵 공유 27/27이었습니다.
- 실제 화면과 입력 검증은 진단 드라이버의 게임 경로이며 OS 키보드·마우스 이벤트 자동화는 아닙니다. 최신 미사일과 바리게이트 캡처는 문서 위쪽에 기록했습니다. Relay/인위적 지연은 최신 변경에서 재검증하지 않았습니다.
- 바리게이트는 `RaceCourse.Barricades.cs`에서 연결된 지면의 횡단 폭을 측정하고 평탄한 뒤쪽 공간에 설치합니다. 설치 위치에 다른 참가자가 있거나 벽·낭떠러지·폭 부족으로 우회를 확보하지 못하면 아이템을 유지합니다. 공식 14m 트랙에서 측정된 바리게이트 폭은 약 4.63m입니다.
- 폐기한 5종 구현의 과거 검증 **2026-10-30, 프로토콜 27**은 26/26이었습니다. 당시 공·레이저·방어막 동작은 현재 규칙이 아닙니다. 현재 `TestResults/race-network.json`은 프로토콜 31의 27/27 결과입니다.
- `Previews/ItemRace_RollingBall.png`, `Previews/ItemRace_Laser.png`는 제거한 아이템의 과거 이미지입니다. 현재 대표 화면은 `Previews/ItemRace_Barricade.png`입니다.
- **5인 시작 테이블 가림 수정:** 테이블 자체는 존재했으나 `(0,4.8,-5)`의 도로 재질 장식 지붕이 높아진 카드 카메라 시야를 가렸습니다. 저장된 `RaceCourse.unity`와 빌더에서 지붕/장식 기둥 2개만 제거했습니다. 테이블·좌석·시작 데크·트랙·물리·카메라는 유지합니다. 프로토콜 28 실제 사람 2명+AI 3명 경기에서 Reveal 후 테이블 전체와 카드가 보이는 캡처를 확인했습니다. 같은 빌드의 기존 레이스 26항목도 통과했습니다.

## 확정된 게임 규칙과 의도

- 1인칭이며 손이 보입니다. 사람+AI 합계 최대 6명, 실제 참가자 수로 시작합니다. 빈자리를 자동으로 AI로 채우지 않습니다.
- 모두 앉았을 때 자기 차례 좌클릭으로 카드 공개. 카드 시점은 책상 위로 전환되고 공개 방향 표시가 있습니다. 의자는 일렬, 카드는 원형입니다. 비공개 덱 두께로 대략적인 장수를 보여줍니다.
- 정확히 5개인지는 플레이어가 판단합니다. E로 언제든 일어날 수 있고, 누군가 일어나면 카드 공개를 멈춥니다.
- 잘못 일어나면 다른 생존 참가자에게 카드 페널티를 지급하고 자기 자리에서 직접 다시 앉아야 합니다. 자동으로 정답을 알려주는 UI를 넣지 않습니다.
- 종을 누르면 공개 더미를 획득하고 모두 즉시 착석합니다. 되돌아 달리는 구간은 없습니다. 종 한 번으로 대결이 끝나지 않습니다.
- 1대1은 비공개 카드가 바닥나면 패배합니다. 3인 이상으로 시작한 대결만 마지막 카드 공개 후 공개 더미를 잃기 전까지 참여하는 규칙입니다.
- 승자 닉네임, 승자 배너, 짧은 승리 효과음 → 잠깐 표시 후 모두 자동으로 대기방 복귀.
- Shift 달리기는 제거됐고 걷기 속도가 기본입니다. Space/휠점프, 공중 스트레이프, 점프 앉기를 지원합니다.
- 좌클릭은 좌우 한 손 교대 밀치기(수평 4.5m/s), F는 오른발 발차기(22.5m/s)이며 시작부터 2초 쿨타임입니다. 수평으로만 밀며 시야 가림·입력 중단은 없습니다. .65초 피격 보호 중에도 반격할 수 있습니다. 오른쪽 아래에 좌클릭/F/Q를 표시합니다. 마비총은 제거했습니다.
- Q 밧줄은 직선 투척, 재입력 당기기, 횟수 제한 없이 5초 쿨타임입니다. 기본 당김은 시전자 쪽, **당기는 순간** 마우스를 휘두르면 그 방향으로 던집니다. 현재 PullSpeed=16, PullLift=3.5이며 과거의 과도한 힘으로 되돌리지 마세요.
- 밧줄에 걸린 당사자는 조준점 주변 X 표시와 소리로 알아챕니다. R은 최근 체크포인트 복귀입니다.
- 정상 쟁탈은 하나의 봉우리를 돌아 오르는 공통 코스입니다. EASY/HARD 분리는 폐기했고, 추월 가능한 발판과 일부 숏컷을 둡니다. 코스 예상 소요 시간은 UI에 표시하지 않습니다.
- 첫걸음 마지막 두 운반 발판은 긴 이동 구간과 엇박. 폐공장 프레스에 깔리면 복귀. 서프 수로의 스킵 방지 칸막이는 전기 위험 표시와 접촉 복귀가 있습니다.
- 현실적인 대머리 캐릭터와 손, 직접 만든 모션, 과한 설명/네모 패널을 줄인 통일된 UI를 유지합니다.

## 코드와 에셋 위치

| 작업 영역 | 시작 파일/폴더 |
| --- | --- |
| 시작 씬 | `Assets/HalliGalliMovement/Demo/BellRunCourse.unity` |
| 왕의 회랑 씬 | `Assets/HalliGalliMovement/Demo/PyramidSurfCourse.unity` |
| 피라미드 동선 | `Editor/PyramidRouteLayout.cs` |
| 씬·배치·미리보기 | `Editor/PyramidSurfBuilder.cs`, `PyramidArtBuilder.cs`, `PyramidCourseObstacles.cs` |
| 삼각형과 기존 서프 메시 | `Editor/SurfCourseBuilder.cs`의 `BuildRamp`, `BuildTriangle` |
| 진행 판정·AI 조향 | `Runtime/SurfCourse.cs`, `CourseRival.cs` |
| 이동·입력 | `Runtime/SourceMotor.cs`, `SourcePlayerController.cs`, `ControlBindings.cs` |
| 카드 규칙 | `Assets/HalliGalliCards/Runtime/` 및 `Runtime/CourseCardGame.cs` |
| 인원·좌석·체크포인트 | `Runtime/CourseRoster.cs`, `CourseSession.cs`, `CourseCheckpoints.cs` |
| 방·네트워크 | `Runtime/CourseNetwork*.cs` |
| 밧줄·근접 전투 | `Runtime/LassoTool.cs`, `LassoVisual.cs`, `LassoAudio.cs`, `MeleeCombat.cs`, `MeleeReceiver.cs` |
| UI | `Runtime/CourseHUD*.cs` |
| 검사 실행기 | `Assets/HalliGalliMovement/Tests/` |

위 표에서 `Editor/`, `Runtime/`는 `Assets/HalliGalliMovement/` 아래입니다. 생성된 씬만 바꾸면 나중에 빌더를 실행할 때 수정이 사라질 수 있으므로 해당 빌더도 함께 검토하세요. 모든 맵 재생성 버튼은 일상적인 실행에 필요하지 않습니다.

## 개발 환경과 온라인 연결

- Unity **6000.6.0f1**, Built-in Render Pipeline, Windows 64비트, Mono 빌드.
- 패키지: `com.unity.netcode.gameobjects` **2.13.3**, `com.unity.services.multiplayer` **2.4.1**. 실제 잠금 파일을 우선하세요.
- Unity Lobby + Relay, 방장 호스트 권위 방식입니다. 별도 상시 전용 서버는 없습니다. 포트포워딩이 필요 없는 인터넷 방/초대코드를 사용합니다.
- Unity Cloud 프로젝트 ID: `201afba2-da25-4265-8865-73b028280c20`, 조직 `alphamon1114`. 이 ID는 비밀 키가 아닙니다. 비밀번호/토큰은 저장소에 넣지 않습니다.
- 프로토콜 **33**. 다른 버전끼리 입장하지 못하도록 호환성 검사를 합니다.
- 방장이 나가면 방이 끝납니다. 이미 진행 중인 게임에는 새로 입장하지 못합니다.
- Unity 라이선스가 없다는 오류가 과거에 반복됐습니다. 그런 경우 Hub 로그인과 에디터 라이선스부터 확인합니다. 게임 소스 오류로 오인하지 마세요.

## 검증과 빌드

최신 전투 HUD·발차기 복원·쿨타임 검증은 **2026-11-02, 프로토콜 33**입니다. 같은 날 앞선 밀치기 전환은 프로토콜 32, 1인칭 제작·쉬운 맵 만들기·3종 아이템은 31, 왕의 회랑은 30에서 검증했습니다. 이전 Relay와 전체 배포 검증은 **2026-10-27**입니다.

| 검증 | 결과·날짜 | 기록 |
| --- | --- | --- |
| 발차기 2초 쿨타임·예약/취소·복귀·모션 실제 플레이 모드 | 40/40 · 2026-11-02 | `TestResults/push-cooldown-physics.json` |
| 실제 호스트/참가자 쿨타임·HUD·거절·좌클릭·복귀 | 80/80 · 2026-11-02 | `TestResults/push-cooldown.json` |
| 발차기 복원 에셋/GUID·기존 씬/클립 보존 | 성공·57개 해시 일치 · 2026-11-02 | `TestResults/kick-motion-migration.json`, `kick-cooldown-preservation.json` |
| 최신 Windows 저장 씬 빌드 | 성공 · 2026-11-02 | `Logs/kick-cooldown-player.log` |
| 밀치기 판정·연타 보호·조작·모션 복귀 실제 플레이 모드 | 35/35 · 2026-11-02 | `TestResults/push-physics.json` |
| 실제 호스트·참가자 양방향 밀치기·보호·입력·위치 동기화 | 28/28 · 2026-11-02 | `TestResults/push-network.json` |
| 이동 모션/프로필/모터 보존·폐기 필드만 정리 | 해시 45개 일치, 씬 8개 필드 외 보존 · 2026-11-02 | `TestResults/push-preservation.json` |
| 공격 클립 GUID·이동 에셋 보존, 다리 표시 모델 정리 | 성공 · 2026-11-02 | `TestResults/push-motion-migration.json` |
| 최신 밀치기/발차기 1인칭·전신 접촉/복귀 캡처 | 12프레임·발 표시/복귀 확인 · 2026-11-02 | `TestResults/push-preview.json`, `Previews/Push_*.png` |
| 앞선 양손 밀치기 Windows 저장 씬 빌드 | 성공 · 2026-11-02 | `Logs/push-player.log` |
| 1인칭 공중 배치·높이·경계·모드/인증 분리, 두 해상도 | 13/13씩 · 2026-11-02 | `TestResults/first-person-1440.json`, `first-person-1280.json` |
| 실제 키/마우스 제작·V 걷기·점프/착지·겹침 방지·F8 복귀 | 16/16 · 2026-11-02 | `TestResults/first-person-native.json` |
| 1인칭 변경 중 씬 9개·프로필 2개·SourceMotor 보존 | 12개 해시 일치 · 2026-11-02 | `TestResults/first-person-preservation.json` |
| 앞선 1인칭 제작 Windows 저장 씬 빌드 | 성공 · 2026-11-02 | `Logs/first-person-player.log` |
| 쉬운 전체 보기 제작·경계/보호·복구·자유 카메라 | 24/24 · 1440×900 최신, 1280×720 앞선 변경 · 2026-11-02 | `TestResults/easy-builder-1440.json`, `easy-builder-1280.json` |
| 실제 OS 마우스 회전/팬/줌·이동 발판 UI 버튼 | 4/4 · 2026-11-02 | `TestResults/easy-builder-pointer.json` |
| 솔로 클리어·저장·인증 취소·재실행 | 37/37 · 2026-11-02 | `TestResults/usermap-creator-clear.json` |
| 저장 데이터 검증 | 14/14 · 2026-11-02 | `TestResults/easy-builder-storage.json` |
| 편집기 변경 중 씬 9개·프로필 2개·SourceMotor 보존 | 12개 해시 일치 · 2026-11-02 | `TestResults/easy-builder-preservation.json` |
| 앞선 쉬운 편집기 Windows 저장 씬 빌드 | 성공 · 2026-11-02 | `Logs/easy-builder-player.log` |
| 최신 아이템 레이스 실제 두 플레이어·미사일 제동·바리게이트 충돌/정리 | 27/27 · 2026-11-02 | `TestResults/race-network.json` |
| 최신 사용자 맵 제작/공유·미사일·회전 바리게이트·5인 시작 | 26/26 · 2026-11-02 | `TestResults/usermap-items.json` |
| 아이템 작업 중 9개 씬·이동 프로필 2개 보존 | 해시 일치 · 2026-11-02 | `TestResults/item-preservation.json` |
| 앞선 아이템 Windows 저장 씬 빌드 | 성공 · 2026-11-02 | `Logs/item-impact-player.log` |
| 실제 물리·양면 완주·고저차·직각 전환·CP·낙하·스킵 방지·장식 비충돌 | 38/38 · 2026-11-02 | `TestResults/pyramid-validation.json` |
| 로컬 두 플레이어·맵·CP·종·재대기방·기존 서프 | 26/26 · 2026-11-02 | `TestResults/pyramid-network-loopback.json` |
| 다른 8개 씬·이동 프로필 2개 보존 | 해시 일치 · 2026-11-02 | `TestResults/pyramid-preservation.json` |
| Windows 저장 씬 빌드 | 성공 · 2026-11-02 | `Logs/pyramid-compact-player.log` |
| 이전 인터넷 Relay 검사 | 26/26 · 2026-10-27 | `TestResults/pyramid-network-validation.json` |

`windows-build.txt`, `windows-build-messages.txt`, `pyramid-release.json`은 이전 배포 기록입니다. 과거 `*-probe.json`과 현재 검증을 혼동하지 마세요. 이번 최종 플레이어 로그에 런타임 예외는 없었습니다.

프로젝트가 Unity에서 열린 상태에서 저장소 루트의 PowerShell로 실행할 수 있습니다.

```powershell
# 씬을 다시 생성하지 않고 현재 저장된 씬을 검사
./Tools/Editor-Job.ps1 pyramid-validate
# 미리보기만 갱신
./Tools/Editor-Job.ps1 pyramid-capture
# Windows 빌드와 unitypackage 내보내기
./Tools/Editor-Job.ps1 capture-build
```

`pyramid` 작업은 왕의 회랑을 빌더에서 다시 생성하므로 의도한 경우에만 사용하세요. `pyramid-probe`는 짧은 양면 완주 조사입니다. `CourseEditorJobs`는 프로젝트 **상위 폴더**의 `outputs/course-jobs/request.txt`를 읽습니다. 도우미가 이 위치를 계산합니다. `DISPATCHED`는 비동기 테스트 완료를 뜻하지 않습니다. `TestResults` 갱신 및 `PYRAMID_VALIDATION_RESULT 38/38` 같은 최종 로그를 확인하고, 실행 중에 다음 요청을 보내지 마세요. 다음 요청은 현재 Play Mode를 중단합니다.

`capture-build` 결과는 `Build/Windows/HalliGalliMovement.exe`와 `Exports/HalliGalliMovement.unitypackage`입니다. Unity Build Profiles로도 Windows 실행 파일을 만들 수 있습니다. 플레이 가능한 모든 씬은 `Editor/CourseMapsBuilder.cs`의 빌드 목록을 사용합니다.

실제 인터넷 재현 검사에는 최신 Windows 빌드, Node.js, 인터넷과 Unity Services 접근이 필요합니다.

```powershell
./Tools/Network/Test-PyramidRelay.ps1 -Relay
# Node가 PATH에 없을 때
./Tools/Network/Test-PyramidRelay.ps1 -Relay -NodePath 'C:/path/to/node.exe'
```

이 검사는 테스트용 게임 두 개를 숨김으로 열고 자기 프로세스만 종료합니다. 결과는 `TestResults/pyramid-network-validation.json`, 원시 기록은 무시되는 `Tools/Network/Runs/`에 씁니다. Relay 테스트는 실제 서비스를 사용하므로 필요할 때만 실행합니다.

## Git과 원래 작업 PC

- 저장소에 `Assets`와 `.meta`, `Packages`, `ProjectSettings`, `Previews`, 최종 검증 기록, 문서를 보존합니다. 바이너리는 Git LFS를 사용합니다.
- `Library`, `Temp`, `Logs`, `UserSettings`, `Build`, `Exports`, 자동 백업 씬, 테스트 원시 로그는 제외합니다. 파일을 로컬에서 삭제한 것이 아닙니다.
- 원래 작업 폴더: `C:/Users/mbc/.codex/.chatgpt-projects/g-p-6ad5650149248191884587ca1dbe7d19/HalliGalliDeathrun`.
- 원래 배포 ZIP은 그 상위 `outputs/source_movement/`에 있습니다. 이 폴더는 Git 저장소 밖이며 GitHub에 실행 파일을 Release로 올리는 작업은 이번 요청에 포함하지 않았습니다.
- 기존 `outputs/check-controls-compile.ps1`은 원래 PC의 Unity/Bee 경로에 의존하는 빠른 보조 검사입니다. 새 클론에서 그대로 실행하기보다 Unity 가져오기/컴파일 결과를 사용하세요.
- 상위 `sources/`는 읽기 전용 동기화 자료입니다. 이 저장소와 별개이며 수정하지 않습니다.

## 다음 세션 시작 순서

1. `git status`를 확인하고 기존 사용자 수정을 보존합니다. 새 클론이면 LFS 가져오기를 확인합니다.
2. 최신 사용자 요청을 확인합니다. 이 문서의 과거 결정은 새 사용자 지시로 바뀔 수 있습니다.
3. Unity에서 저장된 씬을 먼저 열고 현재 모습을 확인합니다. 문제를 보기 전에 전체 맵을 재생성하지 않습니다.
4. 변경된 영역에 맞는 검사와 필요한 빌드만 수행합니다. 완료한 작업과 남은 제한을 이 문서에 남깁니다.

## GitHub 등록 확인 · 2026-10-30

- 비공개 저장소 생성 및 `main` 업로드 완료. 최초 소스 커밋은 `fa84b375ba78cce06bcde7e04960cae5fff6dd94`입니다.
- 원래 작업 폴더의 `origin`을 새 저장소에 연결했습니다.
- 별도 빈 폴더에서 원격 저장소 복제, 모든 LFS 에셋 다운로드, 체크아웃 및 `git lfs fsck`가 성공했습니다. 검증용 임시 복제본은 검사 후 정리합니다.
- 새 인계 문서 링크와 PowerShell/JavaScript 도우미의 구문을 확인했습니다. 이번 업로드 과정에서 게임 소스·물리·맵을 변경하거나 게임 검사를 다시 실행하지 않았습니다.
