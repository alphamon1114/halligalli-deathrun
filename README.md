# 할리갈리 데스런 · Bell Run

과일 카드의 합을 직접 판단하고, 자리에서 일어나 장애물 코스 끝의 종을 차지하는 1인칭 경쟁 게임입니다. 최대 6명이며 친구와 AI를 함께 넣어 현재 인원으로 시작합니다.

## 다른 세션에서 이어가기

**먼저 [AGENTS.md](AGENTS.md)와 [작업 인계 기록](Docs/HANDOFF.md)을 읽으세요.** 최신 변경은 왕의 회랑의 25개 발판을 양면 삼각형 서프로 바꾼 것입니다. 좌우를 강제로 번갈아 타는 구조를 다시 넣지 마세요.

이 저장소의 루트가 Unity 프로젝트입니다. 별도의 하위 프로젝트 폴더를 만들 필요가 없습니다.

```powershell
git lfs install
git clone https://github.com/alphamon1114/halligalli-deathrun.git
cd halligalli-deathrun
git lfs pull
git lfs fsck
```

GitHub의 ZIP 다운로드 대신 Git + Git LFS로 복제하세요. 모델, 텍스처, 효과음, 미리보기는 LFS로 저장합니다. 비공개 저장소이므로 접근 권한이 있는 GitHub 계정이 필요합니다.

## 실행

1. Unity Hub에서 **Unity 6000.6.0f1**과 Windows Build Support를 설치하고 이 폴더를 추가합니다.
2. 패키지 설치와 에셋 가져오기가 끝날 때까지 기다립니다. `Packages/manifest.json`과 `packages-lock.json`이 포함돼 있습니다.
3. `Assets/HalliGalliMovement/Demo/BellRunCourse.unity`를 열고 Play를 누릅니다.
4. 방 만들기 → AI 추가 또는 친구 초대 → 맵 선택 → 시작 순서로 진행합니다. 연습실에는 공격 연습용 정지 상대가 있습니다.

| 기본 입력 | 기능 |
| --- | --- |
| WASD / 마우스 | 이동 / 시점 |
| Space / 휠 아래 | 점프 |
| Ctrl | 웅크리기 |
| 좌클릭 | 주먹 / 내 차례 카드 공개 |
| F | 발차기 |
| Q | 밧줄 투척 / 다시 눌러 당기기 |
| E | 일어나기 / 앉기 / 종 |
| R | 체크포인트 복귀 |
| Esc | 메뉴 / 조작키 설정 |

Shift 달리기와 마비총은 제거된 상태입니다. 기본 이동은 4.5m/s이며 조작키를 바꿀 수 있습니다.

## 현재 코스

- **첫걸음**: 점프 코스와 서로 엇박으로 움직이는 마지막 운반 발판.
- **정상 쟁탈**: 한 봉우리를 돌아 오르는 공통 등반 코스와 일부 숏컷.
- **서프 수로**: 11개 편측 서프 구간. 구간 스킵 방지용 전기 칸막이.
- **폐공장**: 움직이는 발판, 컨베이어, 닿으면 복귀하는 프레스.
- **왕의 회랑**: 피라미드 내부의 25개 양면 삼각 서프, 5개 방, 4개 체크포인트. 왼쪽 또는 오른쪽을 선택해 같은 면으로 완주할 수 있습니다.

![왕의 회랑 양면 서프](Previews/Pyramid_Triangle_Choice.png)

## 개발과 검증

- 최신 게임 변경 검증: **2026-10-27**, 왕의 회랑 물리·복귀 검사 **35/35**, 실제 Unity Relay 두 클라이언트 검사 **26/26**.
- 네트워크 프로토콜: **24**. 함께 플레이하는 사람은 같은 최신 빌드를 사용해야 합니다.
- 상세 기록: [TestResults/pyramid-release.json](TestResults/pyramid-release.json), [재현 방법](Docs/HANDOFF.md#검증과-빌드).
- 기존 조작·규칙 문서: [START_HERE_KO.txt](START_HERE_KO.txt), [게임 상세](Assets/HalliGalliMovement/README_KO.md).
- 에셋 출처: [Docs/ASSETS.md](Docs/ASSETS.md).

`Library`, 캐시, 로그, Windows 실행 파일, 배포 ZIP, 자동 백업 씬은 Git에서 제외했습니다. Unity에서 다시 가져오고 빌드하면 생성됩니다. 이번 저장소 등록은 게임 동작을 변경하지 않습니다.
