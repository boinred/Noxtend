using System;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 유사도 평가 프롬프트 시드 (background-similarity-tuning §15.1) —
    /// SimilarityEvaluate/Background 슬롯에만 쓴다. 버전 번호는 고정값이 아니라
    /// 그 슬롯의 ISNULL(MAX(Version),0)+1 — 운영자가 이미 발급했을 수 있다 (NFR-05).
    /// 다른 kind/category 는 건드리지 않는다.
    /// </summary>
    public partial class SimilarityEvaluatePrompt : Migration
    {
        private static readonly Guid NewId = Guid.Parse("c1000000-0000-4000-8000-000000000001");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Background.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.SimilarityEvaluateBackground();
            var kind = LlmOperationKind.SimilarityEvaluate.ToString();

            // 비활성화 먼저 — filtered unique 가 활성 한 행만 허용한다 (BackgroundGeneratePromptV2 와 같은 규칙)
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(NewId)}, {Literal(kind)}, {Literal(Category)},
                    ISNULL(MAX([Version]), 0) + 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)}, 1,
                    {Literal(SeededAt)}
                FROM [PromptVersions]
                    WHERE [Kind] = {Literal(kind)} AND [Category] = {Literal(Category)};
                """);
        }

        /// <inheritdoc />
        // 이 슬롯의 첫 행이라 되살릴 직전 행이 없다 — 지우면 슬롯이 다시 빈다
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: NewId);

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
