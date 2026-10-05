using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 면을 덮는 파츠를 표시하는 배경 분해 프롬프트.
    ///
    /// Design Ref: background-surface-parts(#20) §4.1
    ///
    /// 종류별 슬롯 교체 방식은 BackgroundScaleCalibrationPrompts 와 같다.
    /// </summary>
    public partial class BackgroundSurfacePartsPrompt : Migration
    {
        private static readonly Guid DecomposeId =
            Guid.Parse("b6000000-0000-4000-8000-000000000004");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 6, 6, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Background.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var prompt = SeedPrompts.BackgroundDecompose();
            var kind = TaskKind.Decompose.ToString();

            // 종류 슬롯을 비활성화한 뒤 MAX(Version)+1 로 할당한다 — 고정 버전을 쓰면
            // 운영자가 만든 버전을 덮어쓴다 (background-scale-calibration D-09)
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(DecomposeId)}, {Literal(kind)}, {Literal(Category)},
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
            var kind = TaskKind.Decompose.ToString();

            migrationBuilder.Sql($"""
                DELETE FROM [PromptVersions] WHERE [Id] = {Literal(DecomposeId)};
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
