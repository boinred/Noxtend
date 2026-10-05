using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // workstream O 철회 — "인물의 topmost/bottommost 픽셀을 찾아 기준으로 삼아라" 가
    // 역효과였다.
    //
    // 실 job 관측(O 적용 후):
    //   베이스바디 y 0.025~0.995 — 재는 대신 프레임을 그냥 채웠다. 그 상자가 기준이라
    //     나머지가 전부 어긋났다 (O 이전엔 0.045~0.935).
    //   "오른쪽 신발 무릎 위" x 0.282~0.390 vs "오른쪽 신발 무릎~발목" x 0.545~0.670 —
    //     같은 다리 조각이 화면 반대편에 놓였다. O 이전 장갑 조각들은 좌우가 일관됐다.
    //
    // O 이전에도 허리 아래가 밀리는 문제는 있었다. 그것 하나만 남기고, 내가 새로 만든 두
    // 문제(전신 상자 붕괴·좌우 섞임)를 없앤다. 원인 재조사는 별도로 한다.
    public partial class RevertCharacterDecomposeFigureAnchoredBounds : Migration
    {
        // O 가 심은 것 — Down 에서 되살린다
        private static readonly Guid PreviousId = Guid.Parse("c4000000-0000-4000-8000-000000000112");

        private static readonly Guid NewId = Guid.Parse("c4000000-0000-4000-8000-000000000113");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 2, 14, 16, 11, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.CharacterDecompose();
            var kindLiteral = Literal(TaskKind.Decompose.ToString());
            var categoryLiteral = Literal(Category);

            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral};

                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                VALUES (
                    {Literal(NewId)}, {kindLiteral}, {categoryLiteral},
                    ISNULL((SELECT MAX([Version]) FROM [PromptVersions]
                            WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral}), 0) + 1,
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
