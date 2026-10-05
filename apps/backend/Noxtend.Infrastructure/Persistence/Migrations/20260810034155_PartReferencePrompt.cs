using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartReferencePrompt : Migration
    {
        private static readonly Guid V1Id =
            Guid.Parse("a1000000-0000-4000-8000-000000000003");

        private static readonly Guid V2Id =
            Guid.Parse("a1000000-0000-4000-8000-000000000005");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Active prompt switch before insert because the filtered index permits one active row
            migrationBuilder.UpdateData(
                table: "PromptVersions",
                keyColumn: "Id",
                keyValue: V1Id,
                column: "IsActive",
                value: false);

            var (system, user, schema, note) = SeedPrompts.DecomposeV2();

            migrationBuilder.InsertData(
                table: "PromptVersions",
                columns: ["Id", "Kind", "Version", "System", "User", "JsonSchema", "Note", "IsActive", "CreatedAt"],
                values:
                [
                    V2Id, TaskKind.Decompose.ToString(), 2, system, user, schema, note, true,
                    new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero),
                ]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PromptVersions",
                keyColumn: "Id",
                keyValue: V2Id);

            migrationBuilder.UpdateData(
                table: "PromptVersions",
                keyColumn: "Id",
                keyValue: V1Id,
                column: "IsActive",
                value: true);
        }
    }
}
