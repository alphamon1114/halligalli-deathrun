# 다른 세션을 위한 작업 인계

기록일: **2026-10-30**. 사용자 맵 스튜디오와 파일 공유를 추가하고 실제 Windows 플레이어에서 검증했습니다. 기존 공식 맵 전체 검증일은 **2026-10-27**입니다.

## 지금 이어받을 상태

사용자는 Unity 없이 게임 안에서 할리갈리 데스런 맵을 제작하고, 방에서 자동 공유하며, 파일 하나로 다른 사람에게 전달하기를 요청했습니다. 마지막 공식 아이템 레이스도 추가했습니다. 기존 카드·종·승패 규칙을 유지하며 공개 맵 자료실과 범용 규칙 편집기는 선택하지 않았습니다. 저장소는 `alphamon1114/halligalli-deathrun`의 `main`입니다. 사용자가 Unity 저장·Git 커밋·GitHub 반영을 요청했으므로 아래 검증을 마친 프로젝트 상태를 전달 대상으로 삼습니다.

공식 맵은 기존 동선과 물리를 유지한 채 **바닥 면적 표시용 테두리를 제거**했습니다. 체크포인트 표시와 위험/진행 표시는 유지합니다. 그 이전 동선 변경은 **왕의 회랑에서 좌측/우측 서프를 번갈아 타도록 강요하는 구조 제거**입니다.

- 25개 발판은 실제 충돌체를 가진 **∧ 모양의 양면 삼각형**입니다. 가운데가 높고 양옆으로 내려갑니다. 마주 보는 벽 두 개로 만든 ∨ 모양이 아닙니다.
- 왼쪽 또는 오른쪽을 선택해 동일한 면으로 끝까지 갈 수 있습니다. 틈 앞뒤 중심선이 정렬돼 있고 석문과 착지 발판은 두 면을 받습니다.
- 5개 방의 급강하, 180도 나선, 낙하, 공중 제단, 마지막 도약은 유지합니다. 총 활주 길이는 약 882.2m, 기존 서프의 약 2.62배입니다.
- 4개 체크포인트의 완료 구간 수는 **4, 9, 14, 20**입니다. 25개 구간을 순서대로 완료해야 종을 누를 수 있습니다.
- `SurfCourse.Section.twoSided`가 양면을 구분합니다. 기존 **서프 수로는 11개 편측 면 그대로**이며 이번 변경 대상이 아니었습니다.
- 기본 이동 4.5m/s, 자동 버니합 꺼짐, 별도 스크립트 가속 없음. 기존 서프 이동 프로필을 유지했습니다.

## 사용자 맵 스튜디오 · 2026-10-30

