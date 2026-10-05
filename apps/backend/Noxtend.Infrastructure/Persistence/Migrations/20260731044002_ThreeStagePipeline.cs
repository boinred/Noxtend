using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThreeStagePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // **이름만 바꾸지 않는다.** 기존 열에는 사이클 #4 의 자유 문장
            // ("해질녘 항구 마을…") 이 들어 있어 SceneSpec 으로 역직렬화하면 터진다.
            // 산문을 구조화 값으로 되살릴 방법이 없으므로 버린다 — 사이클 #4 의
            // 시험 실행 기록이고, 다시 돌리면 새 형식으로 채워진다.
            migrationBuilder.DropColumn(
                name: "ConsistencyPrompt",
                table: "Jobs");

            migrationBuilder.AddColumn<string>(
                name: "SceneJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundsH",
                table: "AssetParts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundsW",
                table: "AssetParts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundsX",
                table: "AssetParts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundsY",
                table: "AssetParts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "AssetParts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepthOrder",
                table: "AssetParts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AssetParts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OccludedBy",
                table: "AssetParts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoundsH",
                table: "AssetParts");

            migrationBuilder.DropColumn(
                name: "BoundsW",
                table: "AssetParts");

            migrationBuilder.DropColumn(
                name: "BoundsX",
                table: "AssetParts");

            migrationBuilder.DropColumn(
                name: "BoundsY",
                table: "AssetParts");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AssetParts");

            migrationBuilder.DropColumn(
                name: "DepthOrder",
                table: "AssetParts");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AssetParts");

            migrationBuilder.DropColumn(
                name: "OccludedBy",
                table: "AssetParts");

            migrationBuilder.DropColumn(
                name: "SceneJson",
                table: "Jobs");

            migrationBuilder.AddColumn<string>(
                name: "ConsistencyPrompt",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
