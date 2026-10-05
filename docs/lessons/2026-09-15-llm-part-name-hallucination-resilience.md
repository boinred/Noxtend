# Lesson: LLM 파츠 이름 환각(Hallucination) 방지를 위한 3중 방어막 아키텍처

- **날짜**: 2026-09-15
- **분류**: 백엔드 / LLM 파이프라인 / 에러 복원력 (Resilience)

## 1. 증상 (Symptom)
- 파츠 검수 단계에서 사용자가 파츠 이름을 수동 추가/수정(예: `'상의'`)하고 승인한 후, 서술 재작성(`RewriteDescriptions`) 파이프라인 실행 시 `PART_UNKNOWN_REFERENCE` 예외가 발생함.
- 백엔드가 파이프라인을 3회 자동 재시도함에도 3회 연속 실패하며 전체 작업이 `Failed` 처리됨.

## 2. 근본 원인 (Root Cause)
- 비결정적인 LLM(AI 모델)이 응답 생성 시 요청에 명시된 exact 파츠 이름(`'상의'`)을 그대로 리턴하지 않고 영문 카테고리명(`'Top'`)이나 임의의 요약 명칭으로 바꾸어 리턴함.
- 백엔드 도메인 유효성 검사(`PipelineJob.ValidateDescriptionRewrites`)가 DB에 등록된 파츠 이름 목록과 1:1 교차 검증을 수행하기 때문에 존재하지 않는 이름(`'Top'`)을 발견하자 에러를 발생시킴.
- 재시도를 하더라도 동일한 AI 모델이 동일한 변형을 응답하여 3회 연속 실패함.

## 3. 해결책 및 학습 규칙 (Lesson & Rule)

### ① LLM 기반 파이프라인에는 프롬프트에만 의존하지 말고 백엔드 정규화(Sanitizing) 레이어를 둔다
LLM은 본질적으로 비결정적 시스템이므로 프롬프트 지시문만으로는 100% 동일한 고정 문자열 출력을 보장할 수 없다.<br>
백엔드 입구 stage(`Interpret`)에서 외부 LLM 출력을 도메인 모델 계약에 맞게 안전하게 번역/보정해주는 스마트 매핑(Smart Fallback Mapping) 레이어가 필수적이다.

### ② 3중 방어막 (Defense in Depth) 아키텍처 적용
1. **1차 방어**: 프롬프트 지시문 강화 (System Prompt에 Exact Target Name 변경 금지 명시)
2. **2차 방어**: Strict JSON Schema (OpenAI API level `name` enum 제약)
3. **3차 방어**: 백엔드 스마트 매핑 레이어 (`RewriteDescriptionsStage.Interpret` 1:1 보정)

### ③ 도메인 불변성과 애플리케이션 보정의 분리
핵심 도메인의 검증 규칙(`ValidateDescriptionRewrites`)을 삼키거나 해제하지 않고, 애플리케이션 단계(`Stage.Interpret`)에서 안전하게 정제하여 넘김으로써 도메인 안전성과 시스템 회복력을 모두 만족시킨다.
