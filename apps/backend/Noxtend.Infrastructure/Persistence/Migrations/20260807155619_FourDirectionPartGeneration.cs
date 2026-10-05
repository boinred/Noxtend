using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FourDirectionPartGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ViewDirection",
                table: "Tasks",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ViewDirection",
                table: "GeneratedImages",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Front");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ViewDirection",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ViewDirection",
                table: "GeneratedImages");
        }
    }
}
