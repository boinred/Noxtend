using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream G — 2026-08-18 실사용 확인 중 발견한 세 번째 문제에 대응한다.
    //
    // 손끝부터 팔꿈치까지 이어진 장갑(택소노미 "장갑" 힌트의 "팔꿈치형" 변형)이 손목 아래
    // (손) 없이 팔 부분 하나로만 뽑혔다 — workstream F가 신발/발목에 적용한 것과 정확히
    // 같은 결(다리를 덮는 다른 갑주가 있을 때만 발목 경계를 다뤘고, 신발 자체가 긴 경우는
    // 빈틈이었다)의 문제다.
    //
    // 처음엔 신발/발목·장갑/손목을 각각 별도 규칙으로 다뤘는데, 사용자가 "관절에 있는
    // 부위는 관절 단위로 분리해야 한다(갑옷의 갑주 부분도 마찬가지)"는 일반 원칙을
    // 요구했다. 그래서 Extract 규칙을 발목·손목·팔꿈치·무릎·어깨를 지나는 모든 통짜
    // 오브젝트(갑옷판 포함)에 적용되는 하나의 관절-경계 규칙으로 합쳤다 — 3D 리깅에서
    // 관절 양쪽은 항상 독립된 두 파츠여야 한다는 게 근거(사용자 설명).
    //
    // 문구만 바꾼 가설이다 — 실 API 재검증 전까지는 확정 아님(workstream E/F와 같은 원칙).
    //
    // 🔴 스코프 함정(이전 캐릭터 마이그레이션과 동일): 비활성화·채번을
    // (Kind, Category='Character')로 스코프해 기본(NULL) 활성을 건드리지 않는다.
    public partial class CharacterPromptWorkstreamG : Migration
    {
        // 직전 활성 id — Down 에서 되살린다.
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000061");

        // 이번에 심는 새 버전 id
        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000071");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 18, 1, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Reseed(migrationBuilder, TaskKind.Extract, ExtractNewId, SeedPrompts.CharacterExtract());
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: ExtractNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: ExtractPrevId,
                column: "IsActive", value: true);
        }

        private static void Reseed(
            MigrationBuilder migrationBuilder,
            TaskKind kind,
            Guid id,
            (string System, string User, string Schema, string Note) prompt)
        {
            var kindLiteral = Literal(kind.ToString());
            var categoryLiteral = Literal(Category);

            migrationBuilder.Sql($"""
                UPDATE [PromptVersions] SET [IsActive] = 0
                    WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral};

                INSERT INTO [PromptVersions]
                    ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                VALUES (
                    {Literal(id)}, {kindLiteral}, {categoryLiteral},
                    ISNULL((SELECT MAX([Version]) FROM [PromptVersions]
                            WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral}), 0) + 1,
                    {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)}, {Literal(prompt.Note)},
                    1, {Literal(SeededAt)});
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
