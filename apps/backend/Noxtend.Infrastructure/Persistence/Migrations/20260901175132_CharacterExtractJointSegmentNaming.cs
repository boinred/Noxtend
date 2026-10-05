using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream M — 관절이 둘 이상인 파츠에서 중간 구간에 이름이 두 개
    // 생겼다.
    //
    // 실 job 관측:
    //   왼쪽 장갑 팔꿈치 위 / 팔꿈치 아래 / 손목 위 / 손목 아래
    //     → "팔꿈치 아래" 와 "손목 위" 가 같은 팔뚝이다. 파츠는 4개인데 실제 조각은 3개다
    //   왼쪽 신발 무릎 위 / 무릎 아래 / 발목 아래
    //     → "무릎 아래" 가 정강이와 발 둘 다로 읽힌다
    //   좌우 조각 수가 실행마다 달랐다 (왼발 3개 · 오른발 2개)
    //
    // 중간 구간을 "무릎~발목" 처럼 양끝 관절로 부르게 하고, 좌우가 같은 수로 나뉜다는
    // 것을 명시했다. Extract 만 바뀐다.
    public partial class CharacterExtractJointSegmentNaming : Migration
    {
        // 직전 활성 id (workstream K 가 심은 것) — Down 에서 되살린다
        private static readonly Guid PreviousId = Guid.Parse("c4000000-0000-4000-8000-000000000104");

        private static readonly Guid NewId = Guid.Parse("c4000000-0000-4000-8000-000000000110");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 1, 17, 51, 32, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (system, user, schema, note) = SeedPrompts.CharacterExtract();
            var kindLiteral = Literal(TaskKind.Extract.ToString());
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
