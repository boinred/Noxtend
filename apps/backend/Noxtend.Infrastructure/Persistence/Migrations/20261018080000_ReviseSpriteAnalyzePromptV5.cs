using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(NoxtendDbContext))]
    [Migration("20261018080000_ReviseSpriteAnalyzePromptV5")]
    public partial class ReviseSpriteAnalyzePromptV5 : Migration
    {
        private static readonly Guid PreviousId = Guid.Parse("c7000000-0000-4000-8000-000000000004");
        private static readonly Guid AnalyzeId = Guid.Parse("c7000000-0000-4000-8000-000000000005");
        private static readonly DateTimeOffset SeededAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        private static readonly string Kind = TaskKind.AnalyzeSprites.ToString();
        private static readonly string Category = AssetCategory.Background.ToString();

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var previous = SeedPrompts.AnalyzeSpritesV4();
            var prompt = SeedPrompts.AnalyzeSpritesV5();

            // 운영자 활성 선택·시드 직접 수정 보존
            migrationBuilder.Sql($"""
                IF EXISTS (
                    SELECT 1 FROM [PromptVersions]
                    WHERE [Id] = {Literal(PreviousId)} AND [Kind] = {Literal(Kind)}
                        AND [Category] = {Literal(Category)} AND [IsActive] = 1
                        AND DATALENGTH([System]) = DATALENGTH({Literal(previous.System)})
                        AND [System] COLLATE Latin1_General_100_BIN2 = {Literal(previous.System)}
                        AND DATALENGTH([User]) = DATALENGTH({Literal(previous.User)})
                        AND [User] COLLATE Latin1_General_100_BIN2 = {Literal(previous.User)}
                        AND DATALENGTH([JsonSchema]) = DATALENGTH({Literal(previous.Schema)})
                        AND [JsonSchema] COLLATE Latin1_General_100_BIN2 = {Literal(previous.Schema)}
                        AND DATALENGTH([Note]) = DATALENGTH({Literal(previous.Note)})
                        AND [Note] COLLATE Latin1_General_100_BIN2 = {Literal(previous.Note)})
                BEGIN
                    UPDATE [PromptVersions] SET [IsActive] = 0 WHERE [Id] = {Literal(PreviousId)};
                    INSERT INTO [PromptVersions]
                        ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                    SELECT {Literal(AnalyzeId)}, {Literal(Kind)}, {Literal(Category)},
                        ISNULL(MAX([Version]), 0) + 1,
                        {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)},
                        {Literal(prompt.Note)}, 1, {Literal(SeededAt)}
                    FROM [PromptVersions]
                    WHERE [Kind] = {Literal(Kind)} AND [Category] = {Literal(Category)};
                END;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 이후 운영자 선택 보존·실제 교체한 V4 ID 복구
            migrationBuilder.Sql($"""
                DECLARE @RestorePrevious bit = CASE WHEN EXISTS (
                    SELECT 1 FROM [PromptVersions] WHERE [Id] = {Literal(AnalyzeId)} AND [IsActive] = 1)
                    THEN 1 ELSE 0 END;
                DELETE FROM [PromptVersions] WHERE [Id] = {Literal(AnalyzeId)};
                IF @RestorePrevious = 1
                    UPDATE [PromptVersions] SET [IsActive] = 1 WHERE [Id] = {Literal(PreviousId)};
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";
        private static string Literal(Guid value) => $"'{value}'";
        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}'";
    }
}
