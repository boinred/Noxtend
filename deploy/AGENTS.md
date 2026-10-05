# 배포 작업 지침

`deploy/**`와 루트 CI 변경에 적용한다. 루트 `AGENTS.md`를 상속하고 [noxtend-workflow](../.agents/skills/noxtend-workflow/SKILL.md)의 배포·검증 자료를 읽는다. `docs/infrastructure.md`, `deploy/k8s/README.md`, API 시작 코드·Dockerfile이 확인 자료다.

## 공개·승인 경계

- 현재 API는 인증이 없다. 기본 개발 경로는 Docker Desktop Kubernetes의 `noxtend` namespace·`localhost:18080` 포트 포워딩이며 공개 API 배포는 지원하지 않는다.
- `azure/`는 실행자의 공인 IP를 API·SCM에 허용하고 SQL에 실행자 IP·`AllowAzureServices`를 허용하는 내부 테스트 정의다. 사설망·사용자 인증 구현으로 간주하지 않는다.
- Azure Static Web App에는 접근 제한이 선언되어 있지 않다. Frontend 공개와 API 접근을 별도로 확인하며 CORS·IP 제한을 인증으로 해석하지 않는다.
- 문서·코드·정적 검증 요청을 실제 배포 요청으로 확대하지 않는다. `local-up.sh`·`--skip-build`는 Secret/PVC/Deployment 적용·API 재시작이며 Azure 스크립트는 유료 리소스 생성·변경과 기본 Frontend 배포까지 수행한다. 실제 실행은 승인된 범위에서만 한다.
- SKU·지역·API 버전·도구·Kubernetes context·Azure subscription·대상·비용을 실행 전에 확인한다. 템플릿이나 과거 기록으로 배포·비용·정상 동작을 확정하지 않는다.

## 데이터·비밀값 보존

- SQL이 정본이고 Redis는 디스패치 수단이다. 로컬 Redis는 PVC 없이 Sweeper가 DB에서 재적재하므로 스트림 영속성을 새 계약으로 추가하지 않는다.
- `mssql-data`·`azurite-data`·`api-dataprotection` PVC를 보존한다. Data Protection 키는 로컬 `/keys`, Azure `/home/keys`와 `SetApplicationName("Noxtend")`를 유지한다. DB 백업만으로 암호화된 공급자 키를 복원할 수 없다.
- `k8s/secrets.example.yaml`로 Secret을 만들고 실제 `k8s/secrets.yaml`은 Git에서 제외한다. 실제 파일을 예시로 덮어쓰지 않으며 SQL 비밀번호·`ConnectionStrings__Db`를 일치시킨다. 비밀값을 출력·커밋하지 않는다.
- 관리자 등록 공급자 키는 Data Protection으로 암호화하여 DB에 저장한다. 연결 문자열·키·SWA 배포 토큰을 클라이언트 번들·문서·로그에 넣지 않는다.
- `Llm__UseFake`는 텍스트·이미지·메시 전체에 적용된다. 현재 매니페스트 기본값 `false`는 유료 호출 가능성이 있으므로 일반 기능 검증에는 Fake를 사용한다.
- API 기동은 `MigrateAsync`로 DB를 변경할 수 있다. 단일 replica·`Recreate` 변경은 동시 migration·RWO·Worker 중복 실행을 검토하며 실제 DB 적용을 테스트 대체로 사용하지 않는다.

## 상태 확인·복구 경계

- `/health/live`는 프로세스·liveness/readiness·Azure health check, `/health`는 DB·Redis·Blob과 구조화된 503 확인이다. Probe를 `/health`로 교체하지 않는다. DB 프로브의 `master` `SELECT 1` 성공은 앱 스키마·migration·공급자 설정 완료 증거가 아니다.
- 이미지 rollback은 DB migration을 되돌리지 않는다. DB·Blob·Data Protection 키 호환성을 함께 확인한다. 같은 `:local`·기본 `latest` 태그는 이전 이미지 보존을 보장하지 않는다.
- namespace·PVC·Azure resource group 삭제는 데이터·키·리소스 제거다. 배포 실패의 자동 복구 수단으로 사용하지 않는다. 서비스 중지만 필요하면 PVC를 보존하는 Deployment 축소를 사용한다.
