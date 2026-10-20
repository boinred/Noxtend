# GitHub CI·main 보호·로컬 배포 추적 검증

## 대상과 근거

- 기준 main: `316faf6`, 별도 관리 worktree·`codex/github-ci-deploy-tracking`에서 작업.
- CI: `21babe5`, 로컬 배포 추적: `40f6dd5`, E2E 준비 조건: `427ea8f`.
- 결과·병합 상태의 정본: [PR #19](https://github.com/boinred/Noxtend/pull/19). 현재 revision의 원격 검사는 [PR Checks](https://github.com/boinred/Noxtend/pull/19/checks)에서 확인한다. 이 문서의 측정은 아래 시점별 기록으로 보존한다.
- 구현은 작업별 분담·독립 리뷰(Subagent-driven). CI·배포 추적·E2E 보완은 구현과 별도 리뷰에서 설계 준수와 품질을 확인했다. 전체 변경의 최종 리뷰는 같은 PR의 검증 기록으로 유지한다.

## 최초 원격 실행

[CI run 39397182384](https://github.com/boinred/Noxtend/actions/runs/39397182384), source `21babe5`, GitHub hosted Ubuntu 24.04·Node 24·pnpm 11.9.0·.NET 10.

| 검사 | 확인 결과 |
| --- | --- |
| Repository checks | 문서·기존 훅 4건·기존 Fake 배포 23건·Shell 문법 통과 |
| Frontend | lint·typecheck·build·단위 574건 통과. E2E 419건 통과·2건 실패 |
| Backend | 전체 회귀 2,069건 통과·실패 0·건너뜀 0, 6분 34초. Dockerfile build 통과 |

유료 `TripoSmokeTests`·`SimilaritySmokeTests`는 필터로 제외했다. 이 실행을 전체 성공으로 계산하지 않는다.

## E2E 실패 원인과 보완

겹친 생성 기준/자리 상자 드래그 두 경우는 계획 저장 버튼이 disabled여서 30초 timeout이 났다. 보관된 trace snapshot은 비교 pressed·편집 not pressed·대상 선택 pressed였다. `selectBox`는 재구성 준비 이후 비교 상태에서만 편집으로 전환하고, 준비 이전 선택은 초기 비교 모드를 유지한다. 테스트가 이 비동기 준비 시점에 의존했다.

기존 `editBoxes(review)` helper를 공통 준비 단계에 한 줄 추가했다. 제품 소스·타임아웃·assertion은 바꾸지 않았다. 선택 유지·이동 좌표·크기·다른 상자 불변·저장 요청 검증은 유지했다. 전용 preview 4387·새 build·`reuseExistingServer=false`에서 focused 2/2, placements 파일 10/10, 기존 lint·커밋 훅 통과.

## 배포 추적 검증

기존 코드에 성공 기록 요구를 먼저 적용해 `KeyError: revision`으로 Red를 확인했다. 추가 비소유 Pod가 같은 selector에 섞이는 사례도 잘못된 기록 갱신으로 Red를 확인한 뒤 소유 관계·단일 active Pod 검증으로 수정했다.

- `python3 deploy/test_local_up.py`: 기존 23 + 신규 22 = 서로 다른 Fake 45건 통과. 최종 helper 소규모 조정 뒤 관련 3건을 추가 재확인했다.
- 가짜 Docker·kubectl·curl·lsof·ps·git, 임시 디렉터리와 fixture 프로세스만 사용. 실제 host listener 조회·종료, Docker·Kubernetes·DB 변경 없음.
- 다른 checkout의 `--skip-build`, dirty 출처, 출처 미상 시 기존 포워딩 보존, 빌드/apply/health 실패, 잘못된 Pod·이미지, health 중 Pod 교체, 성공 이력 생성과 기존 기록 보존 확인.
- Shell/Python 문법·Git ignore·공백 검사 통과. 기록은 Backend 성공 당시 정보이며 Vite Frontend와 구분한다.

## main 보호 확인

GitHub API PUT 후 GET으로 실제 원격 설정을 확인했다. 지침 정리 중 2026-10-20 09:17 UTC에도 GET으로 아래 값과 저장소 설정 본문의 일치를 재확인했다.

- PR 필요, GitHub Actions app `15368`의 `Repository checks`·`Frontend`·`Backend` 필수.
- `strict=true`, 관리자 적용·미해결 대화 해소 요구.
- 강제 push·삭제 금지. GitHub 승인 인원 0명, 독립 에이전트 리뷰 유지.

재현 가능한 설정은 `.github/main-protection.json`에 있다. 설정 파일 수정만으로 원격 적용으로 계산하지 않는다. 처음 PUT에서 구형 contexts와 checks를 함께 넣은 요청은 422로 거절됐고, checks만 사용해 적용·재조회했다.

## 지침·정적 검증

사용하지 않는 `deploy/azure/deploy.sh`·`deploy/azure/main.bicep`·`azure-pipelines.yml`만 삭제하고 Azure Blob SDK·Azurite·과거 설계 기록은 보존했다. 배포 기록 필드와 사용법은 `40f6dd5`의 실제 shell/helper와 대조했다.

`pnpm docs:check`, 수정 문서 상대 링크 69개, 보호 JSON 파싱·확인된 API 본문 비교·원격 GET 대조, actionlint v1.7.12, `git diff --check`가 통과했다. 기존 500행 초과 문서 3개는 경고만 있었다. skill-creator의 `quick_validate.py`는 `ModuleNotFoundError: No module named 'yaml'`로 미실행이며 Ruby YAML로 frontmatter·이름·설명을, 상대 링크 검사로 참조 경로를 확인했다. 문서·삭제·설정 본문만 바꿔 같은 애플리케이션 회귀는 중복 실행하지 않았다.

## 실행하지 않은 범위

실제 `deploy/local-up.sh` 실행, Docker Desktop containerd 반입·Kubernetes rollout·DB migration, 실제 공급자 호출·유료 smoke, Azure 리소스 작업은 하지 않았다. 기존 label 없는 이미지는 다음 실제 기동에서 정상 빌드가 한 번 필요하다. 이미지 rollback은 DB migration rollback이 아니며 로컬 JSON은 이미지 registry를 대신하지 않는다.
