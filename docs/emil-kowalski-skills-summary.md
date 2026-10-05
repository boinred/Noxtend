# Emil Kowalski의 Design Engineering Skills 정리

> 참고 글: [애플의 설계 원칙을 AI에게 주입하면 생기는 일](https://brunch.co.kr/@kim-yezi/41)<br>
> 원본 저장소: [emilkowalski/skills](https://github.com/emilkowalski/skills)<br>
> 확인일: 2026-07-27

## 1. 핵심 요약

이 사례의 핵심은 **전문가의 감각을 설명 가능한 규칙으로 바꾸고, 그 규칙을 AI가 읽는 `SKILL.md`로 만들 수 있다**는 것이다.

Skill은 단순한 프롬프트 모음이 아니다. 특정 분야에서 좋은 결과와 나쁜 결과를 구분하는 기준, 수치, 예외, 작업 순서와 출력 형식을 AI에게 제공하는 작업 지침서에 가깝다.

```text
전문가의 경험
→ 좋은 결과와 나쁜 결과를 구별하는 기준
→ 이유·수치·예외가 포함된 규칙
→ AI가 읽는 SKILL.md
→ 반복 가능한 전문가 수준의 판단
```

Emil Kowalski는 Apple의 WWDC 디자인 강연과 자신의 디자인 엔지니어링 경험을 웹 환경에 맞는 규칙으로 번역했다. 그 결과 AI 에이전트가 애니메이션과 인터페이스를 단순히 동작하게 만드는 수준을 넘어, 자연스럽게 느껴지는지를 판단하고 개선할 수 있게 했다.

## 2. 저장소에 포함된 Skills

현재 저장소에는 7개의 skill이 포함되어 있다.

| Skill | 역할 | 적합한 요청 |
| --- | --- | --- |
| [`emil-design-eng`](https://github.com/emilkowalski/skills/blob/main/skills/emil-design-eng/SKILL.md) | Emil의 UI 완성도, 컴포넌트 설계, 애니메이션 판단 철학을 포괄적으로 적용하는 메인 skill | “이 UI를 더 완성도 있게 다듬어줘.” |
| [`apple-design`](https://github.com/emilkowalski/skills/blob/main/skills/apple-design/SKILL.md) | Apple의 인터페이스·모션 원칙을 CSS, Pointer Events, spring 등 웹 기술로 번역 | “이 바텀시트가 iOS처럼 자연스럽게 움직이게 해줘.” |
| [`review-animations`](https://github.com/emilkowalski/skills/blob/main/skills/review-animations/SKILL.md) | 기존 애니메이션 코드나 diff를 엄격한 기준으로 리뷰 | “이 PR의 애니메이션을 검토해줘.” |
| [`improve-animations`](https://github.com/emilkowalski/skills/blob/main/skills/improve-animations/SKILL.md) | 프로젝트 전체의 모션을 감사하고 우선순위별 개선 계획 작성 | “이 프로젝트의 모션을 전체적으로 개선할 계획을 만들어줘.” |
| [`find-animation-opportunities`](https://github.com/emilkowalski/skills/blob/main/skills/find-animation-opportunities/SKILL.md) | 아직 움직이지 않는 UI에서 애니메이션이 실제로 필요한 지점 탐색 | “이 화면에서 애니메이션을 추가할 만한 곳을 찾아줘.” |
| [`animation-vocabulary`](https://github.com/emilkowalski/skills/blob/main/skills/animation-vocabulary/SKILL.md) | 막연한 모션 묘사를 정확한 애니메이션 용어로 변환 | “iOS 스크롤 끝에서 늘어나는 효과를 뭐라고 해?” |
| [`pick-ui-library`](https://github.com/emilkowalski/skills/blob/main/skills/pick-ui-library/SKILL.md) | 프론트엔드 작업에 맞는 검증된 라이브러리를 선별해 추천 | “Toast에는 어떤 라이브러리를 쓰는 게 좋아?” |

## 3. `apple-design`

글에서 가장 비중 있게 다루는 skill이다. Apple의 WWDC 디자인 강연을 바탕으로 유려한 인터페이스가 갖춰야 할 원칙을 코드 수준으로 정리한다.

### 핵심 원칙

#### 3.1 즉각적인 반응

- 버튼은 `click`이 끝난 뒤가 아니라 `pointer-down` 순간부터 반응해야 한다.
- 드래그, 슬라이더, 서랍 같은 UI는 조작이 끝났을 때만 움직이지 않고 입력 과정 전체에 걸쳐 피드백을 제공해야 한다.
- 불필요한 debounce, timer, transition 대기를 입력 경로에 두지 않는다.

#### 3.2 직접 조작

- 드래그하는 요소는 손가락이나 포인터와 1:1로 붙어서 움직여야 한다.
- 요소를 잡은 지점의 offset을 유지해야 하며, 드래그 시작 시 요소의 중앙으로 위치가 튀면 안 된다.
- 포인터가 요소 바깥으로 나가도 추적이 계속되도록 Pointer Events와 `setPointerCapture`를 활용한다.

#### 3.3 중단 가능성

- 모든 애니메이션은 도중에 다시 잡거나 방향을 바꿀 수 있어야 한다.
- 입력을 transition이 끝날 때까지 잠그지 않는다.
- 새 애니메이션은 논리적인 목표 값이 아니라 현재 화면에 표시된 값에서 시작해야 한다.
- 사용자가 직접 조작하는 요소에는 재시작되는 keyframe보다 현재 위치와 속도를 계승할 수 있는 spring이 적합하다.

#### 3.4 Spring 기반 움직임

- 기본 UI는 overshoot가 없는 움직임을 사용한다.
- 사용자가 요소를 던지거나 튕긴 것처럼 실제 momentum을 만든 경우에만 약한 bounce를 사용한다.
- 고정된 재생 시간보다 현재 위치, 목표, 속도에 따라 움직이는 동작을 설계한다.

Apple 원칙을 웹 환경에 적용할 때 사용할 수 있는 예시는 다음과 같다.

```ts
// 기본 UI: overshoot 없음
animate(element, { y: 0 }, {
  type: "spring",
  bounce: 0,
  duration: 0.4,
});

// 사용자가 튕긴 동작: 약한 bounce 허용
animate(element, { y: target }, {
  type: "spring",
  bounce: 0.2,
  duration: 0.4,
});
```

#### 3.5 속도 전달

- 손가락을 놓은 순간의 속도를 이어지는 애니메이션의 시작 속도로 넘긴다.
- 드래그와 후속 애니메이션 사이의 속도가 갑자기 끊기면 눈에 보이는 이음매가 생긴다.

```text
relativeVelocity = gestureVelocity / (targetValue - currentValue)
```

Motion이나 Framer Motion처럼 절대 속도를 받는 라이브러리에서는 포인터의 실제 속도를 직접 넘길 수 있다.

#### 3.6 Momentum 예측

- 손을 놓은 현재 위치만 보고 가장 가까운 경계에 붙이지 않는다.
- 손을 놓은 속도를 이용해 예상 도착점을 계산한 후, 그 지점에서 가장 가까운 snap point를 선택한다.

#### 3.7 Rubber-banding

- 경계에서 요소를 단단히 멈추지 않고, 더 당길수록 강해지는 저항을 준다.
- 이는 “더 이상 콘텐츠가 없다”는 상태를 오류나 정지가 아닌 물리적인 감각으로 전달한다.

#### 3.8 공간적 일관성

- 오른쪽에서 나타난 패널은 오른쪽으로 사라져야 한다.
- Popover나 menu는 화면 중앙이 아니라 실행한 trigger를 기준으로 나타나야 한다.
- 사용자가 요소의 출발점과 도착점을 공간적으로 이해할 수 있도록 한다.

#### 3.9 접근성

- `prefers-reduced-motion`을 존중한다.
- 모션을 전부 제거하기보다 이동과 bounce를 줄이고 opacity나 색상 변화 같은 피드백은 유지할 수 있다.
- 모션, 사운드, 햅틱 피드백은 같은 순간에 발생하도록 맞춘다.

### 한 문장 요약

> 자연스러운 인터페이스는 현재 화면상의 값에서 움직임을 시작하고, 사용자의 속도를 이어받으며, 언제든 다시 잡아 방향을 바꿀 수 있어야 한다.

## 4. `emil-design-eng`

특정 Apple 스타일에 한정하지 않고 Emil의 전반적인 디자인 엔지니어링 철학을 적용한다.

주요 판단 기준은 다음과 같다.

- 애니메이션을 넣기 전에 정말 필요한지 판단한다.
- 자주 사용하는 동작일수록 애니메이션을 줄이거나 제거한다.
- UI 애니메이션은 대체로 300ms 이내로 유지한다.
- 등장과 퇴장에는 즉각 반응하는 강한 `ease-out` 계열을 사용한다.
- `transition: all`을 피하고 실제로 변하는 속성을 지정한다.
- `scale(0)`에서 시작하지 않고 `scale(0.9~0.97)`과 opacity를 결합한다.
- 버튼과 pressable 요소에는 즉각적인 `:active` 피드백을 제공한다.
- Popover는 trigger에서 커져 나오는 것처럼 `transform-origin`을 지정한다.
- 모션의 목적이 단지 “멋있어 보여서”라면 사용 빈도를 고려해 제거한다.

## 5. 애니메이션 관련 Skills의 차이

```text
기존 모션 하나 또는 diff를 리뷰
└─ review-animations

프로젝트 전체의 기존 모션을 감사하고 개선 계획 작성
└─ improve-animations

아직 움직이지 않는 UI에서 추가 기회 탐색
└─ find-animation-opportunities

원하는 효과의 정확한 이름 찾기
└─ animation-vocabulary

직접 구현하거나 UI 전반을 다듬을 때 판단 기준 제공
├─ emil-design-eng
└─ apple-design
```

### `review-animations`

하나의 변경사항이나 애니메이션 코드를 검토하는 review 전용 skill이다.

다음 항목을 엄격하게 확인한다.

1. 모션에 명확한 목적이 있는가
2. 사용 빈도에 비해 과도하지 않은가
3. easing과 duration이 적절한가
4. origin과 움직임이 물리적으로 자연스러운가
5. 애니메이션 도중에 중단할 수 있는가
6. `transform`과 `opacity` 위주로 GPU 친화적으로 구현됐는가
7. reduced motion과 pointer 환경을 고려했는가
8. 제품의 다른 모션과 일관되는가

결과는 `Before | After | Why` 표와 `Block` 또는 `Approve` 판정으로 출력한다.

### `improve-animations`

프로젝트 전체를 분석하는 advisor 성격의 skill이다. 직접 source code를 수정하지 않고, 다음 8개 기준으로 문제를 찾는다.

1. 목적과 사용 빈도
2. Easing과 duration
3. 물리성과 transform origin
4. 중단 가능성
5. 성능
6. 접근성
7. 일관성과 design token
8. 놓친 애니메이션 기회

분석 결과를 영향도와 구현 비용에 따라 정렬하고, 선택된 항목에 대해 다음 내용을 포함한 독립적인 실행 계획을 만든다.

- 정확한 파일 경로
- 현재 코드
- 목표 easing과 duration
- Spring 설정
- 수정 범위와 제외 범위
- 구현 순서
- 검증 방법

이렇게 작성된 계획은 디자인 판단력이 없는 다른 에이전트나 저비용 모델도 실행할 수 있다.

### `find-animation-opportunities`

현재 애니메이션이 없는 화면에서 모션을 추가할 후보를 찾는다. 다만 모션을 많이 추천하는 것이 목표가 아니라, 대부분의 후보를 걸러내는 것이 핵심이다.

각 후보는 다음 gate를 통과해야 한다.

1. 얼마나 자주 보이는가
2. 모션의 목적이 무엇인가
3. 표준 시간 안에 표현할 수 있는가
4. 기능 사용을 돕는가, 방해하는가

키보드 단축키나 하루에 수백 번 실행되는 동작에는 원칙적으로 애니메이션을 추천하지 않는다. 첫 실행, 성공, 완료, 빈 상태처럼 드물고 감정적인 순간에만 상대적으로 풍부한 모션을 허용한다.

### `animation-vocabulary`

사용자가 감각적으로 설명한 효과를 정확한 용어로 바꿔준다.

예:

| 사용자 표현 | 정확한 용어 |
| --- | --- |
| “iOS에서 끝까지 당기면 저항하다 돌아오는 효과” | Rubber-banding |
| “여러 항목이 조금씩 늦게 연속으로 나타나는 효과” | Stagger |
| “Popover가 누른 버튼에서 커져 나오는 효과” | Origin-aware animation |
| “하나의 도형이 다른 도형으로 자연스럽게 변하는 효과” | Morph |
| “목록의 위치가 바뀔 때 새 자리로 부드럽게 이동하는 효과” | Layout animation |

이 skill은 애니메이션을 설계하거나 구현하지 않고, AI나 디자이너에게 원하는 효과를 정확히 전달할 단어를 찾는 데 사용한다.

## 6. `pick-ui-library`

검증된 프론트엔드 라이브러리 목록에서 작업에 맞는 하나의 라이브러리를 추천한다.

주요 매핑은 다음과 같다.

| 작업 | 라이브러리 |
| --- | --- |
| 접근 가능한 dialog, popover, menu, select | Base UI |
| Command menu | cmdk |
| Toast와 notification | Sonner |
| OTP 입력 | input-otp |
| 범용 animation과 gesture | Motion |
| 숫자 animation | NumberFlow |
| 일반 chart | Recharts |
| 실시간 streaming chart | Liveline |
| Drag and drop | dnd kit |
| 긴 목록과 대형 table virtualization | Virtuoso |
| 상태 관리 | Zustand |
| 조건부 `className` 조합 | clsx |
| Tailwind variant API | cva |
| Theme과 dark mode | next-themes |

추천 전에 프로젝트의 `package.json`을 확인하고, 이미 사용하는 적절한 라이브러리가 있다면 불필요한 dependency 교체를 피하도록 설계되어 있다.

## 7. 추천 사용 순서

실제 프로젝트에서는 다음 흐름으로 사용할 수 있다.

1. `improve-animations`로 프로젝트 전체의 기존 모션을 감사한다.
2. `find-animation-opportunities`로 빠진 모션을 찾는다.
3. Gesture 중심 UI에는 `apple-design`을 적용한다.
4. UI 전반에는 `emil-design-eng`의 기준을 적용한다.
5. 적절한 구현 도구가 필요하면 `pick-ui-library`를 사용한다.
6. 실제 수정 후 `review-animations`로 변경사항을 검수한다.

## 8. 설치

저장소 전체를 설치하려면 다음 명령어를 사용한다.

```bash
npx skills@latest add emilkowalski/skills
```

저장소는 MIT 라이선스로 공개되어 있어 개인 및 상업 프로젝트에서 사용할 수 있다.

## 9. 활용 예시

```text
improve-animations를 사용해서 이 프로젝트의 전체 모션을 감사해줘.
영향도가 높은 문제부터 정리하고, 아직 코드는 수정하지 마.
```

```text
apple-design 원칙을 적용해서 이 drawer를 검토해줘.
중단 가능성, velocity handoff, rubber-banding을 중점적으로 봐줘.
```

```text
find-animation-opportunities를 사용해서 이 화면에서 모션이
실제로 도움이 되는 지점만 찾아줘. 최대 5개로 제한해줘.
```

```text
review-animations를 사용해서 현재 diff의 애니메이션을 리뷰해줘.
Before, After, Why 표와 최종 판정을 제공해줘.
```

## 10. 이 사례에서 배울 점

이 사례의 가치는 Apple 스타일의 애니메이션 자체에만 있지 않다.

팀이나 개인의 암묵적인 전문성을 skill로 만들려면 다음 내용을 명시해야 한다.

- 좋은 결과와 나쁜 결과를 구분하는 기준
- 각 판단의 이유
- 적용할 정확한 수치와 값
- 상황별 예외
- 작업 순서
- 수정 가능한 범위와 금지 범위
- 결과의 출력 형식
- 성공 여부를 검증하는 방법

이 구조는 디자인 외에도 다음과 같은 분야에 적용할 수 있다.

- 좋은 기획서의 기준
- 브랜드 카피와 문체
- 코드 리뷰 기준
- 장애 대응 절차
- 테스트 전략
- 보안 점검 기준
- 팀의 반복 업무와 의사결정 방식

즉, “감”이라고 부르던 전문성을 이유와 규칙으로 설명할 수 있다면, 그것을 AI가 반복해서 적용할 수 있는 조직 자산으로 만들 수 있다.

## 참고 자료

- [브런치 — 애플의 설계 원칙을 AI에게 주입하면 생기는 일](https://brunch.co.kr/@kim-yezi/41)
- [GitHub — emilkowalski/skills](https://github.com/emilkowalski/skills)
- [Apple Design Skill](https://github.com/emilkowalski/skills/blob/main/skills/apple-design/SKILL.md)
- [Emil Kowalski — Agents with Taste](https://emilkowal.ski/ui/agents-with-taste)
- [Apple Developer — Designing Fluid Interfaces, WWDC 2018](https://developer.apple.com/videos/play/wwdc2018/803/)
