using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // review-gate-staged 사이클 1 (docs/02-design/features/review-gate-staged.design.md §3.6) —
    // 파츠 서술 출처 컬럼. 기존 파츠는 사람 편집 이전이라 전부 Model.
    public partial class DescriptionSourceColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionSource",
                table: "AssetParts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Model");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 옛 코드는 Descriptions 를 못 읽음. Approved 로 옮기면 스위퍼가 팬아웃 없이
            // 빈 출력을 전원 성공으로 확정(0장 성공) — 상자 검수 대기로 되돌려 재승인 유도
            migrationBuilder.Sql("UPDATE [Jobs] SET [ReviewPhase] = N'Boxes' WHERE [ReviewPhase] = N'Descriptions';");

            migrationBuilder.DropColumn(
                name: "DescriptionSource",
                table: "AssetParts");
        }
    }
}
