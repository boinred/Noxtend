using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noxtend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MeshTaskInputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MeshInputsJson",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeshModel",
                table: "Jobs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MeshProviderConfigId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_PartId_Kind",
                table: "Tasks",
                columns: new[] { "PartId", "Kind" },
                unique: true,
                filter: "[Kind] = 'Reconstruct'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_PartId_Kind",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "MeshInputsJson",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "MeshModel",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "MeshProviderConfigId",
                table: "Jobs");
        }
    }
}
