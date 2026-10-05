using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DecomposePlacementsPrompt : Migration
    {
        private static readonly Guid V2Id =
            Guid.Parse("a1000000-0000-4000-8000-000000000005");

        private static readonly Guid V3Id =
            Guid.Parse("a1000000-0000-4000-8000-000000000007");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.DecomposeV3();
            var kind = TaskKind.Decompose.ToString();

            // **버전 번호를 고정값으로 박지 않는다.** 프롬프트 화면이 같은 표에 행을 발급하므로
            // 운영자가 이미 그 번호를 써 버린 환경이 있다 — `structured-palette` 에서 실제로
            // `IX_PromptVersions_Kind_Version` 에 걸려 기동이 죽었다 (D-09 · NFR-05).
            //
            // 종류 전체를 끄는 것도 같은 이유다. 운영자가 켜 둔 행이 v2 가 아닐 수 있다
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0 WHERE [Kind] = {Literal(kind)};

                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(V3Id)}, {Literal(kind)}, ISNULL(MAX([Version]), 0) + 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)}, 1,
                    {Literal(SeededAt)}
                FROM [PromptVersions] WHERE [Kind] = {Literal(kind)};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 프롬프트만 되돌리는 것은 롤백이 아니다 — `DecomposeStage` 는 v3 모양만 읽으므로
            // 릴리스 전체를 되돌려야 한다 (§6.3)
            migrationBuilder.DeleteData(
                table: "PromptVersions", keyColumn: "Id", keyValue: V3Id);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: V2Id,
                column: "IsActive", value: true);
        }

        // 마이그레이션은 파라미터를 붙일 자리가 없어 값을 문장에 박는다. 프롬프트 원문에
        // 작은따옴표가 있으므로 이스케이프가 선택이 아니다
        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
