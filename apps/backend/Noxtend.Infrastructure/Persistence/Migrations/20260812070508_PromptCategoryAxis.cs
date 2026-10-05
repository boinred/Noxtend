using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromptCategoryAxis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PromptVersions_ActiveByKind",
                table: "PromptVersions");

            migrationBuilder.DropIndex(
                name: "IX_PromptVersions_Kind_Version",
                table: "PromptVersions");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "PromptVersions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_ActiveByKindCategory",
                table: "PromptVersions",
                columns: new[] { "Kind", "Category" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_Kind_Version",
                table: "PromptVersions",
                columns: new[] { "Kind", "Category", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        // 롤백 선행 조건(운영 런북): 이 Down 은 카테고리 열을 없애고 카테고리를 모르는 옛
        // 유니크 인덱스를 되살린다. 카테고리 전용 활성 행이 있거나 카테고리 간 버전 번호가
        // 겹친 상태에서 되돌리면 그 옛 인덱스가 중복을 거부해 롤백이 실패한다. 데이터가 없을
        // 때는 정상. 실데이터가 쌓인 뒤 되돌리려면 카테고리 행부터 정리하고 이 마이그레이션을 내린다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PromptVersions_ActiveByKindCategory",
                table: "PromptVersions");

            migrationBuilder.DropIndex(
                name: "IX_PromptVersions_Kind_Version",
                table: "PromptVersions");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "PromptVersions");

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
        }
    }
}
