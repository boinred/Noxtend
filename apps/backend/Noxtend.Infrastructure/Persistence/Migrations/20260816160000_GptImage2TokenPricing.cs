using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // README "미구현·가라 목록" 대응 — gpt-image-2가 장당 정액(0.053)만으로 과금돼
    // 참조 이미지 장수(텍스트 대비 토큰이 훨씬 많음)가 비용에 안 반영되던 문제.
    // 새 시행일(2026-08-16)로 토큰 단가 행을 추가한다 — 기존 행(2026-08-01)은 그대로
    // 두므로 그 이전 호출의 비용은 안 바뀐다. 근거는 SeedModelPrices.GptImage2TokenPricing
    // 문서 참고.
    public partial class GptImage2TokenPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var row = SeedModelPrices.GptImage2TokenPricing;
            var effectiveFrom = Literal(SeedModelPrices.GptImage2TokenPricingEffectiveFrom);

            // 조건부 삽입 — 운영자가 같은 (모델, 시행일)로 이미 등록해뒀을 수 있다
            // (ImageCatalogPrices와 같은 이유, §6.1)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ModelPrices", keyColumn: "Id", keyValue: SeedModelPrices.GptImage2TokenPricing.Id);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";

        private static string Literal(decimal? value)
            => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : "NULL";

        private static string Literal(int? value)
            => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : "NULL";
    }
}
