using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MeshRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeneratedMeshes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeshRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelBlobKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ModelContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModelSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PreviewBlobKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PreviewContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreviewSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    CreditsConsumed = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedMeshes", x => x.Id);
                    table.CheckConstraint("CK_GeneratedMeshes_ModelSize", "[ModelSizeBytes] > 0");
                    table.ForeignKey(
                        name: "FK_GeneratedMeshes_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeshRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunNumber = table.Column<int>(type: "int", nullable: false),
                    ProviderConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModelSeed = table.Column<int>(type: "int", nullable: false),
                    TextureSeed = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderTaskId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    CreditsConsumed = table.Column<int>(type: "int", nullable: true),
                    LastProviderCode = table.Column<int>(type: "int", nullable: true),
                    LastProviderRequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ModelBlobKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModelSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    PreviewBlobKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PreviewContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreviewSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeshRuns", x => x.Id);
                    table.CheckConstraint("CK_MeshRuns_Progress", "[Progress] BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "MeshRunInputs",
                columns: table => new
                {
                    MeshRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewDirection = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    GeneratedImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UploadContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderFileToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeshRunInputs", x => new { x.MeshRunId, x.ViewDirection });
                    table.ForeignKey(
                        name: "FK_MeshRunInputs_MeshRuns_MeshRunId",
                        column: x => x.MeshRunId,
                        principalTable: "MeshRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedMeshes_JobId",
                table: "GeneratedMeshes",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedMeshes_PartId_CreatedAt",
                table: "GeneratedMeshes",
                columns: new[] { "PartId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MeshRuns_JobId_PartId_RunNumber",
                table: "MeshRuns",
                columns: new[] { "JobId", "PartId", "RunNumber" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MeshRuns_ProviderTaskId",
                table: "MeshRuns",
                column: "ProviderTaskId",
                unique: true,
                filter: "[ProviderTaskId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MeshRuns_TaskId_RunNumber",
                table: "MeshRuns",
                columns: new[] { "TaskId", "RunNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedMeshes");

            migrationBuilder.DropTable(
                name: "MeshRunInputs");

            migrationBuilder.DropTable(
                name: "MeshRuns");
        }
    }
}
