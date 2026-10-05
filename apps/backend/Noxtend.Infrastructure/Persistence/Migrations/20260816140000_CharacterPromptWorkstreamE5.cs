using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream E §5 결정⑤ — 힌트 스코프-분리 충돌 패턴(이슈 4·12)
    // 대응을 Extract 프롬프트에 반영한다. 힌트 타입의 구조적 연장(벨트 부착물·다리 갑주)은
    // 스코프 안, 힌트와 무관한 완전히 새 카테고리는 여전히 금지 — 구체 사례로 명시했다
    // (추상 원칙 문구는 이미 한 번 실패했다, 이슈 11).
    //
    // Decompose·Generate는 이번에 안 건드린다 — ⑤는 Extract 스코프 규칙(③)의 절충이라
    // Extract 하나만 재시드한다.
    //
    // 🔴 스코프 함정(CharacterPromptWorkstreamDE와 동일): 비활성화·채번을
    // (Kind, Category='Character')로 스코프해 기본(NULL) 활성을 건드리지 않는다.
    public partial class CharacterPromptWorkstreamE5 : Migration
    {
        // 직전 활성 id — CharacterPromptWorkstreamDE가 심은 것. Down에서 되살린다.
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000031");

        // 이번에 심는 새 버전 id
        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000041");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
            => ReseedCharacterExtract(migrationBuilder, ExtractNewId, SeedPrompts.CharacterExtract());

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: ExtractNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: ExtractPrevId,
                column: "IsActive", value: true);
        }

        // CharacterPromptWorkstreamDE.ReseedCharacterPrompt와 같은 로직 — Extract 하나만 대상
        private static void ReseedCharacterExtract(
            MigrationBuilder migrationBuilder,
            Guid id,
            (string System, string User, string Schema, string Note) prompt)
        {
            var kindLiteral = Literal(TaskKind.Extract.ToString());
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
