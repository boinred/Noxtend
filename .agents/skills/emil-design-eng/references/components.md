# 컴포넌트 상호작용

버튼·popover·tooltip·toast 등 조작의 시각적 피드백을 변경할 때 읽는다. 모션 수치는 [motion.md](motion.md), 성능·reduced motion은 [performance-accessibility.md](performance-accessibility.md)에서 선택한다.

## 버튼과 조작 피드백

- 눌림·로딩·완료·실패를 명확하게 구분한다. 색상·텍스트·테두리만으로 충분하면 이동 효과를 추가하지 않는다.
- 눌림 scale은 선택지다. 기존 프리미티브가 사용하면 `0.95–0.98` 정도를 시작점으로 하고 주변 컨트롤과 맞춘다. 글자·아이콘도 함께 축소된다는 점을 확인한다.
- pointer-down부터 시각적으로 반응하되 실제 작업은 버튼의 기존 click·키보드 의미론을 유지한다.

## Popover와 tooltip

- 트리거에 연결된 popover는 트리거 방향의 `transform-origin`을 사용한다. 중앙 modal은 중앙 origin을 유지한다.
- 프리미티브가 제공하는 origin 변수·열림 상태·포털을 재사용한다. Base UI 예시의 변수를 Radix에 그대로 복사하지 않는다.
- 작은 scale과 opacity 조합은 진입 효과의 시작점이다. 단순 fade가 의미를 충분히 전달하면 그대로 사용한다.
- 첫 tooltip의 표시 지연은 우발적 활성화를 줄인다. 인접 tooltip 사이 이동은 기존 프리미티브의 skip-delay 기능으로 빠르게 처리한다.
- native dialog 안의 메뉴·Select는 기존 top layer 경계를 유지한다. 열림·닫힘·Escape·포커스 복귀를 함께 확인한다.

## Toast와 빠른 상태 변경

- 빠르게 추가·삭제되는 toast나 toggle은 현재 표시 값에서 전환하도록 한다. CSS transition 또는 기존 spring을 먼저 검토한다.
- 모션 종료를 기다리느라 새 입력을 버리지 않는다. keyframe을 사용할 경우 재입력에서 위치가 튀는지 실제로 확인한다.
- 시간 제한 toast는 탭 비활성화·hover·포커스 동안의 타이머 정책을 기존 컴포넌트와 맞춘다.
- 스택 사이 간격 때문에 hover가 끊기거나, swipe 중 포인터가 밖으로 나가 조작이 끊기지 않는지 확인한다.

## 조건부 효과

| 효과 | 적용할 때 | 확인할 것 |
| --- | --- | --- |
| `@starting-style` | CSS만으로 요소 진입 표현 | 대상 브라우저 지원과 기본 표시 상태 |
| 작은 blur crossfade | 겹친 두 상태가 부자연스럽게 보이는 경우 | 단순 fade로 해결 가능한지, 실제 성능 |
| `clip-path: inset()` | 이미지 비교·reveal·진행 영역 | 키보드 대안·reduced motion·브라우저 성능 |
| `translateY(100%)` | 요소 자신의 높이만큼 이동 | 백분율은 부모가 아닌 요소 크기 기준 |
| `preserve-3d`·회전 | 공간 관계를 설명하는 드문 효과 | 목적·레이어 비용·이동 제거 대안 |

hold-to-confirm은 제품이 해당 상호작용을 요구할 때만 추가한다. pointer를 유지하는 방식만으로 파괴적 작업을 실행하지 않고 키보드·보조 기술에서도 같은 행동을 수행할 수 있게 한다.

## 제스처

- pointer capture와 잡은 위치의 offset을 유지한다. 추가 pointer는 기존 drag를 덮지 않게 처리한다.
- release 속도는 최근 이동 구간으로 계산하고 시간 단위·이동 방향·거리 조건을 함께 확인한다. 특정 속도 임계값을 모든 sheet·swipe에 복사하지 않는다.
- 경계 마찰·관성·spring 속도 인계가 필요한 경우 `$apple-design`를 선택한다.
