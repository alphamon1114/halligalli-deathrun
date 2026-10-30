# 다른 세션을 위한 작업 인계

기록일: **2026-10-30**. 마지막 게임 기능 변경과 플레이 검증은 **2026-10-27**입니다.

## 지금 이어받을 상태

사용자는 이 게임을 새 GitHub 저장소에 올리고, 다른 세션에서 이어갈 기록을 요청했습니다. 저장소 이름은 `alphamon1114/halligalli-deathrun`, 기본 브랜치는 `main`, 공개 범위는 비공개입니다. 현재 기능 요청으로 남아 있는 미완료 항목은 없습니다. 다음 변경은 사용자의 플레이 피드백을 기준으로 진행하세요.

최근 완료한 요청은 **왕의 회랑에서 좌측/우측 서프를 번갈아 타도록 강요하는 구조 제거**입니다.

- 25개 발판은 실제 충돌체를 가진 **∧ 모양의 양면 삼각형**입니다. 가운데가 높고 양옆으로 내려갑니다. 마주 보는 벽 두 개로 만든 ∨ 모양이 아닙니다.
- 왼쪽 또는 오른쪽을 선택해 동일한 면으로 끝까지 갈 수 있습니다. 틈 앞뒤 중심선이 정렬돼 있고 석문과 착지 발판은 두 면을 받습니다.
- 5개 방의 급강하, 180도 나선, 낙하, 공중 제단, 마지막 도약은 유지합니다. 총 활주 길이는 약 882.2m, 기존 서프의 약 2.62배입니다.
- 4개 체크포인트의 완료 구간 수는 **4, 9, 14, 20**입니다. 25개 구간을 순서대로 완료해야 종을 누를 수 있습니다.
- `SurfCourse.Section.twoSided`가 양면을 구분합니다. 기존 **서프 수로는 11개 편측 면 그대로**이며 이번 변경 대상이 아니었습니다.
- 기본 이동 4.5m/s, 자동 버니합 꺼짐, 별도 스크립트 가속 없음. 기존 서프 이동 프로필을 유지했습니다.

## 확정된 게임 규칙과 의도

- 1인칭이며 손이 보입니다. 사람+AI 합계 최대 6명, 실제 참가자 수로 시작합니다. 빈자리를 자동으로 AI로 채우지 않습니다.
- 모두 앉았을 때 자기 차례 좌클릭으로 카드 공개. 카드 시점은 책상 위로 전환되고 공개 방향 표시가 있습니다. 의자는 일렬, 카드는 원형입니다. 비공개 덱 두께로 대략적인 장수를 보여줍니다.
- 정확히 5개인지는 플레이어가 판단합니다. E로 언제든 일어날 수 있고, 누군가 일어나면 카드 공개를 멈춥니다.
- 잘못 일어나면 다른 생존 참가자에게 카드 페널티를 지급하고 자기 자리에서 직접 다시 앉아야 합니다. 자동으로 정답을 알려주는 UI를 넣지 않습니다.
- 종을 누르면 공개 더미를 획득하고 모두 즉시 착석합니다. 되돌아 달리는 구간은 없습니다. 종 한 번으로 대결이 끝나지 않습니다.
- 1대1은 비공개 카드가 바닥나면 패배합니다. 3인 이상으로 시작한 대결만 마지막 카드 공개 후 공개 더미를 잃기 전까지 참여하는 규칙입니다.
- 승자 닉네임, 승자 배너, 짧은 승리 효과음 → 잠깐 표시 후 모두 자동으로 대기방 복귀.
- Shift 달리기는 제거됐고 걷기 속도가 기본입니다. Space/휠점프, 공중 스트레이프, 점프 앉기를 지원합니다.
- 좌클릭은 양손 교대 펀치. 피격 효과는 시야 가림이며 이동을 막지 않습니다. F 발차기는 넉백입니다. 마비총은 제거했습니다.
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
- 프로토콜 **24**. 다른 버전끼리 입장하지 못하도록 호환성 검사를 합니다.
- 방장이 나가면 방이 끝납니다. 이미 진행 중인 게임에는 새로 입장하지 못합니다.
- Unity 라이선스가 없다는 오류가 과거에 반복됐습니다. 그런 경우 Hub 로그인과 에디터 라이선스부터 확인합니다. 게임 소스 오류로 오인하지 마세요.

## 검증과 빌드

2026-10-27에 완료한 결과이며, 저장소 등록일에 게임 테스트를 다시 했다는 뜻은 아닙니다.

| 검증 | 결과 | 기록 |
| --- | --- | --- |
| 왕의 회랑 실제 이동 물리·양면 완주·CP·낙하·스킵 방지·셰이더 | 35 통과 / 0 실패 | `TestResults/pyramid-validation.json` |
| 실제 Relay 두 클라이언트·맵·CP·종·재대기방·기존 서프 | 26 통과 / 0 실패 | `TestResults/pyramid-network-validation.json` |
| Windows 빌드 | 성공, 오류 0, 경고 2 | `TestResults/windows-build.txt`, `windows-build-messages.txt` |
| 원래 서프 씬/이동 프로필 보존 | 해시 일치 | `TestResults/pyramid-preservation.json` |

경고는 향후 Unity 버전의 메시 충돌 사전 베이크 설정과 에디터 Services 대시보드 연결에 관한 것이었습니다. 실제 Relay 검사는 성공했습니다. 다른 오래된 결과와 `*-probe.json`은 중간 조사 기록일 수 있으므로 위 최종 결과와 날짜를 구분하세요.

프로젝트가 Unity에서 열린 상태에서 저장소 루트의 PowerShell로 실행할 수 있습니다.

```powershell
# 씬을 다시 생성하지 않고 현재 저장된 씬을 검사
./Tools/Editor-Job.ps1 pyramid-validate
# 미리보기만 갱신
./Tools/Editor-Job.ps1 pyramid-capture
# Windows 빌드와 unitypackage 내보내기
./Tools/Editor-Job.ps1 capture-build
```

`pyramid` 작업은 왕의 회랑을 빌더에서 다시 생성하므로 의도한 경우에만 사용하세요. `pyramid-probe`는 짧은 양면 완주 조사입니다. `CourseEditorJobs`는 프로젝트 **상위 폴더**의 `outputs/course-jobs/request.txt`를 읽습니다. 도우미가 이 위치를 계산합니다. `DISPATCHED`는 비동기 테스트 완료를 뜻하지 않습니다. `TestResults` 갱신 및 `PYRAMID_VALIDATION_RESULT 35/35` 같은 최종 로그를 확인하고, 실행 중에 다음 요청을 보내지 마세요. 다음 요청은 현재 Play Mode를 중단합니다.

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
