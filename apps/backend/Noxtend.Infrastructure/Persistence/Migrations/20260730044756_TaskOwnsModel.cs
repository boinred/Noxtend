using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskOwnsModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 모델이 공급자에서 공정으로 옮겨간다.
            //
            // ProviderConfigs.Model 은 버린다 — 옮길 자리가 없다. 그 값은 "이 키로 무엇을
            // 부를까" 였고, 이제 그 결정은 작업마다 내린다. 기존 Tasks 는 Model 이 NULL 이
            // 되는데, 모두 종료된 공정이라 다시 실행되지 않는다.
            migrationBuilder.DropColumn(
                name: "Model",
                table: "ProviderConfigs");

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Tasks",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Model",
                table: "Tasks");

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "ProviderConfigs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }
    }
}
