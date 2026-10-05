using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning — 2026-09-03 3D 모델러 골든셋 피드백 두 건.
    //
    // #13 손 자세: 손목 아래 장갑 세그먼트가 원본의 검 쥔 주먹을 그대로 받아쓰고 있었다.
    // 자세 정규화 규칙(A-포즈·의류 재단·강체 눕히기) 어디에도 손이 걸리지 않는 규칙 공백이라,
    // 다섯 손가락을 펴고 사이를 벌린 리깅 자세를 Generate 에 명시했다.
    //
    // #11 관절 갑주: 어깨 갑주가 앞면만 덮는 판으로 나와 리깅 후 가드 구실을 못 했다.
    // 관절을 앞·위·뒤로 감싸는 캡 형태를 Decompose(서술)와 Generate(작화) 양쪽에 넣었다 —
    // 벨트 제외 규칙이 두 곳에 걸려 있는 것과 같은 방식이다.
    public partial class CharacterHandPoseAndJointArmorWrap : Migration
    {
        // 직전 활성 id — Down 에서 되살린다.
        // Decompose 는 RevertCharacterDecomposeFigureAnchoredBounds(113), Generate 는 workstream K(106).
        private static readonly Guid DecomposePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000113");
        private static readonly Guid GeneratePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000106");

        private static readonly Guid DecomposeNewId = Guid.Parse("c4000000-0000-4000-8000-000000000114");
        private static readonly Guid GenerateNewId = Guid.Parse("c4000000-0000-4000-8000-000000000115");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 9, 4, 10, 30, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Reseed(migrationBuilder, TaskKind.Decompose, DecomposeNewId, SeedPrompts.CharacterDecomposeV11());
            Reseed(migrationBuilder, TaskKind.Generate, GenerateNewId, SeedPrompts.CharacterGenerateV8());
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeNewId);
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: GenerateNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: DecomposePrevId,
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
                    {Literal(prompt.System)}, {Literal(prompt.User)}, {Literal(prompt.Schema)},
                    {Literal(prompt.Note)}, 1, {Literal(SeededAt)});
                """);
        }

        private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

        private static string Literal(Guid value) => $"'{value}'";

        private static string Literal(DateTimeOffset value)
            => $"'{value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'";
    }
}
