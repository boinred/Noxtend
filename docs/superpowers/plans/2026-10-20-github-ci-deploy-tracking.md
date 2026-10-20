# GitHub 검사·main 보호·로컬 배포 추적

**Goal:**

승인된 GitHub flow를 유지하면서 공개 저장소의 PR 검사, main 보호, 로컬 배포 버전 추적을 적용한다. Azure 배포는 사용하지 않는다.

**Spec:** [승인안 기록](../specs/2026-10-20-github-ci-deploy-tracking-design.md)

사용자 승인: main과 기능 브랜치, PR 자동 검사, main 보호, 배포 버전 기록 보완. 별도 develop/release 브랜치 및 Azure 배포 없음.

## Global Constraints

- 실행 방식: 작업별 구현·독립 리뷰(Subagent-driven).
- 기존 Frontend lint·포맷·계층 검사, Backend 스타일·테스트·격리 Testcontainers 유지. 유료 smoke 제외.
- pull_request 이벤트와 읽기 전용 토큰 사용. 공개 PR에 비밀값·자체 호스트 실행기·실 AI 호출 제공 금지.
- 현재 main에서 분기. main 보호는 실제 PR 검사 이름 확인 후 설정하며 승인자 수 0명으로 1인 개발 유지. 독립 에이전트 리뷰는 별도 기록.
- 실제 Kubernetes 배포·DB migration·Azure 리소스 작업은 이번 범위에 없음. 배포 동작은 격리 Fake로 검증.
- 작업 브랜치의 로컬 시험 허용. source revision·dirty 여부·이미지 식별자를 혼동하지 않고 --skip-build에서 현재 checkout을 기존 이미지의 출처로 기록하지 않음.
- SQL·Blob·Data Protection PVC 보존. 과거 실행 문서는 역사 기록으로 유지.

## Review Focus

Fork PR 권한, 검사 생략으로 인한 우회, 정확한 배포 이미지 출처, 실패한 배포의 성공 기록 방지, 기존 포워딩 보호 회귀.

## Task 1: GitHub PR 자동 검사

**Files:**

- Create: `.github/workflows/ci.yml`

- [x] main 대상 PR과 main push에서 항상 Repository checks, Frontend, Backend 검사 실행. 경로 필터 없음.
- [x] Node 24·pnpm 11.9.0·.NET 10 사용, actions는 확인된 공식 버전의 commit SHA 고정.
- [x] Repository checks: 문서·훅 회귀·Shell 문법·local-up Fake 회귀.
- [x] Frontend: frozen install·기존 lint/typecheck/unit/build·Chromium E2E.
- [x] Backend: build·유료 smoke 제외 전체 회귀·Dockerfile build.
- [x] 읽기 전용 permissions·실행 제한 시간·중복 실행 취소·실패 자료 업로드.
- [x] 워크플로 문법과 최초 GitHub 실행 확인. 최초 실행의 E2E 실패·보완 결과는 아래 기록 참조.
- [ ] 보완된 최종 revision의 세 원격 검사 통과 확인.

## Task 2: 로컬 배포 버전 추적

**Files:**

- Modify: `deploy/local-up.sh`
- Modify: `deploy/test_local_up.py`
- Create: `deploy/local-deployment.py`
- Modify: `deploy/k8s/api.yaml`
- Modify: `.gitignore`

- [x] 기존 실행 명령과 포워딩 보호를 유지하며 이미지에 source revision·dirty 여부 기록.
- [x] 빌드한 이미지에는 식별 가능한 태그를 사용하고 Kubernetes의 실제 적용 이미지를 확인.
- [x] --skip-build는 기존 이미지의 메타데이터 사용. 출처가 없으면 빌드 안내 후 apply 전 실패.
- [x] 성공한 rollout·health 후에만 Git 제외된 로컬 JSON 기록 저장. 시간·context·namespace·revision·dirty·image ID·실행 파드 image ID 포함. Secret·health 전체 본문 제외. 배포별 timestamp JSON 이력과 latest.json을 유지.
- [x] 실패 시 기존 성공 기록 보존. 알 수 없는 이미지 출처를 현재 revision으로 대체하지 않음.
- [x] Fake 회귀에 기본 빌드·재사용·다른 checkout·dirty·실패 기록·기존 포워딩 보호 추가.