- 시작 화면 **맵 만들기** → 편집 → **F8 클리어 검증** → 혼자 출발점부터 도착 종을 E로 울리기 → F8 편집 복귀 → 저장 → 홈 → 방의 **사용자 맵** 선택으로 플레이합니다. 검증 중 AI/카드/체크포인트를 끄며 낙하/R은 중앙 출발점으로 돌아갑니다. 초안과 편집 카메라 위치는 유지합니다.
- 11종 부품: Block, Ramp, Surf, Checkpoint, MovingPlatform, Conveyor, Press, Hazard, Bell, Waypoint, ItemBox. 출발 책상/자리/규칙은 보호합니다. 배치·선택·축 드래그·수치 변환·장치 설정·복제·삭제·64단계 실행 취소/다시 실행을 제공합니다.
- 초보자 카메라: 기본 직교 위 시점, 빈 바닥 드래그 팬, 휠/−/＋ 줌, 클릭 선택만/더블클릭 프레이밍, 명시적 손잡이로만 변환합니다. 위/사선/옆 시점 및 전체/선택/출발/종 버튼, 별도 고급 비행을 제공합니다. 화면 이동에 관성이 없고 휠로 비행 속도가 바뀌지 않습니다. 카메라 조작은 맵 데이터와 인증을 변경하지 않습니다.
- `UserMapStudio.Camera.cs`가 편집 뷰포트의 실제 픽셀 영역에 맞춰 직교 카메라를 배치하고 회전된 부품/이동 발판 경로를 포함해 화면을 맞춥니다. F8 왕복에서 중심·배율·회전을 유지합니다. 편집 카메라 안개는 해당 카메라 렌더 전후로만 비활성화/복원합니다.
- 부품 목록은 기본 닫힌 햄버거 ‘부품 메뉴’로 전환했습니다. 열면 부품/개체 목록이 나타나며 선택하면 자동으로 닫힙니다. 메뉴 버튼/Esc/맵 영역 클릭으로 닫을 수 있고 닫는 맵 클릭은 소비해 오배치를 막습니다. 닫힌 동안 왼쪽 패널 폭을 편집 뷰포트에 반환합니다. `TestResults/usermap-menu.json` 실제 플레이어 8항목 통과 및 열린/닫힌 화면 확인. 메뉴·부품 선택 경로를 진단 드라이버로 호출했고 OS 마우스/Esc 이벤트는 자동화하지 않았습니다.
- 부품 메뉴 버튼은 최상단 대신 이동·회전·크기 도구와 같은 두 번째 줄로 내렸습니다. 선택/배치 취소는 위로 옮겨 겹침을 피했습니다. Windows 재빌드 후 실제 실행에서 메뉴 열기와 화면 배치를 확인했습니다.
- **내보내기/가져오기**는 Windows 파일 선택 창을 사용합니다. `.bellmap` UTF-8 JSON 한 파일에 모든 배치 데이터를 저장합니다. 가져오기는 새 GUID로 복사하여 기존 내 맵을 덮어쓰지 않습니다. 맵은 `Application.persistentDataPath/UserMaps`에 저장하며 게임 설치 폴더와 분리합니다.
- 제작 초안은 클리어 전 저장·내보내기·방 선택을 차단합니다. 수정/실행 취소/다시 실행은 현재 편집 인증을 해제합니다. 파일 가져오기는 원본을 받은 맵 보관함에 저장하고 별도 GUID의 편집 초안을 엽니다. 받은 원본은 재클리어 없이 방 선택·원본 내보내기가 가능하며, 편집 사본은 직접 클리어해야 저장됩니다. 원본 수신은 솔로 클리어 인증을 만들지 않습니다.
- `UserMapClearance.cs`는 맵 ID와 정규화 JSON SHA256에 묶인 로컬 기록을 `persistentDataPath/UserMapClearance`에 원자적으로 저장합니다. 실제 솔로 종 완료 이벤트에서만 기록하며 휴대 파일/네트워크 스키마에는 인증을 넣지 않습니다. 재실행 후 같은 저장 맵 인증은 유지됩니다. 로컬 파일/수정된 실행 파일에 대한 안티치트나 서버 공인 인증은 아닙니다.
- 스키마 1, 최대 512개/524288바이트. 이름 60자, 설명 1000자. 위치 X±500/Y−100~300/Z0~1000, 이동 경로와 회전 후 경계도 검증합니다. 크기 0.1~100m(Y≤60), 낙하 높이 −200~−2. 종은 정확히 하나이며 크기 0.5~2. 시작 데크의 윗면은 Y=0/Z≤0입니다.
- 지형 position은 중심, Bell/Press/ItemBox는 바닥 기준, Waypoint는 플레이어 발 위치입니다. Checkpoint/MovingPlatform/Conveyor/Press/Bell/Waypoint/ItemBox는 Y축 회전만 허용합니다. ItemBox는 단위 scale 고정입니다. 이동 발판 travel은 월드 축 진폭, period는 초, 컨베이어 speed는 로컬 +Z 방향 m/s입니다.
- 외부 코드·에셋 로딩은 없습니다. 엄격한 스키마/유한 수치/고유 ID/크기 검사, SHA256, 원자적 저장을 사용합니다. `UserMapDownloads.cs`는 받은 원본을 `persistentDataPath/DownloadedMaps/<canonical-sha256>.bellmap`에 보관하며 제작 맵을 덮어쓰지 않습니다. 같은 ID/이름도 내용이 다르면 별도 버전입니다. 로비는 내 완주 맵과 받은 원본을 병합 표시하고 해시로 선택을 구분합니다.
- 호스트는 선택 revision/hash를 고정하고 1KiB 조각을 전송합니다. 참가자는 검증된 수신 파일을 보관한 뒤 ACK합니다. 정확한 캐시가 있으면 내용을 다시 받지 않고 ACK하며, 호스트는 초기 캐시 ACK와 전송 큐의 순서 차이를 처리합니다. 맵 변경 시 준비를 해제하고 전송을 무효화합니다. 현재 프로토콜은 사용자 맵 아이템 박스 추가 후 28이며 같은 최신 빌드를 사용해야 합니다.
- 구현: `Runtime/UserMapData.cs`, `UserMapStorage.cs`, `UserMapRuntime.cs`, `UserMapStudio*.cs`, `CourseNetwork.UserMaps.cs`. 일반 로스터·체크포인트·전투·카드 판정을 재사용합니다. 움직이는 장치는 공유 서버 시각을 사용합니다.
- 전용 씬: `Demo/UserMapCourse.unity`. `Editor/UserMapTemplateBuilder.Build()`는 저장된 첫걸음 씬의 복사본에서 코스 지형을 제거하고 원래 책상·캐릭터·자체 모션·종·소리·프로필을 보존합니다. 공식 씬을 다시 만들지 않습니다. `CourseMapsBuilder.BuildScenePaths()`에 전용 씬을 포함했습니다.
- 최신 검증: `TestResults/usermap-storage-build.json` 15항목 및 Windows 빌드 성공. `usermap-creator-clear.json` 실제 플레이어 37항목 통과: 클리어 전 저장/내보내기 차단, 실제 이동·낙하·R 재시작, CP 무효화, 종 접근만으로 인증되지 않음, 실제 종 완료 후 저장, 수정/실행 취소 후 재검증, 파일 가져오기 격리, 프로세스 재실행 후 인증 유지. 성공 HUD와 저장 가능 편집 화면도 캡처해 확인했습니다.
- 최신 네트워크: `TestResults/usermap-network-loopback.json` 27항목 통과. 미검증 호스트 맵 거절, 인증된 맵 자동 전달, 동일 스냅샷 로딩, 실제 대결 CP/종 동작, 큰 맵 전송 취소/재선택 및 공식 맵 복귀를 확인했습니다. 이전 스튜디오 18항목/Unity Relay 26항목 결과는 인증 기능 추가 전 기록이며 이번에는 Relay를 다시 실행하지 않았습니다.
- 다운로드 보관 추가 검증: `TestResults/usermap-downloads.json` 27항목 통과. 실제 파일 가져오기 후 수신자 방 생성, 캐시 없는 참가자 다운로드·영구 보관·실제 경기, 프로세스 재시작 후 재호스팅, 수정본 거절, 다음 참가자의 캐시 재사용과 실제 경기를 확인했습니다. 로비 화면도 캡처했습니다. 제작자 37항목/네트워크 27항목을 최신 빌드에서 재검증했습니다. 기존 네트워크 테스트의 일시적인 두 프로세스 보고 시점 차이 조건은 실제 다운로드 미완료/준비 차단 조건으로 교체했습니다.
- 카메라 변경 검증: `TestResults/usermap-camera-1440.json`, `usermap-camera-1280.json` 각각 실제 Windows 플레이어 24항목 통과. 이동 정지·줌 한계·시점·목적지·먼 부품·클릭/더블클릭 공통 처리 경로·데이터 불변·고급 모드·F8 복귀를 검사했습니다. 1440×900/1280×720 캡처에서 버튼과 편집 뷰포트를 확인했습니다. 입력은 플레이어의 실제 카메라/선택 메서드를 진단 드라이버로 호출했으며 OS 마우스 이벤트 자동화는 하지 않았습니다. Windows 빌드 및 기존 저장 경계 검사도 통과했습니다.
- 재현: Unity batchmode `-executeMethod HalliGalli.EditorTools.UserMapVerification.Build -quit` 후 `node Tools/Network/check-creator-clear.mjs .`, 이어서 `node Tools/Network/check-usermaps.mjs .`, `node Tools/Network/check-map-downloads.mjs .`. 네트워크 검증은 앞선 실제 클리어 시나리오의 파일을 사용합니다. Node.js 또는 호환 Bun이 필요합니다. `check-usermaps.mjs . --relay`는 실제 Relay 서비스를 사용합니다.
- 제한: 공개 자료실·외부 모델·임의 스크립트·공동 편집은 범위 밖입니다. 솔로 클리어 검증은 멀티플레이 난이도나 AI 동선의 보증이 아닙니다. 복잡한 AI 동선은 제작자가 경로점을 지정하고 실제 방에서 확인해야 합니다. Windows 파일 창의 직접 클릭 검증은 이전 작업에서 사용자 마우스 입력과 겹쳐 중단했으며 파일 저장/불러오기 동작은 실제 플레이어 API로 검증했습니다.

