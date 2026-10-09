using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceGenerationCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LlmCalls_ExactlyOneCorrelation",
                table: "LlmCalls");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "LlmCalls",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceGenerationId",
                table: "LlmCalls",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_SourceGenerationId",
                table: "LlmCalls",
                column: "SourceGenerationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LlmCalls_ExactlyOneCorrelation",
                table: "LlmCalls",
                sql: "([JobId] IS NOT NULL AND [TaskId] IS NOT NULL AND [SimilarityEvaluationId] IS NULL AND [SourceGenerationId] IS NULL) OR ([JobId] IS NOT NULL AND [TaskId] IS NULL AND [SimilarityEvaluationId] IS NOT NULL AND [SourceGenerationId] IS NULL) OR ([JobId] IS NULL AND [TaskId] IS NULL AND [SimilarityEvaluationId] IS NULL AND [SourceGenerationId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LlmCalls_SourceGenerationId",
                table: "LlmCalls");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LlmCalls_ExactlyOneCorrelation",
                table: "LlmCalls");

            migrationBuilder.DropColumn(
                name: "SourceGenerationId",
                table: "LlmCalls");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "LlmCalls",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LlmCalls_ExactlyOneCorrelation",
                table: "LlmCalls",
                sql: "([TaskId] IS NULL AND [SimilarityEvaluationId] IS NOT NULL) OR ([TaskId] IS NOT NULL AND [SimilarityEvaluationId] IS NULL)");
        }
    }
}
