using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // 서술 재작성 프롬프트 v4 — 스키마를 구조화 출력 strict 규칙에 맞춘다.
    //
    // v3 는 category 를 properties 에만 넣고 required 에서 빠뜨렸다. OpenAiProvider 는
    // strict = true 로 부르는데(OpenAiProvider.cs:40) 그 모드는 properties 의 모든 키가
    // required 에 있어야 한다 — 빠지면 400 이고, 그 실패는 재시도 대상이 아니라 작업
    // 전체를 죽인다. 실 API 에서 그렇게 죽었다.
    //
    // 선택값은 required 에 넣되 타입을 nullable 로 둔다.
    public partial class RewriteDescriptionsPromptStrictSchema : Migration
    {
        private static readonly Guid PreviousId = Guid.Parse("c4000000-0000-4000-8000-000000000201");

        private static readonly Guid NewId = Guid.Parse("c4000000-0000-4000-8000-000000000202");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 1, 16, 18, 3, TimeSpan.Zero);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.RewriteDescriptionsV3();
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
