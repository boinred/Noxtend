# 모션 성능과 접근성

프레임 저하·렌더링 비용·reduced motion·hover 처리를 변경하거나 검토할 때 읽는다.

## 성능

- `transform`·`opacity`를 기본 선택으로 사용한다. 일반적으로 layout·paint 부담이 작지만 특정 구현의 GPU 가속을 보장하지는 않는다.
- `width`·`height`·`padding`·`margin`은 주변 layout에 영향을 줄 수 있다. 격리된 작은 영역에서 필요한 효과라면 실제 브라우저·낮은 성능의 기기에서 확인하고 판단한다.
- `filter`·`clip-path`·색상 등의 가속 지원은 브라우저·속성·구현에 따라 다르다. 이 속성의 사용 자체를 결함으로 선언하지 않는다.
- `transition: all` 대신 실제 변경 속성을 명시해 예상하지 못한 전환을 줄인다.
- 부모의 상속 CSS 변수로 많은 자식의 모션을 구동하면 style 계산이 늘 수 있다. 프레임 저하가 확인되면 해당 요소의 transform 갱신 등 작은 대안을 비교한다.
- CSS·WAAPI는 적합한 속성을 main thread 밖에서 처리할 수 있다. JavaScript 사용 여부만으로 성능을 판정하지 않는다.
- Motion의 개별 transform·layout animation은 버전과 구현에 따라 비용이 달라진다. 가속이 필요한 경우 설치된 API·공식 문서·프로파일을 확인하고 전체 `transform` 문자열을 대안으로 비교한다.
- `will-change`는 필요한 요소에만 적용한다. 레이어 수와 메모리 비용도 함께 확인한다.

브라우저별 예외와 확인 방법은 [Motion 공식 성능 안내](https://motion.dev/docs/performance)를 따른다. 측정하지 않은 프레임 저하는 위험 추정으로 표시한다.

## Reduced motion

[PRODUCT.md](../../../../PRODUCT.md)의 정책에 따라 반복·공간 이동 효과를 제거하거나 최소화한다. 의미를 전달하는 짧은 opacity·색상 피드백은 유지할 수 있다. fade 자체가 불편하거나 목적이 없으면 정적인 상태 변경을 사용한다.

```css
.panel {
  transition: transform 180ms var(--ease-out), opacity 180ms var(--ease-out);
}

@media (prefers-reduced-motion: reduce) {
  .panel {
    transform: none;
    transition-property: opacity;
  }
}
```

이 예시는 패턴이다. `.panel`·토큰·표시 상태는 실제 컴포넌트로 대체한다. 외부 규칙이 `transform`을 덮는지 확인하고, 진행 상태는 텍스트·ARIA로도 전달한다.

## 입력과 접근성

- 모션 완료를 기다리며 입력·닫기·포커스 이동을 막지 않는다. 빠른 재입력이 결과를 덮거나 상태를 되살리는지 확인한다.
- `hover` 이동 효과는 `@media (hover: hover) and (pointer: fine)`으로 제한한다. touch에서 hover가 남아도 필수 행동이 가능해야 한다.
- 키보드 사용자에게도 포커스·눌림·완료 피드백을 제공한다. 움직임이 없어도 접근 가능한 이름·텍스트·ARIA로 결과를 전달한다.
- 이미지 비교·swipe·hold에는 키보드로 실행 가능한 동등한 행동을 제공한다.

## 확인 범위

- 프레임·layout·paint 비용은 DevTools로 확인한다. 정상 속도·빠른 재입력·reduced motion·좁은 화면을 함께 점검한다.
- 느린 재생은 시각적 연결을 확인하는 도구이며 정상 속도의 성능 측정을 대신하지 않는다.
- 실제 터치 기기·낮은 성능의 기기를 확인하지 못했다면 그 범위를 명시한다.
