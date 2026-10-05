using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MeshArtifacts : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// 산출물을 형식별 열에서 목록 표로 옮긴다 (Design §9 · D-08).
        ///
        /// **순서가 전부다.** 스캐폴딩은 열을 먼저 지우고 표를 만드는데, 그러면 옮길 값이
        /// 이미 사라진 뒤다. 표를 먼저 만들고, 옮기고, 그 다음에 지운다.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ① 이름 정정 — Meshy 는 아무것도 올리지 않는다
            migrationBuilder.RenameColumn(
                name: "UploadedAt",
                table: "MeshRunInputs",
                newName: "PreparedAt");

            // ② 새 표
            migrationBuilder.CreateTable(
                name: "GeneratedMeshArtifacts",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    GeneratedMeshId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedMeshArtifacts", x => new { x.GeneratedMeshId, x.Kind });
                    table.CheckConstraint("CK_GeneratedMeshArtifacts_Size", "[SizeBytes] > 0");
                    table.ForeignKey(
                        name: "FK_GeneratedMeshArtifacts_GeneratedMeshes_GeneratedMeshId",
                        column: x => x.GeneratedMeshId,
                        principalTable: "GeneratedMeshes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeshRunArtifacts",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MeshRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeshRunArtifacts", x => new { x.MeshRunId, x.Kind });
                    table.ForeignKey(
                        name: "FK_MeshRunArtifacts_MeshRuns_MeshRunId",
                        column: x => x.MeshRunId,
                        principalTable: "MeshRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ③ 기존 값 이관.
            //
            // **실서버에는 옮길 행이 없다** — 3D 가 한 번도 성공한 적이 없어 GeneratedMeshes
            // 가 비어 있다. 그래도 쓰는 이유는 로컬·테스트 DB 에는 행이 있고, 이관 없는
            // 마이그레이션은 그 환경들을 조용히 깨뜨리기 때문이다
            migrationBuilder.Sql("""
                INSERT INTO [GeneratedMeshArtifacts]
                    ([GeneratedMeshId], [Kind], [BlobKey], [ContentType], [SizeBytes], [CreatedAt])
                SELECT [Id], 'Glb', [ModelBlobKey], [ModelContentType], [ModelSizeBytes], [CreatedAt]
                FROM [GeneratedMeshes]
                WHERE [ModelBlobKey] IS NOT NULL AND [ModelSizeBytes] > 0;
                """);

            migrationBuilder.Sql("""
                INSERT INTO [GeneratedMeshArtifacts]
                    ([GeneratedMeshId], [Kind], [BlobKey], [ContentType], [SizeBytes], [CreatedAt])
                SELECT [Id], 'Preview', [PreviewBlobKey],
                       ISNULL([PreviewContentType], 'image/png'), [PreviewSizeBytes], [CreatedAt]
                FROM [GeneratedMeshes]
                WHERE [PreviewBlobKey] IS NOT NULL AND [PreviewSizeBytes] > 0;
                """);

            migrationBuilder.Sql("""
                INSERT INTO [MeshRunArtifacts]
                    ([MeshRunId], [Kind], [BlobKey], [ContentType], [SizeBytes], [CreatedAt])
                SELECT [Id], 'Glb', [ModelBlobKey], 'model/gltf-binary', [ModelSizeBytes], [UpdatedAt]
                FROM [MeshRuns]
                WHERE [ModelBlobKey] IS NOT NULL AND [ModelSizeBytes] > 0;
                """);

            migrationBuilder.Sql("""
                INSERT INTO [MeshRunArtifacts]
                    ([MeshRunId], [Kind], [BlobKey], [ContentType], [SizeBytes], [CreatedAt])
                SELECT [Id], 'Preview', [PreviewBlobKey],
                       ISNULL([PreviewContentType], 'image/png'), [PreviewSizeBytes], [UpdatedAt]
                FROM [MeshRuns]
                WHERE [PreviewBlobKey] IS NOT NULL AND [PreviewSizeBytes] > 0;
                """);

            // ④ 옛 열을 지운다
            migrationBuilder.DropCheckConstraint(
                name: "CK_GeneratedMeshes_ModelSize",
                table: "GeneratedMeshes");

            migrationBuilder.DropColumn(name: "ModelBlobKey", table: "MeshRuns");
            migrationBuilder.DropColumn(name: "ModelSizeBytes", table: "MeshRuns");
            migrationBuilder.DropColumn(name: "PreviewBlobKey", table: "MeshRuns");
            migrationBuilder.DropColumn(name: "PreviewContentType", table: "MeshRuns");
            migrationBuilder.DropColumn(name: "PreviewSizeBytes", table: "MeshRuns");

            migrationBuilder.DropColumn(name: "ModelBlobKey", table: "GeneratedMeshes");
            migrationBuilder.DropColumn(name: "ModelContentType", table: "GeneratedMeshes");
            migrationBuilder.DropColumn(name: "ModelSizeBytes", table: "GeneratedMeshes");
            migrationBuilder.DropColumn(name: "PreviewBlobKey", table: "GeneratedMeshes");
            migrationBuilder.DropColumn(name: "PreviewContentType", table: "GeneratedMeshes");
            migrationBuilder.DropColumn(name: "PreviewSizeBytes", table: "GeneratedMeshes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedMeshArtifacts");

            migrationBuilder.DropTable(
                name: "MeshRunArtifacts");

            migrationBuilder.RenameColumn(
                name: "PreparedAt",
                table: "MeshRunInputs",
                newName: "UploadedAt");

            migrationBuilder.AddColumn<string>(
                name: "ModelBlobKey",
                table: "MeshRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ModelSizeBytes",
                table: "MeshRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewBlobKey",
                table: "MeshRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewContentType",
                table: "MeshRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PreviewSizeBytes",
                table: "MeshRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelBlobKey",
                table: "GeneratedMeshes",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ModelContentType",
                table: "GeneratedMeshes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "ModelSizeBytes",
                table: "GeneratedMeshes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "PreviewBlobKey",
                table: "GeneratedMeshes",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewContentType",
                table: "GeneratedMeshes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PreviewSizeBytes",
                table: "GeneratedMeshes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GeneratedMeshes_ModelSize",
                table: "GeneratedMeshes",
                sql: "[ModelSizeBytes] > 0");
        }
    }
}
