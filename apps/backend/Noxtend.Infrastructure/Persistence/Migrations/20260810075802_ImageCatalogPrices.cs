using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImageCatalogPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedMissingImagePrices(migrationBuilder);
        }

        /// <summary>
        /// 카탈로그에는 있는데 단가표에는 없던 이미지 모델을 심는다 (사이클 #9).
        ///
        /// 빠져 있는 동안 그 모델을 고른 작업은 생성 공정이 통째로 "단가 미등록" 이 됐다.
        /// 비용은 조회 시점에 단가표로 다시 계산되므로, 이 행들이 들어가면 이미 쌓인
        /// 호출의 비용도 함께 채워진다.
        ///
        /// **`InsertData` 가 아니라 조건부 삽입인 이유.** 단가가 빈 것을 먼저 발견한 운영자는
        /// 관리자 화면에서 그 행을 직접 등록한다 — 그러라고 만든 화면이다. 그 환경에는 같은
        /// (모델, 시행일) 행이 이미 있고, `IX_ModelPrices_ModelEffectiveFrom` 이 유니크라
        /// 그냥 넣으면 **배포가 통째로 실패한다.** 덮어쓰는 것도 답이 아니다. 운영자가 확인한
        /// 값이 씨앗과 다를 수 있고, 그 차이가 조용히 사라지면 지난 비용이 함께 바뀐다.
        /// </summary>
        private static void SeedMissingImagePrices(MigrationBuilder migrationBuilder)
        {
            var effectiveFrom = Literal(SeedModelPrices.EffectiveFrom);

            foreach (var row in SeedModelPrices.MissingImagePrices)
            {
                migrationBuilder.Sql($"""
                    IF NOT EXISTS (
                        SELECT 1 FROM [ModelPrices]
                        WHERE [Model] = {Literal(row.Model)} AND [EffectiveFrom] = {effectiveFrom})
                    INSERT INTO [ModelPrices] (
                        [Id], [Model], [InputPerMillion], [OutputPerMillion],
                        [LongContextFrom], [LongInputPerMillion], [LongOutputPerMillion],
                        [EffectiveFrom], [Note], [PerImage])
                    VALUES (
                        {Literal(row.Id)}, {Literal(row.Model)}, {Literal(row.Input)}, {Literal(row.Output)},
                        {Literal(row.LongFrom)}, {Literal(row.LongInput)}, {Literal(row.LongOutput)},
                        {effectiveFrom}, {Literal(row.Note)}, {Literal(row.PerImage)});
                    """);
            }
        }

        // 마이그레이션은 파라미터를 붙일 자리가 없어 값을 문장에 박는다. 씨앗은 코드 안의
        // 상수라 외부 입력이 아니지만, 작은따옴표는 그래도 이스케이프한다
        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";

        private static string Literal(decimal? value)
            => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : "NULL";

        private static string Literal(int? value)
            => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : "NULL";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var row in SeedModelPrices.MissingImagePrices)
            {
                migrationBuilder.DeleteData(table: "ModelPrices", keyColumn: "Id", keyValue: row.Id);
            }
        }
    }
}
