---
name: emil-design-eng
description: Polish UI components and choose purposeful motion using existing primitives and tokens. Use for component interaction, visual polish, and motion implementation; specialized motion audits and diff reviews have separate skills.
---

# Design Engineering

Emil Kowalski의 컴포넌트·모션 원칙을 Noxtend에 맞게 적용한다. [원본 라이선스](../LICENSE.emilkowalski)를 유지한다.

## 작업 범위와 우선순위

- 사용자 요청과 루트·영역별 `AGENTS.md`를 먼저 따른다. 제품 의도는 `PRODUCT.md`, 구체적인 UI 토큰·정책은 `DESIGN.md`와 현재 구현에서 확인한다.
- 기존 프리미티브·훅·테마 토큰을 재사용한다. 스킬 예시를 이유로 라이브러리를 설치하거나 기존 shadcn/ui·Radix를 교체하지 않는다.
- 아래 자료는 해당 결정을 바꿀 때만 읽는다. UI 변경마다 모든 참조나 다른 UI 스킬을 함께 읽지 않는다.

| 작업 | 읽을 자료 |
| --- | --- |
| 버튼·popover·tooltip·toast·컴포넌트 조작 | [컴포넌트](references/components.md) |
| 모션 필요성·easing·duration·spring·stagger | [모션](references/motion.md) |
| 프레임 저하·CSS/WAAPI 선택·reduced motion·hover | [성능과 접근성](references/performance-accessibility.md) |

제스처의 속도·관성·중단 처리처럼 더 깊은 내용이 필요하면 `$apple-design`를 추가로 선택한다. 코드베이스 모션 감사는 `$improve-animations`, 명시적인 모션 diff 리뷰는 `$review-animations`를 사용한다.

## 공통 판단

1. 변경할 실제 컴포넌트와 토큰을 확인하고 사용자가 겪는 문제를 정한다.
2. 상태 설명·피드백·공간 관계에 필요한 효과부터 선택한다. 반복 조작에는 즉시 반응과 짧은 피드백을 우선한다.
3. 참조의 값은 시작점이다. 기존 제품 정책과 관찰된 문제에 맞춰 선택하고, 예외에는 이유와 확인 방법을 남긴다.
4. 입력·포커스·오류 상태를 모션 완료에 종속시키지 않는다. reduced motion에서도 결과를 텍스트·ARIA와 정적인 피드백으로 전달한다.

## 검증과 리뷰

- 실제 조작·빠른 재입력·키보드·reduced motion·좁은 화면에서 변경 효과를 확인한다. 제스처는 가능한 경우 실제 터치 기기에서 확인한다.
- 눈으로 확인하지 못한 느낌이나 측정하지 않은 성능은 미확인으로 표시한다. 기본 권고와 다르다는 이유만으로 결함을 선언하지 않는다.
- 리뷰는 위치·재현 조건·사용자 영향·최소 수정안을 제시한다. 여러 변경을 비교할 때 `Before | After | Why` 표를 사용한다.
- 코드 검증과 완료 기준은 `$noxtend-workflow`의 검증 자료를 따른다. 이 스킬은 별도 승인 절차를 만들지 않는다.
