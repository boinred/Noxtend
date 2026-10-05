using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // armor-any-region ①-보강 2·3 (docs/specs/2026-09-08-armor-any-region.md) — 실 API 실측
    // F96BE0F7. 힌트가 상의·하의·신발·무기·장갑 5종뿐인데 Extract 가 힌트에 없는 베이스바디·
    // 머리·머리카락을 냈다("항상 한 파츠" / "ALWAYS two … 반환하라" 는 강제 반환 문구가 힌트
    // 스코프 규칙보다 앞서 읽혔다) — 두 문장을 스코프 조건절로 바꾼다. 같은 실측에서 힌트에
    // 벨트가 없고 하의만 있었는데 벨트 5종이 딸려 나왔다("상의, 하의, 원피스" 열거 탓) —
    // 벨트 자동 분리를 상의 제외 하의·원피스로 좁힌다. Extract 만 재시드한다.
    public partial class CharacterHintScopeWins : Migration
    {
        // 직전 활성 id — armor-any-region(20260908104531)의 ExtractNewId. Down 에서 되살린다.
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000116");

        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000119");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 9, 15, 0, 25, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ReseedCharacter(migrationBuilder, TaskKind.Extract, ExtractNewId, SeedPrompts.CharacterExtract());
        }

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

        private static void ReseedCharacter(
            MigrationBuilder migrationBuilder,
            TaskKind kind,
            Guid id,
            (string System, string User, string Schema, string Note) prompt)
        {
            var kindLiteral = Literal(kind.ToString());
            var categoryLiteral = Literal(Category);

            migrationBuilder.Sql($"""
                IF NOT EXISTS (
                    SELECT 1 FROM [PromptVersions]
                    WHERE [Kind] = {kindLiteral}
                      AND [Category] = {categoryLiteral}
                      AND [IsActive] = 1
                      AND [System] = {Literal(prompt.System)}
                      AND [User] = {Literal(prompt.User)}
                      AND [JsonSchema] = {Literal(prompt.Schema)}
                )
                BEGIN
                    UPDATE [PromptVersions] SET [IsActive] = 0
                        WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral};

                    INSERT INTO [PromptVersions]
                        ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                    VALUES (
                        {Literal(id)}, {kindLiteral}, {categoryLiteral},
                        ISNULL((SELECT MAX([Version]) FROM [PromptVersions]
                                WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral}), 0) + 1,
                        {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)},
                        {Literal(prompt.Note)}, 1, {Literal(SeededAt)});
                END
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
