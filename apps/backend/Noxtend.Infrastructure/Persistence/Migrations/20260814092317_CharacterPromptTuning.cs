using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning — 실 LLM 검증에서 나온 캐릭터 프롬프트 개선을 시드로 굳힌다.
    // Extract(머리/머리카락 분해 강화), Generate(A포즈·얼굴 방향·머리 민머리),
    // Decompose(배치 좌표 좌상단 명확화 — workstream C)를 다음 버전으로 재시드한다.
    //
    // 🔴 스코프 함정(CharacterPromptSeed 와 동일): 비활성화·채번을 (Kind, Category='Character')
    // 로 스코프해 기본(NULL) 활성을 건드리지 않는다. VALUES + 스칼라 서브쿼리로 채번한다.
    public partial class CharacterPromptTuning : Migration
    {
        // 직전 활성 id — Down 에서 되살린다. Extract 는 v1(CharacterPromptSeed),
        // Decompose·Generate 는 v2(CharacterPromptGenderSilhouette).
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000001");
        private static readonly Guid DecomposePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000012");
        private static readonly Guid GeneratePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000013");

        // 이번에 심는 새 버전 id
        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000021");
        private static readonly Guid DecomposeNewId = Guid.Parse("c4000000-0000-4000-8000-000000000022");
        private static readonly Guid GenerateNewId = Guid.Parse("c4000000-0000-4000-8000-000000000023");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ReseedCharacterPrompt(migrationBuilder, TaskKind.Extract, ExtractNewId, SeedPrompts.CharacterExtract());
            ReseedCharacterPrompt(migrationBuilder, TaskKind.Decompose, DecomposeNewId, SeedPrompts.CharacterDecompose());
            ReseedCharacterPrompt(migrationBuilder, TaskKind.Generate, GenerateNewId, SeedPrompts.CharacterGenerate());
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: ExtractNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: GenerateNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: ExtractPrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: DecomposePrevId,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: GeneratePrevId,
                column: "IsActive", value: true);
        }

        // 캐릭터 단계 하나를 다음 버전으로 재시드 — 카테고리 스코프 비활성화 후 스코프 채번 INSERT
        private static void ReseedCharacterPrompt(
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
