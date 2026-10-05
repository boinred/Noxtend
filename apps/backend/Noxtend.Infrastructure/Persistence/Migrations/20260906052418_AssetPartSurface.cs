using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetPartSurface : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Surface part marker (background-surface-parts #20). Existing rows keep 0
            // (None) — parts decomposed before this cycle are all standalone objects.
            migrationBuilder.AddColumn<int>(
                name: "Surface",
                table: "AssetParts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Surface", table: "AssetParts");
        }
    }
}
