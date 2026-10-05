using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SceneLayoutRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 기존 명세 비우기 — 결정적 재계산이 가능한 파생 캐시라 잃는 것이 없고,
            // 남기면 빈 CameraJson 이 역직렬화에서 터진다 (첫 조회가 새 revision 을 만든다)
            migrationBuilder.Sql("DELETE FROM [SceneInstances]; DELETE FROM [SceneLayouts];");

            migrationBuilder.DropIndex(
                name: "IX_SceneLayouts_JobId",
                table: "SceneLayouts");

            migrationBuilder.AddColumn<string>(
                name: "CameraJson",
                table: "SceneLayouts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LightJson",
                table: "SceneLayouts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "SceneLayouts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentLayoutId",
                table: "SceneLayouts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "SceneLayouts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SceneLayouts",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceMeshSignature",
                table: "SceneLayouts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "SceneLayouts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SceneLayouts_ActivePerJob",
                table: "SceneLayouts",
                column: "JobId",
                unique: true,
                filter: "[State] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SceneLayouts_JobId_Revision",
                table: "SceneLayouts",
                columns: new[] { "JobId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SceneLayouts_ActivePerJob",
                table: "SceneLayouts");

            migrationBuilder.DropIndex(
                name: "IX_SceneLayouts_JobId_Revision",
                table: "SceneLayouts");

            migrationBuilder.DropColumn(
                name: "CameraJson",
                table: "SceneLayouts");

            migrationBuilder.DropColumn(
                name: "LightJson",
                table: "SceneLayouts");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "SceneLayouts");

            migrationBuilder.DropColumn(
                name: "ParentLayoutId",
                table: "SceneLayouts");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "SceneLayouts");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SceneLayouts");

            migrationBuilder.DropColumn(
                name: "SourceMeshSignature",
                table: "SceneLayouts");

            migrationBuilder.DropColumn(
                name: "State",
                table: "SceneLayouts");

            migrationBuilder.CreateIndex(
                name: "IX_SceneLayouts_JobId",
                table: "SceneLayouts",
                column: "JobId",
                unique: true);
        }
    }
}
