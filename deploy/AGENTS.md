# 배포 작업 지침

`deploy/**`와 `.github/**` CI·보호 설정 변경에 적용한다. 루트 `AGENTS.md`와 [noxtend-workflow](../.agents/skills/noxtend-workflow/SKILL.md)의 배포·검증 자료를 따른다.

## 공개·승인 경계

- 이 공개 GitHub 저장소는 Azure 배포와 Azure Pipelines를 사용하지 않는다. 공개 소스와 서비스 공개는 별개다. API에 인증이 없어 지원 실행 경로는 Docker Desktop Kubernetes의 `noxtend` namespace·`localhost:18080` 포트 포워딩이다.
- 문서·코드·정적 검사 요청을 실제 배포로 확대하지 않는다. `local-up.sh`·`--skip-build` 모두 Secret/PVC/Deployment 적용과 API 재시작을 수행한다. 실행 전에 도구·현재 Kubernetes context·대상을 확인한다.
- GitHub CI는 `pull_request`와 main `push`에서 GitHub hosted runner·읽기 전용 토큰으로 실행한다. 포크 PR에 Secret·실 공급자 키·자체 호스트 실행기를 제공하지 않고 `pull_request_target`에서 PR 코드를 실행하지 않는다. 유료 smoke는 제외한다.
- main 보호의 필수 검사 이름은 `.github/workflows/ci.yml`과 `.github/main-protection.json` 양쪽을 함께 갱신하고, 실제 원격 검사와 보호 설정을 재조회한다. 관리자 우회·강제 push·main 삭제로 검사 실패를 해결하지 않는다.

## 데이터·비밀값 보존

- SQL이 정본이고 Redis는 디스패치 수단이다. 로컬 Redis는 PVC 없이 Sweeper가 DB에서 재적재하므로 스트림 영속성을 새 계약으로 추가하지 않는다.
- `mssql-data`·`azurite-data`·`api-dataprotection` PVC를 보존한다. Data Protection 키의 `/keys`·`SetApplicationName("Noxtend")`를 유지한다. DB 백업만으로 암호화된 공급자 키를 복원할 수 없다.
- `k8s/secrets.example.yaml`로 실제 `k8s/secrets.yaml`을 만들고 Git에서 제외한다. 예시로 실제 값을 덮어쓰지 않으며 SQL 비밀번호와 `ConnectionStrings__Db`를 일치시킨다.
- 관리자 등록 공급자 키는 Data Protection으로 암호화하여 DB에 저장한다. 연결 문자열·키·인증 헤더를 클라이언트 번들·문서·로그·배포 기록에 넣지 않는다.
- `Llm__UseFake`는 텍스트·이미지·메시 전체에 적용된다. 현재 매니페스트의 `false`는 유료 호출 가능성이 있으므로 일반 검증에는 Fake를 사용한다.
- API 기동의 `MigrateAsync`는 DB를 변경할 수 있다. 단일 replica·`Recreate` 변경 시 동시 migration·RWO·Worker 중복 실행을 검토하며 실제 DB 적용을 테스트 대체로 사용하지 않는다.

## 버전·상태 확인과 복구

- 로컬 이미지는 OCI revision·dirty label과 Docker image ID를 기록하고 고유 태그로 적용한다. `:local`은 재사용 alias다. `--skip-build`는 기존 이미지 메타데이터를 사용하고 출처가 없으면 기존 포워딩 종료·apply 전에 재빌드를 안내한다.
- 성공한 rollout·Pod 확인·health 뒤에만 `deploy/.local/deployments/`에 Backend 이력과 `latest.json`을 저장한다. 기록은 Git에서 제외하며 Secret·환경 변수·전체 health 본문을 넣지 않는다. 실패가 이전 성공 기록을 덮지 않게 한다.
- Frontend는 별도 `pnpm dev`로 실행한다. Backend 이미지 기록을 Frontend 버전·현재 클러스터 상태의 자동 추적 또는 검증 완료로 설명하지 않는다.
- `/health/live`는 프로세스 probe, `/health`는 DB·Redis·Blob 확인이다. Probe를 `/health`로 바꾸지 않는다. DB 프로브의 `master` 조회 성공은 앱 스키마·migration 완료 증거가 아니다.
- 이미지 rollback은 DB migration을 되돌리지 않는다. DB·Blob·Data Protection 키 호환성을 함께 확인한다. 로컬 태그와 JSON은 삭제한 이미지를 복원하는 registry가 아니다.
- namespace·PVC 삭제는 데이터·키 제거다. 배포 실패의 자동 복구 수단으로 사용하지 않는다. 서비스 중지만 필요하면 PVC를 보존하는 Deployment 축소를 사용한다.
