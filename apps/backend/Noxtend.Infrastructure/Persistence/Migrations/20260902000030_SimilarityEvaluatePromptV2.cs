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
    /// 유사도 평가 프롬프트 v2 — strict 모드 400 수정 (실측).
    ///
    /// v1 의 adjustments.items 는 속성 15개 중 3개만 required 라 OpenAI strict 가
    /// 요청 자체를 400 으로 거절했다 (strict = 모든 객체에 additionalProperties:false
    /// + 전 속성 required). v2 는 명령별 anyOf 분기로 그 규칙을 지킨다.
    /// 버전 번호는 이 슬롯의 ISNULL(MAX(Version),0)+1 (NFR-05) — 고정값 금지.
    /// </summary>
    public partial class SimilarityEvaluatePromptV2 : Migration
    {
        private static readonly Guid NewId = Guid.Parse("c2000000-0000-4000-8000-000000000001");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 2, 0, 30, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Background.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.SimilarityEvaluateBackgroundV2();
            var kind = LlmOperationKind.SimilarityEvaluate.ToString();

            // 비활성화 먼저 — filtered unique 가 활성 한 행만 허용한다
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
        // 직전 활성(v1 시드)을 되살린다 — 슬롯이 비면 시작 자격이 무너진다 (§11.1)
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: NewId);
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 1
                    WHERE [Id] = 'c1000000-0000-4000-8000-000000000001';
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
