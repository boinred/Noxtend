using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SceneInstancePerAxisScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 기존 Scale 은 정규화 기준축인 **높이**였다 — ScaleY 로 이름만 바꾼다
            migrationBuilder.RenameColumn(
                name: "Scale",
                table: "SceneInstances",
                newName: "ScaleY");

            migrationBuilder.AddColumn<double>(
                name: "ScaleX",
                table: "SceneInstances",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ScaleZ",
                table: "SceneInstances",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            // **이력 행을 0 으로 만들면 안 된다.** 사이클 #20 이전 인스턴스는 전부 균일
            // 배율이므로 세 축이 같은 값이어야 한다. 기본값 0 을 그대로 두면 과거
            // revision 을 복원했을 때 크기 없는 장면이 나온다
            migrationBuilder.Sql(
                "UPDATE [SceneInstances] SET [ScaleX] = [ScaleY], [ScaleZ] = [ScaleY]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScaleX",
                table: "SceneInstances");

            migrationBuilder.DropColumn(
                name: "ScaleZ",
                table: "SceneInstances");

            migrationBuilder.RenameColumn(
                name: "ScaleY",
                table: "SceneInstances",
                newName: "Scale");
        }
    }
}
