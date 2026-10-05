using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream J — 사용자 요청으로 캐릭터 프롬프트 세 개(Extract·Decompose·
    // Generate) 전체를 한 번씩 다시 읽으며 "불필요하거나 오해가 될 법한 것"을 찾아 정리했다.
    // 오늘(2026-08-18) 하루에만 F/G/H/I가 연달아 붙으면서 예시가 겹치거나, 방금 없앤 힌트
    // 옵션을 여전히 언급하는 죽은 문구가 남아 있었다.
    //
    // 1) Extract — "힌트 연장 예외" 규칙(workstream E)의 "발목 위 갑주" 예시가 이제
    //    workstream G(관절 경계 분리)·workstream I(갑옷 자동 분리)와 세 번째로 같은 사례를
    //    반복하고 있었다. 심지어 파츠 이름 짓는 예시가 서로 달랐다("다리 갑주" vs 관절
    //    규칙의 위치 기반 이름 "왼팔 갑주 팔꿈치 위") — 같은 대상에 서로 다른 네이밍 지시가
    //    동시에 걸려 있던 셈이라 모델을 헷갈리게 할 위험이 있었다. 예시를 지우고 "그 규칙들과
    //    같은 원칙"이라고만 짧게 참조하도록 줄였다.
    // 2) Decompose — occludedBy 배제 규칙(workstream H)의 예시 중 "벨트가 하의 위에 있다"·
    //    "버클·메달·파우치가 벨트 스트랩 위에 있다"는, workstream I로 벨트가 절대 별도
    //    파츠로 안 뽑히게 되면서 다시는 일어날 수 없는 죽은 예시가 됐다 — 여전히 살아있는
    //    예시(부츠/다리 갑주, 갑옷 조각/의상)로 교체했다. 동시에 사용자가 지적한 반대쪽
    //    위험(겹침을 빼라는 지시가 과하게 적용돼 파츠 자기 자신의 형태까지 생략될 수 있음)에
    //    대응해 안전장치 문장을 같은 규칙에 추가했다.
    // 3) Generate — 성별 무관 악세사리 목록의 "belts"를 뺐다. workstream I 이후로는 벨트라는
    //    단독 파츠 자체가 다시는 존재하지 않으니, 이 문구를 남겨두면 벨트도 여전히 독립
    //    파츠로 그려질 수 있다고 오해할 수 있었다.
    //
    // 셋 다 프롬프트 문구 정리이지 새 규칙 추가가 아니다 — 실 API로 결과가 달라지는지는
    // 재검증 대상이지만, 죽은 예시·이중 지시를 없앤 거라 회귀 위험은 낮다고 판단.
    //
    // 🔴 스코프 함정(이전 캐릭터 마이그레이션과 동일): 비활성화·채번을
    // (Kind, Category='Character')로 스코프해 기본(NULL) 활성을 건드리지 않는다.
    public partial class CharacterPromptWorkstreamJ : Migration
    {
        // 직전 활성 id — Down 에서 되살린다.
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000091");
        private static readonly Guid DecomposePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000082");
        private static readonly Guid GeneratePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000062");

        // 이번에 심는 새 버전 id
        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000101");
        private static readonly Guid DecomposeNewId = Guid.Parse("c4000000-0000-4000-8000-000000000102");
        private static readonly Guid GenerateNewId = Guid.Parse("c4000000-0000-4000-8000-000000000103");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 18, 4, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Reseed(migrationBuilder, TaskKind.Extract, ExtractNewId, SeedPrompts.CharacterExtract());
            Reseed(migrationBuilder, TaskKind.Decompose, DecomposeNewId, SeedPrompts.CharacterDecompose());
            Reseed(migrationBuilder, TaskKind.Generate, GenerateNewId, SeedPrompts.CharacterGenerate());
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: ExtractNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: GenerateNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: ExtractPrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: DecomposePrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: GeneratePrevId,
                column: "IsActive", value: true);
        }

        private static void Reseed(
            MigrationBuilder migrationBuilder,
            TaskKind kind,
            Guid id,
            (string System, string User, string Schema, string Note) prompt)
        {
            var kindLiteral = Literal(kind.ToString());
            var categoryLiteral = Literal(Category);

            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral};

                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                VALUES (
                    {Literal(id)}, {kindLiteral}, {categoryLiteral},
                    ISNULL((SELECT MAX([Version]) FROM [PromptVersions]
                            WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral}), 0) + 1,
                    {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)}, {Literal(prompt.Note)},
                    1, {Literal(SeededAt)});
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
