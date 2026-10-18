# 2D 원본 비교 패널 — 통합 기록

이 패널(`SpriteSourceCompare.tsx`)은 [계획 검수 재구성](../superpowers/plans/2026-10-18-2d-source-reconstruction.md)으로 대체되어 제거됐다. 아래는 측정 당시 기록이며 현재 구현을 설명하지 않는다.

## 범위

- 2D 작업 화면의 `SpritePreview` 아래에 원본 비교 패널(`SpriteSourceCompare.tsx`)을 추가한다. 원본 위에 각 대상의 현재 기준 이미지를 원본 px 좌표로 다시 배치하고 투명도 슬라이더로 비교한다.
- 배치 규칙은 `domain/sprites/rules.ts`의 `assetSourceRect`다. 역할별 규칙은 [Frontend 지도](../xHuman/frontend.md#2d-배경-스튜디오)에 있다.
- 서버·HTTP 계약·DB 변경은 없다. 짧은 채팅 설계 승인 후 진행한 bounded 변경이라 별도 spec·plan 문서는 없다.

## 검증 근거

검증 기준은 main `d0bb151` 위 작업 변경이다.

| 검사 | 실행 결과 | 실행 시점 |
| --- | --- | --- |
| `assetSourceRect` 단위 테스트 | 구현 전 실패 확인 후 통과 | 2026-10-16 |
| Frontend unit | 528 통과, 60 files | 2026-10-16 |
| Frontend lint·format·typecheck·build | 통과, 초기 JS 115.14 kB / 예산 122.55 kB | 2026-10-16 |
| Frontend fresh build·전체 E2E | 392 통과, 건너뜀 0, 7.5분 | 2026-10-16 |
| 패널 화면 관찰 | Fake API 1440px·390px, light·dark에서 간격·배치·가로 넘침 없음 | 2026-10-16 |
| 로컬 Docker 반영 | `deploy/local-up.sh -d`, rollout·`/health` 전 항목 ok | 2026-10-16 16:24 KST |
| 로컬 API 실제 작업 조회 | 기존 2D 작업 1건의 실제 생성 타일 8개가 원본 ROI 위치에 렌더링됨 | 2026-10-16 |

- E2E는 저장소 설정과 같은 명령을 포트만 4174로 바꾼 임시 설정으로 실행했다. 4173은 다른 worktree의 오래된 preview 프로세스가 점유해 404를 반환했고, 그 프로세스는 종료하지 않았다.
- 로컬 Docker 이미지는 Backend만 포함하며 이번 변경은 Frontend뿐이다. 재배포 후 API 컨테이너의 이미지 config digest는 재배포 전과 같은 `sha256:3dc290ca10cb16184dca8a5fdfff3750e35c202eaa7a7de53b67df54e98edd16`이다. Frontend는 같은 checkout의 `pnpm dev`(5173)에서 로컬 API(18080)에 연결해 확인했다.
- 실제 작업 확인은 조회와 로컬 슬라이더 조작만 했으며 생성·승인 요청은 보내지 않았다. 재배포 전부터 `pending`이던 작업 `569df6f8-cb3f-4dc2-ad81-ed37242e2937`의 `analyzeSprites` 공정은 재배포 후에도 시도 1회의 `pending`으로 남았다.

## 한계

- 실제 작업의 타일 한 칸은 원본 폭의 약 8%다. 생성 타일이 원본과 비슷하면 패널 크기에서 차이가 잘 보이지 않는다. 확대·윤곽 표시는 이번 범위에 넣지 않았다.
- 타일은 ROI 한 칸에만 놓으며 원본에서 그 타일이 덮는 전체 영역을 반복해 채우지 않는다.
- 실 AI 신규 생성·유료 smoke·실 모바일 기기 확인은 하지 않았다.

## 전달 상태

2026-10-16 PR 병합과 작업 브랜치 정리 완료. 아래 기록은 위 검증 당시 결과를 유지한 후속 전달 기록이다.

- PR: [#16](https://github.com/boinred/Noxtend/pull/16)
- 구현 커밋: `9afbc13ee9c0eebcf829341a2ccf07aacf1b5228`
- main 병합 커밋: `4c2a2df7aa6e732d4128d29c0566110242d3c8bb` (2026-10-16 18:45 KST, 사용자가 직접 병합)
- main 병합 결과와 구현 커밋의 전체 트리가 같다는 것을 `git diff`로 확인했다. 병합 충돌이나 추가 기능 변경은 없었다.
- `claude/sprite-source-compare` 로컬·원격 브랜치가 삭제되었고 기본 checkout이 main과 동기화되었다. 별도 worktree는 없다.
- 커밋 훅의 설계 문서 검사와 Frontend lint·format 검사를 통과했다. GitHub에 등록된 PR 상태 검사는 없었으므로 CI 성공으로 표현하지 않는다.
- 위 화면 관찰 스크린샷과 E2E 임시 설정은 세션 임시 폴더에만 있었으므로 보존 근거로 남기지 않는다. 저장소에 남는 근거는 이 문서와 테스트 코드다.
- 병합 후 추가로 Docker를 재배포하거나 실 AI 검증을 하지 않았다.
