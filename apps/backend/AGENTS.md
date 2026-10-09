# Backend 개발 지침

`apps/backend/**`에 적용한다. 루트 `AGENTS.md`를 상속하고 작업 전에 [noxtend-workflow](../../.agents/skills/noxtend-workflow/SKILL.md)의 Backend·검증 자료를 읽는다. C# 코딩 스타일은 기존 파일의 스타일을 유지한다.

## 계층 경계

API와 Worker는 같은 ASP.NET Core 프로세스에서 실행된다. 실제 등록·계약·상태·저장 코드와 `.csproj`를 기준으로 판단한다.

| 프로젝트 | 책임 및 허용 프로젝트 참조 |
| --- | --- |
| `Noxtend.Domain` | 파이프라인 엔티티·상태 전이·Port. 다른 프로젝트 참조 없음 |
| `Noxtend.Application` | 유스케이스·오케스트레이션. `Domain` 참조 |
| `Noxtend.Tuning.Domain` | 프롬프트·호출 내역·골든 샘플·단가. `Domain` 참조 |
| `Noxtend.Tuning.Application` | 튜닝 유스케이스. `Tuning.Domain`, `Domain` 참조 |
| `Noxtend.Infrastructure` | EF·Redis·Blob·공급자·보안 어댑터. 두 Domain·두 Application 참조 |
| `Noxtend.Api` | HTTP 계약·DI 조립·Worker 호스팅. `Application`, `Infrastructure` 참조 |
| `Noxtend.Tests` | xUnit 테스트. 검증 대상 프로젝트 참조 |

- 파이프라인 Domain·Application은 `Noxtend.Tuning.*`를 직접 참조하지 않는다. `Domain/Ports`와 `Infrastructure/Llm/TuningPortAdapters.cs`로 연결한다.
- Domain에 EF Core·Redis·Blob SDK·HTTP 어댑터를 넣지 않는다. 현재 허용 패키지는 `Microsoft.Extensions.Logging.Abstractions`다. 이미지 공급자 어댑터의 Blob 저장은 유스케이스 책임이다.

## 상태·동시성·재처리

- SQL의 작업·공정·실행 상태가 정본이다. 메시지 존재나 Worker 메모리로 완료·재시도를 결정하지 않는다. 상태 변경은 `PipelineJob`·`PipelineTask` 메서드로 수행하고 `PipelineJob.IsReadyToRun`의 의존 공정·`NotBefore` 판정을 공유한다.
- 중복 메시지·Ack 실패·프로세스 종료·리스 만료는 정상 복구 경로다. 종료 공정을 되살리지 않고 취소 이후 결과 반영을 차단한다. 협조적 취소는 이미 발생한 공급자 과금 취소를 보장하지 않는다.
- Job·Task·MeshRun의 동시성 보호와 검수 revision을 유지한다. 경합 뒤 무조건 재호출하지 않으며 `DbContext`를 병렬 작업이나 Worker 전체 수명에 공유하지 않는다.
- 3D 제출 직전 상태 저장·`ProviderTaskId` 재사용·timeout 후 조회 재개를 유지한다. `SubmissionUnknown`은 공급자 측 확인 없이 새로 제출하지 않는다. 인증 실패·크레딧 부족·크기 초과를 일괄 재시도하지 않는다.

## 계약·보안·비용

- JSON은 응답 DTO와 `{ data, error }`, `ApiResults`의 오류·HTTP 매핑을 사용한다. 기존 204·이미지/메시 파일·파일 미존재의 빈 404는 계약 예외다. 엔티티를 직접 직렬화하거나 예외 응답에 봉투를 강제하지 않는다.
- 업로드 크기·형식·디코딩을 서버에서 검증한다. Blob 키·다운로드 파일명은 서버가 생성하며 사용자 문자열을 경로나 HTTP 헤더에 직접 넣지 않는다.
- 공급자 키는 Data Protection으로 보호한다. 평문·암호문을 응답·로그에 넣지 않고 `ProviderResponse` 마스킹·key ring 영속성을 유지한다.
- 호출 내역·보고서의 프롬프트/텍스트 응답은 민감 내용을 확인한다. 이미지·base64·인증 헤더·전체 외부 요청 본문을 내역에 추가하거나 외부 오류 원문을 API 응답으로 전달하지 않는다.
- 3D 다운로드의 허용 host·크기 상한을 유지하고 자체 Blob으로 옮긴다. 임의 URL을 프록시하거나 공급자 링크를 영구 결과로 저장하지 않는다.
- 키 암호화·CORS·GUID는 접근 제어가 아니다. 현재 인증·인가가 없으므로 네트워크 공개는 [배포 지침](../../deploy/AGENTS.md)을 따른다.
- 일반 검증은 Fake·가짜 HTTP 응답을 사용한다. 프롬프트 버전·공급자·모델·사용량 맥락을 유지하고 미등록 단가·확인 불가능한 토큰/크레딧을 0으로 바꾸지 않는다.

## 데이터 보존

- 이미 적용된 migration은 덮어쓰지 않고 후속 migration으로 수정한다. 시드는 운영자 등록값을 무조건 덮어쓰지 않는다. 레거시 저장 JSON 호환 규칙을 API·공급자 출력 검증에 전역 적용하지 않는다.
- `Program.cs`는 기동 시 `MigrateAsync`를 실행한다. 실제 DB 기동·migration 적용·삭제·재초기화는 요청 범위와 배포 지침을 확인한다.
- Testcontainers fixture를 개발·운영 DB나 Redis로 대체하지 않는다. `FLUSHDB`는 격리된 테스트 컨테이너에서만 실행한다. 유료 smoke는 명시적 검증 요청에서만 실행하며 조기 return을 실 공급자 검증으로 계산하지 않는다.
