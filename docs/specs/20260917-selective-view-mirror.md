# 미니 스펙: 선택적 뷰 생성 + 3D 전송 뷰 자유 선택 + 대칭(합성 이미지)

이 문서는 **최종 계획만** 담는다. 왜 이 결정에 도달했는지(대안 비교, 기각된 안, 리뷰 이력)는 ADR을 참고한다:
- `docs/adr/2026-09-17-provider-adapters-and-hasallinputs-bug.md` — Meshy/Tripo 어댑터, `HasAllInputs` 버그
- `docs/adr/2026-09-17-mirror-as-synthetic-generated-image.md` — 대칭을 합성 이미지로 구현하는 최종 설계
- `docs/adr/2026-09-17-mesh-mirror-input-and-part-level-mesh-planning.md` — **폐기됨**, 기각된 초안(참고용)

## 진행 현황 (2026-09-17)

**완료·커밋됨 (백엔드 전부):**
- `MeshInputSet` 정면 필수 + 비정면 0장 이상 완화
- `MeshyMeshProvider` 빈 슬롯 fallback 제거
- `TripoMeshProvider` 공식 스키마 재작성 + 최소 2장 검증
- `IImageTranscoder.FlipHorizontallyAsync` 구현 + 테스트
- `MeshRun.HasAllInputs` 4장 고정 버그 수정
- `GeneratedImage.IsSynthetic` 필드 + 마이그레이션 + `IsCurrentCandidate` 계산 프로퍼티(갤러리 응답·자동 팬인 통일)
- `TaskKind.Synthesize` 신규 값 + `UnreachableSynthesizeTaskHandler`(방어용 워커 등록)
- `PipelineJob.PlanSyntheticView`, `PipelineJob.LatestCurrentImage`, `PipelineJob.ReplanMeshInputs`(find-or-create) 신규 도메인 메서드
- `RunMeshTaskHandler.ResumeOrStartAsync`에 입력 비교 조건 추가(입력이 다르면 이미 성공한 실행도 재사용 안 함)
- `LeftRightPlan`/`BackPlan` enum + `ReplanPartMeshHandler`(enum 해석 + 대칭 I/O + 한 트랜잭션 처리)
- `POST /jobs/{jobId}/replan-mesh` 엔드포인트

**완료·커밋됨 (프론트):**
- `deriveReplanPlans`/`axisSelectionError` 순수 함수 + 단위 테스트
- `PartGallery`에 파츠별 체크박스(`PartMeshAxisControls`) + 공유 공급자/모델 배치 섹션(`PartMeshBatchSection`) + "3D 생성" 배치 제출
- `useReplanPartMesh`, `replanPartMesh` API 함수, `BackgroundStudioScreen`/`RunResult` 배선
- e2e: `tests/e2e/mesh.spec.ts`에 체크박스 상호작용·유효성 오류·배치 제출 4건 추가

**merge-gate 리뷰 대응 (2026-09-17, 독립 리뷰어 발견 F1~F7 전부 처리):**
- F1: 취소된 작업에 `replan-mesh` 요청 시 500 → 409(`JOB_ALREADY_TERMINAL`)
- F2: 동시/중복 요청 시 500 → 409(`REPLAN_MESH_CONFLICT`, `ConcurrencyConflictException` 도입)
- F3: 대칭 성공 후 다른 축 검증 실패 시 고아 Blob 방지(검증-실행 2단계 분리) + 저장 충돌 시 Blob 정리
- F4: 검수 단계 상호작용 — 코드 수정 대신 실제로 무해함을 회귀 테스트로 확인, 스펙 문구 정정
- F5: "정면 없이는 서버도 거부" 문구가 부정확해 정정(실제로는 프론트 전용 방어)
- F6: `LatestCurrentImage` 동률 타이브레이크 추가
- F7: `MeshInputSet` 비정면끼리 중복 ID 금지
- F8(정보)·F9(정보, 기존 패턴 재확인)는 코드 변경 없음