### 사용자 맵 아이템 박스 · 2026-10-30

- `UserMapPart.ItemBox=10`을 기존 enum 뒤에 추가했습니다. 스키마 1과 이전 부품 번호/맵 파일을 유지합니다. 위치는 바닥 앵커, scale은 정확히 `(1,1,1)`, 수평 회전만 허용합니다. 유효 영역은 애니메이션까지 포함한 중심 `(0,.8,0)`, 크기 `(1.28,1.6,1.28)` 경계로 검사합니다.
- 부품 메뉴 아래에서 선택하며 이동/수평 회전/색 변경·복제·삭제·실행 취소를 지원합니다. 확대/축소와 기울이기는 잠급니다. 충돌체 없이 렌더러 경계로 클릭 선택하되 더 가까운 지형에 가려진 박스는 선택하지 않습니다.
- `UserMapRuntime`이 박스가 있는 맵에만 `RaceCourse`를 구성합니다. 편집 중에는 박스만 정적으로 보이고, 실제 플레이/솔로 검증 때 기존 5종 아이템을 사용합니다. 생성물 교체/비활성화는 이벤트 구독·효과·Active를 정리합니다. 사용자 맵 순위는 종까지 직선거리, HUD 표기는 ‘종 거리’; 공식 레이스는 기존 +Z 순위입니다. 아이템 확률과 쿨다운은 동일합니다.
- 네트워크 버퍼는 헤더·최대 6명·48개 투사체와 실제 박스 타이머 수에 맞게 계산합니다. 512개 부품 제한 안의 510개 박스+바닥+종 맵도 지원합니다. 새 부품을 이해하지 못하는 이전 실행 파일과 접속하지 않도록 프로토콜을 **28**로 올렸습니다.
- Windows 저장 씬 빌드 성공. `node Tools/Network/check-usermap-items.mjs .`의 실제 플레이어 **23/23** 통과: 크기/기울기/영역 밖 거절, 클릭 선택·복제·삭제·undo/redo, 솔로 실제 획득/사용/완주/저장, 수정 인증 해제, 파일 왕복, 캐시 없는 참가자 다운로드, 510번째 박스 획득/쿨다운 동기화, 종 거리 순위와 상대 명중, 맵 전환 정리를 확인했습니다. `check-race.mjs` **26/26**, `check-creator-clear.mjs` **37/37**도 프로토콜 28 빌드에서 재통과했습니다.
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
- `RaceCourse.cs`가 호스트 권위로 박스 접촉·순위·획득·발사·명중을 판정합니다. 선두는 바나나/방어막/후방 큰 공 각각 1/3, 추격자는 미사일 50%/직선 레이저 40%/바나나 10%입니다. 한 칸 보관, 박스 8초 재등장, 방어막 5초, 미사일 사거리 45m/속도 21m/s/최대 5초, 바나나 최대 22초/설치자 보호 .85초. AI도 같은 박스를 획득하고 상황에 맞춰 사용합니다.
- 미사일 명중은 .85초 공중제비와 실제 상승/입력 방해, 바나나는 1.1초 관성 미끄러짐입니다. `SourceMotor`에 순간 슬립 상태를 추가했으며 기본 프로필은 바꾸지 않았습니다. `RaceCourse.Visuals.cs`는 몸 메시만 회전하고 카메라·참가자 루트는 뒤집지 않습니다. 미사일/바나나/방어막/큰 공/레이저 외형을 풀링합니다.
- `CourseNetwork.Race.cs`의 10Hz 호스트 스냅샷은 기존 자세 채널과 분리합니다. epoch/revision/round/respawn/itemSequence로 이전 라운드·생명의 사용 요청을 차단합니다. 최초 슬립 추가 때 프로토콜 26, 큰 공/레이저 추가 후 27, 사용자 맵 아이템 박스 추가 후 **28**입니다. 착석·복귀·종·맵 전환 시 아이템/효과/투사체를 정리합니다.
- 기본 사용 키 **C**, 기존 V 전신 보기 유지. `CourseHUD.Race.cs`에 순위·남은 거리·아이템·락온·방어 잔여시간을 표시합니다. 기존 12행 조작키 저장은 보존해 새 행을 추가하고, C를 이미 사용 중이면 기존 설정을 빼앗지 않고 아이템 키를 미지정으로 둡니다.
- 재현: Unity batchmode에서 `-executeMethod HalliGalli.EditorTools.RaceCourseBuilder.Build`(새 맵 생성이 필요할 때만), 이후 `-executeMethod HalliGalli.EditorTools.RaceCourseBuilder.BuildPlayer`; `node Tools/Network/check-race.mjs .`로 실제 두 플레이어를 실행합니다. 진단 시 박스의 실제 접촉으로 획득하며 아이템을 강제로 지급하지 않습니다.
- 최초 구현 검증 **2026-10-30, 프로토콜 26**: `TestResults/race-build.json` Windows 빌드/조작키 보존/키 충돌 처리 통과. 당시 레이스 18/18, `usermap-creator-clear.json` 37/37, `usermap-network-loopback.json` 27/27. 사용자 맵 전용 검사를 이번 아이템 밸런스 변경에서는 재실행하지 않았습니다.
- 실제 1440×900 화면에서 락온 표시, 거꾸로 도는 상대 몸, 바나나 피격 자세, 완주 안내, 추가된 C 조작키 행을 확인했습니다. 캡처는 `Tools/Network/Runs/race-1793347408374/`; 대표 화면은 `Previews/ItemRace_LockOn.png`, `Previews/ItemRace_Somersault.png`로 보관합니다. 입력은 진단 드라이버의 실제 게임 경로이며 OS 키보드·마우스 이벤트 자동화는 아닙니다. 이번 레이스에서 Unity Relay/인위적 지연 환경은 재검증하지 않았습니다.
- 선두 방어막 고정 지급을 폐기했습니다. `PickItem`은 실제 박스 접촉 시 현재 선두 여부와 한 번의 난수로 위 확률을 적용합니다. 공은 지름 1.5m, 뒤로 8m/s, 최대 8초이며 첫 상대/벽/바닥 끝에서 소멸합니다. 바닥과 .02m 간격을 둬 구체 충돌이 바닥을 벽으로 오인하지 않습니다. 레이저는 즉시 한 번만 판정하고 빔은 네트워크/시각 표시용으로만 남습니다. 두 공격은 .35초 stagger와 수평 밀치기를 적용하며 방어막에 막힙니다. 기본 이동 프로필은 변경하지 않았습니다.
- 밸런스 변경 최종 검증 **2026-10-30, 프로토콜 27**: Windows 빌드 성공, `TestResults/race-network.json` **26/26**. 실제 박스에서 양쪽 순위의 모든 허용 아이템 획득, 공의 후방 밀치기, 레이저 명중/반복 피해 방지/빗나감/벽 차단, 두 새 공격의 방어막 차단, 기존 아이템 및 사람/AI 완주를 확인했습니다. 캡처 `Tools/Network/Runs/race-1793348479181/`에서 공·레이저·벽에 막힌 빔 외형을 확인했습니다. Relay와 인위적 지연 환경은 이번에도 미검증입니다.
- 새 공격의 대표 화면은 `Previews/ItemRace_RollingBall.png`, `Previews/ItemRace_Laser.png`에 보관합니다.
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
- 프로토콜 **28**. 다른 버전끼리 입장하지 못하도록 호환성 검사를 합니다.
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

## GitHub 등록 확인 · 2026-10-30

- 비공개 저장소 생성 및 `main` 업로드 완료. 최초 소스 커밋은 `fa84b375ba78cce06bcde7e04960cae5fff6dd94`입니다.
- 원래 작업 폴더의 `origin`을 새 저장소에 연결했습니다.
- 별도 빈 폴더에서 원격 저장소 복제, 모든 LFS 에셋 다운로드, 체크아웃 및 `git lfs fsck`가 성공했습니다. 검증용 임시 복제본은 검사 후 정리합니다.
- 새 인계 문서 링크와 PowerShell/JavaScript 도우미의 구문을 확인했습니다. 이번 업로드 과정에서 게임 소스·물리·맵을 변경하거나 게임 검사를 다시 실행하지 않았습니다.
