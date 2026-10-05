using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SceneCompositionSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompositionJson",
                table: "SceneLayouts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompositionJson",
                table: "SceneLayouts");
        }
    }
}
