using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundGeneratePromptV2 : Migration
    {
        private static readonly Guid NewId = Guid.Parse("b6000000-0000-4000-8000-000000000001");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 25, 6, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Background.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.BackgroundGenerateV2();
            var kind = TaskKind.Generate.ToString();

            // 버전 번호는 고정값이 아니다 (D-09) — 운영자가 이 (Kind, Category) 에 이미
            // 행을 발급했을 수 있다. 비활성화 먼저: filtered unique 가 활성 한 행만 허용한다
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(NewId)}, {Literal(kind)}, {Literal(Category)},
                    ISNULL(MAX([Version]), 0) + 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)}, 1,
                    {Literal(SeededAt)}
                FROM [PromptVersions]
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                """);
        }

        /// <inheritdoc />
        // Background 행은 이번이 처음이라 되살릴 직전 행이 없다 — 지우면 NULL 기본이 다시 잡는다
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: NewId);

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
