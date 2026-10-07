# 인프라·배포 작업 절차

명령과 경로는 저장소 루트 기준이다. 실제 실행의 승인·공개·데이터 보존 경계는 `deploy/AGENTS.md`를 먼저 따른다.

## 변경 전 확인

- `docs/infrastructure.md`·`deploy/k8s/README.md`, 대상 스크립트/매니페스트/Bicep, API 시작 코드·`apps/backend/Dockerfile`을 읽는다. 로컬 구성은 API·SQL Server·Redis·Azurite의 4개 Deployment다.
- 루트 CI는 `azure-pipelines.yml`과 실제 참조 스크립트를 읽는다. Slack 전송은 실제 외부 행동이며 DryRun·정적 확인과 구분한다.

## 검증과 실행의 구분

명령은 저장소 루트 기준이다. 문서·문법 확인을 배포 완료로 보고하지 않는다.

| 목적 | 명령 또는 절차 | 효과와 한계 |
|---|---|---|
| Shell 문법 | `bash -n deploy/local-up.sh deploy/azure/deploy.sh` | 로컬 파싱만 수행, 스택 기동 없음 |
| Bicep 문법 | 설치된 `bicep build deploy/azure/main.bicep --stdout` | 로컬 컴파일, Azure 리소스 미생성. 도구가 없으면 설치와 리소스 검증을 구분 |
| Kubernetes 확인 | context·namespace·대상 매니페스트 확인 후 `kubectl apply --dry-run=client --validate=false -f <파일>` | apply하지 않지만 discovery 등 클러스터 조회가 필요할 수 있음. 정책·동작·보안 검증을 대신하지 않음 |
| 이미지 검증 | `docker build -t noxtend-api:local apps/backend` | 로컬 이미지 생성·베이스 이미지 다운로드 가능, 클러스터 배포는 아님 |
| 로컬 기동 | `deploy/local-up.sh [--skip-build] [--forward \| -d \| --detach]` | 실제 Secret/PVC/Deployment 적용 및 API 재시작. `--skip-build`도 상태 변경이며 이미지 갱신을 생략함. `--forward`는 터미널 유지, `-d`/`--detach`는 확인 후 포트 포워딩만 백그라운드에 남김 |
| Azure 변경 예측 | `az deployment group what-if` | Azure 조회와 인증 필요. 실제 리소스 생성과 별개이며 사전 권한·대상 확인 필요 |
| Azure 배포 | `deploy/azure/deploy.sh` | 공인 IP 조회, 유료 리소스 생성·변경, ACR 빌드, API 재시작, 기본적으로 프론트엔드 배포까지 수행 |

`local-up.sh`는 현재 Kubernetes context를 강제하지 않는다. 실행 전에 `kubectl config current-context`가 의도한 `docker-desktop`인지 확인한다. Azure 실행 전에는 subscription·resource group·지역·이름 접두어·이미지 태그·허용 IP·비용을 확인한다. 문서 수정이나 코드 검증 요청을 실제 배포 요청으로 확대하지 않는다.

## 변경 시 확인 사항

- 로컬 API 이미지는 `noxtend-api:local`, `imagePullPolicy: Never`다. kind 기반 Docker Desktop에서는 `desktop-control-plane`의 containerd 이미지 저장소로 반입해야 한다. 이미지 이름·반입 방식·재시작을 함께 확인하고 `--skip-build`로 새 코드가 반영되었다고 보고하지 않는다.
- API는 시작 시 Kestrel 바인딩 전에 EF Core `MigrateAsync`를 실행한다. DB 연결 또는 마이그레이션 실패는 API 기동 실패가 될 수 있다. 현재 단일 replica와 `Recreate` 전제를 바꾸기 전에 동시 마이그레이션·RWO 볼륨·Worker 중복 실행을 검토한다.
- DB 스키마와 시드 데이터 변경은 Backend 지침을 함께 따른다. 기존 DB에 적용할 migration과 데이터 보존·복구 방법을 검토하고 실제 DB 적용을 테스트 대체로 사용하지 않는다.
- 새 Backend 프로젝트나 native 의존성은 `apps/backend/Dockerfile`의 restore/publish 및 runtime 패키지에도 반영되어야 한다. 호스트 `dotnet build` 성공만으로 컨테이너 빌드를 확인했다고 하지 않는다.
- 연결 문자열, `ASPNETCORE_ENVIRONMENT`, Data Protection 경로, lease/sweep, Fake 설정의 로컬/Azure 차이를 명시한다. Azure Redis는 암호화된 `10000` 포트이며 로컬 Redis의 `6379` 설정을 복사하지 않는다.
- 프론트엔드 API 주소는 `VITE_API_BASE_URL`로 빌드에 반영된다. Azure 배포 스크립트는 API URL을 넣어 프론트엔드를 다시 빌드하며 `SKIP_FRONTEND=1`은 그 단계와 프론트엔드 배포를 생략한다. Production CORS는 App Service의 SWA origin 설정이므로 주소 변경 시 함께 확인한다.

## 승인된 기동 확인·복구

- rollout·로그의 migration 결과, health, Frontend API 주소/CORS, 실제 이미지·환경을 확인한다. 필요한 최소 기능은 Fake로 확인하며 미실행 항목을 보고한다.
- 기본 포트 포워딩은 스크립트 종료 시 사라진다. 사용자 터미널에서 `-d`/`--detach`로 백그라운드에 남기거나 `--forward` 또는 `kubectl -n noxtend port-forward svc/api 18080:8080`으로 유지한다. `-d`는 PID·로그 경로·종료 명령을 출력하며 API 파드 재시작 후 자동 재연결하지 않는다. 에이전트의 시간 제한 있는 백그라운드 실행에 맡기지 않는다.
- 스크립트 회귀는 `python3 deploy/test_local_up.py`로 확인한다. 임시 디렉터리의 가짜 Docker·kubectl·curl을 사용하며 실제 배포·AI 호출 없이 detach 수명·SIGHUP·포트 충돌·실패 정리를 검증한다.
- 복구 전 기존 이미지·환경과 DB·Blob·Data Protection 키 호환성을 확인한다. 식별 가능한 이미지 태그/digest·적용 migration을 배포 이력에 기록한다.

정적 검증 범위와 완료 보고는 [verification.md](verification.md)를 따른다.
