using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream H — 2026-08-18 실사용 확인 중 발견한 네 번째 문제에 대응한다.
    //
    // 실제 생성 이미지를 확인한 결과, 벨트가 하의 렌더에도, 부츠가 다리 갑주 렌더에도,
    // 버클·장식 메달·파우치가 벨트 스트랩 렌더에도 그대로 남아 있었다. 셋 다 Extract가
    // 파츠를 별개로 잘 뽑았는데도(예: "하의"와 "벨트 스트랩"이 이미 독립 파츠) 렌더 단계에서
    // 겹침이 사라지지 않는, workstream F(머리/머리카락)와 같은 근본 원인의 다른 사례다.
    //
    // 원인: Decompose가 각 파츠의 description을 쓸 때 "사진에 보이는 대로" 서술하다 보니,
    // 위에 겹쳐진 다른 파츠(이미 자기 occludedBy로 표시한 것)까지 같이 옮겨 적었다 — 그
    // 겹치는 파츠가 별도 에셋으로 이미 생성된다는 사실을 description 규칙이 몰랐다.
    //
    // 대응: description 규칙에 "자기 occludedBy에 든 파츠는 빼고, 그게 없다고 치고
    // 서술하라"를 명시한다. 특정 파츠 쌍(벨트-하의 등)을 나열하는 대신 occludedBy라는
    // 기존 스키마 필드를 활용해 일반화했다 — 새 겹침 사례가 나올 때마다 규칙을 또
    // 추가할 필요가 없다.
    //
    // 문구만 바꾼 가설이다 — 실 API 재검증 전까지는 확정 아님(workstream E/F/G와 같은 원칙).
    //
    // 🔴 스코프 함정(이전 캐릭터 마이그레이션과 동일): 비활성화·채번을
    // (Kind, Category='Character')로 스코프해 기본(NULL) 활성을 건드리지 않는다.
    public partial class CharacterPromptWorkstreamH : Migration
    {
        // 직전 활성 id — Down 에서 되살린다.
        private static readonly Guid DecomposePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000052");

        // 이번에 심는 새 버전 id
        private static readonly Guid DecomposeNewId = Guid.Parse("c4000000-0000-4000-8000-000000000082");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 18, 2, 0, 0, TimeSpan.Zero);

        private static readonly string Category = AssetCategory.Character.ToString();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Reseed(migrationBuilder, TaskKind.Decompose, DecomposeNewId, SeedPrompts.CharacterDecompose());
        }

        /// <inheritdoc />
        // 새 버전을 지우고 직전 버전을 다시 활성으로. 삭제(활성 제거)가 먼저라 필터 유니크
        // (Kind, Category) 에 걸리지 않는다.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "PromptVersions", keyColumn: "Id", keyValue: DecomposeNewId);

            migrationBuilder.UpdateData(
                table: "PromptVersions", keyColumn: "Id", keyValue: DecomposePrevId,
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