**2차 merge-gate 리뷰 대응 (2026-09-17, 서브에이전트 + agy 교차검증, B1~B5 처리):**
- 서브에이전트와 agy(Gemini) 양쪽에 독립적으로 재검토를 맡겼는데 결과가 갈렸다 — 서브에이전트는 B1(머지 차단)을 찾았고 agy는 "병합 승인"으로 놓쳤다. 직접 코드를 읽어 서브에이전트 쪽이 맞다고 확인한 뒤 처리했다.
- B1(머지 차단): `EfJobRepository.SaveChangesAsync`가 `DbUpdateException`을 통째로 "동시 충돌"로 번역해, FK/NOT NULL 위반 같은 진짜 버그까지 로그 없이 409로 삼켰다. SQL Server 유니크 위반 번호(2601/2627)만 좁혀 번역하도록 고치고 `ReplanPartMeshHandler`에 로거 추가
- B2: `PartGallery.tsx` 주석에 남아있던 F5의 틀린 주장 정정
- B3: `TaskWorker`의 "경합은 오류 아님" 동작을 고정하는 회귀 테스트 추가(필터를 일시 제거해 실제로 잡아내는 것까지 확인)
- B4: `PART_NOT_FOUND`/`REPLAN_MESH_SELECTION_INVALID`로 진단 코드 세분화
- B5: 배치 제출 중간 실패 시 몇 번째까지 접수됐는지 메시지에 표시

**이번 스펙은 완료됐다.** 남은 항목은 없음 — 이후 발견되는 개선점은 새 스펙으로 분리한다.

처음에 "이번에 안 하는 것"으로 적었던 "여러 파츠 중 어떤 파츠를 3D화할지"(파츠 단위 포함 선택)는 별도 기능으로 만들지 않았지만, 정면 사용 체크박스를 끄면 그 파츠가 이번 3D 생성 배치에서 빠지는 동작으로 결과적으로 해결됐다 — 아래 "이번에 안 하는 것"에서 제외했다.

## 목표 (한 문장)
파츠별로 정면을 우선 생성한 뒤, 사용자가 체크박스로 고른 비정면(Left/Right/Back)만 골라 한 번에 생성하고, 3D 생성 시점에는 이미 만든 뷰 중 원하는 것만(정면 포함 1~4장) 골라 보내거나 어느 한쪽 축을 "대칭"으로 지정해 실제 반전 이미지를 새로 만들어 쓸 수 있게 하며, 이 전부를 Meshy와 Tripo 두 공급자 모두에서 지원한다.

## 입력 → 출력

**입력 1 — 선택적 뷰 생성 (기존 기능, 변경 없음)**
- 입력: 정면이 완료된 파츠 카드에서 사용자가 `Left`, `Back` 체크박스를 켜고 "선택 생성" 클릭
- 출력: 해당 파츠에 Left, Back 두 개의 `GeneratedImage` 행이 생성되고, Right는 `unplanned`로 유지됨

**입력 2 — 3D 전송 뷰 자유 선택**
- 입력: Front/Left/Right/Back 4장이 모두 생성된 칼 파츠에서, `PartGallery`의 Left/Right 카드 "사용" 체크박스를 끄고 "3D 생성" 클릭 (납작한 파츠라 옆면 없이도 충분하다고 판단)
- 출력: 3D 입력에는 Front, Back 이미지만 담기고, Left/Right는 실제로 존재해도 이번 요청에서 제외됨

**입력 3 — 대칭 (합성 이미지 생성)**
- 입력: Left 이미지만 생성 완료된 파츠에서, `PartGallery`의 Left 카드 "좌우대칭" 체크박스를 켬(Right 카드는 자동으로 "대칭 소스 있음" 표시) → "3D 생성" 클릭
- 출력: 서버가 Left의 최신 실제 이미지를 반전해 새 Blob + 새 `GeneratedImage`(방향=Right, `IsSynthetic=true`)를 만들고, 이 3D 요청은 그 이미지를 Right 자리에 써서 제출된다. Left의 실제 이미지는 그대로 남고, 합성 Right 이미지는 일반 갤러리의 "현재 이미지" 계산에는 안 낀다(아래 설계 참고). **합성 이미지 생성은 별도 API 호출이 아니라 "3D 생성" 요청 하나 안에서 서버가 처리한다** — 체크박스는 프론트 로컬 상태일 뿐이고, 프론트는 이미지 ID를 전혀 몰라도 된다(아래 "설계 결정" 참고).

