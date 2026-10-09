---
name: context-map
description: Map the files, callers, contracts, and tests needed for a Noxtend task. Use for cross-layer changes or unclear dependencies; skip routine edits with an already known scope.
license: MIT
---

# 작업별 컨텍스트 맵

[Awesome Copilot 원본](https://github.com/github/awesome-copilot/blob/143a3d976b3c1603cc8932984d5e1f28501cb5fc/skills/context-map/SKILL.md)을 Noxtend에 맞게 수정했다. [MIT 라이선스](../LICENSE.awesome-copilot)를 유지한다.

요청의 변경 경로·호출부·검증 경로를 실제 파일에 연결한다. 맵만 요청하면 읽기 전용이고, 구현이 이미 요청됐으면 필요한 맵을 만든 뒤 추가 검토 대기 없이 진행한다.

## 탐색 범위

- `docs/xHuman/CODEMAP.md`와 해당 영역 지도에서 출발하고, 루트·해당 영역의 `AGENTS.md`와 `$noxtend-workflow`에서 관련 자료만 선택한다. 코드 탐색·리뷰는 `$karpathy-guidelines`를 따른다.
- Backend 진입점·DTO·handler·Domain 상태·Port·Infrastructure 저장/공급자 연결, Frontend API·타입·query/hook·화면 중 요청에 필요한 경로만 추적한다.
- 공유 함수는 실제 호출부와 실패·취소·재시도 경로를 확인한다. HTTP·상태·저장 형식 변경이면 반대편 소비 코드·기존 데이터 읽기·관련 테스트까지 연결한다.
- `rg`로 심볼·import·route를 찾고 유사한 기존 구현을 확인한다. 파일명이나 과거 설계만으로 연결을 만들지 않는다. 동적 등록·소비 코드를 찾지 못한 부분은 미확인으로 남긴다.

## 최소 결과

요청에 필요한 항목만 짧은 목록이나 표로 작성한다.

| 항목 | 기록 |
| --- | --- |
| 변경 후보 | 실제 파일·심볼과 변경 이유 |
| 영향 경로 | 호출부·공유 계약·상태/저장 전이와 연결 근거 |
| 검증 | 기존 테스트·확인할 동작·실행 위치와 환경 제약 |
| 남은 판단 | 범위를 바꾸는 미확인 사항·API/DB/유료 호출 영향 |

일반 편집마다 맵 파일·작업 문서·전체 저장소 색인을 생성하지 않는다. 지속되는 결정은 필요한 기존 문서에 반영한다. 문서화 요청이 없으면 맵은 작업 보고에 포함한다.
