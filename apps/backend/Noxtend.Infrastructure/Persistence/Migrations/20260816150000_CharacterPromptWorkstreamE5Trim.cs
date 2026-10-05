using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // 재현성 이슈(workstream-e.spec.md §1.6 이슈 13) 대응 후보 중 하나 — 규칙 커버리지를
    // 하나도 줄이지 않고, 같은 예시(벨트→파우치 분리)가 세 곳에서 반복되던 것만 하나로
    // 합쳐 Extract·Decompose 프롬프트 분량을 줄인다. "프롬프트가 길수록 디테일 포착에
    // 쓸 주의력이 분산된다"는 가설을 검증하기 위한 통제 변수 — 뜻은 그대로, 분량만 축소.
    //
    // 🔴 스코프 함정(이전 캐릭터 마이그레이션과 동일): 비활성화·채번을
    // (Kind, Category='Character')로 스코프해 기본(NULL) 활성을 건드리지 않는다.
    public partial class CharacterPromptWorkstreamE5Trim : Migration
    {
        // 직전 활성 id — Down에서 되살린다.
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000041");
        private static readonly Guid DecomposePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000032");

        // 이번에 심는 새 버전 id
        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000051");
        private static readonly Guid DecomposeNewId = Guid.Parse("c4000000-0000-4000-8000-000000000052");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Reseed(migrationBuilder, TaskKind.Extract, ExtractNewId, SeedPrompts.CharacterExtract());
            Reseed(migrationBuilder, TaskKind.Decompose, DecomposeNewId, SeedPrompts.CharacterDecompose());
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: ExtractNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: ExtractPrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: DecomposePrevId,
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