## 이번에 안 하는 것 (제외 범위)
- 이미지 생성 단계 자체를 건너뛰고 3D부터 시작하는 파이프라인, 외부 업로드 이미지를 3D 입력으로 쓰는 것 — `docs/specs/_backlog-external-image-input.md`로 분리(미래 구현)

## 설계 결정

- **대칭은 3D 제출 시점 처리가 아니라 이미지 생성 단계의 산출물이다.** 사용자가 "대칭"을 켜면 서버가 소스 이미지를 반전해 실제 새 `GeneratedImage`(`IsSynthetic=true`)를 만든다. 이후 3D 파이프라인(`MeshInputSet`, `MeshRunInput`, `RunMeshTaskHandler`)은 이 이미지를 평범한 이미지로 취급한다 — 아무것도 안 바뀐다. 근거는 `docs/adr/2026-09-17-mirror-as-synthetic-generated-image.md`.
- **합성 이미지는 매번 새로 만든다(재사용 안 함).** 재사용하면 원본이 그 사이 재생성됐을 때 낡은 걸 반영하는 문제가 생기고, 그 판정 로직이 오히려 코드를 늘린다.
- **"최신 이미지" 판정은 `GeneratedImage.IsCurrentCandidate` 계산 프로퍼티 하나로 통일한다** (`!IsObsoleted && !IsSynthetic`). `Noxtend.Api/Contracts/JobResponse.cs:281-282`(일반 갤러리 응답)와 `Noxtend.Domain/Job/PipelineJob.cs:680` `LatestImagesFor`(자동 3D 팬인) 둘 다 이 프로퍼티로 필터링한다 — 두 곳이 각자 조건을 손으로 적으면(이미 `IsObsoleted` 필터가 한쪽에만 있어 어긋나 있었음) 나중에 또 어긋난다.
- **파츠별 "3D 전송 뷰 선택"은 두 번째 `Reconstruct` 공정을 만들지 않는다.** 파츠당 `Reconstruct` 공정은 DB 유니크 제약(필터 `Kind='Reconstruct'`)으로 하나만 존재할 수 있고, 자동 팬인(`JobOrchestrator`→`PlanMeshFanIn`)이 이미지 2장만 모여도 그 자리를 먼저 선점한다 — "자동 팬인은 안 건드리고 새 경로만 추가한다"는 건 성립하지 않는다. 대신 `PipelineJob.ReplanMeshInputs(partId, providerConfigId, model, MeshInputSet, now)`를 추가한다 — 이미 있는 `RetryOutputTask`+`MeshRun.RunNumber`("한 공정, 여러 실행" 이력) 패턴을 그대로 쓰므로 새 스키마가 없다.
  - **find-or-create다, "재실행"만이 아니다.** 자동 팬인(`LatestImagesFor`)은 지금도 "정면 + 비정면 최소 1장"이 모여야만 `Reconstruct` 공정을 만든다. 그런데 이번 스펙은 정면 단독 3D 생성(Meshy 한정, 예상 함정 3)도 지원하기로 했다 — 이 경우 비정면이 0장이라 자동 팬인이 애초에 공정을 안 만들었을 것이므로, `ReplanMeshInputs`를 처음 호출하는 시점에 그 파츠에 `Reconstruct` 공정이 **아예 없을 수 있다.** 유니크 제약은 "두 번째"만 막으므로 첫 생성은 문제없다 — `ReplanMeshInputs`는 "있으면 입력을 다시 얼려 새 실행을 만들고, 없으면 공정 자체를 새로 계획한다."
