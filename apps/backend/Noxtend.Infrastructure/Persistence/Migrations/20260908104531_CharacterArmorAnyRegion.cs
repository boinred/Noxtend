using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // armor-any-region (docs/specs/2026-09-08-armor-any-region.md) — 골든셋(03900D2A) DB 실측이
    // 밝힌 두 갭. 오른쪽 발목 가드가 자동 분리 없이 "오른쪽 신발 발목 아래"에 흡수됐고(①의 근거,
    // 규칙이 상의·하의 위 갑주만 명시해 부위를 열거로 놓쳤다), 하의 파츠 렌더에 원본의 허벅지
    // 스트랩이 중복으로 남았다(②-3의 근거, 벨트·머리카락만 있던 "원본에 보여도 그리지 마라"
    // 반례를 일반화해야 한다). v2 원칙: 부위·타입을 열거하지 않고 "힌트된 파츠 위에 따로 고정된
    // 것", "리그가 굽히는 관절"처럼 원리로 쓴다 — 그래서 이번 재시드는 상의/하의 열거를
    // any hinted part 로, 관절 열거를 any joint the rig will bend 로 일반화한다.
    public partial class CharacterArmorAnyRegion : Migration
    {
        // 직전 활성 id — Down 에서 되살린다.
        // Extract 는 workstream N(111), Decompose·Generate 는 모델러 피드백 #11·#13(114·115),
        // RewriteDescriptions 는 strict 스키마 수정 v4(202).
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000111");
        private static readonly Guid DecomposePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000114");
        private static readonly Guid GeneratePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000115");
        private static readonly Guid RewritePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000202");

        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000116");
        private static readonly Guid DecomposeNewId = Guid.Parse("c4000000-0000-4000-8000-000000000117");
        private static readonly Guid GenerateNewId = Guid.Parse("c4000000-0000-4000-8000-000000000118");
        private static readonly Guid RewriteNewId = Guid.Parse("c4000000-0000-4000-8000-000000000203");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 8, 10, 45, 31, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ReseedCharacter(migrationBuilder, TaskKind.Extract, ExtractNewId, SeedPrompts.CharacterExtractV13());
            ReseedCharacter(migrationBuilder, TaskKind.Decompose, DecomposeNewId, SeedPrompts.CharacterDecompose());
            ReseedCharacter(migrationBuilder, TaskKind.Generate, GenerateNewId, SeedPrompts.CharacterGenerate());

            // RewriteDescriptions 는 카테고리 무관(Category IS NULL) 기본 슬롯이다 —
            // 검수 게이트는 캐릭터·배경·소품에 공통이라 캐릭터로 스코프를 좁히지 않는다
            ReseedDefault(migrationBuilder, TaskKind.RewriteDescriptions, RewriteNewId, SeedPrompts.RewriteDescriptions());
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: ExtractNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: GenerateNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: RewriteNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: ExtractPrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: DecomposePrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: GeneratePrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: RewritePrevId,
                column: "IsActive", value: true);
        }

        private static void ReseedCharacter(
            MigrationBuilder migrationBuilder,
            TaskKind kind,
            Guid id,
            (string System, string User, string Schema, string Note) prompt)
        {
            var kindLiteral = Literal(kind.ToString());
            var categoryLiteral = Literal(Category);

            migrationBuilder.Sql($"""
                IF NOT EXISTS (
                    SELECT 1 FROM [PromptVersions]
                    WHERE [Kind] = {kindLiteral}
                      AND [Category] = {categoryLiteral}
                      AND [IsActive] = 1
                      AND [System] = {Literal(prompt.System)}
                      AND [User] = {Literal(prompt.User)}
                      AND [JsonSchema] = {Literal(prompt.Schema)}
                )
                BEGIN
                    UPDATE [PromptVersions] SET [IsActive] = 0
                        WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral};

                    INSERT INTO [PromptVersions]
                        ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                    VALUES (
                        {Literal(id)}, {kindLiteral}, {categoryLiteral},
                        ISNULL((SELECT MAX([Version]) FROM [PromptVersions]
                                WHERE [Kind] = {kindLiteral} AND [Category] = {categoryLiteral}), 0) + 1,
                        {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)},
                        {Literal(prompt.Note)}, 1, {Literal(SeededAt)});
                END
                """);
        }

        private static void ReseedDefault(
            MigrationBuilder migrationBuilder,
            TaskKind kind,
            Guid id,
            (string System, string User, string Schema, string Note) prompt)
        {
            var kindLiteral = Literal(kind.ToString());

            migrationBuilder.Sql($"""
                IF NOT EXISTS (
                    SELECT 1 FROM [PromptVersions]
                    WHERE [Kind] = {kindLiteral}
                      AND [Category] IS NULL
                      AND [IsActive] = 1
                      AND [System] = {Literal(prompt.System)}
                      AND [User] = {Literal(prompt.User)}
                      AND [JsonSchema] = {Literal(prompt.Schema)}
                )
                BEGIN
                    UPDATE [PromptVersions] SET [IsActive] = 0
                        WHERE [Kind] = {kindLiteral} AND [Category] IS NULL;

                    INSERT INTO [PromptVersions]
                        ([Id], [Kind], [Category], [Version], [System], [User], [JsonSchema], [Note], [IsActive], [CreatedAt])
                    VALUES (
                        {Literal(id)}, {kindLiteral}, NULL,
                        ISNULL((SELECT MAX([Version]) FROM [PromptVersions]
                                WHERE [Kind] = {kindLiteral} AND [Category] IS NULL), 0) + 1,
                        {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)},
                        {Literal(prompt.Note)}, 1, {Literal(SeededAt)});
                END
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
