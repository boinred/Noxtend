# 프롬프트 마이그레이션 멱등성 보장 및 SQL/C# 문법 학습 로그

- **작성일**: 2026-09-15
- **프로젝트**: Noxtend (Character Tuning Feature)
- **주요 개념**: 멱등성(Idempotency), DB 마이그레이션 버전 제어, SQL & C# 조건문

---

## 1. 멱등성(Idempotency) 개념
- **정의**: 동일한 작업을 한 번 실행하든 여러 번 반복 실행하든, 그 결과가 항상 동일하게 유지되는 성질.
- **적용 이유**:
  - DB 마이그레이션 실행 시 동일한 프롬프트가 중복 저장되는 것을 차단.
  - 사용자가 저장 버튼을 여러 번 연속 클릭해도 무의미하게 버전(`v1`, `v2`, `v3`...)만 상승하지 않도록 설정.

---

## 2. 문제 및 해결 요약

### 증상
- 마이그레이션 실행 시 이전 프롬프트 내용이 저장되지 않거나, 동일 내용에 대해 무의미한 버전 상승이 발생함.

### 원인
- 과거 마이그레이션 파일들이 시점별 정적 프롬프트 텍스트 대신 최신 `SeedPrompts` 헬퍼 메서드를 동적 호출했음.

### 해결 규칙
1. 과거 마이그레이션 파일에는 최신 메서드가 아닌 시점별 고정 프롬프트(`RewriteDescriptionsV3`, `CharacterGenerateV8` 등)를 1:1로 고정 매핑.
2. 마이그레이션 SQL 구문 및 서버 백엔드 코드에 멱등성 검사(`IF NOT EXISTS` / `System == request.System`)를 적용.

---

## 3. 코드 예시 및 문법 설명

### ① SQL 문법 (DB 마이그레이션 구문)
```sql
IF NOT EXISTS (
    SELECT 1 FROM [PromptVersions]
    WHERE [Kind] = 'Extract' AND [System] = N'프롬프트 내용...'
)
BEGIN
    UPDATE [PromptVersions] SET [IsActive] = 0 WHERE [Kind] = 'Extract';
    INSERT INTO [PromptVersions] ([Id], [Kind], [Version], ...) VALUES (...);
END
```
- **`IF NOT EXISTS (...)`**: 괄호 안의 데이터가 존재하지 않을 때만 이하 코드 실행.
- **`SELECT 1 FROM [PromptVersions] WHERE ...`**: 조건에 맞는 데이터가 DB에 이미 존재하는지 검색.
- **`BEGIN ... END`**: 실행할 명령문들을 하나의 그룹으로 묶음.
- **`UPDATE ... SET [IsActive] = 0`**: 기존 활성 프롬프트를 비활성 처리.
- **`INSERT INTO ... VALUES (...)`**: 새 프롬프트 데이터 1줄 추가.

### ② C# 문법 (백엔드 처리 로직)
```csharp
var activePrompt = await db.PromptVersions
    .FirstOrDefaultAsync(p => p.Kind == request.Kind && p.IsActive);

if (activePrompt != null &&
    activePrompt.System == request.System &&
    activePrompt.User == request.User)
{
    return activePrompt.ToDto();
}
```
- **`var activePrompt`**: 데이터를 담을 변수 선언.
- **`await`**: 데이터베이스 조회 완료 시까지 비동기로 대기.
- **`FirstOrDefaultAsync(...)`**: 조건에 부합하는 첫 번째 데이터를 가져옴 (없으면 `null`).
- **`p => p.Kind == request.Kind && p.IsActive`**: 요청한 종류와 같고 활성화된 데이터 필터링.
- **`if (activePrompt != null && ...)`**: 기존 최신 데이터가 있고 내용이 완전히 일치하는지 확인.
- **`return activePrompt.ToDto();`**: 내용이 같으면 새 생성을 중단하고 기존 프롬프트 즉시 반환.
