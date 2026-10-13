using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <summary>2D 배경 원본 보완 v1 시드. 운영자가 만든 슬롯은 보존한다.</summary>
    public partial class SeedSpriteSourceEnhancePrompt : Migration
    {
        private static readonly Guid SeedId = Guid.Parse("c9000000-0000-4000-8000-000000000002");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.EnhanceSpriteSource();
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM [PromptVersions] WHERE [Kind] = N'EnhanceSpriteSource' AND [Category] = N'Background')
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                VALUES ('{SeedId}', N'EnhanceSpriteSource', N'Background', 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)}, 1, '2026-10-13T09:00:00+00:00');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: SeedId);

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";
    }
}
