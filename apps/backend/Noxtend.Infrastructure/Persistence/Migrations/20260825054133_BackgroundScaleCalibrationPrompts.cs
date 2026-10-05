using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundScaleCalibrationPrompts : Migration
    {
        private static readonly Guid AnalyzeId =
            Guid.Parse("b6000000-0000-4000-8000-000000000002");

        private static readonly Guid ExtractId =
            Guid.Parse("b6000000-0000-4000-8000-000000000003");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 25, 7, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Background.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedPrompt(
                migrationBuilder,
                TaskKind.Analyze,
                AnalyzeId,
                SeedPrompts.BackgroundAnalyze());
            SeedPrompt(
                migrationBuilder,
                TaskKind.Extract,
                ExtractId,
                SeedPrompts.BackgroundExtract());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RestorePreviousPrompt(migrationBuilder, TaskKind.Analyze, AnalyzeId);
            RestorePreviousPrompt(migrationBuilder, TaskKind.Extract, ExtractId);
        }

        // Category-scoped prompt activation with operator-safe version allocation
        private static void SeedPrompt(
            MigrationBuilder migrationBuilder,
            TaskKind taskKind,
            Guid id,
            (string System, string User, string Schema, string Note) prompt)
        {
            var kind = taskKind.ToString();
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(id)}, {Literal(kind)}, {Literal(Category)},
                    ISNULL(MAX([Version]), 0) + 1,
                    {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)},
                    {Literal(prompt.Note)}, 1, {Literal(SeededAt)}
                FROM [PromptVersions]
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                """);
        }

        // Exact-slot rollback with fallback to the newest remaining categorized row
        private static void RestorePreviousPrompt(
            MigrationBuilder migrationBuilder,
            TaskKind taskKind,
            Guid id)
        {
            var kind = taskKind.ToString();
            migrationBuilder.Sql($"""
                DELETE FROM [PromptVersions] WHERE [Id] = {Literal(id)};
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                UPDATE [PromptVersions] SET [IsActive] = 1
                    WHERE [Id] = (
                        SELECT TOP (1) [Id]
                        FROM [PromptVersions]
                        WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)}
                        ORDER BY [Version] DESC);
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
