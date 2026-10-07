using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 방향 정의를 담은 배경 Generate v3 프롬프트.
    ///
    /// 종류별 슬롯 교체 방식은 BackgroundSurfacePartsPrompt 와 같다.
    /// </summary>
    public partial class BackgroundGeneratePromptV3 : Migration
    {
        private static readonly Guid GenerateId =
            Guid.Parse("b6000000-0000-4000-8000-000000000005");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 10, 7, 6, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Background.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var prompt = SeedPrompts.BackgroundGenerateV3();
            var kind = TaskKind.Generate.ToString();

            // 종류 슬롯을 비활성화한 뒤 MAX(Version)+1 로 할당한다 — 고정 버전을 쓰면
            // 운영자가 만든 버전을 덮어쓴다
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(GenerateId)}, {Literal(kind)}, {Literal(Category)},
                    ISNULL(MAX([Version]), 0) + 1,
                    {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)},
                    {Literal(prompt.Note)}, 1, {Literal(SeededAt)}
                FROM [PromptVersions]
                WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var kind = TaskKind.Generate.ToString();

            migrationBuilder.Sql($"""
                DELETE FROM [PromptVersions] WHERE [Id] = {Literal(GenerateId)};
                UPDATE [PromptVersions] SET [IsActive] = 1
                    WHERE [Id] = (
                        SELECT TOP 1 [Id] FROM [PromptVersions]
                        WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)}
                        ORDER BY [Version] DESC);
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}'";
    }
}
