using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    public partial class SeedSpriteSourcePrompt : Migration
    {
        private static readonly Guid SeedId = Guid.Parse("c9000000-0000-4000-8000-000000000001");

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.GenerateSpriteSource();
            // 운영자 등록 기준 이미지 프롬프트 슬롯 보존
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM [PromptVersions] WHERE [Kind] = N'GenerateSpriteSource' AND [Category] = N'Background')
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                VALUES ('{SeedId}', N'GenerateSpriteSource', N'Background', 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)}, 1, '2026-10-08T00:00:00+00:00');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: SeedId);

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";
    }
}
