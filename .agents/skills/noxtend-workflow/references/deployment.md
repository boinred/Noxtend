# GitHub CI·로컬 배포 절차

저장소 루트 기준이다. 공개·승인·데이터 보존 경계는 [배포 지침](../../../../deploy/AGENTS.md)을 따른다. Azure 배포는 사용하지 않는다.

## 변경 전 확인

- CI: `.github/workflows/ci.yml`·실제 검사 명령·`.github/main-protection.json`.
- 로컬 배포: `docs/infrastructure.md`·`deploy/k8s/README.md`·`deploy/local-up.sh`·`deploy/local-deployment.py`·매니페스트·API 시작 코드·Dockerfile.
- 지원 구성은 API·SQL Server·Redis·Azurite의 4개 Deployment다. Frontend는 별도 Vite 실행이다.

## GitHub flow와 main 보호

최신 main에서 기능별 작업 브랜치를 만들고 독립 리뷰·PR 검사를 거쳐 main에 병합한다. develop/release 상시 브랜치는 두지 않는다. CI는 main 대상 PR과 main push에서 경로 필터 없이 `Repository checks`·`Frontend`·`Backend`를 실행한다. 포크 PR은 읽기 전용 토큰·Fake·격리 Testcontainers만 사용한다.

보호 설정은 PR·세 검사·최신 main 반영·대화 해소를 요구하고 관리자에게도 적용한다. 강제 push·삭제는 금지한다. GitHub 승인 인원은 1인 개발을 위해 0명이며 독립 에이전트 리뷰를 대체하지 않는다. 별도 사람 리뷰어를 운영하게 되면 승인 인원을 재검토한다.

보호를 수정할 때는 의도한 GitHub 저장소와 실제 검사 이름을 먼저 확인하고, 사용자 승인 범위에서 다음 명령을 실행한 뒤 조회한다. 파일 변경만으로 원격 설정이 바뀌지는 않는다.

```bash
gh api --method PUT repos/boinred/Noxtend/branches/main/protection --input .github/main-protection.json
gh api repos/boinred/Noxtend/branches/main/protection
```

## 정적 검증과 실제 실행

| 목적 | 명령 | 범위 |
| --- | --- | --- |
| Shell 문법 | `bash -n deploy/local-up.sh` | 기동 없음 |
| Python 문법 | `python3 -m py_compile deploy/local-deployment.py deploy/test_local_up.py` | 기동 없음 |
| 배포 회귀 | `python3 deploy/test_local_up.py` | 격리 PATH·Fake·fixture 프로세스만 사용 |
| CI 문법 | `go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12 .github/workflows/ci.yml` | 선택적 로컬 도구, 실제 GitHub 실행과 별개 |
| 이미지 빌드 | `docker build -t noxtend-api:local apps/backend` | 로컬 산출물만 생성, 추적 label 없음 |
| 로컬 기동 | `deploy/local-up.sh [--skip-build] [--forward \| -d \| --detach]` | 실제 Secret/PVC/Deployment 적용·API 재시작 |

직접 `docker build`한 label 없는 이미지는 `--skip-build`로 추적할 수 없다. 최초 한 번은 `deploy/local-up.sh -d`로 빌드한다. 현재 context를 강제하지 않으므로 실행 전 `kubectl config current-context`가 의도한 `docker-desktop`인지 확인한다. 스크립트는 실행 중 context를 고정한다.

## 로컬 버전 추적

- 새 빌드는 full Git revision·저장소 전체 dirty 여부를 label로 넣고 Docker image ID를 확인한다. dirty는 Backend 변경만을 뜻하지 않는다. 고유 revision/dirty/image ID 태그를 containerd로 반입하고 매니페스트에 렌더한다. `api.yaml`의 `:local`은 템플릿 alias다.
- `--skip-build`도 실제 적용·재시작을 수행하며, 현재 checkout이 아니라 기존 이미지 label을 읽는다. 출처가 없거나 잘못되면 rebuild 안내로 종료하고 기존 포워딩을 보존한다.
- rollout 뒤 Deployment·ReplicaSet·Pod 소유 관계, 단일 active Pod와 api 컨테이너의 기대 이미지·ready·runtime imageID를 확인한다. health 전후 같은 Pod인지 재확인한다. Docker config ID와 Pod runtime imageID를 같은 값이라고 가정하지 않는다.
- 성공 기록은 `deploy/.local/deployments/latest.json`, 이전 성공은 같은 디렉터리의 UTC timestamp·Docker ID 이름 JSON이다. 안전 필드는 `recordedAt`, `context`, `namespace`, `revision`, `dirty`, `imageTag`, `imageID`, `deploymentUID`, `podName`, `podUID`, `podImageID`다.
- 이 기록은 Backend 성공 당시 정보다. 별도 Vite의 Frontend revision, 이후 수동 클러스터 변경, 실 AI 검증, DB rollback을 보장하지 않는다.

```bash
cat deploy/.local/deployments/latest.json
```

## 포워딩·데이터 보존

- 기본 포워딩은 종료 시 사라진다. `-d`는 PID·로그·종료 명령을 출력하고 백그라운드에 남긴다. `--forward`는 터미널을 유지한다. API Pod 재시작 뒤 자동 재연결하지 않는다.
- 기동 전 18080 listener 전체를 확인하고 현재 사용자·kubectl·Noxtend 대상이 확인된 포워딩만 TERM 종료한다. 명시 context는 현재 context와 일치해야 하며 context 생략은 기존 스크립트 명령 형태에 한해 허용한다. 미확인 listener가 있으면 종료·build·apply 전에 실패한다. 포트 해제는 최대 5초 기다린다.
- 단일 replica·Recreate·API 시작 migration·세 PVC·Data Protection 경로를 보존한다. 이미지 rollback은 DB rollback이 아니다. namespace/PVC 삭제로 배포 실패를 복구하지 않는다.

검증 명령·완료 보고는 [verification.md](verification.md)를 따른다.
