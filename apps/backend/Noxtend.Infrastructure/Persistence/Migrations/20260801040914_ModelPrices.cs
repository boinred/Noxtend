using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 단가를 코드에서 DB 로 옮긴다.
    ///
    /// 그전에는 `static Dictionary` 라 단가 하나를 고치려면 재배포해야 했다. 공급자가
    /// 단가를 바꿀 때마다 배포가 필요한 구조는 결국 낡은 표를 방치하게 만든다.
    ///
    /// **모델당 시행일별로 한 행**이라 인상과 오타 수정이 구분된다 — 행이 하나뿐이면
    /// 인상분이 과거 지출까지 소급되어 지난달 비용이 조용히 부풀어 오른다.
    /// </summary>
    public partial class ModelPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InputPerMillion = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    OutputPerMillion = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    LongContextFrom = table.Column<int>(type: "int", nullable: true),
                    LongInputPerMillion = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    LongOutputPerMillion = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelPrices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelPrices_ModelEffectiveFrom",
                table: "ModelPrices",
                columns: new[] { "Model", "EffectiveFrom" },
                unique: true);

            SeedPrices(migrationBuilder);
        }

        /// <summary>
        /// 초기 단가를 심는다.
        ///
        /// 프롬프트와 같은 이유로 **고정 id** 를 쓴다 — 환경마다 다르면 "어느 행이
        /// 문제인가" 를 대조할 수 없다.
        /// </summary>
        private static void SeedPrices(MigrationBuilder migrationBuilder)
        {
            foreach (var row in SeedModelPrices.All)
            {
                migrationBuilder.InsertData(
                    table: "ModelPrices",
                    columns:
                    [
                        "Id", "Model", "InputPerMillion", "OutputPerMillion",
                        "LongContextFrom", "LongInputPerMillion", "LongOutputPerMillion",
                        "EffectiveFrom", "Note",
                    ],
                    values:
                    [
                        row.Id, row.Model, row.Input, row.Output,
                        row.LongFrom, row.LongInput, row.LongOutput,
                        SeedModelPrices.EffectiveFrom, row.Note,
                    ]);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 표를 지우므로 심은 행도 함께 사라진다 — 별도 삭제가 필요 없다
            migrationBuilder.DropTable(
                name: "ModelPrices");
        }
    }
}
