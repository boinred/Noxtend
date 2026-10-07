using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    public partial class SeedSpriteAnalyzePrompt : Migration
    {
        private static readonly Guid SeedId = Guid.Parse("c7000000-0000-4000-8000-000000000001");

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.AnalyzeSprites();
            // Preserve an operator-created sprite prompt slot
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM [PromptVersions] WHERE [Kind] = N'AnalyzeSprites' AND [Category] = N'Background')
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                VALUES ('{SeedId}', N'AnalyzeSprites', N'Background', 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)}, 1, '2026-10-06T00:00:00+00:00');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: SeedId);

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";
    }
}