- **계층 분리: enum 해석·합성 이미지 I/O는 애플리케이션, `MeshInputSet` 조립 이후는 도메인.** 요청 모양은 축별로 `LeftRightPlan{Skip,Left,Right,Both,MirrorFromLeft,MirrorFromRight}`, `BackPlan{Skip,Include,MirrorFromFront}`이지만, 이 enum을 해석하는 주체는 **애플리케이션 계층 핸들러**다 — 도메인(`PipelineJob`)은 I/O를 할 수 없으므로 "필요하면 합성 이미지를 만든다"는 판단과 실행(Blob 읽기·반전·저장)을 도메인 메서드 안에 둘 수 없다. 흐름: (1) 애플리케이션 핸들러가 `Mirror*`면 소스 이미지 바이트를 읽어 반전·저장(I/O)한 뒤 도메인의 `PlanSyntheticView`+`AttachGeneratedImage`로 기록하고, 대칭이 아니면 최신 실제 이미지 ID를 그대로 조회한다 (2) 이렇게 모은 ID로 `MeshInputSet`을 조립한다(순수 생성자 호출) (3) 완성된 `MeshInputSet`을 들고 도메인의 `ReplanMeshInputs`를 호출해 기존 Reconstruct 공정을 재실행한다.
- **합성 이미지 생성과 3D 제출은 한 요청, 한 애플리케이션 핸들러 안에서 처리한다(별도 API 없음).** "대칭" 체크박스는 프론트 로컬 상태일 뿐 서버에 아무것도 안 보낸다.
- **잡이 이미 종료 상태면 재개방한다.** 기존에 이미 6곳에서 쓰는 패턴(`Status = JobStatus.Running` 재설정)을 그대로 재사용한다 — 새 규칙 아님.
- **더블클릭 등 동시 요청은 이미 방어돼 있다.** `PipelineJob`/`PipelineTask` 둘 다 `RowVersion`(낙관적 동시성 토큰)이 있어, EF가 두 번째 요청을 충돌로 잡는다 — 새로 만들 것 없음.

## 프론트 UI 설계

- **파츠 행마다 4방향 이미지 박스 위에, 항상 펼쳐진 상태로 체크박스를 둔다.** 접었다 펴는 토글 없음 — 파츠 정보(이름·명세·서술 복귀) 바로 아래, 이미지 결과 바로 위라는 기존 세로 흐름(파츠 정보 → 선택 → 이미지 → 3D 상태)을 그대로 따른다.
- **체크박스 4개(정면/좌측/우측/후면 "사용") + 대칭 토글 2개(좌우대칭, 전후대칭)를 파츠마다 둔다.** 정면도 이번 스펙부터 껐다 켤 수 있다 — 이미 생성된 정면 이미지가 있어도 이번 3D 생성 라운드에서는 이 파츠를 통째로 빼고 싶을 수 있기 때문이다(예: 이번엔 다른 파츠만 다시 만들고 싶을 때).
- **기본값은 전부 켜짐이다.** 좌/우/후는 해당 방향 이미지가 있으면 켜짐(없으면 꺼짐, 대상 자체가 없으므로), 정면은 이미지가 있으면 켜짐. 사용자가 1명뿐인 현재 상황에서 "실수로 원치 않는 파츠까지 비용이 나가는" 위험보다 "매번 다 켜야 하는 번거로움"을 없애는 쪽이 낫다고 판단했다(2026-09-17 사용자 결정).
- **정면이 꺼져 있는데 좌/우/후 중 하나라도 켜져 있으면 유효성 오류다 — 프론트 전용 방어다(merge-gate 리뷰 F5, 정정).** 처음엔 "서버에서도 거부된다"고 적었지만 확인해보니 부정확했다: `POST /jobs/{jobId}/replan-mesh` 요청 모양(`ReplanPartMeshRequest`)에는 "정면 사용 여부" 필드 자체가 없다. 서버가 아는 건 "정면 **이미지**가 DB에 있는가"뿐이라, 프론트가 정면 체크를 꺼서 그 파츠를 요청 목록에서 통째로 빼지 않는 한 서버는 이 조합을 인지할 방법이 없다(정면 이미지가 있으면 `LeftRight=Both` 등만 와도 정면을 포함해 정상 접수됨). 조용한 오동작은 아니다 — 방어가 없으면 사용자가 요청한 것보다 파츠가 하나 더 3D화될 뿐이다. 그래도 이 조합의 유일한 방어가 프론트뿐이라는 사실은 스펙에 정확히 적어 둔다. (정면 꺼짐 + 나머지 전부 꺼짐은 오류가 아니라 "이 파츠는 이번엔 3D 안 만듦"으로 정상 처리한다.)
- **공급자/모델 선택은 파츠마다 두지 않고 한 번만 둔다.** 기존 `MeshBackfillRow`(`RunResult.tsx`)와 같은 자리·같은 패턴(`ProviderSelect`+`ModelSelect`, `useProviders`/`useProviderMeshModels` 재사용)으로, 파츠 목록 전체에 대해 하나의 공급자/모델을 고르는 별도 섹션을 둔다 — 파츠마다 같은 공급자를 다시 고르게 하는 것은 의미가 없다.
- **"3D 생성" 버튼은 배치 액션이다.** 클릭 시 정면 체크박스가 켜진 파츠들만 골라 각 파츠마다 `replanPartMesh`를 호출한다(공급자/모델은 공유). 유효성 오류가 있는 파츠가 하나라도 있으면 버튼을 막거나 클릭 시 해당 파츠를 짚어주고 진행하지 않는다.

