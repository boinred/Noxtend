using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream F — 2026-08-18 실사용(캐릭터 1건, 파츠 18개) 확인 중 발견한
    // 두 문제에 대응한다.
    //
    // 1) Extract: 발목 위까지 올라가는 신발(예: "왼쪽 무릎형 전투화")이 별도 다리 갑주 없이
    //    통짜 한 파츠로 뽑혔다. 기존 규칙(workstream E §5 결정⑤)은 "다른 갑주가 다리를 덮을
    //    때" 발목 경계를 다뤘을 뿐, 신발 자체 디자인이 긴 경우는 다루지 않았다 — 그 빈틈을
    //    메운다: 신발 디자인이 발목 위로 올라가면 별도 갑주 유무와 무관하게 항상 발목아래/
    //    발목위~무릎 두 파츠로 분리하도록 명시한다.
    // 2) Generate: Extract는 머리·머리카락을 별개 파츠로 정확히 뽑았는데도, 머리 파츠 렌더
    //    이미지에 머리카락이 그대로 남아 있었다(원본 참조 사진에 있는 머리카락을 그대로
    //    베낀 것으로 추정). 기존 "머리는 민머리로 그려라" 지시가 참조 사진의 강한 시각적
    //    편향을 못 이겼을 가능성이 높아, "참조에 머리카락이 보여도 무조건 제외"라는 명시적
    //    반례 문구를 추가한다.
    //
    // 둘 다 문구만 바꾼 가설이다 — 실 API 재검증 전까지는 확정 아님(workstream E와 같은 원칙).
    //
    // 🔴 스코프 함정(이전 캐릭터 마이그레이션과 동일): 비활성화·채번을
    // (Kind, Category='Character')로 스코프해 기본(NULL) 활성을 건드리지 않는다.
    public partial class CharacterPromptWorkstreamF : Migration
    {
        // 직전 활성 id — Down 에서 되살린다.
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000051");
        private static readonly Guid GeneratePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000033");

        // 이번에 심는 새 버전 id
        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000061");
        private static readonly Guid GenerateNewId = Guid.Parse("c4000000-0000-4000-8000-000000000062");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Reseed(migrationBuilder, TaskKind.Extract, ExtractNewId, SeedPrompts.CharacterExtract());
            Reseed(migrationBuilder, TaskKind.Generate, GenerateNewId, SeedPrompts.CharacterGenerate());
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: ExtractNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: GenerateNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: ExtractPrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: GeneratePrevId,
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