## Task 3: 공개 저장소 지침과 원격 적용

**Files:**

- Delete: `deploy/azure/deploy.sh`
- Delete: `deploy/azure/main.bicep`
- Delete: `azure-pipelines.yml`
- Create: `.github/main-protection.json`
- Modify: `AGENTS.md`, `deploy/AGENTS.md`, `README.md`, `docs/infrastructure.md`, `deploy/k8s/README.md`, `docs/xHuman/CODEMAP.md`
- Create: `docs/verification/2026-10-20-github-ci-deploy-tracking.md`
- Modify: `.agents/skills/noxtend-workflow/SKILL.md`, `.agents/skills/noxtend-workflow/references/deployment.md`, `.agents/skills/noxtend-workflow/references/verification.md`

- [x] Azure 배포·사용하지 않는 Azure Pipelines 정의와 현재 지침의 관련 참조 제거. Azure Blob SDK와 과거 기록은 보존.
- [x] main + 짧은 작업 브랜치, PR 필수 검사, 배포 버전 확인·실행 명령 문서화.
- [x] main 보호: PR 필수·실제 확인된 검사 필수·최신 main 반영·대화 해소·관리자 적용·강제 push/삭제 금지. GitHub 승인 인원 0명.
- [x] 커밋·PR의 실행 결과와 원격 보호 설정을 조회하여 기록.

## Task 4: 첫 CI에서 드러난 E2E 준비 조건 보완

**Files:**

- Modify: `apps/frontend/tests/e2e/sprites-placements.spec.ts`

- [x] 최초 CI의 겹친 상자 드래그 2건 실패를 trace·화면 상태·실제 비교/편집 모드 전이와 대조.
- [x] 제품 동작을 바꾸거나 assertion을 약화하지 않고, 기존 editBoxes helper로 편집 모드를 명시적으로 선택.
- [x] 전용 preview 포트·새 build에서 해당 두 건과 placements 파일을 검증하고 기존 lint 유지.
- [ ] 최종 GitHub 전체 CI로 확인.

## 실행 기록

- 기준: origin/main 316faf6. 별도 관리 worktree에서 진행.
- 구현: CI `21babe5`, 로컬 Backend 배포 추적 `40f6dd5`, E2E 준비 조건 `427ea8f`. 각각 구현과 별도 설계·품질 리뷰를 완료했다.
- 지침 정리: Azure 배포·Azure Pipelines 정의 제거, main 보호 설정 본문과 현재 운영 지침·배포 기록 사용법을 정리했다. 원격 보호는 PUT 후 GET으로 실제 필수 검사·관리자 적용·승인 인원 0·강제 push/삭제 금지를 확인했다.
- 최초 원격 CI [39397182384](https://github.com/boinred/Noxtend/actions/runs/39397182384): Repository checks·Backend 통과, Backend 2,069건·Docker build 통과. Frontend 단위 574건 통과, E2E 419건 통과·2건 실패로 전체 성공은 아니었다.
- E2E 보완: 기존 editBoxes helper로 편집 준비를 명시했다. 별도 포트 4387·새 build에서 focused 2/2·placements 10/10·기존 lint 통과. 제품 동작·assertion·timeout은 유지했다.
- 측정·원격 보호·미실행 범위의 보존 기록: [검증 문서](../../verification/2026-10-20-github-ci-deploy-tracking.md). 로컬 로그·리뷰 작업 자료는 ignored `.superpowers/sdd/2026-10-20-github-ci-deploy-tracking/`에 있다.
- 후속 확인: 이번 지침 정리의 독립 리뷰, 최종 전체 변경 리뷰와 최종 원격 CI는 controller가 진행한다. 위 체크는 완료된 구현·측정 범위이며 최종 CI 통과를 뜻하지 않는다.
- 결과·병합·정리의 최종 상태는 [PR #19](https://github.com/boinred/Noxtend/pull/19), 최신 revision 검사는 [PR Checks](https://github.com/boinred/Noxtend/pull/19/checks)를 정본으로 확인한다. 이 문서의 최초 측정 수치는 그대로 보존한다.
