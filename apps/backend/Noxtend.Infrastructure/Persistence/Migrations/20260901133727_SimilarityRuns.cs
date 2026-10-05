using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimilarityRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SimilarityRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MaxIterations = table.Column<int>(type: "int", nullable: false),
                    CurrentIteration = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimilarityRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SimilarityEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RenderBlobKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RenderContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RenderSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    RenderSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PromptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScoreJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdjustmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RegenerationNotesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimilarityEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimilarityEvaluations_SimilarityRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "SimilarityRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SimilarityEvaluations_RunId_Sequence",
                table: "SimilarityEvaluations",
                columns: new[] { "RunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SimilarityRuns_JobId_IdempotencyKey",
                table: "SimilarityRuns",
                columns: new[] { "JobId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SimilarityRuns_OpenPerJob",
                table: "SimilarityRuns",
                column: "JobId",
                unique: true,
                filter: "[Status] IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SimilarityEvaluations");

            migrationBuilder.DropTable(
                name: "SimilarityRuns");
        }
    }
}
