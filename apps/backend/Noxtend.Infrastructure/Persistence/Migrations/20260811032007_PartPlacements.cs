using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartPlacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartPlacements",
                columns: table => new
                {
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    AssetPartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    W = table.Column<double>(type: "float", nullable: false),
                    H = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartPlacements", x => new { x.AssetPartId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_PartPlacements_AssetParts_AssetPartId",
                        column: x => x.AssetPartId,
                        principalTable: "AssetParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // **열을 지우기 전에 옮긴다.** 순서가 반대면 기존 좌표가 사라진다 (§6.1).
            // 배치가 하나뿐이므로 번호는 0 이다 — 도메인이 0부터 매긴다
            migrationBuilder.Sql("""
                INSERT INTO [PartPlacements] ([AssetPartId], [Ordinal], [X], [Y], [W], [H])
                SELECT [Id], 0, [BoundsX], [BoundsY], [BoundsW], [BoundsH]
                FROM [AssetParts]
                WHERE [BoundsX] IS NOT NULL;
                """);

            // 분해 전 파츠는 좌표가 없었고, 이제 배치 행이 없다 — 빈 목록으로 읽힌다
            migrationBuilder.DropColumn(name: "BoundsX", table: "AssetParts");
            migrationBuilder.DropColumn(name: "BoundsY", table: "AssetParts");
            migrationBuilder.DropColumn(name: "BoundsW", table: "AssetParts");
            migrationBuilder.DropColumn(name: "BoundsH", table: "AssetParts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // **배치가 여럿이던 파츠는 첫 개만 남는다.** 열이 넷뿐이라 구조적으로 담을 수
            // 없다 — 되돌릴 수 없는 손실이므로 릴리스 전체를 되돌리는 것이 전제다 (§6.3)

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

            migrationBuilder.Sql("""
                UPDATE a
                SET a.[BoundsX] = p.[X], a.[BoundsY] = p.[Y],
                    a.[BoundsW] = p.[W], a.[BoundsH] = p.[H]
                FROM [AssetParts] a
                JOIN [PartPlacements] p ON p.[AssetPartId] = a.[Id] AND p.[Ordinal] = 0;
                """);

            migrationBuilder.DropTable(name: "PartPlacements");
        }
    }
}
