using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 역할·격자를 필수로 안내하는 2D 분석 v3 프롬프트.
    ///
    /// 종류별 슬롯 교체 방식은 BackgroundGeneratePromptV3 와 같다.
    /// </summary>
    [DbContext(typeof(NoxtendDbContext))]
    [Migration("20261014080000_ReviseSpriteAnalyzePromptV3")]
    public partial class ReviseSpriteAnalyzePromptV3 : Migration
    {
        private static readonly Guid AnalyzeId =
            Guid.Parse("c7000000-0000-4000-8000-000000000003");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

        private static readonly string Kind = TaskKind.AnalyzeSprites.ToString();

        private static readonly string Category = AssetCategory.Background.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var prompt = SeedPrompts.AnalyzeSpritesV3();

            // 운영자 버전 보존을 위한 MAX(Version)+1 할당
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {Literal(Kind)} AND [Category] = {Literal(Category)};
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(AnalyzeId)}, {Literal(Kind)}, {Literal(Category)},
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
                DELETE FROM [PromptVersions] WHERE [Id] = {Literal(AnalyzeId)};
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
