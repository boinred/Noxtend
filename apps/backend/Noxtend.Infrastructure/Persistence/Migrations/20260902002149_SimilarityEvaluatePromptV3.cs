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
    /// 유사도 평가 프롬프트 v3 — 자유 문장 한국어 (사용자 피드백).
    ///
    /// 화면의 근거·권고·보정 사유·재생성 노트가 영어로 나와 읽는 부담이 컸다.
    /// enum(kind·type)·숫자 계약은 그대로다 — 파서가 그 값으로 분기한다.
    /// 버전 번호는 이 슬롯의 ISNULL(MAX(Version),0)+1 (NFR-05) — 고정값 금지.
    /// </summary>
    public partial class SimilarityEvaluatePromptV3 : Migration
    {
        private static readonly Guid NewId = Guid.Parse("c3000000-0000-4000-8000-000000000001");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 2, 0, 40, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Background.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.SimilarityEvaluateBackgroundV3();
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
        // 직전 활성(v2 시드)을 되살린다 — 슬롯이 비면 시작 자격이 무너진다 (§11.1)
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: NewId);
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 1
                    WHERE [Id] = 'c2000000-0000-4000-8000-000000000001';
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
