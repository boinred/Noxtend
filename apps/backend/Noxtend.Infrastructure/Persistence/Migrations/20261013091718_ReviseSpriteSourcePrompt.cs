using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 설정 우선 규칙과 {{settings}} 를 더한 2D 원본 후보 생성 v2 프롬프트.
    ///
    /// 슬롯 교체 방식은 ReviseSpriteAnalyzePrompt 와 같다.
    /// </summary>
    public partial class ReviseSpriteSourcePrompt : Migration
    {
        private static readonly Guid ReviseId =
            Guid.Parse("c9000000-0000-4000-8000-000000000003");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

        private static readonly string Kind = "GenerateSpriteSource";

        private static readonly string Category = "Background";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var prompt = SeedPrompts.GenerateSpriteSourceV2();

            // 비활성화 뒤 MAX(Version)+1 - 고정 버전은 운영자 버전을 덮어쓴다
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {Literal(Kind)} AND [Category] = {Literal(Category)};
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(ReviseId)}, {Literal(Kind)}, {Literal(Category)},
                    ISNULL(MAX([Version]), 0) + 1,
                    {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)},
                    {Literal(prompt.Note)}, 1, {Literal(SeededAt)}
                FROM [PromptVersions]
                WHERE [Kind] = {Literal(Kind)} AND [Category] = {Literal(Category)};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DELETE FROM [PromptVersions] WHERE [Id] = {Literal(ReviseId)};
                UPDATE [PromptVersions] SET [IsActive] = 1
                    WHERE [Id] = (
                        SELECT TOP 1 [Id] FROM [PromptVersions]
                        WHERE [Kind] = {Literal(Kind)} AND [Category] = {Literal(Category)}
                        ORDER BY [Version] DESC);
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}'";
    }
}
