# 모션 리뷰 참조

수치와 예외 조건은 아래 공통 자료를 정본으로 사용한다. 이번 finding을 판단하는 데 필요한 자료만 읽는다.

| 판단할 내용 | 참조 |
| --- | --- |
| 필요성·빈도·easing·duration·spring·origin·stagger | [모션 기본 권고와 예외](../emil-design-eng/references/motion.md) |
| 렌더링 비용·가속 여부·reduced motion·hover·입력 | [성능과 접근성](../emil-design-eng/references/performance-accessibility.md) |
| tooltip·toast·포털·제스처의 구체적인 조작 | [컴포넌트 상호작용](../emil-design-eng/references/components.md) |

기본 권고와 다르다는 이유만으로 결함을 선언하지 않는다. 코드 위치·재현 조건·관찰된 사용자 영향으로 finding을 뒷받침하고, 측정하지 않은 성능과 선택적 polish는 구분한다.
