using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream N — 신발·장갑·갑주 파츠의 관절 분할 규칙은 3D 모델러 요청대로
    // 유지하되, 상의·하의(바지)·원피스 등 소프트 의류는 3D 리깅 및 스키닝 시 찢어짐/왜곡 방지를 위해
    // 관절(무릎·팔꿈치)에서 분할하지 않고 통짜(바지는 허리~발목 1개, 상의는 어깨~손목 1개)로 유지하도록
    // Extract 프롬프트에 예외를 명시했다.
    public partial class CharacterExtractSoftGarmentsNoJointSplit : Migration
    {
        // 직전 활성 id (workstream M 이 심은 것) — Down 에서 되살린다
        private static readonly Guid PreviousId = Guid.Parse("c4000000-0000-4000-8000-000000000110");

        private static readonly Guid NewId = Guid.Parse("c4000000-0000-4000-8000-000000000111");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 2, 9, 35, 0, TimeSpan.Zero);

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
