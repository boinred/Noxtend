# 2D 검수 UI와 공급자 오류 수정 — 통합 기록

## 범위

- OpenAI 텍스트·이미지 오류의 안전한 원인 기록과 AnalyzeSprites V4 anyOf 호환 migration
- 기준 이미지 전체 선택·아이콘 조작·재생성 확인, 승인 결과 내보내기 전체 선택과 선택 요약
- 이미지 ROI 선택·이동·8개 손잡이 크기 조절, 숫자 정밀 입력 및 설정 카드 정리
- 기존 3D mouse 편집·서버 저장/검수 계약 유지

## 검증 근거

검증 기준은 main `fbe2bc07fe823b12dc7c125f1e6def8f48062a33` 위 작업 변경이다. 2026-10-16 통합 전 기존 실행 기록과 실제 파일을 다시 비교했다. Backend 9개 소스·테스트·migration은 최종 리뷰 diff의 blob 또는 신규 파일 전체 내용과 일치하며, ROI 관련 10개 경로는 최종 SHA-256 기록과 일치한다. 같은 구현에 대한 전체 회귀를 반복 실행한 것으로 표현하지 않는다.

| 검사 | 실행 결과 | 실행 시점 |
| --- | --- | --- |
| Backend build | 오류 0, 기존 SSH.NET NU1903 경고 4 | 2026-10-15 |
| Backend 전체 회귀(유료 smoke 제외) | 2016 통과, 실패·건너뜀 0 | 2026-10-15 |
| Frontend unit | 527 통과, 60 files | 2026-10-15 |
| Frontend lint·format·typecheck | 통과 | 2026-10-15 |
| Frontend fresh build·전체 E2E | 390 통과, 7.5분 | 2026-10-15 |
| 추가 중첩 ROI 직접 클릭 E2E | 별도 fresh build에서 1 통과 | 2026-10-15 |
| 작업별 및 최종 독립 리뷰 | 승인, 중첩 선택 테스트 증거 지적 보완·재리뷰 완료 | 2026-10-15 |

Frontend E2E는 Fake API·Chromium을 사용하며 실제 AI 호출 증거가 아니다. 390개 전체 실행과 추가 1개 실행은 별도다. 자세한 Backend 범위와 과거 실패·배포 기록은 [원래 계획](../superpowers/plans/2026-10-15-provider-errors-sprite-schema.md)에 보존한다.

유지되는 기본 checkout의 로컬 근거:

- `.superpowers/sdd/2026-10-15-provider-errors-sprite-schema/`: 구현·독립 리뷰·focused 로그
- `.superpowers/sdd/noxtend-base-review-confirm-plan/`, `.superpowers/sdd/noxtend-export-selection-plan/`: 검수·내보내기 리뷰
- `.superpowers/sdd/noxtend-roi-editor-plan/`: 최종 파일 해시·전체 검사 로그·리뷰·1440/390px 스크린샷

위 로컬 근거 폴더는 Git 제외 대상이다. 이 문서는 저장소에 남는 검증 요약이며 로컬 로그는 기본 checkout에 보존한다.

## 한계

- 이번 통합에서는 실 공급자 호출·유료 smoke·실 모바일 touch/pen 기기·Docker 재배포를 수행하지 않는다.
- Backend 수정은 2026-10-15 별도 사용자 승인으로 로컬 배포됐으나 이번 Frontend UI 변경의 Docker 반영을 의미하지 않는다.
- 운영자 수정/활성 프롬프트는 자동 교체하지 않으며, 알 수 없는 공급자 자유 오류 문장은 안전을 위해 생략한다.

## 전달 상태

2026-10-16 PR 병합과 작업 브랜치 정리 완료. 아래 기록은 위 검증 당시 결과를 유지한 후속 전달 기록이다.

- PR: [#15](https://github.com/boinred/Noxtend/pull/15)
- 구현 커밋: `2c4bab8d3fc90d97ff11a02b1d991f81dc9ffd08`
- main 병합 커밋: `212e742a853a0e2bbee48e65ce6714d1372132be` (2026-10-16 13:16 KST)
- main 병합 결과와 구현 커밋의 전체 트리가 동일함을 `git diff`로 확인했다. 병합 충돌 또는 추가 기능 변경은 없었다.
- `codex/provider-errors-sprite-schema` 로컬·원격 브랜치를 삭제했고 기본 checkout을 main으로 동기화했다. 별도 worktree는 없으며 위 로컬 근거는 기본 checkout에 그대로 남아 있다.
- 통합 전 문서·공백·비밀값 패턴 검사와 실제 커밋 훅의 Frontend lint·format 검사를 통과했다. GitHub에 등록된 PR 상태 검사는 없었으며 CI 성공으로 표현하지 않는다.
- 이 전달 기록은 main의 문서 후속 커밋으로 보존한다. 실 AI·Docker 재배포·실 모바일 기기 검증은 추가하지 않았다.