## 검증 방법 (프론트)
- 테스트: `deriveReplanPlans` — 체크박스 조합별 `LeftRightPlan`/`BackPlan` 파생 (완료)
- 테스트: 정면 꺼짐 + 좌/우/후 중 하나라도 켜짐 → 유효성 오류로 판정하는 함수
- 테스트: 정면 켜짐 + 나머지 전부 꺼짐 → 유효(정면 단독 3D)로 판정
- 테스트: 정면 꺼짐 + 나머지 전부 꺼짐 → 유효(이 파츠는 3D 생성 대상에서 제외)
- UI: "3D 생성" 클릭 시 정면 켜진 파츠 수만큼 `replanPartMesh` 호출이 나가는지, 공급자/모델이 공유되는지 확인

## 예상 함정
1. **"서술 복귀"와의 상호작용**: 대칭 소스로 쓰인 Left가 나중에 서술 복귀로 obsolete 처리되면, 이미 만들어진 합성 Right 이미지는 그대로 남아있다(연결이 끊김, 이미 제출된 3D 요청엔 영향 없음). 이후 새로 "대칭" 요청이 오면 소스 이미지 조회도 다른 모든 곳과 동일하게 `IsCurrentCandidate` 기준 최신 조회를 쓰므로, obsolete된 이미지는 자동으로 후보에서 빠진다 — 별도 "제외 처리"를 새로 만들 필요 없음, 예상 함정 4(소스 없음)의 실패 경로로 자연스럽게 흡수된다.
2. **Tripo 스키마 재작성 회귀 위험**: 기존 Tripo 관련 테스트가 옛 스키마를 검증하고 있었다면 새 스키마로 다시 써야 한다. Tripo가 아직 실사용 전이라 프로덕션 회귀 위험은 낮다.
3. **정면 단독 3D 생성의 품질(Meshy 한정)**: 비정면 없이 정면 1장만으로 3D를 생성하면 옆면/뒷면 형태를 추정해야 해서 품질 편차가 클 수 있다 — 결과물 확인은 사용자 책임. Tripo는 최소 2장이라 이 경우 자체가 안 생긴다.
4. **대칭 소스 이미지가 없는 경우**: 사용자가 "대칭"을 켰는데 소스 방향 이미지가 한 번도 생성된 적 없으면 합성 생성 자체가 실패해야 한다 — 조용히 건너뛰지 않는다.
5. **검수 단계(`ReviewPhase.Descriptions`)와의 상호작용 — 실측 결과 무해함(merge-gate 리뷰 F4)**: `ReviewPhase`는 잡 전체에 하나뿐인 필드라서, 파츠 A를 "서술 복귀"시키면 잡 전체가 `Descriptions`로 바뀐다 — 처음 우려한 대로 "정면 자체가 없어 못 켜진다"로 막히지 않는다. 손대지 않은 파츠 B에 대칭 요청을 보내면 `AttachGeneratedImage`가 방금 만든 합성 이미지를 곧바로 `MarkObsoleted()` 한다. 다만 직접 확인해보니 무해하다: `IsCurrentCandidate`는 `IsSynthetic`만으로 이미 합성 이미지를 걸러내므로 `IsObsoleted`가 겹쳐 켜져도 조회 결과는 그대로고, 3D 제출은 "최신" 조회가 아니라 이 요청이 만든 ID를 그대로 쓰므로 정상 제출된다. 그래서 방어 코드 대신 이 사실을 확인하는 회귀 테스트만 둔다(`ReplanPartMeshHandlerTests.MirrorSucceeds_EvenWhenAnotherPartReturnedJobToDescriptionsPhase`).

