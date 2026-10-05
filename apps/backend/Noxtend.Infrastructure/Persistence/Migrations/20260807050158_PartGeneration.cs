using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PartId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PerImage",
                table: "ModelPrices",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputImages",
                table: "LlmCalls",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageModel",
                table: "Jobs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImageProviderConfigId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GeneratedImageId",
                table: "AssetParts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GeneratedImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedImages_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_PartId",
                table: "Tasks",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedImages_JobId",
                table: "GeneratedImages",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedImages_PartId",
                table: "GeneratedImages",
                column: "PartId");

            SeedGeneratePrompt(migrationBuilder);
            SeedImagePrices(migrationBuilder);
        }

        /// <summary>
        /// Seeds the v1 Generate prompt. Fixed ids, same rule as earlier seeds — a per-environment
        /// id makes it impossible to compare which row misbehaved.
        /// </summary>
        private static void SeedGeneratePrompt(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.For(TaskKind.Generate);

            migrationBuilder.InsertData(
                table: "PromptVersions",
                columns: ["Id", "Kind", "Version", "System", "User", "JsonSchema", "Note", "IsActive", "CreatedAt"],
                values:
                [
                    Guid.Parse("a1000000-0000-4000-8000-000000000004"),
                    TaskKind.Generate.ToString(), 1, system, user, schema, note, true, SeededAt,
                ]);
        }

        /// <summary>
        /// Seeds per-image prices. These live in a separate list from the token rows because the
        /// earlier ModelPrices migration runs before the PerImage column exists — seeding them
        /// there would insert image models with no per-image price at all.
        /// </summary>
        private static void SeedImagePrices(MigrationBuilder migrationBuilder)
        {
            foreach (var row in SeedModelPrices.Images)
            {
                migrationBuilder.InsertData(
                    table: "ModelPrices",
                    columns:
                    [
                        "Id", "Model", "InputPerMillion", "OutputPerMillion",
                        "LongContextFrom", "LongInputPerMillion", "LongOutputPerMillion",
                        "EffectiveFrom", "Note", "PerImage",
                    ],
                    values:
                    [
                        row.Id, row.Model, row.Input, row.Output,
                        row.LongFrom, row.LongInput, row.LongOutput,
                        SeedModelPrices.EffectiveFrom, row.Note, row.PerImage,
                    ]);
            }
        }

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedImages");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_PartId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PartId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PerImage",
                table: "ModelPrices");

            migrationBuilder.DropColumn(
                name: "OutputImages",
                table: "LlmCalls");

            migrationBuilder.DropColumn(
                name: "ImageModel",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ImageProviderConfigId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "GeneratedImageId",
                table: "AssetParts");
        }
    }
}
