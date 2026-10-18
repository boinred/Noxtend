# Sprite 역할 서버 연결 fixture

기준 revision: `c2dc3f9` (Tasks 1–5) 위 Task 6 API 변경. 생성 원본은 `apps/backend/Noxtend.Tests/Api/SpriteApiTests.cs`의 `ServerRolesFixture_MatchesActualResponseMapperAndJsonOptions`다. 이 테스트는 실제 `JobResponse.From` → `SpriteResponse.From` 및 `Program.cs`와 공유하는 `ApiJson.Configure` MVC JSON 설정을 사용한다. ID·시간은 고정한다.

저장소 루트에서 명시적으로 재생성한다.

```bash
NOXTEND_REGENERATE_SPRITE_ROLES_FIXTURE=1 dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~ServerRolesFixture_MatchesActualResponseMapperAndJsonOptions'
```

일반 실행은 파일을 쓰지 않고 JSON 구조·값을 비교한다. 속성 순서·공백은 비교하지 않는다. 경로는 테스트 실행 위치 대신 상위 `apps/backend/Noxtend.slnx`가 있는 저장소 루트를 찾는다.

```bash
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~SpriteApiTests|FullyQualifiedName~JsonBinding_'
git diff -- apps/frontend/src/infra/api/fixtures/sprite-roles.server.json
```

서버 fixture 최상위 키의 값은 모두 실제 `ApiResponse<JobResponse>`다.

- `mixedJob`: 명시적 Grid, backdrop·tile·object, 에셋별 canvas·scale (Backdrop 축소 포함). object plan만 `placements` 2개를 갖고 backdrop·tile plan은 `placements: null`이다.
- `legacyJob`: 새 DB 열 Canvas·Role이 null인 레이어 작업. 새 서버는 layer·작업 캔버스·scale 1·`placements: null`을 응답한다.
- `legacyApproval`: 새 DB 열이 null인 타일과 과거 `SpriteApprovedAsset` 승인 snapshot 재구성. 새 서버는 tile·작업 캔버스·scale 1·`placements: null`을 응답하고 snapshot 필드를 추가하지 않는다. SQL 연결 검사는 별도 persistence 테스트 범위다.
- `gridNull`: 계획이 없는 타일 작업의 명시적 grid null 및 빈 assets.

구형 DB 행의 **새 서버 응답** fixture다. 과거 서버가 role·canvas·scale·grid를 생략한 응답은 포함하지 않으며, 클라이언트의 과거 응답 호환성 검사는 별도 입력으로 검증한다.

`sprite-roles.request.json`은 `tiles`·`layers` 두 plan PUT 요청이다. 각 요청은 역할을 명시하고 grid 값 또는 null을 명시한다. `tiles`의 object만 `placements` 2개를 보내고 나머지 asset은 `placements`를 생략한다. 서버 테스트가 MVC formatter로 읽어 실제 controller·handler에 전달하고 저장 역할·Grid·placements를 비교한다. 역할·Grid 생략 호환성은 별도 서버 테스트이며 이 요청 비교 fixture에 섞지 않는다.
