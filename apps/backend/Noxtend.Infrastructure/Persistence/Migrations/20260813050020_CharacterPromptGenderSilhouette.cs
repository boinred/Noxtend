using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-studio 재정의 — 성별을 몸통뿐 아니라 몸에 걸치는 파츠(상의·하의 등) 실루엣에
    // 반영하도록 캐릭터 Decompose·Generate 프롬프트를 v2 로 재시드한다. Extract 는 변경 없음.
    //
    // 🔴 스코프 함정(CharacterPromptSeed 와 동일): 비활성화·채번을 (Kind, Category='Character')
    // 로 스코프해 기본(NULL) 활성을 건드리지 않는다. VALUES + 스칼라 서브쿼리로 채번한다.
    public partial class CharacterPromptGenderSilhouette : Migration
    {
        // v1 은 CharacterPromptSeed 가 심은 고정 id — Down 에서 되살린다
        private static readonly Guid DecomposeV1Id = Guid.Parse("c4000000-0000-4000-8000-000000000002");
        private static readonly Guid GenerateV1Id = Guid.Parse("c4000000-0000-4000-8000-000000000003");

        private static readonly Guid DecomposeV2Id = Guid.Parse("c4000000-0000-4000-8000-000000000012");
        private static readonly Guid GenerateV2Id = Guid.Parse("c4000000-0000-4000-8000-000000000013");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ReseedCharacterPrompt(migrationBuilder, TaskKind.Decompose, DecomposeV2Id, SeedPrompts.CharacterDecompose());
            ReseedCharacterPrompt(migrationBuilder, TaskKind.Generate, GenerateV2Id, SeedPrompts.CharacterGenerate());
        }

        /// <inheritdoc />
        // v2 를 지우고 v1 을 다시 활성으로. 삭제(활성 v2 제거)가 먼저라 필터 유니크(Kind,Category)
        // 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeV2Id);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: GenerateV2Id);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeV1Id,
                column: "IsActive", value: true);
            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: GenerateV1Id,
                column: "IsActive", value: true);
        }

        // 캐릭터 단계 하나를 v2 로 재시드 — 카테고리 스코프 비활성화 후 스코프 채번 INSERT
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
