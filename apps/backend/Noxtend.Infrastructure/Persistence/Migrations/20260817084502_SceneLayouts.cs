using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SceneLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SceneLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceMeshCount = table.Column<int>(type: "int", nullable: false),
                    ComposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneLayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SceneInstances",
                columns: table => new
                {
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    SceneLayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    Z = table.Column<double>(type: "float", nullable: false),
                    RotationY = table.Column<double>(type: "float", nullable: false),
                    Scale = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneInstances", x => new { x.SceneLayoutId, x.PartId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_SceneInstances_SceneLayouts_SceneLayoutId",
                        column: x => x.SceneLayoutId,
                        principalTable: "SceneLayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SceneLayouts_JobId",
                table: "SceneLayouts",
                column: "JobId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SceneInstances");

            migrationBuilder.DropTable(
                name: "SceneLayouts");
        }
    }
}
