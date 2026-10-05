using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // review-gate-staged 사이클 0 (docs/02-design/features/review-gate-staged.design.md §3.3) —
    // 검수 승인 bool 의 단계 enum 문자열 전환. 추가 → 이관 → 삭제 순서 고정.
    // 삭제는 DropColumn API — ReviewApproved 기본값 제약의 선삭제를 SqlServer 생성기에 위임.
    public partial class ReviewPhaseColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewPhase",
                table: "Jobs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Boxes");

            // 이미 승인된 작업이 검수 대기로 되돌아가지 않게 승인 값 이관
            migrationBuilder.Sql("UPDATE [Jobs] SET [ReviewPhase] = N'Approved' WHERE [ReviewApproved] = 1;");

            migrationBuilder.DropColumn(
                name: "ReviewApproved",
                table: "Jobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReviewApproved",
                table: "Jobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE [Jobs] SET [ReviewApproved] = 1 WHERE [ReviewPhase] = N'Approved';");

            migrationBuilder.DropColumn(
                name: "ReviewPhase",
                table: "Jobs");
        }
    }
}
