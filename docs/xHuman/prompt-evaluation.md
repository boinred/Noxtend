# 프롬프트 평가 절차

프롬프트·모델·응답 해석 변경의 계약 준수와 출력 품질을 구분해 평가한다. [$agentic-eval](../../.agents/skills/agentic-eval/SKILL.md)이 이 절차를 사용하며 공급자·보안·호출 흐름은 [공급자와 프롬프트](providers-and-prompts.md)가 정본이다.

## 기준선과 입력

- 변경한 공정·프롬프트 버전·비교 목적을 정한다. 같은 이미지·대상 파츠·설정으로 비교하고 다른 입력·모델·파라미터가 섞이면 차이를 표시한다. 실행 횟수는 평가 목적과 승인된 예산으로 정한다.
- 기존 업로드와 `Noxtend.Tuning.Domain/Golden/GoldenSample.cs`의 기대 메모를 재사용한다. 새 데이터셋·저장 구조를 기본 전제로 추가하지 않는다.
- `PromptVersion`과 실제 호출에 사용된 snapshot·공급자·모델·입력을 연결한다. 활성 프롬프트가 과거 작업에 사용된 버전이라고 가정하지 않는다.
- `GoldenSample.ExpectedNote`와 `Verdict.IsPass`·`Memo`는 사람의 기대와 최신 판정이다. 기존 자유 메모·합불 계약을 자동 점수·판정 이력 구조로 변경하지 않는다. 비교에는 각 작업의 원본 결과와 판정 시점을 함께 확인한다.

## 평가 항목

요청과 공정에 맞는 항목만 선택한다. 임의의 종합 점수·통과율 목표를 기본값으로 만들지 않는다.

| 항목 | 근거와 판정 |
| --- | --- |
| 응답 계약 | JSON 파싱·스키마·필수값·타입·enum·단계별 도메인 검증 |
| 이름 환각·누락·중복 | 재작성 대상 이름과 실제 결과를 대조하고 현재 허용된 1:1 보정·모호한 응답 거부 규칙 확인 |
| 의미 품질 | 골든 기대 메모·이미지·대상 파츠와 사람의 판정 근거. 이름·JSON 준수만으로 이미지 분석 정확성 판정 불가 |
| 실패·취소·재시도 | 잘못된 응답의 실패 처리·취소 후 결과 반영·재시도 범위의 기존 테스트 |
| 비용·지연 | 호출 내역에서 확인한 사용량·등록 단가·소요 시간. 확인 불가능한 값은 미확인 |

## 오프라인 회귀

코드 경로는 저장소 루트 기준이다.

- 템플릿·변수·구성: [PromptVariableInjectionTests](../../apps/backend/Noxtend.Tests/Application/PromptVariableInjectionTests.cs), [PromptCompositionTests](../../apps/backend/Noxtend.Tests/Infrastructure/PromptCompositionTests.cs)
- 재작성 이름과 응답: [Stages.cs](../../apps/backend/Noxtend.Application/Stages/Stages.cs)의 `RewriteDescriptionsStage`, [RewriteDescriptionsStageTests](../../apps/backend/Noxtend.Tests/Application/RewriteDescriptionsStageTests.cs)
- 모델 응답 어댑터·사용량: [OpenAiProviderTests](../../apps/backend/Noxtend.Tests/Application/OpenAiProviderTests.cs), [GoogleProviderTests](../../apps/backend/Noxtend.Tests/Application/GoogleProviderTests.cs), [ImageProviderUsageTests](../../apps/backend/Noxtend.Tests/Application/ImageProviderUsageTests.cs)의 가짜 HTTP 패턴
- 프롬프트 저장·활성화·기존 DB 갱신: 해당 seed·migration·persistence 테스트

기존 실패 출력이 있으면 개인정보·비밀값을 제거해 회귀 입력으로 사용한다. 테스트는 현행 계약의 정확한 처리와 실패 거부를 검증한다. Fake·저장된 응답의 통과는 실제 모델의 품질 향상이나 실 API 실행 증거가 아니다.

실행 명령·전체 회귀·Docker 요구는 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)를 따른다. 문서·평가 설계만 변경하면 링크·경로·지침 충돌을 확인하며 애플리케이션 회귀를 일괄 실행하지 않는다.

## 실제 출력 비교

- 보존된 결과부터 비교하고 새 실행이 필요하면 공급자·모델·입력·호출/시간/비용 상한을 실행 전 정한다. 유료 호출은 사용자가 승인한 범위에서만 수행한다.
- JSON·이름·누락 검사를 먼저 확인하고 의미 품질은 사람이 동일한 기대 기준으로 판정한다. LLM-as-judge가 요청되면 평가 모델·프롬프트와 사람 판정 대비 한계를 표시하며 그 점수만으로 성공·자동 승인을 결정하지 않는다.
- 품질 개선을 위한 반복 호출은 명시적으로 요청된 경우에만 수행한다. 상한 도달·개선 정체·판정 불확실·실행 오류를 중단 조건으로 정하고 실패 출력도 결과에 포함한다.
- `Verdict` 기록은 상태 변경이다. 분석 요청만으로 DB에 판정을 기록하거나 프롬프트를 활성화하지 않는다.

## 결과 보고

입력·기준/후보 버전·실행 모드·검사 또는 판정 근거·실행/실패/건너뜀 수·확인된 비용/지연·남은 제약을 기록한다. 단일 사례·LLM 점수를 전체 품질 개선으로 일반화하지 않는다. 프롬프트 원문·이미지·base64·키 대신 필요한 식별자와 마스킹한 근거를 사용한다.

## 확인 기준과 미확인

- 마지막 확인: 2026-10-08, revision `d2cad8d`. 코드 지도 도입 때 문서의 코드 경로와 상대 링크 존재를 자동 대조했다. 서술된 규칙 전체를 코드와 다시 대조하지는 않았다.
- 미확인: 이 문서는 절차다. 실제 모델 품질 비교 결과는 담지 않으며 유료 실행은 별도 승인 범위에서 기록한다.
