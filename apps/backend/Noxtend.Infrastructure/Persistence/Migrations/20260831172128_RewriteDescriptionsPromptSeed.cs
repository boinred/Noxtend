using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // occludedby-recompute §구현 범위 2 — 새 단계에는 활성 프롬프트가 있어야 한다.
    // 없으면 RunTaskHandler 가 "활성 프롬프트가 없습니다" 로 실패하고, 그 실패가 비-Generate
    // 라 작업 전체를 죽인다.
    //
    // **Category NULL(기본)로 한 벌만 심는다.** 검수 게이트는 per-job 플래그라 어느
    // 카테고리에서도 켜질 수 있고, 이 단계가 하는 일(가린 부분 제외)은 카테고리에 무관하다.
    //
    // 🔴 이 파일은 반드시 `dotnet ef migrations add` 로 만든다. 손으로 쓰면 Designer 와
    // [Migration] 속성이 없어 EF 가 통째로 건너뛰는데, partial 짝이 없어도 컴파일은
    // 통과해서 빌드·테스트가 전부 초록불이다 (독립 리뷰 지적, 2026-09-01).
    public partial class RewriteDescriptionsPromptSeed : Migration
    {
        private static readonly Guid SeedId = Guid.Parse("c4000000-0000-4000-8000-000000000200");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.RewriteDescriptions();
            var kindLiteral = Literal(TaskKind.RewriteDescriptions.ToString());

            // 비활성화·채번을 (Kind, Category IS NULL) 로 스코프한다 — 다른 단계의 기본 활성을
            // 건드리지 않는다. UPDATE 가 먼저라 활성 필터 유니크에 걸리지 않는다
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {kindLiteral} AND [Category] IS NULL;

                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                VALUES (
                    {Literal(SeedId)}, {kindLiteral}, NULL,
                    ISNULL((SELECT MAX([Version]) FROM [PromptVersions]
                            WHERE [Kind] = {kindLiteral} AND [Category] IS NULL), 0) + 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)},
                    1, {Literal(SeededAt)});
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DeleteData(
                table: "PromptVersions", keyColumn: "Id", keyValue: SeedId);

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
