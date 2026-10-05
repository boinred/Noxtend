using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LlmCallSimilarityCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "TaskId",
                table: "LlmCalls",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SimilarityEvaluationId",
                table: "LlmCalls",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LlmCalls_ExactlyOneCorrelation",
                table: "LlmCalls",
                sql: "([TaskId] IS NULL AND [SimilarityEvaluationId] IS NOT NULL) OR ([TaskId] IS NOT NULL AND [SimilarityEvaluationId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LlmCalls_ExactlyOneCorrelation",
                table: "LlmCalls");

            migrationBuilder.DropColumn(
                name: "SimilarityEvaluationId",
                table: "LlmCalls");

            migrationBuilder.AlterColumn<Guid>(
                name: "TaskId",
                table: "LlmCalls",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
