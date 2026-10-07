using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpriteProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpriteExportInputJson",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpriteInputJson",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductionMode",
                table: "Jobs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "ThreeD");

            migrationBuilder.AddColumn<Guid>(
                name: "SpriteCompletedExportId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpriteGenerationCanvasJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpriteOutputCanvasJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpritePhase",
                table: "Jobs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpriteReviewRevision",
                table: "Jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpriteSettingsJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpriteSourceCanvasJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpriteTransformJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpriteAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlanRevision = table.Column<int>(type: "int", nullable: false),
                    AnchorJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovedBaseImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteAssets_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpriteExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlobKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteExports_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpriteImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FrameIndex = table.Column<int>(type: "int", nullable: false),
                    PlanRevision = table.Column<int>(type: "int", nullable: false),
                    BaseImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BlobKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteImages_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpriteRequests",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReceiptJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteRequests", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_SpriteRequests_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpriteFrames",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteFrames", x => new { x.AssetId, x.Index });
                    table.ForeignKey(
                        name: "FK_SpriteFrames_SpriteAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "SpriteAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteAssets_JobId",
                table: "SpriteAssets",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteExports_JobId",
                table: "SpriteExports",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteImages_JobId",
                table: "SpriteImages",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteRequests_JobId",
                table: "SpriteRequests",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpriteExports");

            migrationBuilder.DropTable(
                name: "SpriteFrames");

            migrationBuilder.DropTable(
                name: "SpriteImages");

            migrationBuilder.DropTable(
                name: "SpriteRequests");

            migrationBuilder.DropTable(
                name: "SpriteAssets");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SpriteExportInputJson",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SpriteInputJson",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ProductionMode",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SpriteCompletedExportId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SpriteGenerationCanvasJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SpriteOutputCanvasJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SpritePhase",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SpriteReviewRevision",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SpriteSettingsJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SpriteSourceCanvasJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SpriteTransformJson",
                table: "Jobs");
        }
    }
}
