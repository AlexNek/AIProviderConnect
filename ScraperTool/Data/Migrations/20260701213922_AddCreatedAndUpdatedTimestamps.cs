using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScraperTool.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAndUpdatedTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Models_ProviderEntryId_ModelId",
                table: "Models");

            migrationBuilder.RenameColumn(
                name: "FetchedAt",
                table: "Models",
                newName: "UpdatedAt");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Models",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Models_ProviderEntryId_ModelId",
                table: "Models",
                columns: new[] { "ProviderEntryId", "ModelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Models_ProviderEntryId_ModelId",
                table: "Models");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Models");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Models",
                newName: "FetchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Models_ProviderEntryId_ModelId",
                table: "Models",
                columns: new[] { "ProviderEntryId", "ModelId" },
                unique: true);
        }
    }
}
