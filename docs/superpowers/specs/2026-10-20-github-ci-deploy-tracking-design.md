# GitHub 검사·main 보호·로컬 배포 추적 설계

## 목적

공개 GitHub 저장소에서 main과 짧은 기능 브랜치를 유지하고, 병합 전 자동 검사와 배포한 버전의 식별을 보장한다. 사용자가 추천안을 승인하고 Azure 배포를 사용하지 않는다고 명시했다.

## 범위

- GitHub Actions의 Repository checks·Frontend·Backend 검사. main PR과 main push에서 실행하며 경로별 검사 생략 없음.
- Node 24·pnpm 11.9.0·.NET 10, 기존 Frontend 검사·Chromium E2E·격리 Testcontainers 회귀·Backend Docker build 유지. 유료 smoke 제외.
- 공개 포크 PR에는 읽기 전용 토큰만 사용하며 secrets·실 AI 호출·자체 호스트 실행기 없음. actions는 공식 commit SHA로 고정.
- main은 PR·필수 검사·최신 main 반영·대화 해소를 요구하고 관리자에게도 적용. 강제 push와 삭제 금지. 협업자 1명이므로 GitHub 승인 인원은 0명이며 지침의 독립 에이전트 리뷰는 유지.
- 로컬 배포는 이미지 출처·dirty 여부·실행 이미지·성공 시간을 기록. --skip-build는 기존 이미지의 출처를 사용하고 출처 미상은 재빌드 안내로 실패. 실패한 실행은 최근 성공 기록을 덮지 않음.
- Azure 배포와 사용하지 않는 Azure Pipelines 정의 제거. Azure Blob SDK와 과거 설계·실행 기록 보존.

## 검증

워크플로 문법, 문서·훅 회귀, 격리 Fake 배포 회귀와 실제 GitHub PR 검사를 확인한다. 확인된 검사 이름으로 main 보호를 설정한 뒤 API로 재조회한다. 실제 Kubernetes 배포·DB migration·유료 AI 호출·Azure 리소스 작업은 이번 검증 범위가 아니다.
