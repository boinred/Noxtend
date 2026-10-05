using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StructuredPalettePrompt : Migration
    {
        private static readonly Guid V1Id =
            Guid.Parse("a1000000-0000-4000-8000-000000000001");

        private static readonly Guid V2Id =
            Guid.Parse("a1000000-0000-4000-8000-000000000006");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.AnalyzeV2();
            var kind = TaskKind.Analyze.ToString();

            // **버전 번호를 고정값으로 박지 않는다.** 프롬프트 화면이 같은 표에 행을 발급하므로
            // 운영자가 이미 2번을 써 버린 환경이 있다 — 실제로 개발 DB 에 그런 행이 있었고
            // `IX_PromptVersions_Kind_Version` 에 걸려 기동이 죽었다. 번호는 이 종류의
            // 마지막 번호 다음이며, 그것이 `PromptVersion` 이 말하는 "Kind 안에서 1부터" 다.
            //
            // 활성 전환을 먼저 하는 이유는 filtered unique index 가 종류당 활성 한 행만
            // 허용하기 때문이다. `Id` 로 한 행만 끄지 않고 종류 전체를 끄는 것도 같은 이유다 —
            // 운영자가 켜 둔 행이 v1 이 아닐 수 있다.
            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0 WHERE [Kind] = {Literal(kind)};

                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                SELECT
                    {Literal(V2Id)}, {Literal(kind)}, ISNULL(MAX([Version]), 0) + 1,
                    {Literal(system)}, {Literal(user)}, {Literal(schema)}, {Literal(note)}, 1,
                    {Literal(SeededAt)}
                FROM [PromptVersions] WHERE [Kind] = {Literal(kind)};
                """);

            // SceneJson 열은 건드리지 않는다. 구형 문자열 팔레트는 읽을 때 승격한다
            // (SceneJsonSerializer) — 자연어 색상어 해석은 SQL 로 표현할 수 없고,
            // 일괄 UPDATE 는 롤백이 위험하다
        }

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

        // 마이그레이션은 파라미터를 붙일 자리가 없어 값을 문장에 박는다. 프롬프트 원문에
        // 작은따옴표가 있다("the viewer's height") — 이스케이프가 선택이 아니다
        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 되돌리면 v1 이 다시 선다. **다만 프롬프트만 되돌리는 것은 롤백이 아니다**
            // (Design §15.3) — `AnalyzeStage` 는 v2 모양만 파싱하므로 릴리스 전체를 되돌려야 한다
            migrationBuilder.DeleteData(
                table: "PromptVersions",
                keyColumn: "Id",
                keyValue: V2Id);

            migrationBuilder.UpdateData(
                table: "PromptVersions",
                keyColumn: "Id",
                keyValue: V1Id,
                column: "IsActive",
                value: true);
        }
    }
}
