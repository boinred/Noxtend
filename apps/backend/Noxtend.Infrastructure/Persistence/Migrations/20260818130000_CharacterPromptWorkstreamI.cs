using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream I — 2026-08-18 실사용 확인 중 발견한 다섯 번째 문제에
    // 대응한다. 여러 사용자가 같이 쓰는 툴이라 혼동 여지를 없애야 한다는 요구로, 이번엔
    // 프롬프트만이 아니라 프론트 택소노미(UI)도 같이 바꾼다.
    //
    // 실제 생성 이미지를 분석해 벨트-하의처럼 "몸의 같은 부위를 덮는 두 타입이 서로
    // 독립적인 top-level 힌트로 존재하면, 둘 다 고를 때 렌더가 겹친다"는 패턴을
    // 택소노미 전체에서 훑었다. 그 결과 "벨트"와 "갑옷"을 별도 힌트 옵션에서 제외했다
    // (apps/frontend/src/features/screens/character/characterPartTaxonomy.ts). 모자·
    // 가방·목걸이·겉옷은 사용자 판단으로 겹칠 일이 없다고 보고 그대로 뒀다.
    //
    // 힌트를 없앤 뒤 프롬프트도 다시 점검했다 — "벨트는 스트랩·버클·메달로 분리된다"는
    // 예시 문장이 힌트 여부와 무관하게 분리를 부추기고 있어서 그대로 뒀다면 힌트를
    // 없애도 여전히 벨트가 별도로 뽑힐 위험이 있었다. 벨트와 갑옷을 다르게 처리하기로
    // 사용자와 합의했다:
    // - 벨트: 그 타입 자체를 다시는 힌트로 못 주니, 항상 걸친 의상(상의/하의)의 표면
    //   장식으로 흡수하고 절대 별도 파츠로 안 뽑는다.
    // - 갑옷: 3D 리깅 가치가 있는 별도 오브젝트라 판단해, "갑옷" 자체 힌트 없이도
    //   상의/하의 힌트 범위 안에서 자동으로 서브파츠로 나뉘게 남겼다(가방 힌트가
    //   몸체/스트랩/버클로 자동 분리되는 것과 같은 방식). 이러면 중복 렌더가 재발할
    //   것 같지만, 갑주가 상의/하의의 occludedBy로 잡히고 workstream H(occludedBy
    //   배제 서술)가 상의/하의 자신의 description에서 그 갑주를 빼도록 이미 처리한다.
    //
    // 문구만 바꾼 가설이다 — 실 API 재검증 전까지는 확정 아님(workstream E/F/G/H와
    // 같은 원칙).
    //
    // 🔴 스코프 함정(이전 캐릭터 마이그레이션과 동일): 비활성화·채번을
    // (Kind, Category='Character')로 스코프해 기본(NULL) 활성을 건드리지 않는다.
    public partial class CharacterPromptWorkstreamI : Migration
    {
        // 직전 활성 id — Down 에서 되살린다.
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000071");

        // 이번에 심는 새 버전 id
        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000091");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 18, 3, 0, 0, TimeSpan.Zero);

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
