# 에셋 출처와 보존

- 캐릭터, 손, 실내·산·밧줄·피라미드 장식은 기존 VARCO 작업에서 생성/가공한 모델입니다. Unity에 필요한 모델·머티리얼·텍스처·프리팹은 `Assets/HalliGalliMovement/Art`에 있습니다.
- 워크플로우: https://3d.varco.ai/flow/f3bda240-6222-43a4-a214-a85fdffa8af1
- 피라미드 장식 6종: StoneGate, LotusColumn, Anubis, ReliefPanel, Sarcophagus, Brazier. `TestResults/pyramid-varco-placement.json`에 배치 기록이 있습니다.
- 손/몸/공격 모션은 별도 제작한 클립입니다. VARCO 기본 모션을 사용하지 않는 것이 사용자 요구입니다.
- 밧줄 투척·걸림·당김, 승리 효과음은 기존 VARCO Sound 작업을 사용합니다. `TestResults/lasso-summary.json`, `lasso-audio-validation.json`, `victory-validation.json`을 참고하세요.
- 피라미드 벽의 Sandstone Blocks 05는 Poly Haven / Rob Tuytel의 CC0 에셋입니다. 원본 출처: https://polyhaven.com/a/sandstone_blocks_05 . 라이선스 사본은 `Assets/HalliGalliMovement/Art/Pyramid/StoneScan/LICENSE.txt`에 보존합니다.
- 카드는 기본판의 과일·장수 분포를 사용하되 카드 그림과 뒷면은 직접 제작한 SVG/텍스처입니다. `Assets/HalliGalliCards/README_KO.md`에 설명돼 있습니다.
- 태엽 요새의 창·화살·기어·격자 메시와 금속/바닥 패턴은 `ClockworkCourseBuilder`에서 직접 생성합니다. 석조 표면과 실내 장식은 위 기존 프로젝트 에셋을 재사용하며 새 유료 생성은 하지 않았습니다. 생성 결과는 `Assets/HalliGalliMovement/Art/Clockwork`에 보존합니다.

저장소를 공개 오픈소스로 라이선스 지정한 것은 아닙니다. 외부 서비스 생성물과 포함 에셋의 권리는 각각의 적용 조건을 따릅니다. 새 배포나 공개 범위를 바꿀 때 출처와 적용 조건을 유지하세요.

이 저장소는 게임을 열고 빌드하는 데 필요한 Unity 에셋을 포함합니다. 작업 PC의 `outputs/`에 남아 있는 중간 원본·변환 캐시·배포 ZIP 전체를 보관하는 아카이브는 아닙니다.