## 검증 방법
- 테스트: `MeshRun.HasAllInputs` — 비정면 0/1/3장 각각에서 모든 입력이 준비되면 true인지 확인
- 테스트: Front만 선택(비정면 0장), 공급자가 Meshy → 제출 성공 / 공급자가 Tripo → 제출 시점에 거부(`MESH_INPUT_REJECTED`)
- 테스트: `TripoMeshProvider.Payload()`가 `files` 배열 4칸 고정, `{type, file_token}` 객체 스키마를 만드는지 확인
- 테스트: Left 이미지로 "좌우대칭" 요청 → 새 `GeneratedImage`(Right, `IsSynthetic=true`)가 생성되고, 그 Blob이 Left를 실제로 좌우 반전한 픽셀인지 확인
- 테스트: 합성 이미지가 있어도 `GeneratedImage.IsCurrentCandidate` 기준 조회(갤러리 응답, 자동 팬인)가 여전히 실제 이미지를 반환하는지 확인
- 테스트: 대칭 소스 이미지가 없을 때 합성 생성 요청이 실패하는지 확인
- 테스트: 합성 이미지 생성 후에도 같은 파츠·방향에 대해 사용자가 진짜 이미지를 다시 생성할 수 있는지(`TaskKind.Synthesize`가 `TaskKind.Generate` 중복 검사에 안 걸리는지) 확인
- 테스트: 이미 4장이 있고 자동 팬인으로 3D가 한 번 만들어진 파츠에, `ReplanMeshInputs`로 Front+Back만 다시 제출 → 같은 `Reconstruct` 공정에 새 `MeshRun`(RunNumber 2)이 생기고 새 입력으로 제출되는지 확인
- 테스트: 정면만 있는(자동 팬인이 아직 아무 공정도 안 만든) 파츠에 `ReplanMeshInputs`를 처음 호출 → `Reconstruct` 공정이 새로 계획되는지 확인(find-or-create의 "create" 경로)
- 테스트: 종료된 잡에 `ReplanMeshInputs` 호출 → 재개방(`Status=Running`) 되는지 확인
- UI: `PartGallery`에서 Left/Back 체크박스 동시 선택 후 "선택 생성" 클릭 → 두 요청이 배치로 나가고 카드에 반영되는지 확인
- UI: Left 카드의 "좌우대칭" 체크박스를 켜면 Right 카드의 "사용" 체크박스가 자동으로 꺼지고 비활성화되는지 확인

## 공식 문서 출처 (조회일 2026-09-17)
- Meshy Multi-Image API — https://docs.meshy.ai/en/api/multi-image-to-3d — "meshy-7(또는 latest): the first image is used as the primary (front) view. The order of the remaining images doesn't matter." (모델: `meshy-7`)
- Tripo Multiview-to-Model P1 — https://docs.tripo3d.ai/model-generation/multiview-to-model-p1-20260311.html — "'files': ... The list must contain exactly 4 items in the order [front, left, back, right]." / "Do not use less than two images to generate." / "You may omit certain input files by omitting the file_token, but the front input cannot be omitted."
- 원문 페이지가 이후 개정될 수 있으므로, 구현 시점에 다시 한 번 직접 열어 확인 권장. 이 문서 근거로 만든 코드에는 조회일 + URL을 주석으로 남긴다(이미 반영됨, `MeshyMeshProvider.cs`/`TripoMeshProvider.cs` 참고).

## 승인
- [x] 사용자 승인 (2026-09-17, 대칭을 합성 이미지 방식으로 확정한 최종안 기준)
