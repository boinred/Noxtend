# 백로그: 외부에서 만든 이미지를 3D 입력으로 받기 (스펙 아님, 기록용)

## 배경
2026-09-17 대화에서 나온 미래 방향: 지금은 3D 생성의 유일한 입력이 우리 파이프라인이 만든 `GeneratedImage` 행뿐이다. 나중에는 (1) 이미지 생성 단계를 건너뛰고 3D부터 시작하는 파이프라인, (2) 사용자가 다른 곳에서 만든 이미지를 업로드해서 3D 입력으로 쓰는 것 — 둘 다 검토 대상이다.

## 지금 왜 막혀 있는지
- `StartJobHandler.cs`가 "3D 를 만들려면 이미지 생성을 함께 선택해야 합니다"(`JobMeshRequiresImages`)를 강제한다 — 3D 입력이 이미지 생성 산출물이라는 전제.
- `MeshInputSet.cs`가 `Guid FrontImageId/RightImageId/BackImageId/LeftImageId`로 **우리 DB의 `GeneratedImage` 행 ID**를 직접 참조한다. 외부 업로드 이미지는 이 타입에 넣을 수 없다.

## 지금 당장 할 일 (없음)
이번 스펙(`20260917-selective-view-mirror.md`, 대칭/뷰 자유 선택)은 이 결합을 더 깊게 만들지도, 풀지도 않는다 — `MeshInputSet` 위에서 "어떤 `GeneratedImage`를 골라 쓸지"를 다루는 수준이라 무관하다.

## 나중에 이 방향으로 갈 때 고려할 것
- `MeshInputSet`을 `GeneratedImage` 전용 ID 필드 대신 더 느슨한 참조(예: 이미지 바이트/URL을 가리키는 핸들)로 일반화할지, 아니면 "외부 업로드 이미지"를 가짜 `GeneratedImage` 행으로 만들어 넣는 어댑터 계층을 둘지 — 백엔드 계약을 크게 바꾸지 않는 선택지를 그때 다시 비교해야 한다 (사용자 확정: "계약이 엄청 바뀌지 않게").
- `JobMeshRequiresImages` 검증을 어디까지 완화할지 (완전히 없앨지, "이미지 생성 아니면 업로드 중 하나는 있어야 한다"로 바꿀지).

이 문서는 스펙이 아니라 기록이다 — 실제 작업 시작 시 정식 spec-first 절차로 다시 스펙을 쓴다.
