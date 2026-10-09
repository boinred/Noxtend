using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModelPriceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowHistoricalFallback",
                table: "ModelPrices",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "ModelPrices",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEvidenceJson",
                table: "ModelPrices",
                type: "nvarchar(max)",
                nullable: true);

            // 확인된 기존 ID만 공급자 분류
            migrationBuilder.Sql("""
                UPDATE [ModelPrices] SET [Provider] = N'anthropic'
                WHERE [Provider] IS NULL AND [Model] COLLATE Latin1_General_100_BIN2 IN (N'claude-fable-5', N'claude-opus-5', N'claude-opus-4-8', N'claude-opus-4-7', N'claude-opus-4-6', N'claude-opus-4-5', N'claude-opus-4-1', N'claude-sonnet-5', N'claude-sonnet-4-6', N'claude-sonnet-4-5', N'claude-haiku-4-5');
                """);
            migrationBuilder.Sql("""
                UPDATE [ModelPrices] SET [Provider] = N'openai'
                WHERE [Provider] IS NULL AND [Model] COLLATE Latin1_General_100_BIN2 IN (N'gpt-5', N'gpt-5-mini', N'gpt-5-nano', N'gpt-5.1', N'gpt-5.2', N'gpt-5.3-codex', N'gpt-5.3-chat-latest', N'gpt-5.4', N'gpt-5.4-mini', N'gpt-5.4-nano', N'gpt-5.4-pro', N'gpt-5.5', N'gpt-5.6-sol', N'gpt-5.6-terra', N'gpt-5.6-luna', N'gpt-image-1', N'gpt-image-1-mini', N'gpt-image-2');
                """);
            migrationBuilder.Sql("""
                UPDATE [ModelPrices] SET [Provider] = N'google'
                WHERE [Provider] IS NULL AND [Model] COLLATE Latin1_General_100_BIN2 IN (N'gemini-3-pro-image-preview', N'gemini-2.5-flash-image', N'gemini-3.1-flash-image', N'gemini-3.1-flash-lite-image', N'gemini-3-pro-image');
                """);
            migrationBuilder.Sql("""
                UPDATE [ModelPrices] SET [Provider] = N'tripo'
                WHERE [Provider] IS NULL AND [Model] COLLATE Latin1_General_100_BIN2 IN (N'P1-20260311');
                """);
            migrationBuilder.Sql("""
                UPDATE [ModelPrices] SET [Provider] = N'meshy'
                WHERE [Provider] IS NULL AND [Model] COLLATE Latin1_General_100_BIN2 IN (N'meshy-7');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowHistoricalFallback",
                table: "ModelPrices");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "ModelPrices");

            migrationBuilder.DropColumn(
                name: "SourceEvidenceJson",
                table: "ModelPrices");
        }
    }
}
