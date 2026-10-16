# 2D 원본 비교 패널 — 통합 기록

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
