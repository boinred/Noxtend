using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TuningFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoldenSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoredImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpectedNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldenSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LlmCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PromptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponsePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    System = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    User = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JsonSchema = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Verdicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPass = table.Column<bool>(type: "bit", nullable: false),
                    Memo = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verdicts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoldenSamples_StoredImageId",
                table: "GoldenSamples",
                column: "StoredImageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_JobId",
                table: "LlmCalls",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_Kind_At",
                table: "LlmCalls",
                columns: new[] { "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_TaskId",
                table: "LlmCalls",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_ActiveByKind",
                table: "PromptVersions",
                column: "Kind",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_Kind_Version",
                table: "PromptVersions",
                columns: new[] { "Kind", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Verdicts_JobId",
                table: "Verdicts",
                column: "JobId",
                unique: true);

            SeedActivePrompts(migrationBuilder);
        }

        /// <summary>
        /// 세 단계의 v1 프롬프트를 심고 활성화한다.
        ///
        /// Design Ref: §3.5 — **활성 프롬프트가 없으면 작업이 시작조차 못 한다.**
        /// 접수가 `PROMPT_NOT_ACTIVE` 로 막히므로 빈 DB 로 뜬 서버는 아무것도 할 수 없다.
        ///
        /// id 를 고정한다. `Guid.NewGuid()` 를 쓰면 DB 마다 값이 달라져, 내역의
        /// `PromptVersionId` 를 다른 환경과 대조할 수 없다.
        /// </summary>
        private static void SeedActivePrompts(MigrationBuilder migrationBuilder)
        {
            var seeded = new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

            (TaskKind Kind, Guid Id)[] rows =
            [
                (TaskKind.Analyze,   Guid.Parse("a1000000-0000-4000-8000-000000000001")),
                (TaskKind.Extract,   Guid.Parse("a1000000-0000-4000-8000-000000000002")),
                (TaskKind.Decompose, Guid.Parse("a1000000-0000-4000-8000-000000000003")),
            ];

            foreach (var (kind, id) in rows)
            {
                var (system, user, schema, note) = SeedPrompts.For(kind);

                migrationBuilder.InsertData(
                    table: "PromptVersions",
                    columns: ["Id", "Kind", "Version", "System", "User", "JsonSchema", "Note", "IsActive", "CreatedAt"],
                    values: [id, kind.ToString(), 1, system, user, schema, note, true, seeded]);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 표를 지우므로 심은 행도 함께 사라진다 — 별도 삭제가 필요 없다
            migrationBuilder.DropTable(
                name: "GoldenSamples");

            migrationBuilder.DropTable(
                name: "LlmCalls");

            migrationBuilder.DropTable(
                name: "PromptVersions");

            migrationBuilder.DropTable(
                name: "Verdicts");
        }
    }
}
