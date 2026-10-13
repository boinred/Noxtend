using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpriteSourceDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceInputJson",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceImageId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "SpriteSourceDraftJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 원본 준비 작업은 이전 스키마로 표현 불가
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM Jobs WHERE SourceImageId IS NULL)
                    THROW 50000, 'Delete source draft jobs (SourceImageId IS NULL) before reverting AddSpriteSourceDraft', 1;
                """);

            migrationBuilder.DropColumn(
                name: "SourceInputJson",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SpriteSourceDraftJson",
                table: "Jobs");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceImageId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
