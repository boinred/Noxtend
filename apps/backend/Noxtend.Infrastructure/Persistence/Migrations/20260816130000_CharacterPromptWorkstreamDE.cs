using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // character-tuning workstream D/E — 오늘 실 API로 검증한(DB 프롬프트 버전으로 등록·활성화해
    // 테스트) 캐릭터 프롬프트를 시드로 굳힌다. Extract(베이스바디 통짜·부착 소품 분리·스코프
    // 강제), Decompose(무기류 파지 자세 대신 중립 정자세), Generate(정면 참조 앵커·회전 규칙·
    // 단면 오브젝트 규칙·강체 자세 규칙)를 다음 버전으로 재시드한다.
    //
    // 알려진 미해결 이슈(스펙에 기록, 이 시드로도 안 풀림): workstream E §5 결정 ⑤
    // (스코프-분리 충돌 패턴, 이슈 4·12), §1.6 이슈 13(재현성 — 실행마다 파츠 설명이 달라짐).
    //
    // 🔴 스코프 함정(CharacterPromptSeed 와 동일): 비활성화·채번을 (Kind, Category='Character')
    // 로 스코프해 기본(NULL) 활성을 건드리지 않는다. VALUES + 스칼라 서브쿼리로 채번한다.
    public partial class CharacterPromptWorkstreamDE : Migration
    {
        // 직전 활성 id — Down 에서 되살린다. 전부 CharacterPromptTuning(v2/v5/v4)이 심은 것.
        private static readonly Guid ExtractPrevId = Guid.Parse("c4000000-0000-4000-8000-000000000021");
        private static readonly Guid DecomposePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000022");
        private static readonly Guid GeneratePrevId = Guid.Parse("c4000000-0000-4000-8000-000000000023");

        // 이번에 심는 새 버전 id
        private static readonly Guid ExtractNewId = Guid.Parse("c4000000-0000-4000-8000-000000000031");
        private static readonly Guid DecomposeNewId = Guid.Parse("c4000000-0000-4000-8000-000000000032");
        private static readonly Guid GenerateNewId = Guid.Parse("c4000000-0000-4000-8000-000000000033");

        private static readonly DateTimeOffset SeededAt =
            new(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);

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
