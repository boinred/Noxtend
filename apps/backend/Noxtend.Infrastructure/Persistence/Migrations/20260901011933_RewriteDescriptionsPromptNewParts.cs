using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // 서술 재작성 프롬프트 v2 — 사람이 새로 그린 파츠도 대상이 된다.
    //
    // 검수자가 설명을 비우면 승인 시 이 공정이 원본 이미지의 해당 영역을 보고 서술과
    // 카테고리를 직접 쓴다. v1 문구는 "가려진 파츠의 서술을 다시 써라" 만 알아서, 서술이
    // 없는 파츠를 받으면 무엇을 하라는 것인지 모른다.
    //
    // **문자열만 고치면 이미 적용된 DB 는 옛 문구를 그대로 쓴다** — 시드는 INSERT 한 번이고
    // 마이그레이션은 재실행되지 않는다. 같은 함정을 이미 한 번 겪었다.
    public partial class RewriteDescriptionsPromptNewParts : Migration
    {
        private static readonly Guid PreviousId = Guid.Parse("c4000000-0000-4000-8000-000000000200");

        private static readonly Guid NewId = Guid.Parse("c4000000-0000-4000-8000-000000000201");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 1, 1, 19, 33, TimeSpan.Zero);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.RewriteDescriptions();
            var kindLiteral = Literal(TaskKind.RewriteDescriptions.ToString());

            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {kindLiteral} AND [Category] IS NULL;

                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                VALUES (
                    {Literal(NewId)}, {kindLiteral}, NULL,
                    ISNULL((SELECT MAX([Version]) FROM [PromptVersions]
                            WHERE [Kind] = {kindLiteral} AND [Category] IS NULL), 0) + 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)},
                    1, {Literal(SeededAt)});
                """);
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제가 먼저라 활성 필터 유니크에 안 걸린다
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: NewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: PreviousId,
                column: "IsActive", value: true);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
