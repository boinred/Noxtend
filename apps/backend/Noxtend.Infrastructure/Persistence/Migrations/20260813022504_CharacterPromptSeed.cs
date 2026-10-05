using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-studio slice 4 · §D-04 — Extract·Decompose·Generate 를 카테고리 'Character' 로 시드한다.
    //
    // 🔴 **스코프 함정**(prompt-category-axis §6.1 · SeedPrompts.cs 상단 주석): 카테고리 열이 생긴
    // 뒤로 비활성화·채번 SQL 은 반드시 (Kind, Category) 로 스코프한다. 스코프를 빼면 기본(NULL)
    // 활성까지 꺼지거나 (Kind, Category, Version) 유니크에 걸려 기동이 죽는다.
    //
    // **VALUES + 스칼라 서브쿼리로 채번한다** — 하우스 패턴의 `INSERT ... SELECT ... FROM
    // [PromptVersions] WHERE [Category]=...` 는 캐릭터 행이 아직 하나도 없는 fresh DB 에서 FROM 이
    // 비어 아무 행도 넣지 못한다. VALUES 는 항상 한 행을 넣고, 버전은 카테고리 스코프 MAX+1 로 센다.
    public partial class CharacterPromptSeed : Migration
    {
        // Down 삭제와 테스트 단정을 위한 고정 id
        private static readonly Guid ExtractId = Guid.Parse("c4000000-0000-4000-8000-000000000001");
        private static readonly Guid DecomposeId = Guid.Parse("c4000000-0000-4000-8000-000000000002");
        private static readonly Guid GenerateId = Guid.Parse("c4000000-0000-4000-8000-000000000003");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

        // 카테고리 스코프 리터럴 — HasConversion<string> 이 enum 이름을 저장하므로 "Character"
        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedCharacterPrompt(migrationBuilder, TaskKind.Extract, ExtractId, SeedPrompts.CharacterExtract());
            SeedCharacterPrompt(migrationBuilder, TaskKind.Decompose, DecomposeId, SeedPrompts.CharacterDecompose());
            SeedCharacterPrompt(migrationBuilder, TaskKind.Generate, GenerateId, SeedPrompts.CharacterGenerate());
        }

        /// <inheritdoc />
        // 프롬프트만 되돌리는 것은 릴리스 롤백이 아니다 (§6.3). 여기서는 이 마이그레이션이 심은
        // 캐릭터 활성 행 셋만 지운다 — 기본(NULL) 활성은 애초에 건드리지 않았으므로 복구가 없다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: ExtractId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: GenerateId);
        }

        // 한 단계의 캐릭터 활성 프롬프트를 심는다 — 카테고리 스코프 비활성화 후 스코프 채번 INSERT
        private static void SeedCharacterPrompt(
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

        // 마이그레이션은 파라미터를 붙일 자리가 없어 값을 문장에 박는다. 프롬프트 원문에
        // 작은따옴표가 있으므로 이스케이프가 선택이 아니다
        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
