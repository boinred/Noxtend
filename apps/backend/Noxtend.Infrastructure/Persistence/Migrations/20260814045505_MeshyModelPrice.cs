using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MeshyModelPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var effectiveFrom = Literal(SeedModelPrices.EffectiveFrom);

            foreach (var row in SeedModelPrices.MeshyPrices)
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
                        {Literal(row.Id)}, {Literal(row.Model)},
                        {Literal(row.Input)}, {Literal(row.Output)},
                        NULL, NULL, NULL,
                        {effectiveFrom}, {Literal(row.Note)}, {Literal(row.PerImage)});
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var row in SeedModelPrices.MeshyPrices)
            {
                migrationBuilder.DeleteData(table: "ModelPrices", keyColumn: "Id", keyValue: row.Id);
            }
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";

        private static string Literal(decimal value)
            => value.ToString(CultureInfo.InvariantCulture);

        private static string Literal(decimal? value)
            => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : "NULL";
    }
}
