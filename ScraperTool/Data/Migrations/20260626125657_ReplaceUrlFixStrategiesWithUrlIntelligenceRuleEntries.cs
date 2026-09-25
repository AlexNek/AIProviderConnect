using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScraperTool.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceUrlFixStrategiesWithUrlIntelligenceRuleEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UrlFixStrategies",
                table: "UrlFixStrategies");

            migrationBuilder.RenameTable(
                name: "UrlFixStrategies",
                newName: "UrlIntelligenceRuleEntries");

            migrationBuilder.RenameIndex(
                name: "IX_UrlFixStrategies_RuleKind",
                table: "UrlIntelligenceRuleEntries",
                newName: "IX_UrlIntelligenceRuleEntries_RuleKind");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "UrlIntelligenceRuleEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UrlIntelligenceRuleEntries",
                table: "UrlIntelligenceRuleEntries",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UrlIntelligenceRuleEntries",
                table: "UrlIntelligenceRuleEntries");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "UrlIntelligenceRuleEntries");

            migrationBuilder.RenameTable(
                name: "UrlIntelligenceRuleEntries",
                newName: "UrlFixStrategies");

            migrationBuilder.RenameIndex(
                name: "IX_UrlIntelligenceRuleEntries_RuleKind",
                table: "UrlFixStrategies",
                newName: "IX_UrlFixStrategies_RuleKind");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UrlFixStrategies",
                table: "UrlFixStrategies",
                column: "Id");
        }
    }
}
