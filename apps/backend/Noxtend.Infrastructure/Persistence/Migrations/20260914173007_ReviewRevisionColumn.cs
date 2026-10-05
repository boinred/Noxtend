using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // review-gate-staged 사이클 1 독립 리뷰 #5 — 파츠만 바뀌는 검수 편집도 Jobs 행 갱신(RowVersion 충돌 검사)
    public partial class ReviewRevisionColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReviewRevision",
                table: "Jobs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewRevision",
                table: "Jobs");
        }
    }
}
